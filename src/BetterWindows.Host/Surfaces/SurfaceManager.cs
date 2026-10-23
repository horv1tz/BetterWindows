using System.Globalization;
using System.IO;
using System.Text;
using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Host.Bridge;
using BetterWindows.Host.Interop;
using Microsoft.Extensions.Logging;

namespace BetterWindows.Host.Surfaces;

/// <summary>
/// Owns every native surface window of the shell and implements the Core-facing <see cref="ISurfaceManager"/> plus the lifecycle-facing <see cref="ISurfaceHost"/>.
/// Threading: every window and every piece of mutable state is touched ONLY on the WPF dispatcher. The ports may be called from any thread: they are marshalled with
/// BeginInvoke (never blocking the caller); the getters read a locked snapshot. Event subscribers that arrive on OS threads only marshal (docs/IPC.md section 13).
///
/// What it does (docs/IPC.md sections 3, 5, 14-15; docs/ARCHITECTURE.md section 3):
///  - reconciles the windows with the desired set (<see cref="SurfacePlanner"/>) on every settings or monitor change; kept windows are re-placed, never re-created;
///  - dock and menu bar: AppBar reservation (only without auto-hide), glass backdrop + region from surface.shape, auto-hide through the single cursor poll;
///  - overlay: created hidden at startup; ShowOverlay queues until its page is ready (latest wins, 3 s), translates popover anchors, publishes overlay.show/hide, and
///    dismisses itself on deactivation (with a grace period so a denied foreground change does not close Spotlight at once); OverlayToken increments per show;
///  - automatic fullscreen suppression per monitor (reason "fullscreen:&lt;monitorId&gt;") and Core reasons, both through <see cref="VisibilityRegistry"/>;
///  - desktop layer per monitor, re-attached when the shell restarts; settings window: single instance, placement remembered, navigate on request.
/// </summary>
public sealed class SurfaceManager : ISurfaceManager, ISurfaceHost, IDisposable
{
    private static readonly SurfaceId OverlayId = new(SurfaceKind.Overlay);
    private static readonly SurfaceId SettingsId = new(SurfaceKind.Settings);

    private readonly IEventHub _hub;
    private readonly SurfaceRegistry _registry;
    private readonly ISettingsService _settings;
    private readonly IThemeService _theme;
    private readonly IMonitorProvider _monitors;
    private readonly IWindowSystem _windowSystem;
    private readonly ICursorProvider _cursor;
    private readonly IAppBarFactory _appBars;
    private readonly IDesktopIconsController _desktopIcons;
    private readonly IWebViewBridge _bridge;
    private readonly IAppPaths _paths;
    private readonly TimeProvider _clock;
    private readonly ILogger<SurfaceManager> _log;

    // ---- UI-thread state (touched only on the dispatcher) ----
    private readonly Dictionary<SurfaceId, SurfaceWindowBase> _windows = new();
    private readonly Dictionary<SurfaceId, bool> _visibleState = new();
    private readonly VisibilityRegistry _visibility = new();
    private readonly HashSet<string> _fullscreenMonitors = new(StringComparer.Ordinal);
    private readonly HashSet<SurfaceId> _autoHideRegistered = new();
    private readonly PendingOverlayRequest _pendingOverlay = new();
    private OverlayWindow? _overlay;
    private SettingsWindow? _settingsWindow;
    private bool _hasSettingsPage;
    private string? _settingsPage;
    private AutoHideCoordinator? _autoHide;
    private ITimer? _deactivationTimer;
    private int _retries;
    private TimeSpan _shownAt;
    private readonly long _t0Stamp;
    private System.Windows.Threading.Dispatcher? _dispatcher;
    private bool _started;
    private bool _stopped;

    // ---- cross-thread state (locked snapshot) ----
    private readonly object _snapshotLock = new();
    private bool _overlayVisible;
    private OverlayMode? _overlayMode;
    private int _overlayToken;

    // Fullscreen watcher is created/stopped on the UI thread but read by the AppBar/WindowSystem callbacks: volatile reference.
    private volatile FullscreenWatcher? _fullscreen;

    private readonly List<Action> _unsubscribe = new();
    private Action? _overlayHiddenHandlers; // guarded by _snapshotLock

