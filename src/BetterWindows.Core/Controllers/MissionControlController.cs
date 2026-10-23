using BetterWindows.Core.Desktop;
using BetterWindows.Core.Ipc;
using BetterWindows.Core.MissionControl;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Core.Controllers;

/// <summary>
/// Owner of every <c>mc.*</c> call. <c>mc.snapshot</c> returns the windows of ONE monitor (all virtual desktops) with lazily served thumbnails
/// (<c>/dyn/thumb/&lt;handle&gt;?w=480&amp;rev=&lt;n&gt;</c>, the provider captures on demand); while the overlay shows Mission Control, window/desktop changes are pushed as the
/// transient <c>mc.changed</c> (coalesced to &gt;= 100 ms, deduplicated). Every window handle goes through <see cref="IWindowTracker.Find"/> and a fresh process-id check
/// before it is acted upon. Also pre-captures the thumbnail of the window that just lost the foreground, because windows on other desktops cannot be captured later.
/// </summary>
public sealed class MissionControlController : IShellController
{
    /// <summary>Minimum spacing of mc.changed events.</summary>
    public static readonly TimeSpan LiveCoalesce = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan SettleStep = TimeSpan.FromMilliseconds(25);
    private const int SettleAttempts = 20; // 500 ms in total
    private static readonly TimeSpan KeystrokeGap = TimeSpan.FromMilliseconds(120);
    private const int MaxIdChars = 128;

    private readonly IMessageRouter _router;
    private readonly IEventHub _hub;
    private readonly IWindowTracker _tracker;
    private readonly IVirtualDesktopService _desktops;
    private readonly IWindowSystem _windows;
    private readonly IMonitorProvider _monitorPort;
    private readonly ICursorProvider _cursor;
    private readonly IInputSimulator _input;
    private readonly ISurfaceManager _surfaces;
    private readonly IOverlayState _overlay;
    private readonly IDynamicResourceProvider _dyn;
    private readonly TimeProvider _time;
    private readonly ILogger _log;

    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private ITimer? _liveTimer;
    private bool _liveArmed;
    private int _rev;
    private int _revToken = -1;
    private string? _lastJson;
    private string? _lastJsonMonitor;
    private long _lastForeground;

    public MissionControlController(IMessageRouter router, IEventHub hub, IWindowTracker tracker, IVirtualDesktopService desktops, IWindowSystem windows,
        IMonitorProvider monitors, ICursorProvider cursor, IInputSimulator input, ISurfaceManager surfaces, IOverlayState overlay, IDynamicResourceProvider dyn,
        TimeProvider time, ILogger<MissionControlController>? log = null)
    {
        _router = router;
        _hub = hub;
        _tracker = tracker;
        _desktops = desktops;
        _windows = windows;
        _monitorPort = monitors;
        _cursor = cursor;
        _input = input;
        _surfaces = surfaces;
        _overlay = overlay;
        _dyn = dyn;
        _time = time;
        _log = (ILogger?)log ?? NullLogger.Instance;
    }

    public string Name => "MissionControlController";

