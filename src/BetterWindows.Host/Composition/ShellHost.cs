using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using BetterWindows.Core.Hosting;
using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Host.Bridge;
using BetterWindows.Host.Cli;
using BetterWindows.Host.Lifecycle;
using BetterWindows.Host.Surfaces;
using BetterWindows.Host.Tray;
using BetterWindows.Native;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using Microsoft.Web.WebView2.Core;

namespace BetterWindows.Host.Composition;

/// <summary>
/// The shell's startup and shutdown sequence (docs/ARCHITECTURE.md section 5, docs/IPC.md section 15), in this order: elevation check, single instance, restore of
/// stale journals, crash counter (safe mode), autostart wait, WebView2 runtime check, DI, Core services and controllers, surfaces, tray, listeners.
/// The shutdown plan is the only place that restores the user's shell on a normal exit: surfaces, controllers, taskbar, desktop icons, tray, WebView, state, mutex.
/// </summary>
public sealed class ShellHost : IDisposable
{
    /// <summary>Smoke mode uses its own mutex so a developer's running shell does not block CI.</summary>
    public const string SmokeMutexName = @"Local\BetterWindows.Smoke";
    /// <summary>Time the surfaces get to become ready in smoke mode (docs/IPC.md section 16 ready timeout is 10 s; smoke allows more on slow runners).</summary>
    public static readonly TimeSpan SmokeTimeout = TimeSpan.FromSeconds(40);
    /// <summary>How long Explorer's taskbar may take to appear for <c>--autostart</c>.</summary>
    public static readonly TimeSpan ExplorerWait = TimeSpan.FromSeconds(30);
    /// <summary>Evergreen runtime download page shown when the runtime is missing.</summary>
    private const string WebView2RuntimeUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";

#if DEBUG
    public const bool DebugBuild = true;
#else
    public const bool DebugBuild = false;
#endif

    private readonly CommandLineOptions _cli;
    private readonly IAppPaths _paths;
    private readonly Serilog.ILogger _serilog;
    private readonly Dispatcher _dispatcher;
    private readonly ILogger _log;
    private readonly bool _smoke;
    private readonly Action<int> _exitApp;
    private SingleInstance? _instance;
    private StartupStateStore? _startup;
    private ServiceProvider? _provider;
    private ShellRuntime? _runtime;
    private ISurfaceHost? _surfaces;
    private TrayIconHost? _tray;
    private WebViewBridge? _bridge;
    private ShutdownCoordinator? _shutdown;
    private ITimer? _healthyTimer, _smokeTimer;
    private DateTimeOffset _smokeStarted;
    private int _disposed;

    /// <param name="exitApp">Ends the WPF application with an exit code (App.Shutdown).</param>
    public ShellHost(CommandLineOptions cli, IAppPaths paths, Serilog.ILogger serilog, Dispatcher dispatcher, Action<int> exitApp)
    {
        _cli = cli;
        _paths = paths;
        _serilog = serilog;
        _dispatcher = dispatcher;
        _exitApp = exitApp;
        _smoke = cli.Mode == LaunchMode.Smoke;
        _log = new LoggerFactory().AddSerilog(serilog).CreateLogger<ShellHost>();
    }

