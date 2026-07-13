using System;
using System.ComponentModel.Design;
using System.Linq;
using System.Threading;
using System.Windows;
using CodingWithCalvin.MCPServer.Services;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace CodingWithCalvin.MCPServer.Commands;

internal sealed class ServerCommands
{
    private static int _operationInProgress;
    private static string? _activeOperation;

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
        if (commandService == null)
        {
            return;
        }

        // Start Server command
        var startCommandId = new CommandID(VSCommandTableVsct.guidMCPServerPackageCmdSet.Guid, VSCommandTableVsct.guidMCPServerPackageCmdSet.cmdidStartServer);
        var startCommand = new OleMenuCommand(OnStartServer, startCommandId);
        startCommand.BeforeQueryStatus += OnBeforeQueryStatusStart;
        commandService.AddCommand(startCommand);

        // Stop Server command
        var stopCommandId = new CommandID(VSCommandTableVsct.guidMCPServerPackageCmdSet.Guid, VSCommandTableVsct.guidMCPServerPackageCmdSet.cmdidStopServer);
        var stopCommand = new OleMenuCommand(OnStopServer, stopCommandId);
        stopCommand.BeforeQueryStatus += OnBeforeQueryStatusStop;
        commandService.AddCommand(stopCommand);

        // Restart Server command
        var restartCommandId = new CommandID(VSCommandTableVsct.guidMCPServerPackageCmdSet.Guid, VSCommandTableVsct.guidMCPServerPackageCmdSet.cmdidRestartServer);
        var restartCommand = new OleMenuCommand(OnRestartServer, restartCommandId);
        restartCommand.BeforeQueryStatus += OnBeforeQueryStatusRestart;
        commandService.AddCommand(restartCommand);

        // Copy Server URL command
        var copyUrlCommandId = new CommandID(VSCommandTableVsct.guidMCPServerPackageCmdSet.Guid, VSCommandTableVsct.guidMCPServerPackageCmdSet.cmdidCopyServerUrl);
        var copyUrlCommand = new OleMenuCommand(OnCopyServerUrl, copyUrlCommandId);
        commandService.AddCommand(copyUrlCommand);