    public void Start()
    {
        _router.Handle<McSnapshotParams, McSnapshot>(Msg.McSnapshot, (ctx, p) => Task.FromResult(Snapshot(p)));
        _router.Handle<WindowHandleParams>(Msg.McActivateWindow, (ctx, p) => ActivateWindowAsync(p, ctx.Cancellation));
        _router.Handle<WindowHandleParams>(Msg.McCloseWindow, (ctx, p) => { CloseWindow(p); return Task.CompletedTask; });
        _router.Handle<DesktopIdParams>(Msg.McSwitchDesktop, (ctx, p) => SwitchDesktopAsync(p, ctx.Cancellation));
        _router.Handle<McMoveParams>(Msg.McMoveWindow, (ctx, p) => { MoveWindow(p); return Task.CompletedTask; });
        _router.HandleNoParams<DesktopInfo>(Msg.McCreateDesktop, ctx => Task.FromResult(CreateDesktop()));
        _router.Handle<DesktopIdParams>(Msg.McRemoveDesktop, (ctx, p) => { RemoveDesktop(p); return Task.CompletedTask; });

        _lastForeground = _tracker.Snapshot.Foreground?.Handle ?? 0;
        _liveTimer = _time.CreateTimer(_ => OnLiveTimer(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _tracker.Changed += OnTrackerChanged;
        _tracker.ForegroundChanged += OnForegroundChanged;
        _desktops.Changed += ScheduleLive;
        _overlay.Changed += OnOverlayChanged;
    }

    public void Stop()
    {
        _tracker.Changed -= OnTrackerChanged;
        _tracker.ForegroundChanged -= OnForegroundChanged;
        _desktops.Changed -= ScheduleLive;
        _overlay.Changed -= OnOverlayChanged;
        try { _stop.Cancel(); } catch (ObjectDisposedException) { }
        lock (_gate) { _liveTimer?.Dispose(); _liveTimer = null; _liveArmed = false; }
    }

    // ------------------------------------------------------------------ snapshot

    private bool MissionControlShown() { var st = _overlay.Current; return st.Visible && st.Mode == OverlayMode.MissionControl; }

    /// <summary>
    /// The revision of the current overlay show: constant while one Mission Control session lasts (stable thumbnail URLs, accepted by the UI's monotonic check),
    /// larger for every new show. Outside Mission Control mode every snapshot is its own "show".
    /// </summary>
    private int CurrentRev()
    {
        var st = _overlay.Current;
        lock (_gate)
        {
            if (st.Visible && st.Mode == OverlayMode.MissionControl)
            {
                if (_revToken != st.Token || _rev == 0) { _revToken = st.Token; _rev++; _lastJson = null; }
            }
            else { _rev++; _revToken = -1; _lastJson = null; }
            return _rev;
        }
    }

    private MonitorInfo ResolveTarget(string? requested, IReadOnlyList<MonitorInfo> monitors)
    {
        if (requested is not null)
        {
            if (requested.Length > MaxIdChars) throw ProtocolException.BadRequest("unknown monitor");
            return MonitorGeometry.Find(monitors, requested) ?? throw ProtocolException.BadRequest("unknown monitor");
        }
        // null = the overlay's monitor; hotkey-opened overlays are on the monitor under the cursor.
        var st = _overlay.Current;
        if (st.MonitorId is not null && MonitorGeometry.Find(monitors, st.MonitorId) is { } overlayMonitor) return overlayMonitor;
        if (SafeCursor(out var pt) && (SafeFromPoint(pt) ?? MonitorGeometry.MonitorOfPoint(monitors, pt.X, pt.Y)) is { } underCursor) return underCursor;
        return MonitorGeometry.Primary(monitors) ?? throw ProtocolException.Failed("no monitor is available");
    }

    private bool SafeCursor(out NativePoint pt)
    {
        try { return _cursor.TryGetCursorPosition(out pt); }
        catch (Exception ex) { _log.LogDebug(ex, "Reading the cursor position failed"); pt = default; return false; }
    }

    private MonitorInfo? SafeFromPoint(NativePoint pt)
    {
        try { return _monitorPort.FromPoint(pt.X, pt.Y); }
        catch (Exception ex) { _log.LogDebug(ex, "FromPoint failed"); return null; }
    }

    private McSnapshot Snapshot(McSnapshotParams p)
    {
        var monitors = _monitorPort.GetMonitors();
        var target = ResolveTarget(p.MonitorId, monitors);
        var rev = CurrentRev();
        var snap = McSnapshotBuilder.Build(_tracker.Snapshot, monitors, target, _desktops, rev, _log);
        lock (_gate) { _lastJson = ProtocolJson.Serialize(snap); _lastJsonMonitor = target.Id; }
        return snap;
    }

    // ------------------------------------------------------------------ live updates (mc.changed)

    private void OnTrackerChanged(WindowSnapshot _) => ScheduleLive();

    private void OnOverlayChanged(OverlayState state)
    {
        if (state.Visible && state.Mode == OverlayMode.MissionControl) return;
        lock (_gate) { _liveTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan); _liveArmed = false; _lastJson = null; }
    }

    /// <summary>Port-event safe: a flag check and one timer arm. Nothing is scheduled unless Mission Control is on screen.</summary>
    private void ScheduleLive()
    {
        if (!MissionControlShown()) return;
        lock (_gate)
        {
            if (_liveArmed || _liveTimer is null) return;
            _liveArmed = true;
            _liveTimer.Change(LiveCoalesce, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnLiveTimer()
    {
        try
        {
            lock (_gate) _liveArmed = false;
            var st = _overlay.Current;
            if (!st.Visible || st.Mode != OverlayMode.MissionControl) return;

            var monitors = _monitorPort.GetMonitors();
            MonitorInfo target;
            try { target = ResolveTarget(null, monitors); }
            catch (ProtocolException) { return; }
            var snap = McSnapshotBuilder.Build(_tracker.Snapshot, monitors, target, _desktops, CurrentRev(), _log);
            var json = ProtocolJson.Serialize(snap);
            lock (_gate)
            {
                if (_lastJson == json && _lastJsonMonitor == target.Id) return; // nothing the UI does not already show
                _lastJson = json;
                _lastJsonMonitor = target.Id;
            }
            _hub.PublishToKind(SurfaceKind.Overlay, Msg.McChanged, snap);
        }
        catch (Exception ex) { _log.LogError(ex, "Publishing mc.changed failed"); }
    }

    // ------------------------------------------------------------------ thumbnails of windows that lose the foreground

    private void OnForegroundChanged(TrackedWindow? now)
    {
        var handle = now?.Handle ?? 0;
        var lost = Interlocked.Exchange(ref _lastForeground, handle);
        if (lost == 0 || lost == handle) return;
        _ = Task.Run(async () =>
        {
            try
            {
                if (_tracker.Find(lost) is null) return; // gone, or ours
                await _dyn.PrewarmWindowThumbnailAsync(lost, _stop.Token).ConfigureAwait(false);
            }
            catch (Exception ex) { _log.LogDebug(ex, "Thumbnail prewarm of {Handle} failed", lost); }
        });
    }

    // ------------------------------------------------------------------ calls

    /// <summary>The tracked window for a wire handle, re-checked against the live window (same process) so a recycled HWND can never be acted upon.</summary>
    private (TrackedWindow Tracked, RawWindow Live) ResolveWindow(string? wire)
    {
        if (!Hwnd.TryParse(wire, out var handle) || handle == 0) throw ProtocolException.BadRequest("invalid window handle");
        var tracked = _tracker.Find(handle) ?? throw ProtocolException.NotFound("no such window");
        var live = _windows.TryDescribe(handle);
        if (live is null || live.ProcessId != tracked.Raw.ProcessId) throw ProtocolException.NotFound("the window no longer exists");
        return (tracked, live);
    }

    private static string ValidateDesktopId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > MaxIdChars) throw ProtocolException.BadRequest("invalid desktop id");
        foreach (var c in id) if (c < 0x20 || c == 0x7f) throw ProtocolException.BadRequest("invalid desktop id");
        return id;
    }

    private IReadOnlyList<DesktopInfo> Desktops()
    {
        try { return _desktops.GetDesktops().ToArray(); }
        catch (Exception ex) { _log.LogDebug(ex, "GetDesktops failed"); return Array.Empty<DesktopInfo>(); }
    }

    private bool OnCurrentDesktop(long handle)
    {
        try { return _desktops.IsWindowOnCurrentDesktop(handle); }
        catch (Exception ex) { _log.LogDebug(ex, "IsWindowOnCurrentDesktop failed"); return true; }
    }

    private async Task ActivateWindowAsync(WindowHandleParams p, CancellationToken ct)
    {
        var (tracked, live) = ResolveWindow(p.Handle);
        var handle = tracked.Handle;

        string? desktopId = null;
        try { desktopId = _desktops.GetWindowDesktopId(handle); } catch (Exception ex) { _log.LogDebug(ex, "GetWindowDesktopId failed"); }
        if (_desktops.IsSupported && desktopId is not null && !OnCurrentDesktop(handle))
        {
            await SwitchToAsync(desktopId, ct).ConfigureAwait(false);
            // The shell un-cloaks the window asynchronously after the switch; activating it earlier would fail.
            for (var i = 0; i < SettleAttempts && !OnCurrentDesktop(handle); i++)
                await Task.Delay(SettleStep, _time, ct).ConfigureAwait(false);
        }

        // Activate restores a minimised window itself; an explicit Restore is only for minimised ones (it would un-maximise a maximised window).
        if (live.IsMinimized) _windows.Restore(handle);
        if (!_windows.Activate(handle))
            throw ProtocolException.Failed(live.IsElevated ? "the window runs as administrator and cannot be activated from here" : "the window could not be activated");
        _surfaces.HideOverlay();
    }

    private void CloseWindow(WindowHandleParams p)
    {
        var (tracked, _) = ResolveWindow(p.Handle);
        if (!_windows.Close(tracked.Handle)) throw ProtocolException.Failed("the window could not be closed");
        ScheduleLive();
    }

    private async Task SwitchDesktopAsync(DesktopIdParams p, CancellationToken ct)
    {
        await SwitchToAsync(ValidateDesktopId(p.DesktopId), ct).ConfigureAwait(false);
        _surfaces.HideOverlay();
    }

    /// <summary>
    /// Switches to a desktop: the virtual-desktop service when it supports it; if it is unavailable or refuses, by pressing Win+Ctrl+Left/Right the right number of
    /// times (the shortcut works on every Windows 10/11 build; it needs the desktop list to know how far to go).
    /// </summary>
    private async Task SwitchToAsync(string desktopId, CancellationToken ct)
    {
        var list = Desktops();
        var target = list.FirstOrDefault(d => d.Id == desktopId);
        if (_desktops.IsSupported)
        {
            if (target is null) throw ProtocolException.NotFound("no such desktop");
            if (target.Current) return;
            var ok = false;
            try { ok = _desktops.Switch(desktopId); } catch (Exception ex) { _log.LogDebug(ex, "Switching the virtual desktop failed"); }
            if (ok) return;
        }

        var current = list.FirstOrDefault(d => d.Current);
        if (target is null || current is null) throw ProtocolException.Unsupported("virtual desktops are not available on this system");
        var delta = target.Index - current.Index;
        for (var i = 0; i < Math.Abs(delta); i++)
        {
            if (delta < 0) _input.SwitchDesktopLeft(); else _input.SwitchDesktopRight();
            if (i < Math.Abs(delta) - 1) await Task.Delay(KeystrokeGap, _time, ct).ConfigureAwait(false);
        }
    }

    private void MoveWindow(McMoveParams p)
    {
        var (tracked, _) = ResolveWindow(p.Handle);
        var desktopId = ValidateDesktopId(p.DesktopId);
        if (!_desktops.IsSupported) throw ProtocolException.Unsupported("moving windows between virtual desktops is not available on this system");
        if (Desktops().All(d => d.Id != desktopId)) throw ProtocolException.NotFound("no such desktop");
        var ok = false;
        try { ok = _desktops.MoveWindow(tracked.Handle, desktopId); } catch (Exception ex) { _log.LogDebug(ex, "MoveWindow failed"); }
        if (!ok) throw ProtocolException.Unsupported("Windows does not allow moving this window to another desktop");
        ScheduleLive();
    }

    private DesktopInfo CreateDesktop()
    {
        if (!_desktops.IsSupported) throw ProtocolException.Unsupported("virtual desktops are not available on this system");
        DesktopInfo? created = null;
        try { created = _desktops.Create(); } catch (Exception ex) { _log.LogDebug(ex, "Creating a virtual desktop failed"); }
        if (created is null) throw ProtocolException.Failed("the desktop could not be created");
        ScheduleLive();
        return created;
    }

    private void RemoveDesktop(DesktopIdParams p)
    {
        var id = ValidateDesktopId(p.DesktopId);
        if (!_desktops.IsSupported) throw ProtocolException.Unsupported("virtual desktops are not available on this system");
        var list = Desktops();
        if (list.All(d => d.Id != id)) throw ProtocolException.NotFound("no such desktop");
        if (list.Count <= 1) throw ProtocolException.Failed("the last desktop cannot be removed");
        var ok = false;
        try { ok = _desktops.Remove(id); } catch (Exception ex) { _log.LogDebug(ex, "Removing a virtual desktop failed"); }
        if (!ok) throw ProtocolException.Failed("the desktop could not be removed");
        ScheduleLive();
    }
}