    /// <summary>Runs the startup sequence. Returns an exit code when the process must end right away; null when the shell is running.</summary>
    public int? Start()
    {
        // 1. Never elevated (docs/IPC.md section 15, rule 2). Smoke runs on CI runners that are elevated; it never changes the shell, so it is allowed.
        if (!_smoke && NativeHelpers.IsProcessElevated())
        {
            _log.LogError("Refusing to run elevated");
            NativeHelpers.ShowMessageBox("BetterWindows", "BetterWindows cannot run as administrator. Start it from a normal (non-elevated) session.", false, true);
            return ExitCodes.Elevated;
        }

        // 2. One shell per Windows session. A second launch asks the running one to open Settings and exits normally.
        var instance = SingleInstance.TryAcquire(_smoke ? SmokeMutexName : HostObjectNames.SingleInstanceMutex);
        if (instance is null)
        {
            _log.LogInformation("Another instance is running; asking it to open Settings");
            SingleInstance.SignalOpenSettings();
            return ExitCodes.Ok;
        }
        _instance = instance;
        foreach (var warning in _cli.Errors) _log.LogWarning("Command line: {Warning}", warning);

        var probe = new SystemProcessProbe();
        var pid = Environment.ProcessId;

        // 3. A previous run died while it held the taskbar or the desktop icons: put them back before we do anything else.
        var stale = RestoreJournalScanner.FindStale(_paths.StateDir, probe, pid);
        if (stale.Count > 0)
        {
            _log.LogWarning("{Count} restore journal(s) of a dead owner found; restoring the shell", stale.Count);
            RestoreStaticShell();
        }

        // 4. Crash counter: three unclean starts within ten minutes start the next run in safe mode (not for smoke: CI must test the real shell).
        StartupDecision? decision = null;
        if (!_smoke)
        {
            _startup = new StartupStateStore(_paths.StateDir, TimeProvider.System, probe, pid, _log);
            decision = _startup.BeginRun();
            if (decision.CrashLoop) _log.LogWarning("Crash loop detected: starting in SAFE MODE");
        }

        // 5. Started from the Run key: Explorer's taskbar may not exist yet.
        if (_cli.Autostart) WaitForTaskbar(ExplorerWait);

        // 6. The Evergreen WebView2 runtime is required. Without it nothing can be shown: the taskbar is untouched and the user gets the download link.
        var webView2 = TryGetWebView2Version();
        if (webView2 is null)
        {
            _log.LogError("WebView2 runtime is not installed");
            NativeHelpers.ShowMessageBox("BetterWindows", "BetterWindows needs the Microsoft Edge WebView2 Runtime.\n\n" + WebView2RuntimeUrl, false, true);
            _startup?.MarkCleanExit(); // not a crash of ours: must not count towards safe mode
            return ExitCodes.RuntimeMissing;
        }
        _log.LogInformation("WebView2 runtime {Version}, Windows {Os}", webView2, BuildMetadata.OsDescription());

        // 7. Composition. Options are resolved once; everything below reads them.
        var options = new HostRuntimeOptions(_cli, decision?.CrashLoop ?? false, Environment.GetEnvironmentVariable(HostRuntimeOptions.UiUrlEnvVar), DebugBuild);
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddSerilog(_serilog, dispose: false));
        services.AddBetterWindowsHost(_paths, options, new DispatcherScheduler(_dispatcher));
        var provider = services.BuildServiceProvider();
        _provider = provider;
        var shutdown = provider.GetRequiredService<ShutdownCoordinator>();
        _shutdown = shutdown;
        shutdown.Configure(BuildShutdownSteps, ForceRestoreAndExit);
        var bridge = provider.GetRequiredService<WebViewBridge>();
        _bridge = bridge;
        bridge.Failed += OnSurfaceFailed;

        // 8. Core (services, then controllers), then the surfaces. The taskbar is hidden only later, by the gate (dock and menu bar ready + escape hotkey).
        var runtime = provider.GetRequiredService<ShellRuntime>();
        _runtime = runtime;
        runtime.Start();
        var surfaces = provider.GetRequiredService<ISurfaceHost>();
        _surfaces = surfaces;
        surfaces.Start();
        var tray = provider.GetRequiredService<TrayIconHost>();
        _tray = tray;
        tray.Start();

        // 9. Second launches and --restore-taskbar talk to this instance through named events.
        instance.StartListening(
            openSettings: () => _dispatcher.BeginInvoke(() => SafeInvoke(() => provider.GetRequiredService<ISurfaceManager>().ShowSettingsWindow(null))),
            quit: () => shutdown.RequestExit(restart: false),
            log: _log,
            openSettingsName: HostObjectNames.OpenSettingsEvent,
            quitName: HostObjectNames.QuitEvent);

