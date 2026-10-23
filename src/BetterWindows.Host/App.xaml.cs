using System.IO;
using System.Windows;
using BetterWindows.Core.Services;
using BetterWindows.Host.Bridge;
using BetterWindows.Host.Cli;
using BetterWindows.Host.Composition;
using BetterWindows.Host.Lifecycle;
using BetterWindows.Host.Logging;
using Microsoft.Extensions.Logging;
using Serilog;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace BetterWindows.Host;

/// <summary>
/// Process entry (owner: lifecycle stream). Parses the command line before anything else, dispatches the modes that never show the shell
/// (<c>--guardian</c>, <c>--restore-taskbar</c>, <c>--diagnose</c>) and otherwise runs <see cref="ShellHost"/>. Unhandled exceptions restore the shell and exit.
/// </summary>
public partial class App : Application
{
    private ShellHost? _host;
    private Serilog.Core.Logger? _logger;
    private ILogger? _log;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var cli = Cli.CommandLineParser.Parse(e.Args);
        ProcessEnvironment.Apply(cli); // data dir + WebView2 background must be set before any other code reads them
        var paths = new AppPaths();
        _logger = HostLogging.Create(paths.LogsDir, ShellHost.DebugBuild, shared: true);
        _log = new LoggerFactory().AddSerilog(_logger).CreateLogger<App>();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            _log?.LogCritical(args.ExceptionObject as Exception, "Unhandled exception on a background thread");
        };

        switch (cli.Mode)
        {
            case LaunchMode.Guardian:
                Shutdown(CliCommands.RunGuardian(cli, paths));
                return;
            case LaunchMode.RestoreTaskbar:
                Shutdown(CliCommands.RestoreTaskbar());
                return;
            case LaunchMode.Diagnose:
                var journals = RestoreJournalScanner.FindStale(paths.StateDir, new SystemProcessProbe(), Environment.ProcessId);
                var index = new AppResourceServer(Path.Combine(AppContext.BaseDirectory, "wwwroot")).HasIndex();
                Shutdown(CliCommands.Diagnose(cli, paths, ShellHost.TryGetWebView2Version, journals, NativeHelpers.IsProcessElevated(), index));
                return;
        }

        SessionEnding += (_, _) => _host?.OnSessionEnding();
        var host = new ShellHost(cli, paths, _logger, Dispatcher, Shutdown);
        _host = host;
        int? exit;
        try { exit = host.Start(); }
        catch (Exception ex)
        {
            // A startup that throws has changed nothing the gate allows yet, but restore anyway: the shell must never stay half-changed.
            _log?.LogCritical(ex, "Startup failed");
            host.RestoreShellNow();
            // Closing silently looks like "it starts and disappears": say why and where the log is (never in smoke mode, CI has nobody to click it).
            if (cli.Mode != LaunchMode.Smoke)
                NativeHelpers.ShowMessageBox("BetterWindows", $"BetterWindows could not start.\n\n{ex.GetType().Name}: {ex.Message}\n\nLog folder:\n{paths.LogsDir}", false, true);
            exit = ExitCodes.Failed;
        }
        if (exit is { } code)
        {
            host.Dispose();
            _host = null;
            Shutdown(code);
        }
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        var host = _host;
        if (host is null) return; // before the shell exists there is nothing to restore: let the process fail normally
        e.Handled = true;
        host.OnFatal(e.Exception);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        _host = null;
        _logger?.Dispose();
        base.OnExit(e);
    }
}
