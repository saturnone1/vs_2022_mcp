using System;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Reflection;
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
    private readonly object _processSync = new object();
    private readonly object _logSync = new object();
    private Process? _serverProcess;
    private string _pipeName = string.Empty;
    private StreamWriter? _logFileWriter;
    private string? _logFilePath;
    private IVsOutputWindowPane? _outputPane;

    public bool IsRunning
    {
        get
        {
            lock (_processSync)
            {
                return _serverProcess != null && !_serverProcess.HasExited;
            }
        }
    }
    public string? LogFilePath => _logFilePath;

    [ImportingConstructor]
    public ServerProcessManager(IRpcServer rpcServer)
    {
        _rpcServer = rpcServer;
    }

    public async Task StartAsync(ServerStartSettings settings)
    {
        if (IsRunning)
        {
            return;
        }

        try
        {
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

            lock (_processSync)
            {
                _serverProcess = process;
            }

        process.EnableRaisingEvents = true;
        process.Exited += OnProcessExited;

        // Stream child output without tying process control to log delivery.
        _ = ReadOutputAsync(process.StandardOutput);
        _ = ReadOutputAsync(process.StandardError);

        Log($"Server started (PID: {process.Id})");
        Log($"Binding: http://{settings.BindingAddress}:{settings.Port}");
        Log($"Log file: {_logFilePath}");

            // Starting the child is the Start command's only responsibility.
            // Runtime bind failures are reported by the child process logs.
            await Task.Yield();
        }
        catch (Exception ex)
        {
            Log($"Server startup failed: {ex.Message}");
            await StopAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task StopAsync()
    {
        Process? process;
        lock (_processSync)
        {
            process = _serverProcess;
            _serverProcess = null;
        }

        if (process != null && !process.HasExited)
        {
            try
            {
                Log("Stopping server...");

                // Unsubscribe from Exited event to prevent duplicate logging
                process.Exited -= OnProcessExited;

                // Ask politely, but never let RPC block the Stop command.
                if (_rpcServer.IsConnected)
                {
                    var shutdownTask = _rpcServer.RequestShutdownAsync();
                    await Task.WhenAny(shutdownTask, Task.Delay(500)).ConfigureAwait(false);
                }

                var exited = await Task.Run(() => process.WaitForExit(1000)).ConfigureAwait(false);
                if (!exited)
                {
                    Log("Forcing server termination...");
                    process.Kill();
                    await Task.Run(() => process.WaitForExit(1000)).ConfigureAwait(false);
                }

                Log($"Server stopped (Code: {process.ExitCode})");
            }
            catch
            {
                // Process may have already exited
            }
        }

        process?.Dispose();

        await _rpcServer.StopAsync().ConfigureAwait(false);

        // Close log file
        lock (_logSync)
        {
            _logFileWriter?.Dispose();
            _logFileWriter = null;
        }
    }

    private void InitializeLogging(ServerStartSettings settings)
    {
        // Create log file in temp directory (daily rotation)
        try
        {
            var logDir = SettingsDialogPage.LogFolderPath;
            Directory.CreateDirectory(logDir);

            var date = DateTime.Now.ToString("yyyy-MM-dd");
            _logFilePath = Path.Combine(logDir, $"server_{date}_{Process.GetCurrentProcess().Id}.log");
            lock (_logSync)
            {
                _logFileWriter?.Dispose();
                _logFileWriter = new StreamWriter(_logFilePath, append: true) { AutoFlush = true };
            }

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
            while (true)
            {
                var line = await reader.ReadLineAsync();
                if (line == null)
                {
                    break;
                }

                if (line.Length > 0)
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
            lock (_logSync)
            {
                _logFileWriter?.WriteLine(logLine);
            }
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