    public SurfaceManager(IEventHub hub, SurfaceRegistry registry, ISettingsService settings, IThemeService theme, IMonitorProvider monitors,
        IWindowSystem windowSystem, ICursorProvider cursor, IAppBarFactory appBars, IDesktopIconsController desktopIcons, IWebViewBridge bridge,
        IAppPaths paths, TimeProvider time, ILogger<SurfaceManager> log)
    {
        _hub = hub;
        _registry = registry;
        _settings = settings;
        _theme = theme;
        _monitors = monitors;
        _windowSystem = windowSystem;
        _cursor = cursor;
        _appBars = appBars;
        _desktopIcons = desktopIcons;
        _bridge = bridge;
        _paths = paths;
        _clock = time;
        _log = log;
        _t0Stamp = time.GetTimestamp();
    }

    /// <summary>The WPF dispatcher the surfaces live on (null before <see cref="Start"/>). Used by <see cref="FilePicker"/>.</summary>
    internal System.Windows.Threading.Dispatcher? UiDispatcher => _dispatcher;

    /// <summary>The native window of a surface on the UI thread (null when there is none). Used as the owner of native dialogs.</summary>
    internal SurfaceWindowBase? WindowOf(SurfaceId id)
    {
        if (id.Kind == SurfaceKind.Overlay) return _overlay;
        if (id.Kind == SurfaceKind.Settings) return _settingsWindow;
        return _windows.TryGetValue(id, out var w) ? w : null;
    }

    // =====================================================================================================================
    // ISurfaceHost
    // =====================================================================================================================

    /// <summary>Creates the overlay window and every monitor-bound surface. Call on the UI thread (the WPF dispatcher is captured here).</summary>
    public void Start()
    {
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;
        RunOnUi(StartOnUi);
    }

    /// <summary>
    /// Releases every AppBar reservation, detaches the desktop layers, destroys the windows and stops the watchers. Blocks at most a few seconds when called off the UI
    /// thread (the shutdown order requires the work area to be released before the taskbar is restored). Idempotent.
    /// </summary>
    public void Stop()
    {
        var d = _dispatcher;
        if (d is null) return;
        if (d.CheckAccess()) { StopOnUi(); return; }
        try { d.InvokeAsync(StopOnUi).Task.Wait(TimeSpan.FromSeconds(5)); }
        catch (Exception ex) { _log.LogWarning(ex, "Stopping the surfaces did not complete on the UI thread"); }
    }

    public void Dispose() => Stop();

    // =====================================================================================================================
    // ISurfaceManager
    // =====================================================================================================================

    public void ResizeSurface(SurfaceId surface, double widthDip, double heightDip, double? barThicknessDip) => RunOnUi(() =>
    {
        if (_stopped || !_windows.TryGetValue(surface, out var w) || w is not DockWindow d) return;
        d.SetRequestedSize(widthDip, heightDip, barThicknessDip);
        d.Reposition();
        UpdateDockZones(d);
    });

    public void ShowOverlay(OverlayRequest request, SurfaceId? source) => RunOnUi(() =>
    {
        if (_stopped || _overlay is null) return;
        if (!_overlay.PageReady)
        {
            // The page is not ready yet: keep the latest request (it is shown as soon as surface.ready arrives, or dropped after 3 s).
            _pendingOverlay.Enqueue(request, source, Elapsed());
            return;
        }
        ShowOverlayNow(request, source);
    });

    public void HideOverlay() => RunOnUi(() => HideOverlayCore(raiseHidden: false));

    public bool IsOverlayVisible { get { lock (_snapshotLock) return _overlayVisible; } }

    public OverlayMode? CurrentOverlayMode { get { lock (_snapshotLock) return _overlayMode; } }

    public int OverlayToken { get { lock (_snapshotLock) return _overlayToken; } }

    /// <summary>The overlay has no backdrop region; the blur is a whole-window backdrop only on the bars, so hitRects cannot stay un-blurred.</summary>
    public bool SupportsGlassOverflow => false;

    /// <summary>Raised on the UI thread when the HOST dismisses the overlay (deactivation, monitor change, Alt+F4). Core-initiated hides do not raise it.</summary>
    public event Action? OverlayHidden
    {
        add { lock (_snapshotLock) _overlayHiddenHandlers += value; }
        remove { lock (_snapshotLock) _overlayHiddenHandlers -= value; }
    }

    public void SetDockRevealed(SurfaceId dock, bool revealed) => RunOnUi(() => { if (!_stopped) _autoHide?.SetForced(dock, revealed); });

