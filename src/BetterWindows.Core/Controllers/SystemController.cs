using BetterWindows.Core.Diagnostics;
using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using Microsoft.Extensions.Logging;

namespace BetterWindows.Core.Controllers;

/// <summary>
/// Power, open (http(s) and ms-settings only), quit, about, monitors, the native file picker (every picked path is granted through <see cref="IPathGrants"/>), the
/// taskbar mode and autostart from settings, and diag.report (strictly read-only). The taskbar is hidden only while the dock and the menu bar are running AND an escape
/// hotkey is registered (docs/IPC.md section 15); otherwise it stays visible and diag.report says why.
/// </summary>
public sealed class SystemController : IShellController
{
    private const int MaxPickedPaths = 256, MaxPickTitleChars = 200;

    private readonly IMessageRouter _router;
    private readonly ISystemPower _power;
    private readonly IAppLauncher _launcher;
    private readonly IAppLifecycle _app;
    private readonly IMonitorProvider _monitors;
    private readonly IFilePicker _picker;
    private readonly IPathGrants _grants;
    private readonly ISettingsService _settings;
    private readonly ITaskbarController _taskbar;
    private readonly IHotkeyStatus _hotkeys;
    private readonly SurfaceRegistry _registry;
    private readonly IAutostartService _autostart;
    private readonly IAppPaths _paths;
    private readonly ILogger _log;

    // Taskbar state. Every read-modify-apply runs under _taskbarGate; the reconcile reads the CURRENT settings/hotkeys/surfaces, so concurrent triggers converge.
    private readonly object _taskbarGate = new();
    private TaskbarMode? _override;          // temporary system.showTaskbar request; cleared by the next settings change
    private TaskbarMode? _applied;           // what the port was last told (null = never)
    private TaskbarMode _requested = TaskbarMode.Visible;
    private string _reason = "not evaluated yet";
    private bool _started, _stopped; // _stopped is read under _taskbarGate: a reconcile queued before Stop must never hide the taskbar after the host restored it

    public SystemController(
        IMessageRouter router, ISystemPower power, IAppLauncher launcher, IAppLifecycle app, IMonitorProvider monitors, IFilePicker picker, IPathGrants grants,
        ISettingsService settings, ITaskbarController taskbar, IHotkeyStatus hotkeys, SurfaceRegistry registry, IAutostartService autostart, IAppPaths paths,
        ILogger<SystemController> log)
    {
        _router = router;
        _power = power;
        _launcher = launcher;
        _app = app;
        _monitors = monitors;
        _picker = picker;
        _grants = grants;
        _settings = settings;
        _taskbar = taskbar;
        _hotkeys = hotkeys;
        _registry = registry;
        _autostart = autostart;
        _paths = paths;
        _log = log;
    }

    public string Name => "SystemController";

    public void Start()
    {
        if (_started) return;
        _started = true;
        lock (_taskbarGate) _stopped = false;

        _router.Handle<PowerParams>(Msg.SystemPower, (_, p) => { _power.Perform(p.Action); return Task.CompletedTask; });
        _router.Handle<OpenParams>(Msg.SystemOpen, (_, p) =>
        {
            var target = ValidateOpenTarget(p.Target);
            if (!_launcher.Open(target)) throw ProtocolException.Failed("the address could not be opened");
            return Task.CompletedTask;
        });
        _router.HandleNoParams(Msg.SystemQuit, _ => { _app.Exit(); return Task.CompletedTask; });
        _router.Handle<ShowTaskbarParams>(Msg.SystemShowTaskbar, (_, p) => { ShowTaskbar(p.Show); return Task.CompletedTask; });
        _router.HandleNoParams<AboutInfo>(Msg.SystemAbout, _ => Task.FromResult(new AboutInfo
        {
            Version = _app.Version,
            Build = _app.BuildInfo,
            Os = _app.OsDescription,
            WebView2 = _app.WebView2Version,
        }));
        _router.HandleNoParams<MonitorList>(Msg.SystemMonitors, _ => Task.FromResult(new MonitorList { Monitors = _monitors.GetMonitors() }));
        _router.Handle<PickPathParams, PickPathResult>(Msg.SystemPickPath, PickPathAsync);
        _router.HandleNoParams<DiagReport>(Msg.DiagReport, _ => Task.FromResult(Diagnose()));

        _settings.Changed += OnSettingsChanged;
        _registry.SurfaceReady += OnSurfaceChanged;
        _registry.SurfaceClosed += OnSurfaceChanged;
        _hotkeys.Changed += OnHotkeysChanged;

        // Initial application happens off the caller's thread: both are port calls (registry, taskbar COM/AppBar, autostart registry value).
        ScheduleTaskbar();
        ScheduleAutostart();
    }

