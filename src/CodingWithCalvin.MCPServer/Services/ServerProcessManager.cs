using System;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CodingWithCalvin.MCPServer.Dialogs;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

namespace CodingWithCalvin.MCPServer.Services;

[Export(typeof(IServerProcessManager))]
[PartCreationPolicy(CreationPolicy.Shared)]
public class ServerProcessManager : IServerProcessManager
{
    private readonly IRpcServer _rpcServer;
    private readonly SemaphoreSlim _lifecycleGate = new SemaphoreSlim(1, 1);
    private CancellationTokenSource? _startupCts;
    private Process? _serverProcess;
    private string _pipeName = string.Empty;
    private StreamWriter? _logFileWriter;
    private string? _logFilePath;
    private IVsOutputWindowPane? _outputPane;

    public bool IsRunning => _serverProcess != null && !_serverProcess.HasExited;
    public string? LogFilePath => _logFilePath;

    [ImportingConstructor]
    public ServerProcessManager(IRpcServer rpcServer)
    {
        _rpcServer = rpcServer;
    }

    public async Task StartAsync(ServerStartSettings settings)
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        CancellationTokenSource? startupCts = null;
        try
        {
            if (IsRunning)
            {
                return;
            }

            startupCts = new CancellationTokenSource();
            _startupCts = startupCts;

        // Initialize logging (file + output pane from UI thread)
        InitializeLogging(settings);

        // Generate unique pipe name for this VS instance
        _pipeName = $"vsmcp-{Process.GetCurrentProcess().Id}";

        // Start the RPC server first
        await _rpcServer.StartAsync(_pipeName).ConfigureAwait(false);

        // Find the server executable
        var extensionDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        var serverExe = Path.Combine(extensionDir!, "Server", "CodingWithCalvin.MCPServer.Server.exe");

        if (!File.Exists(serverExe))
        {
            throw new FileNotFoundException("MCP Server executable not found", serverExe);
        }

        // Start the server process
        var arguments = $"--pipe \"{_pipeName}\" --parent-pid {Process.GetCurrentProcess().Id} --host \"{settings.BindingAddress}\" --port {settings.Port} --name \"{settings.ServerName}\" --log-level {settings.LogLevel}";

        var startInfo = new ProcessStartInfo
        {
            FileName = serverExe,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        var process = Process.Start(startInfo);

        if (process == null)
        {
            throw new InvalidOperationException("Failed to start MCP Server process");
        }

        // Store in field and capture local reference to avoid race conditions
        _serverProcess = process;

        process.EnableRaisingEvents = true;
        process.Exited += OnProcessExited;

        // Start reading output streams (server logs go to stderr by convention)
        _ = ReadOutputAsync(process.StandardOutput);
        _ = ReadOutputAsync(process.StandardError);

        Log($"Server started (PID: {process.Id})");
        Log($"Binding: http://{settings.BindingAddress}:{settings.Port}");
        Log($"Log file: {_logFilePath}");

            // Do not report success until Kestrel has bound the HTTP endpoint and the
            // child has acknowledged readiness over RPC.
            var startupTimer = Stopwatch.StartNew();
            while (!_rpcServer.IsReady && startupTimer.Elapsed < TimeSpan.FromSeconds(10))
            {
                if (process.HasExited)
                {
                    throw new InvalidOperationException($"MCP Server process exited during startup with code {process.ExitCode}");
                }

                await Task.Delay(50, startupCts.Token).ConfigureAwait(false);
            }

            if (!_rpcServer.IsReady)
            {
                throw new TimeoutException("MCP Server did not become ready within 10 seconds");
            }

            Log("Server is ready");
        }
        catch (OperationCanceledException) when (startupCts?.IsCancellationRequested == true)
        {
            Log("Server startup was cancelled");
            await StopCoreAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log($"Server startup failed: {ex.Message}");
            await StopCoreAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            if (ReferenceEquals(_startupCts, startupCts))
            {
                _startupCts = null;
            }

            startupCts?.Dispose();
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync()
    {
        // A stop request must interrupt an in-progress start rather than queueing
        // behind the full startup timeout.
        _startupCts?.Cancel();
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task StopCoreAsync()
    {
        // Capture reference to avoid race conditions during shutdown
        var process = _serverProcess;

        if (process != null && !process.HasExited)
        {
            try
            {
                Log("Stopping server...");

                // Unsubscribe from Exited event to prevent duplicate logging
                process.Exited -= OnProcessExited;

                var exited = false;
                if (_rpcServer.IsConnected)
                {
                    // Request graceful shutdown via RPC and wait briefly.
                    await _rpcServer.RequestShutdownAsync().ConfigureAwait(false);
                    exited = await Task.Run(() => process.WaitForExit(3000)).ConfigureAwait(false);
                }

                if (!exited)
                {
                    // Before the pipe connects there is no graceful channel, and
                    // after a timeout the child must not be left behind.
                    Log("Forcing server termination...");
                    process.Kill();
                    await Task.Run(() => process.WaitForExit(2000)).ConfigureAwait(false);
                }

                Log($"Server stopped (Code: {process.ExitCode})");
            }
            catch
            {
                // Process may have already exited
            }
        }

        _serverProcess?.Dispose();
        _serverProcess = null;

        await _rpcServer.StopAsync().ConfigureAwait(false);

        // Close log file
        _logFileWriter?.Dispose();
        _logFileWriter = null;
    }

    private void InitializeLogging(ServerStartSettings settings)
    {
        // Create log file in temp directory (daily rotation)
        try
        {
            var logDir = SettingsDialogPage.LogFolderPath;
            Directory.CreateDirectory(logDir);

            var date = DateTime.Now.ToString("yyyy-MM-dd");
            _logFilePath = Path.Combine(logDir, $"server_{date}.log");
            _logFileWriter = new StreamWriter(_logFilePath, append: true) { AutoFlush = true };

            // Clean up old log files (fire and forget to not block startup)
            var retentionDays = settings.LogRetentionDays;
            Task.Run(() => CleanupOldLogFiles(logDir, retentionDays));
        }
        catch
        {
            // File logging will be unavailable, but continue anyway
            _logFilePath = null;
            _logFileWriter = null;
        }

        // Use output pane passed from UI thread
        _outputPane = settings.OutputPane;

        Log($"=== MCP Server Log Started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
    }

    private void CleanupOldLogFiles(string logDir, int retentionDays)
    {
        if (retentionDays <= 0)
        {
            return; // Keep all logs
        }

        try
        {
            var cutoffDate = DateTime.Now.AddDays(-retentionDays);
            var logFiles = Directory.GetFiles(logDir, "server_*.log");

            foreach (var file in logFiles)
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.LastWriteTime < cutoffDate)
                {
                    try
                    {
                        fileInfo.Delete();
                    }
                    catch
                    {
                        // Ignore individual file deletion errors
                    }
                }
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private async Task ReadOutputAsync(StreamReader reader)
    {
        try
        {
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (!string.IsNullOrEmpty(line))
                {
                    Log($"[SERVER] {line}");
                }
            }
        }
        catch
        {
            // Stream closed, ignore
        }
    }

    private void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var logLine = $"[{timestamp}] {message}";

        // Write to file
        try
        {
            _logFileWriter?.WriteLine(logLine);
        }
        catch
        {
            // Ignore file write errors
        }

        // Write to output pane (OutputStringThreadSafe is thread-safe, no main thread needed)
        try
        {
            _outputPane?.OutputStringThreadSafe(logLine + Environment.NewLine);
        }
        catch
        {
            // Ignore output pane errors
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        // Use sender to get exit code - don't modify _serverProcess here to avoid race conditions
        var exitCode = (sender as Process)?.ExitCode;
        Log($"Server process exited (Code: {exitCode})");
    }
}