    public void SetSurfaceShape(SurfaceId surface, SurfaceShapeParams shape) => RunOnUi(() =>
    {
        if (_stopped || !_windows.TryGetValue(surface, out var w) || w is not (DockWindow or MenuBarWindow)) return;
        var theme = _theme.Current;
        // The first shape makes the bar presentable: the region was empty until now (no white flash, nothing clickable before the page is drawn).
        w.ContentHidden = false;
        w.ApplyGlass(shape.Blur && !theme.ReduceTransparency, theme.Mode == ThemeMode.Dark);
        w.SetShape(shape);
    });

    public void ShowSettingsWindow(string? page) => RunOnUi(() =>
    {
        if (_stopped || _dispatcher is null) return;
        var normalized = SettingsWindowTitle.NormalizePage(page);
        if (_settingsWindow is { IsShutdown: false } open)
        {
            open.Bring();
            if (normalized is not null) NavigateSettings(open, normalized);
            return;
        }
        OpenSettingsWindow(normalized);
    });

    public void SetSurfacesVisible(SurfaceKind? kind, string? monitorId, bool visible, string reason) => RunOnUi(() =>
    {
        if (_stopped) return;
        if (_visibility.Set(kind, monitorId, visible, reason)) ApplyVisibilityAll();
    });

    // =====================================================================================================================
    // Start / stop
    // =====================================================================================================================

    private void StartOnUi()
    {
        if (_started || _stopped) return;
        _started = true;
        var monitors = _monitors.GetMonitors();
        var primary = MonitorGeometry.Primary(monitors) ?? new MonitorInfo();

        _autoHide = new AutoHideCoordinator(_cursor, _clock, OnAutoHideTransition, () => IsOverlayVisible, _log);

        _overlay = new OverlayWindow(OverlayId, primary, _clock, _log);
        _overlay.CreateNativeWindow();
        _overlay.ActivationChanged += OnOverlayActivationChanged;
        _overlay.CloseRequested += _ => RunOnUi(() => HideOverlayCore(raiseHidden: true));
        _ = _overlay.AttachAsync(_bridge);

        Subscribe(() => _settings.Changed += OnSettingsChanged, () => _settings.Changed -= OnSettingsChanged);
        Subscribe(() => _theme.Changed += OnThemeChanged, () => _theme.Changed -= OnThemeChanged);
        Subscribe(() => _monitors.MonitorsChanged += OnMonitorsChanged, () => _monitors.MonitorsChanged -= OnMonitorsChanged);
        Subscribe(() => _windowSystem.FullscreenChanged += OnFullscreenSignal, () => _windowSystem.FullscreenChanged -= OnFullscreenSignal);
        Subscribe(() => _registry.SurfaceReady += OnSurfaceReady, () => _registry.SurfaceReady -= OnSurfaceReady);
        Subscribe(() => _desktopIcons.ShellRestarted += OnShellRestarted, () => _desktopIcons.ShellRestarted -= OnShellRestarted);
        Subscribe(() => _bridge.Failed += OnBridgeFailed, () => _bridge.Failed -= OnBridgeFailed);

        Reconcile();
    }

    private void StopOnUi()
    {
        if (_stopped) return;
        _stopped = true;
        foreach (var undo in _unsubscribe) { try { undo(); } catch (Exception) { } }
        _unsubscribe.Clear();

        var fullscreen = _fullscreen;
        _fullscreen = null;
        fullscreen?.Dispose();
        _autoHide?.Dispose();
        _autoHide = null;
        try { _deactivationTimer?.Dispose(); } catch (Exception) { }
        _deactivationTimer = null;
        _pendingOverlay.Clear();
        _autoHideRegistered.Clear();

        foreach (var w in _windows.Values)
        {
            try { w.Shutdown(_bridge); }
            catch (Exception ex) { _log.LogWarning(ex, "{Surface}: shutdown failed", w.SurfaceId); }
        }
        _windows.Clear();
        _visibleState.Clear();

        if (_settingsWindow is { } sw)
        {
            SaveSettingsState(sw);
            try { sw.Shutdown(_bridge); } catch (Exception) { }
            _settingsWindow = null;
        }
        if (_overlay is { } ov)
        {
            try { ov.Shutdown(_bridge); } catch (Exception) { }
            _overlay = null;
        }
        lock (_snapshotLock) { _overlayVisible = false; _overlayMode = null; }
    }