        // Show Available Tools command
        var showToolsCommandId = new CommandID(VSCommandTableVsct.guidMCPServerPackageCmdSet.Guid, VSCommandTableVsct.guidMCPServerPackageCmdSet.cmdidShowTools);
        var showToolsCommand = new OleMenuCommand(OnShowTools, showToolsCommandId);
        showToolsCommand.BeforeQueryStatus += OnBeforeQueryStatusStop;
        commandService.AddCommand(showToolsCommand);
    }

    private static void EnsureServicesInitialized()
    {
        MCPServerPackage.Instance?.InitializeServices();
    }

    private static void OnBeforeQueryStatusStart(object sender, EventArgs e)
    {
        if (sender is OleMenuCommand command)
        {
            command.Text = _activeOperation == "start" ? "Starting Server..." : "Start Server";
            command.Enabled = Volatile.Read(ref _operationInProgress) == 0
                && (MCPServerPackage.ServerManager == null || !MCPServerPackage.ServerManager.IsRunning);
        }
    }

    private static void OnBeforeQueryStatusStop(object sender, EventArgs e)
    {
        if (sender is OleMenuCommand command)
        {
            command.Text = _activeOperation == "stop" ? "Stopping Server..." : "Stop Server";
            command.Enabled = Volatile.Read(ref _operationInProgress) == 0
                && MCPServerPackage.ServerManager != null
                && MCPServerPackage.ServerManager.IsRunning;
        }
    }

    private static void OnBeforeQueryStatusRestart(object sender, EventArgs e)
    {
        if (sender is OleMenuCommand command)
        {
            command.Text = _activeOperation == "restart" ? "Restarting Server..." : "Restart Server";
            command.Enabled = Volatile.Read(ref _operationInProgress) == 0
                && MCPServerPackage.ServerManager != null;
        }
    }

    private static bool TryBeginOperation(string operation)
    {
        if (Interlocked.CompareExchange(ref _operationInProgress, 1, 0) != 0)
        {
            return false;
        }

        _activeOperation = operation;
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        return true;
    }

    private static void EndOperation()
    {
        _activeOperation = null;
        Volatile.Write(ref _operationInProgress, 0);
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    private static ServerStartSettings CaptureSettings()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var settings = MCPServerPackage.Settings;

        return new ServerStartSettings
        {
            BindingAddress = settings?.BindingAddress ?? "localhost",
            Port = settings?.Port ?? 5050,
            ServerName = settings?.ServerName ?? "Visual Studio MCP",
            LogLevel = settings?.LogLevel.ToString() ?? "Information",
            LogRetentionDays = settings?.LogRetentionDays ?? 7,
            OutputPane = MCPServerPackage.OutputPaneService?.GetPane()
        };
    }

    private static void OnStartServer(object sender, EventArgs e)
    {
        if (!TryBeginOperation("start")) return;
        MCPServerPackage.LogLoad("Command: Start Server requested");
        // Capture everything we need on UI thread before going to background
        EnsureServicesInitialized();
        var serverManager = MCPServerPackage.ServerManager;
        if (serverManager == null)
        {
            EndOperation();
            return;
        }

        var startSettings = CaptureSettings();

        _ = Task.Run(async () =>
        {
            try
            {
                await serverManager.StartAsync(startSettings);
            }
            catch (Exception ex)
            {
                await ShowOperationErrorAsync("start", ex);
            }
            finally
            {
                MCPServerPackage.LogLoad("Command: Start Server completed");
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                EndOperation();
            }
        });
    }

    private static void OnStopServer(object sender, EventArgs e)
    {
        if (!TryBeginOperation("stop")) return;
        MCPServerPackage.LogLoad("Command: Stop Server requested");
        var serverManager = MCPServerPackage.ServerManager;
        if (serverManager == null)
        {
            EndOperation();
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await serverManager.StopAsync();
            }
            catch (Exception ex)
            {
                await ShowOperationErrorAsync("stop", ex);
            }
            finally
            {
                MCPServerPackage.LogLoad("Command: Stop Server completed");
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                EndOperation();
            }
        });
    }

    private static void OnRestartServer(object sender, EventArgs e)
    {
        if (!TryBeginOperation("restart")) return;
        MCPServerPackage.LogLoad("Command: Restart Server requested");
        EnsureServicesInitialized();
        var serverManager = MCPServerPackage.ServerManager;
        if (serverManager == null)
        {
            EndOperation();
            return;
        }

        var startSettings = CaptureSettings();

        _ = Task.Run(async () =>
        {
            try
            {
                await serverManager.StopAsync();
                await serverManager.StartAsync(startSettings);
            }
            catch (Exception ex)
            {
                await ShowOperationErrorAsync("restart", ex);
            }
            finally
            {
                MCPServerPackage.LogLoad("Command: Restart Server completed");
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                EndOperation();
            }
        });
    }

    private static async Task ShowOperationErrorAsync(string operation, Exception exception)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        if (MCPServerPackage.Instance == null)
        {
            return;
        }

        VsShellUtilities.ShowMessageBox(
            MCPServerPackage.Instance,
            $"Failed to {operation} the MCP server.\n\n{exception.Message}",
            "VS MCP Server",
            OLEMSGICON.OLEMSGICON_CRITICAL,
            OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }

    private static void OnCopyServerUrl(object sender, EventArgs e)
    {
        var port = MCPServerPackage.Settings?.Port ?? 5050;
        var url = $"http://localhost:{port}/sse";
        Clipboard.SetText(url);
    }

    private static void OnShowTools(object sender, EventArgs e)
    {
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (MCPServerPackage.Instance == null)
            {
                return;
            }

            if (MCPServerPackage.RpcServer == null || !MCPServerPackage.RpcServer.IsConnected)
            {
                VsShellUtilities.ShowMessageBox(
                    MCPServerPackage.Instance,
                    "Server is not connected. Start the server first.",
                    "VS MCP Server",
                    OLEMSGICON.OLEMSGICON_INFO,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                return;
            }

            var tools = await MCPServerPackage.RpcServer.GetAvailableToolsAsync();
            var message = tools.Count == 0
                ? "No tools available."
                : string.Join("\n", tools.GroupBy(t => t.Category).OrderBy(g => g.Key)
                    .SelectMany(g => new[] { $"\n{g.Key.ToUpperInvariant()} TOOLS:" }
                        .Concat(g.OrderBy(t => t.Name).Select(t => $"  {t.Name}"))));

            VsShellUtilities.ShowMessageBox(
                MCPServerPackage.Instance,
                message,
                "VS MCP Server - Available Tools",
                OLEMSGICON.OLEMSGICON_INFO,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        });
    }
}