    public void Stop()
    {
        if (!_started) return;
        _started = false;
        lock (_taskbarGate) _stopped = true;
        _settings.Changed -= OnSettingsChanged;
        _registry.SurfaceReady -= OnSurfaceChanged;
        _registry.SurfaceClosed -= OnSurfaceChanged;
        _hotkeys.Changed -= OnHotkeysChanged;
    }

    // ---- calls -----------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Only http(s) URLs (IsSafeWebUrl) and ms-settings: URIs. Anything else is refused before it reaches the shell.</summary>
    internal static string ValidateOpenTarget(string? target)
    {
        if (InputPolicy.IsSafeWebUrl(target, out var uri)) return uri.AbsoluteUri;
        if (target is not null && InputPolicy.IsMsSettingsUri(target)) return target;
        throw ProtocolException.BadRequest("system.open accepts only http(s) URLs and ms-settings: URIs");
    }

    private async Task<PickPathResult> PickPathAsync(CallContext ctx, PickPathParams p)
    {
        if (ctx.Surface.Kind != SurfaceKind.Settings) throw ProtocolException.BadRequest("system.pickPath is only available to the settings window");
        var request = p.Title is null ? p : p with { Title = InputPolicy.Truncate(p.Title, MaxPickTitleChars) };
        var picked = await _picker.PickAsync(request, ctx.Surface, ctx.Cancellation).ConfigureAwait(false);

        // Only local drive paths survive: a picked UNC path is dropped (IPathGrants and InputPolicy refuse UNC). Each survivor is granted for this session.
        var paths = new List<string>();
        foreach (var raw in picked.Take(MaxPickedPaths))
        {
            if (!InputPolicy.TryNormalizeLocalPath(raw, out var path))
            {
                _log.LogWarning("system.pickPath returned a path that is not a local drive path; dropped");
                continue;
            }
            _grants.Grant(path);
            paths.Add(path);
        }
        if (!p.Multiple && paths.Count > 1) paths.RemoveRange(1, paths.Count - 1);
        return new PickPathResult { Paths = paths };
    }

    /// <summary>system.showTaskbar: a temporary override (until the next settings change). Hiding still requires the escape hatch.</summary>
    internal void ShowTaskbar(bool show)
    {
        lock (_taskbarGate)
        {
            if (!show)
            {
                var decision = TaskbarSafety.Decide(TaskbarMode.Hidden, IsReady(SurfaceKind.Dock), IsReady(SurfaceKind.Menubar), _hotkeys.EscapeHotkeyRegistered);
                if (decision.Mode != TaskbarMode.Hidden) throw ProtocolException.Unsupported(decision.Reason);
            }
            _override = show ? TaskbarMode.Visible : TaskbarMode.Hidden;
            ApplyTaskbarLocked();
        }
    }