    private void Subscribe(Action subscribe, Action unsubscribe)
    {
        try { subscribe(); _unsubscribe.Add(unsubscribe); }
        catch (Exception ex) { _log.LogError(ex, "Subscribing to a surface dependency failed"); }
    }

    // =====================================================================================================================
    // Reconciliation: settings and monitors -> windows
    // =====================================================================================================================

    private void Reconcile()
    {
        if (_stopped || _dispatcher is null) return;
        var monitors = _monitors.GetMonitors();
        if (monitors.Count == 0)
        {
            // A transient state (RDP, lock screen, sleep): keep the current windows rather than tearing everything down.
            _log.LogDebug("No monitors reported; keeping the current surfaces");
            return;
        }
        var settings = _settings.Current;
        var plan = SurfacePlanner.Plan(settings, monitors);
        var diff = SurfacePlanner.Diff(_windows.Keys.ToArray(), plan);

        foreach (var id in diff.ToRemove) RemoveWindow(id);
        foreach (var p in diff.ToKeep) UpdateWindow(p, settings);
        foreach (var p in diff.ToCreate) CreateWindow(p, settings);

        // A monitor that disappeared takes its overlay with it (the overlay is per monitor).
        if (IsOverlayVisible && _overlay is { } ov && monitors.All(m => m.Id != ov.Monitor.Id))
            HideOverlayCore(raiseHidden: true);

        UpdateWatchers();
        ApplyVisibilityAll();
    }

    private void CreateWindow(PlannedSurface p, Settings settings)
    {
        SurfaceWindowBase window;
        switch (p.Kind)
        {
            case SurfaceKind.Dock:
            {
                var d = new DockWindow(p.Id, p.Route, p.Monitor, _clock, _log, _appBars);
                d.CreateNativeWindow();
                ConfigureDock(d, settings);
                d.Reposition();
                d.AppBarNotified += OnDockAppBar;
                d.ScaleChanged += _ => PostToUi(Reconcile);
                window = d;
                break;
            }
            case SurfaceKind.Menubar:
            {
                var m = new MenuBarWindow(p.Id, p.Route, p.Monitor, _clock, _log, _appBars);
                m.CreateNativeWindow();
                m.AutoHide = settings.MenuBar.AutoHide;
                m.Reposition();
                m.AppBarNotified += OnMenuBarAppBar;
                m.ScaleChanged += _ => PostToUi(Reconcile);
                EnsureMenuBarAutoHide(m);
                window = m;
                break;
            }
            case SurfaceKind.Desktop:
            {
                var w = new DesktopWindow(p.Id, p.Route, p.Monitor, _clock, _log, _desktopIcons);
                w.CreateNativeWindow();
                w.Place(force: true);
                w.ScaleChanged += _ => PostToUi(Reconcile);
                window = w;
                break;
            }
            default:
                _log.LogWarning("Unexpected planned surface kind {Kind}", p.Kind);
                return;
        }
        _windows[p.Id] = window;
        _visibleState[p.Id] = true;
        window.ShowNoActivate();
        _ = window.AttachAsync(_bridge);
        if (window is DockWindow dock) EnsureDockAutoHide(dock, settings);
    }

    private void UpdateWindow(PlannedSurface p, Settings settings)
    {
        if (!_windows.TryGetValue(p.Id, out var window)) return;
        window.Monitor = p.Monitor;
        switch (window)
        {
            case DockWindow d:
                ConfigureDock(d, settings);
                d.Reposition();
                EnsureDockAutoHide(d, settings);
                break;
            case MenuBarWindow m:
                m.AutoHide = settings.MenuBar.AutoHide;
                m.Reposition();
                EnsureMenuBarAutoHide(m);
                break;
            case DesktopWindow w:
                w.Place();
                break;
        }
    }

    private void RemoveWindow(SurfaceId id)
    {
        if (!_windows.Remove(id, out var window)) return;
        _visibleState.Remove(id);
        if (_autoHideRegistered.Remove(id)) _autoHide?.Unregister(id);
        try { window.Shutdown(_bridge); }
        catch (Exception ex) { _log.LogWarning(ex, "{Surface}: removing the window failed", id); }
    }