        if (_smoke) StartSmokeCheck();
        else
        {
            // 10. A run that lasts a minute is healthy: the crash counter resets (docs/IPC.md section 15, rule 5).
            var startup = _startup;
            _healthyTimer = TimeProvider.System.CreateTimer(_ => _dispatcher.BeginInvoke(() => SafeInvoke(() => startup?.MarkHealthy())), null,
                StartupStateStore.HealthyAfter, Timeout.InfiniteTimeSpan);
        }
        _log.LogInformation("Shell started (protocol v{Protocol}, safe mode: {Safe}, smoke: {Smoke})", ProtocolInfo.Version, options.SafeMode, _smoke);
        return null;
    }

    /// <summary>Windows is ending the session: restore synchronously (Windows terminates the process right after this returns).</summary>
    public void OnSessionEnding()
    {
        _log.LogInformation("Session ending");
        _shutdown?.RunNow(TimeSpan.FromSeconds(4));
    }

    /// <summary>An unexpected exception on the UI thread: restore the shell and exit (the shell must never stay half-changed).</summary>
    public void OnFatal(Exception ex)
    {
        _log.LogCritical(ex, "Unhandled exception; restoring the shell and exiting");
        if (!_smoke)
            NativeHelpers.ShowMessageBox("BetterWindows", $"BetterWindows hit an unexpected error and is closing.\n\n{ex.GetType().Name}: {ex.Message}\n\nLog folder:\n{_paths.LogsDir}", false, true);
        var shutdown = _shutdown;
        if (shutdown is null) { RestoreStaticShell(); return; }
        shutdown.RequestExit(restart: false, exitCode: ExitCodes.Failed);
    }

    /// <summary>The Host's own steps of the ordered shutdown (docs/IPC.md section 13, rule 7). Built lazily, so the list reflects what exists at shutdown time.</summary>
    private IReadOnlyList<ShutdownStep> BuildShutdownSteps(bool restart)
    {
        // Captured when the plan is built (at shutdown time): the steps then use exactly the objects that exist, not whatever the fields hold later.
        var surfaces = _surfaces;
        var runtime = _runtime;
        var provider = _provider;
        var tray = _tray;
        var bridge = _bridge;
        var startup = _startup;
        var instance = _instance;
        var shutdown = _shutdown;
        return ShutdownPlan.Build(new ShutdownActions(
            StopSurfaces: () => surfaces?.Stop(),
            StopControllers: () => runtime?.Stop(),
            RestoreTaskbar: () => provider?.GetService<ITaskbarController>()?.Restore(),
            RestoreDesktopIcons: () => provider?.GetService<IDesktopIconsController>()?.Restore(),
            DisposeTray: () => tray?.Dispose(),
            DisposeWebView: () => bridge?.Dispose(),
            MarkCleanExit: () => startup?.MarkCleanExit(),
            ReleaseInstance: () => instance?.Dispose(),
            Relaunch: Relaunch,
            ExitApplication: () => _exitApp(shutdown?.ExitCode ?? ExitCodes.Ok)), relaunch: restart && !_smoke);
    }

    /// <summary>Starts a fresh process after the mutex was released (this instance has already disposed it).</summary>
    private void Relaunch()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        if (_cli.DataDir is { Length: > 0 } dir) { psi.ArgumentList.Add("--data-dir"); psi.ArgumentList.Add(dir); }
        if (_cli.UiOrigin is { Length: > 0 } origin) { psi.ArgumentList.Add("--ui-url"); psi.ArgumentList.Add(origin); }
        using var p = Process.Start(psi);
        _log.LogInformation("Relaunched as process {Pid}", p?.Id ?? 0);
    }

    /// <summary>Last resort (watchdog, unhandled crash without a shutdown object): restore with the static restorers and terminate.</summary>
    private void ForceRestoreAndExit()
    {
        RestoreStaticShell();
        var code = _shutdown?.ExitCode ?? ExitCodes.Ok;
        Environment.Exit(code != ExitCodes.Ok ? code : ExitCodes.Failed);
    }

    /// <summary>Restores the taskbar and desktop icons with the static restorers (idempotent; safe before and after the shell started).</summary>
    public void RestoreShellNow() => RestoreStaticShell();

    private void RestoreStaticShell()
    {
        try { TaskbarRestorer.RestoreNow(); } catch (Exception ex) { _log.LogError(ex, "Static taskbar restore failed"); }
        try { DesktopIconsRestorer.RestoreNow(); } catch (Exception ex) { _log.LogError(ex, "Static desktop icons restore failed"); }
    }

    private void OnSurfaceFailed(SurfaceFailure failure)
    {
        if (failure.Kind != SurfaceFailureKind.RuntimeMissing) return;
        _log.LogError("WebView2 runtime missing while attaching {Surface}; exiting", failure.Surface);
        NativeHelpers.ShowMessageBox("BetterWindows", "BetterWindows needs the Microsoft Edge WebView2 Runtime.\n\n" + WebView2RuntimeUrl, false, true);
        _shutdown?.RequestExit(restart: false, exitCode: ExitCodes.RuntimeMissing);
    }

    private void WaitForTaskbar(TimeSpan limit)
    {
        var sw = Stopwatch.StartNew();
        while (NativeHelpers.FindTrayWindow() == 0 && sw.Elapsed < limit) Thread.Sleep(250);
        _log.LogInformation("Taskbar window {State} after {Ms} ms", NativeHelpers.FindTrayWindow() != 0 ? "found" : "NOT found", sw.ElapsedMilliseconds);
    }

    /// <summary>Smoke: succeed when the dock and the menu bar are ready and the dock received its first state. Writes a short report and exits.</summary>
    private void StartSmokeCheck()
    {
        _smokeStarted = TimeProvider.System.GetUtcNow();
        _smokeTimer = TimeProvider.System.CreateTimer(_ => _dispatcher.BeginInvoke(() => SafeInvoke(PollSmoke)), null, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250));
    }

    private void PollSmoke()
    {
        var bridge = _bridge;
        var shutdown = _shutdown;
        if (bridge is null || shutdown is null || shutdown.IsShuttingDown) return;
        var surfaces = bridge.DescribeSurfaces();
        var dock = surfaces.FirstOrDefault(s => s.Surface.Kind == SurfaceKind.Dock);
        var bar = surfaces.FirstOrDefault(s => s.Surface.Kind == SurfaceKind.Menubar);
        var dockOk = dock is { Ready: true } && bridge.DeliveredEvents(dock.Surface, "dock.state") > 0;
        var barOk = bar is { Ready: true };
        var elapsed = TimeProvider.System.GetUtcNow() - _smokeStarted;
        if (!dockOk || !barOk)
        {
            if (elapsed < SmokeTimeout) return;
        }
        var lines = new List<string>
        {
            $"smoke: {(dockOk && barOk ? "PASS" : "FAIL")} after {elapsed.TotalSeconds:0.0} s",
            $"dock ready: {dockOk}, menubar ready: {barOk}",
        };
        lines.AddRange(surfaces.Select(s => $"{s.Surface}: healthy={s.Healthy} ready={s.Ready} delivered={s.Delivered} pending={s.Pending}"));
        lines.AddRange(bridge.RecentFailures.Select(f => $"failure: {f.Surface} {f.Kind} {f.Detail}"));
        CliCommands.WriteOutput(string.Join(Environment.NewLine, lines), _cli.OutputFile);
        _log.LogInformation("Smoke check {Result}", dockOk && barOk ? "passed" : "failed");
        shutdown.RequestExit(restart: false, exitCode: dockOk && barOk ? ExitCodes.Ok : ExitCodes.SmokeFailed);
    }

    /// <summary>Static, WebView2-free check used by --diagnose and startup. Null when the Evergreen runtime is not installed.</summary>
    public static string? TryGetWebView2Version()
    {
        try { return CoreWebView2Environment.GetAvailableBrowserVersionString(); }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or DllNotFoundException or InvalidOperationException or ArgumentException) { return null; }
    }

    private void SafeInvoke(Action action)
    {
        try { action(); }
        catch (Exception ex) { _log.LogError(ex, "Startup callback failed"); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _healthyTimer?.Dispose();
        _smokeTimer?.Dispose();
        _instance?.Dispose();
        _provider?.Dispose();
    }
}