    private DiagReport Diagnose()
    {
        var store = _settings is SettingsService concrete ? concrete.Status : null;
        var settings = _settings.Current;
        var features = settings.General.Features;
        var enabled = new List<string>();
        if (features.InternalVirtualDesktops) enabled.Add("internalVirtualDesktops");
        if (features.TrayHosting) enabled.Add("trayHosting");
        if (features.WindowChrome) enabled.Add("windowChrome");
        if (features.GlobalMenu) enabled.Add("globalMenu");
        if (features.LiveThumbnails) enabled.Add("liveThumbnails");

        (TaskbarMode requested, TaskbarMode applied, string reason) taskbar;
        lock (_taskbarGate) taskbar = (_requested, _applied ?? TaskbarMode.Visible, _reason);

        var inputs = new DiagInputs(
            Version: _app.Version,
            Os: _app.OsDescription,
            WebView2Version: _app.WebView2Version,
            ConfigFile: store?.ConfigFile ?? _paths.ConfigFile,
            ConfigFileExists: store?.FileExists ?? File.Exists(_paths.ConfigFile),
            PersistenceBlocked: store?.PersistenceBlocked ?? false,
            PersistenceBlockedReason: store?.BlockedReason,
            LastLoad: store?.LastLoad ?? "n/a",
            DataRoot: _paths.Root,
            DataRootExists: Directory.Exists(_paths.Root),
            Hotkeys: _hotkeys.Items,
            EscapeHotkeyRegistered: _hotkeys.EscapeHotkeyRegistered,
            RequestedTaskbar: taskbar.requested,
            AppliedTaskbar: taskbar.applied,
            TaskbarReason: taskbar.reason,
            AutostartWanted: settings.General.Autostart,
            AutostartEnabled: ReadAutostart(),
            MonitorCount: _monitors.GetMonitors().Count,
            DockReady: IsReady(SurfaceKind.Dock),
            MenubarReady: IsReady(SurfaceKind.Menubar),
            EnabledFeatures: enabled);
        return DiagnosticsReport.Build(inputs);
    }

    // ---- taskbar and autostart --------------------------------------------------------------------------------------------------------------------------

    private bool IsReady(SurfaceKind kind) => _registry.ReadyChannels().Any(c => c.Id.Kind == kind);

    private void OnSettingsChanged(Protocol.Settings _)
    {
        lock (_taskbarGate) _override = null; // a settings change ends a temporary showTaskbar request
        ScheduleTaskbar();
        ScheduleAutostart();
    }

    private void OnSurfaceChanged(SurfaceId _) => ScheduleTaskbar();

    private void OnHotkeysChanged() => ScheduleTaskbar();

    /// <summary>Port work is never done on the event's thread: the subscribers only queue a reconcile.</summary>
    private void ScheduleTaskbar() => _ = Task.Run(ReconcileTaskbar);

    private void ScheduleAutostart() => _ = Task.Run(ReconcileAutostart);

    /// <summary>Applies the taskbar mode that is currently allowed. Never throws (logged); safe to call repeatedly.</summary>
    internal void ReconcileTaskbar()
    {
        lock (_taskbarGate)
        {
            try { ApplyTaskbarLocked(); }
            catch (Exception ex) { _log.LogError(ex, "Applying the taskbar mode failed; it will be retried on the next change"); }
        }
    }

    /// <summary>Caller holds <see cref="_taskbarGate"/>.</summary>
    private void ApplyTaskbarLocked()
    {
        if (_stopped) return; // Stop has run: the host owns the taskbar restore now
        var requested = _override ?? _settings.Current.Taskbar.Mode;
        var decision = TaskbarSafety.Decide(requested, IsReady(SurfaceKind.Dock), IsReady(SurfaceKind.Menubar), _hotkeys.EscapeHotkeyRegistered);
        _requested = requested;
        _reason = decision.Reason;
        if (_applied == decision.Mode) return;
        _taskbar.Apply(decision.Mode);
        _applied = decision.Mode;
        _log.LogInformation("Taskbar mode is now {Mode}: {Reason}", decision.Mode, decision.Reason);
    }

    internal void ReconcileAutostart()
    {
        if (!_started) return;
        try
        {
            var wanted = _settings.Current.General.Autostart;
            if (_autostart.IsEnabled != wanted) _autostart.SetEnabled(wanted);
        }
        catch (Exception ex) { _log.LogError(ex, "Applying the autostart setting failed"); }
    }

    private bool ReadAutostart()
    {
        try { return _autostart.IsEnabled; }
        catch (Exception ex) { _log.LogDebug(ex, "Reading the autostart entry failed"); return false; }
    }
}