    private static void ConfigureDock(DockWindow d, Settings settings)
    {
        d.Position = settings.Dock.Position;
        d.IconSize = Math.Clamp(settings.Dock.IconSize, 32, 128);
        d.ReserveSpace = settings.Dock.ReserveSpace;
        d.AutoHide = settings.Dock.AutoHide;
        // The native taskbar stays visible: the dock then stands on the work-area edge instead of covering the taskbar.
        d.UseWorkAreaEdge = settings.Taskbar.Mode == TaskbarMode.Visible;
    }

    private void EnsureDockAutoHide(DockWindow d, Settings settings)
    {
        if (_autoHide is null) return;
        if (settings.Dock.AutoHide)
        {
            if (_autoHideRegistered.Add(d.SurfaceId))
            {
                // Start collapsed; the coordinator reveals it when the pointer reaches the edge.
                _autoHide.Register(d.SurfaceId, initiallyRevealed: false);
                _hub.PublishTo(d.SurfaceId, Msg.DockReveal, new DockRevealState { Revealed = false });
            }
            UpdateDockZones(d);
        }
        else if (_autoHideRegistered.Remove(d.SurfaceId))
        {
            _autoHide.Unregister(d.SurfaceId);
            _hub.PublishTo(d.SurfaceId, Msg.DockReveal, new DockRevealState { Revealed = false });
        }
    }

    private void UpdateDockZones(DockWindow d)
    {
        if (_autoHide is null || !_autoHideRegistered.Contains(d.SurfaceId)) return;
        var bounds = MonitorGeometry.BoundsPx(d.Monitor);
        _autoHide.UpdateZones(d.SurfaceId, AutoHideZones.DockRevealZone(d.Position, bounds, d.RectPx), AutoHideZones.KeepZone(d.RectPx, bounds, d.Scale));
    }

    private void EnsureMenuBarAutoHide(MenuBarWindow m)
    {
        if (_autoHide is null) return;
        var bounds = MonitorGeometry.BoundsPx(m.Monitor);
        if (m.AutoHide)
        {
            if (_autoHideRegistered.Add(m.SurfaceId)) _autoHide.Register(m.SurfaceId, initiallyRevealed: m.IsRevealed);
            // The keep zone is the full-height bar at its shown position, independent of the slide offset.
            var bar = MenuBarPlacement.Compute(bounds, m.Scale);
            _autoHide.UpdateZones(m.SurfaceId, AutoHideZones.MenuBarRevealZone(bounds), AutoHideZones.MenuBarKeepZone(bar, bounds, m.Scale));
        }
        else if (_autoHideRegistered.Remove(m.SurfaceId))
        {
            _autoHide.Unregister(m.SurfaceId);
            m.SetRevealed(revealed: true, animate: false);
        }
    }

    private void OnAutoHideTransition(SurfaceId id, bool reveal) => RunOnUi(() =>
    {
        if (_stopped) return;
        if (id.Kind == SurfaceKind.Dock)
        {
            _hub.PublishTo(id, Msg.DockReveal, new DockRevealState { Revealed = reveal });
        }
        else if (_windows.TryGetValue(id, out var w) && w is MenuBarWindow m)
        {
            m.SetRevealed(reveal, animate: !_theme.Current.ReduceMotion);
        }
    });

    private void OnDockAppBar(DockWindow d, AppBarNotification n)
    {
        if (n is AppBarNotification.FullScreenAppEntered or AppBarNotification.FullScreenAppLeft) { _fullscreen?.RequestEvaluation(); return; }
        if (n == AppBarNotification.PosChanged) RunOnUi(() => { if (!_stopped) { d.Reposition(); UpdateDockZones(d); } });
    }

    private void OnMenuBarAppBar(MenuBarWindow m, AppBarNotification n)
    {
        if (n is AppBarNotification.FullScreenAppEntered or AppBarNotification.FullScreenAppLeft) { _fullscreen?.RequestEvaluation(); return; }
        if (n == AppBarNotification.PosChanged) RunOnUi(() => { if (!_stopped) m.Reposition(); });
    }

    // =====================================================================================================================
    // Visibility: reason-stacked hides (Core reasons + fullscreen suppression)
    // =====================================================================================================================

    private void UpdateWatchers()
    {
        var needsWatch = _windows.Keys.Any(k => k.Kind is SurfaceKind.Dock or SurfaceKind.Menubar);
        if (needsWatch && _fullscreen is null)
        {
            var watcher = new FullscreenWatcher(_windowSystem, _clock, () => _monitors.GetMonitors(), set => RunOnUi(() => OnFullscreenChanged(set)), _log);
            watcher.Start();
            _fullscreen = watcher;
        }
        else if (!needsWatch && _fullscreen is { } watcher)
        {
            _fullscreen = null;
            watcher.Dispose();
            // No bar left to suppress: lift the fullscreen entries (they name monitors that may have gone).
            if (FullscreenSuppression.Apply(_visibility, _fullscreenMonitors, Array.Empty<string>())) ApplyVisibilityAll();
        }
    }

