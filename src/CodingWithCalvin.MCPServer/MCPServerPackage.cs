using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CodingWithCalvin.Otel4Vsix;
using CodingWithCalvin.MCPServer.Commands;
using CodingWithCalvin.MCPServer.Dialogs;
using CodingWithCalvin.MCPServer.Services;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CodingWithCalvin.MCPServer;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration(VsixInfo.DisplayName, VsixInfo.Description, VsixInfo.Version)]
[ProvideOptionPage(
    typeof(SettingsDialogPage),
    "MCP Server",
    "General",
    101,
    111,
    true,
    new string[0],
    ProvidesLocalizedCategoryName = false
)]
[ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideAutoLoad(VSConstants.UICONTEXT.EmptySolution_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[Guid(VSCommandTableVsct.guidMCPServerPackageString)]
public sealed class MCPServerPackage : AsyncPackage
{
    public static MCPServerPackage? Instance { get; private set; }
    public static IServerProcessManager? ServerManager { get; private set; }
    public static IRpcServer? RpcServer { get; private set; }
    public static IVisualStudioService? VsService { get; private set; }
    public static IOutputPaneService? OutputPaneService { get; private set; }
    public static SettingsDialogPage? Settings { get; private set; }

    private IComponentModel? _componentModel;

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        try
        {
            LogLoad("InitializeAsync: begin");

            try
            {
                await base.InitializeAsync(cancellationToken, progress);
                LogLoad("InitializeAsync: base complete");
            }
            catch (Exception ex)
            {
                LogLoad("InitializeAsync: base failed", ex);
                return;
            }

            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            LogLoad("InitializeAsync: on UI thread");

            Instance = this;

            try
            {
                Settings = (SettingsDialogPage)GetDialogPage(typeof(SettingsDialogPage));
                LogLoad("InitializeAsync: settings page ready");
            }
            catch (Exception ex)
            {
                LogLoad("InitializeAsync: settings page failed", ex);
            }

            try
            {
                _componentModel = await GetServiceAsync(typeof(SComponentModel)) as IComponentModel;
                LogLoad(_componentModel == null
                    ? "InitializeAsync: component model unavailable"
                    : "InitializeAsync: component model ready");
            }
            catch (Exception ex)
            {
                LogLoad("InitializeAsync: component model failed", ex);
            }

            try
            {
                var builder = VsixTelemetry.Configure()
                    .WithServiceName(VsixInfo.DisplayName)
                    .WithServiceVersion(VsixInfo.Version)
                    .WithVisualStudioAttributes(this)
                    .WithEnvironmentAttributes();

#if !DEBUG
                if (!string.Equals(HoneycombConfig.ApiKey, "PLACEHOLDER", StringComparison.Ordinal))
                {
                    builder
                        .WithOtlpHttp("https://api.honeycomb.io")
                        .WithHeader("x-honeycomb-team", HoneycombConfig.ApiKey);
                }
#endif

                builder.Initialize();
                LogLoad("InitializeAsync: telemetry ready");
            }
            catch (Exception ex)
            {
                LogLoad("InitializeAsync: telemetry failed", ex);
            }

            try
            {
                await ServerCommands.InitializeAsync(this);
                LogLoad("InitializeAsync: commands ready");
            }
            catch (Exception ex)
            {
                LogLoad("InitializeAsync: commands failed", ex);
            }

            if (Settings?.AutoStart == true)
            {
                try
                {
                    InitializeServices();
                    if (ServerManager != null)
                    {
                        var startSettings = new Services.ServerStartSettings
                        {
                            BindingAddress = Settings.BindingAddress,
                            Port = Settings.Port,
                            ServerName = Settings.ServerName,
                            LogLevel = Settings.LogLevel.ToString(),
                            LogRetentionDays = Settings.LogRetentionDays,
                            OutputPane = OutputPaneService?.GetPane()
                        };
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await ServerManager.StartAsync(startSettings);
                            }
                            catch (Exception ex)
                            {
                                LogLoad("InitializeAsync: autostart failed", ex);
                            }
                        });
                    }
                    LogLoad("InitializeAsync: autostart handled");
                }
                catch (Exception ex)
                {
                    LogLoad("InitializeAsync: autostart failed", ex);
                }
            }

            LogLoad("InitializeAsync: complete");
        }
        catch (Exception ex)
        {
            LogLoad("InitializeAsync: unhandled failure", ex);
        }
    }

    public void InitializeServices()
    {
        if (VsService == null && _componentModel != null)
        {
            VsService = _componentModel.GetService<IVisualStudioService>();
            RpcServer = _componentModel.GetService<IRpcServer>();
            ServerManager = _componentModel.GetService<IServerProcessManager>();
            OutputPaneService = _componentModel.GetService<IOutputPaneService>();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ServerManager?.StopAsync().GetAwaiter().GetResult();
            RpcServer?.Dispose();
            VsixTelemetry.Shutdown();
            Instance = null;
        }

        base.Dispose(disposing);
    }

    internal static void LogLoad(string message, Exception? exception = null)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VS-MCPServer");
            Directory.CreateDirectory(dir);

            var logPath = Path.Combine(dir, "extension-load.log");
            File.AppendAllText(
                logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}{exception}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