    private void OnFullscreenSignal() => _fullscreen?.RequestEvaluation();

    private void OnFullscreenChanged(IReadOnlyCollection<string> fullscreenMonitors)
    {
        if (_stopped) return;
        if (FullscreenSuppression.Apply(_visibility, _fullscreenMonitors, fullscreenMonitors)) ApplyVisibilityAll();
    }

    private void ApplyVisibilityAll()
    {
        if (_stopped) return;
        foreach (var w in _windows.Values.ToArray())
        {
            var window = w;
            ApplyVisibility(window.SurfaceId, window.Monitor.Id, visible => ApplyWindowVisibility(window, visible));
        }
        if (_overlay is { } ov) ApplyVisibility(OverlayId, ov.Monitor.Id, visible => { if (!visible) HideOverlayCore(raiseHidden: false); });
        if (_settingsWindow is { IsShutdown: false } sw)
        {
            ApplyVisibility(SettingsId, null, visible => { if (visible) sw.Bring(); else sw.HideWindow(); });
        }
    }

    private void ApplyWindowVisibility(SurfaceWindowBase window, bool visible)
    {
        switch (window)
        {
            case DockWindow d:
                if (visible) d.Resume(); else d.Suspend();
                break;
            case MenuBarWindow m:
                if (visible) { m.ShowNoActivate(); m.Reposition(); } else m.HideWindow();
                break;
            case DesktopWindow dw:
                if (visible) { dw.ShowNoActivate(); dw.Place(); } else dw.HideWindow();
                break;
        }
    }

    private void ApplyVisibility(SurfaceId id, string? monitorId, Action<bool> apply)
    {
        var visible = _visibility.IsVisible(id.Kind, monitorId);
        var before = _visibleState.GetValueOrDefault(id, true);
        if (before == visible) return;
        _visibleState[id] = visible;
        apply(visible);
        var reasons = _visibility.Reasons(id.Kind, monitorId);
        _hub.PublishTo(id, Msg.SurfaceVisibility, new SurfaceVisibility
        {
            Visible = visible,
            Reason = visible || reasons.Count == 0 ? null : string.Join(",", reasons),
        });
    }

    // =====================================================================================================================
    // Overlay
    // =====================================================================================================================

    private void ShowOverlayNow(OverlayRequest request, SurfaceId? source)
    {
        if (_overlay is null) return;
        var monitors = _monitors.GetMonitors();
        var sourceWindow = source is { } s ? WindowOf(s) : null;
        var monitor = OverlayMonitorResolver.Resolve(monitors, request.MonitorId, sourceWindow?.Monitor.Id, CursorMonitorId(monitors));
        if (monitor is null) return;
        if (!_visibility.IsVisible(SurfaceKind.Overlay, monitor.Id))
        {
            _log.LogDebug("Overlay request ignored: a reason hides the overlay on {Monitor}", monitor.Id);
            return;
        }

        var popover = request.Popover;
        if (popover is not null && sourceWindow is { } src && WindowStyling.TryGetWindowRect(src.Hwnd, out var srcRect))
        {
            // Anchors are DIPs of the SOURCE window; the overlay wants DIPs of its own client area. Both pass through physical pixels (mixed DPI).
            var overlayBounds = MonitorGeometry.BoundsPx(monitor);
            var anchor = AnchorTranslator.Translate(popover.Anchor, new NativePoint(srcRect.Left, srcRect.Top), src.Scale,
                new NativePoint(overlayBounds.Left, overlayBounds.Top), MonitorGeometry.Scale(monitor));
            popover = popover with { Anchor = anchor };
        }

        _overlay.Present(monitor);
        int token;
        lock (_snapshotLock)
        {
            _overlayVisible = true;
            _overlayMode = request.Mode;
            token = ++_overlayToken;
        }
        _retries = 0;
        _shownAt = Elapsed();
        _hub.PublishTo(OverlayId, Msg.OverlayShow, new OverlayShow
        {
            Mode = request.Mode,
            Popover = popover,
            MonitorId = monitor.Id,
            Token = token,
        });
    }

    private void HideOverlayCore(bool raiseHidden)
    {
        _pendingOverlay.Clear();
        bool wasVisible;
        lock (_snapshotLock)
        {
            wasVisible = _overlayVisible;
            _overlayVisible = false;
            _overlayMode = null;
        }
        if (!wasVisible || _overlay is null) return;
        _overlay.Dismiss();
        _hub.PublishTo(OverlayId, Msg.OverlayHide);
        if (raiseHidden) RaiseOverlayHidden();
    }

    private void RaiseOverlayHidden()
    {
        Action? handlers;
        lock (_snapshotLock) handlers = _overlayHiddenHandlers;
        try { handlers?.Invoke(); }
        catch (Exception ex) { _log.LogError(ex, "OverlayHidden handler failed"); }
    }

    private void OnOverlayActivationChanged(SurfaceWindowBase _, bool active)
    {
        if (active || _stopped) return;
        // Debounce: focus moving to the WebView child reports a deactivation that is undone right away.
        _deactivationTimer ??= _clock.CreateTimer(_ => RunOnUi(EvaluateOverlayDeactivation), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _deactivationTimer.Change(OverlayActivationPolicy.Debounce, Timeout.InfiniteTimeSpan);
    }

    private void EvaluateOverlayDeactivation()
    {
        if (_stopped || _overlay is null || !IsOverlayVisible) return;
        var decision = OverlayActivationPolicy.Decide(_overlay.IsForeground, Elapsed() - _shownAt, _retries);
        switch (decision)
        {
            case DeactivationDecision.Keep:
                return;
            case DeactivationDecision.RetryActivation:
                _retries++;
                _overlay.Reactivate();
                return;
            default:
                HideOverlayCore(raiseHidden: true);
                return;
        }
    }

    private void OnSurfaceReady(SurfaceId id) => RunOnUi(() =>
    {
        if (_stopped) return;
        if (id.Kind == SurfaceKind.Overlay && _overlay is { } ov)
        {
            ov.PageReady = true;
            if (_pendingOverlay.TryTake(Elapsed(), out var request, out var source, out var expired)) ShowOverlayNow(request, source);
            else if (expired) _log.LogWarning("A queued overlay request expired before the overlay page was ready");
            return;
        }
        if (id.Kind == SurfaceKind.Settings && _settingsWindow is { } sw)
        {
            sw.PageReady = true;
            if (_hasSettingsPage) { _hasSettingsPage = false; NavigateSettings(sw, _settingsPage); }
            return;
        }
        if (_windows.TryGetValue(id, out var w))
        {
            w.PageReady = true;
            if (w is DesktopWindow) w.ContentHidden = false; // the desktop has no shape: it is presentable as soon as the page is
        }
    });

    private string? CursorMonitorId(IReadOnlyList<MonitorInfo> monitors)
    {
        if (!_cursor.TryGetCursorPosition(out var p)) return null;
        return _monitors.FromPoint(p.X, p.Y)?.Id;
    }

    // =====================================================================================================================
    // Settings window
    // =====================================================================================================================

    private void OpenSettingsWindow(string? page)
    {
        var monitors = _monitors.GetMonitors();
        if (monitors.Count == 0) return;
        var primary = MonitorGeometry.Primary(monitors)!;
        var layout = SettingsWindowPlacement.Resolve(LoadSettingsState(), monitors, primary);
        var target = MonitorContaining(monitors, layout.BoundsPx) ?? primary;
        var language = _settings.Current.General.Language;
        var title = SettingsWindowTitle.For(language, CultureInfo.CurrentUICulture.Name);
        var route = page is null ? "settings" : "settings/" + page;
        var window = new SettingsWindow(SettingsId, route, target, _clock, _log, title);
        window.CreateNativeWindow();
        window.ClosedByUser += OnSettingsClosedByUser;
        _settingsWindow = window;
        _hasSettingsPage = false;
        window.Open(layout);
        _ = window.AttachAsync(_bridge);
        _visibleState[SettingsId] = true;
    }

    private void OnSettingsClosedByUser(SurfaceWindowBase window) => RunOnUi(() =>
    {
        if (window is not SettingsWindow sw) return;
        SaveSettingsState(sw);
        if (ReferenceEquals(_settingsWindow, sw)) _settingsWindow = null;
        try { sw.Shutdown(_bridge); }
        catch (Exception ex) { _log.LogDebug(ex, "settings window shutdown after close failed"); }
    });

    private void NavigateSettings(SettingsWindow window, string? page)
    {
        if (page is null) return;
        if (window.PageReady) _hub.PublishTo(SettingsId, Msg.SettingsNavigate, new SettingsOpenParams { Page = page });
        else { _hasSettingsPage = true; _settingsPage = page; } // delivered when surface.ready arrives (events before ready are dropped)
    }

    private static MonitorInfo? MonitorContaining(IReadOnlyList<MonitorInfo> monitors, NativeRect rect)
    {
        var cx = rect.Left + rect.Width / 2;
        var cy = rect.Top + rect.Height / 2;
        foreach (var m in monitors)
            if (MonitorGeometry.BoundsPx(m).Contains(cx, cy)) return m;
        return null;
    }

    private string SettingsStatePath => Path.Combine(_paths.StateDir, SettingsWindowStateFile.FileName);

    private SettingsWindowState? LoadSettingsState()
    {
        try
        {
            var path = SettingsStatePath;
            return File.Exists(path) ? SettingsWindowStateFile.TryParse(File.ReadAllText(path, Encoding.UTF8)) : null;
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "reading the settings window placement failed; using the default");
            return null;
        }
    }

    private void SaveSettingsState(SettingsWindow window)
    {
        try
        {
            var state = window.CaptureState();
            if (state is null) return;
            Directory.CreateDirectory(_paths.StateDir);
            AtomicFile.WriteAllBytes(SettingsStatePath, Encoding.UTF8.GetBytes(SettingsWindowStateFile.Serialize(state)));
        }
        catch (Exception ex) { _log.LogWarning(ex, "saving the settings window placement failed"); }
    }

    // =====================================================================================================================
    // Shell events (all arrive on foreign threads: they only marshal)
    // =====================================================================================================================

    private void OnSettingsChanged(Settings _) => RunOnUi(Reconcile);

    private void OnMonitorsChanged() => RunOnUi(Reconcile);

    private void OnThemeChanged(ThemeState _) => RunOnUi(RefreshGlass);

    private void OnShellRestarted() => RunOnUi(() =>
    {
        if (_stopped) return;
        foreach (var w in _windows.Values)
        {
            if (w is DesktopWindow dw) { dw.Invalidate(); dw.Place(force: true); }
        }
    });

    private void OnBridgeFailed(SurfaceFailure failure)
    {
        if (failure.Recovered) _log.LogWarning("{Surface} recovered from {Kind}: {Detail}", failure.Surface, failure.Kind, failure.Detail);
        else _log.LogError("{Surface} failed: {Kind}: {Detail}", failure.Surface, failure.Kind, failure.Detail);
    }

    private void RefreshGlass()
    {
        if (_stopped) return;
        var theme = _theme.Current;
        foreach (var w in _windows.Values)
            w.ApplyGlass(w.ShapeWantsBlur && !theme.ReduceTransparency, theme.Mode == ThemeMode.Dark);
    }

    // =====================================================================================================================
    // Marshalling and clock
    // =====================================================================================================================

    /// <summary>Monotonic time since the manager was created (queue timeouts, grace periods); never the wall clock.</summary>
    private TimeSpan Elapsed() => _clock.GetElapsedTime(_t0Stamp);

    /// <summary>Runs <paramref name="action"/> on the dispatcher: synchronously when already there, otherwise queued (never blocks the caller).</summary>
    private void RunOnUi(Action action)
    {
        var d = _dispatcher;
        if (d is null) { _log.LogDebug("Surface call dropped before Start"); return; }
        if (d.CheckAccess()) { Safely(action); return; }
        try { d.BeginInvoke(new Action(() => Safely(action))); }
        catch (Exception) { /* dispatcher shut down */ }
    }

    /// <summary>Queues the work even when already on the dispatcher: used from WndProc callbacks (WM_DPICHANGED), where the re-plan must not run inside the message.</summary>
    private void PostToUi(Action action)
    {
        var d = _dispatcher;
        if (d is null) return;
        try { d.BeginInvoke(new Action(() => Safely(action))); }
        catch (Exception) { /* dispatcher shut down */ }
    }

    private void Safely(Action action)
    {
        try { action(); }
        catch (Exception ex) { _log.LogError(ex, "surface operation failed"); }
    }
}
