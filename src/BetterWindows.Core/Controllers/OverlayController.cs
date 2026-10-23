using BetterWindows.Core.Ipc;
using BetterWindows.Core.Overlay;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Core.Controllers;

/// <summary>
/// Owns overlay.open / overlay.close and <see cref="IOverlayState"/> (spotlight, launchpad, Mission Control and every popover share the one overlay surface).
/// <list type="bullet">
/// <item>overlay.open is validated (<see cref="OverlayRequestValidator"/>) and forwarded as <c>ISurfaceManager.ShowOverlay(request, ctx.Surface)</c>; the Host publishes overlay.show.</item>
/// <item>overlay.close{token} hides the overlay only if the token is the CURRENT show's token (a stale exit animation must not close a newer show).</item>
/// <item>The overlay state is updated on EVERY show and hide, including hides the Host does by itself (<see cref="ISurfaceManager.OverlayHidden"/>).</item>
/// <item>While a dock popover (dockMenu / windowList) is open the dock is held revealed so an auto-hide dock does not slide away under its own menu; released when the popover goes.</item>
/// <item>If another window becomes the foreground while the overlay is visible it is hidden, after a short debounce (a flicker of the foreground must not close it).</item>
/// </list>
/// overlay.show / overlay.hide are published by the Host, never here.
/// </summary>
public sealed class OverlayController : IShellController
{
    /// <summary>Foreground must stay on another window this long before the overlay is hidden.</summary>
    public static readonly TimeSpan ForegroundDebounce = TimeSpan.FromMilliseconds(150);
    private static readonly SurfaceId DockSurface = new(SurfaceKind.Dock);

    private readonly IMessageRouter _router;
    private readonly ISurfaceManager _surfaces;
    private readonly OverlayStateService _state;
    private readonly IMonitorProvider _monitors;
    private readonly IWindowTracker _tracker;
    private readonly TimeProvider _time;
    private readonly OverlayCoordinator _overlay;
    private readonly ILogger _log;
    private readonly object _gate = new();
    private ITimer? _hideTimer;
    private long _baselineForeground;
    private OverlayState _previous = OverlayState.Hidden;
    private bool _dockHeld, _started, _stopped;
    private Action? _onHostHidden;
    private Action<OverlayState>? _onState;
    private Action<TrackedWindow?>? _onForeground;

    public OverlayController(IMessageRouter router, ISurfaceManager surfaces, OverlayStateService state, IMonitorProvider monitors, IWindowTracker tracker, TimeProvider time,
        IServiceProvider services, ILogger<OverlayController>? log = null)
    {
        _router = router;
        _surfaces = surfaces;
        _state = state;
        _monitors = monitors;
        _tracker = tracker;
        _time = time;
        _log = (ILogger?)log ?? NullLogger.Instance;
        _overlay = services.GetService<OverlayCoordinator>() ?? ActivatorUtilities.CreateInstance<OverlayCoordinator>(services);
    }

    public string Name => "OverlayController";

    public void Start()
    {
        if (_started) return;
        _started = true;

        _router.Handle<OverlayRequest>(Msg.OverlayOpen, Open);
        _router.Handle<OverlayCloseParams>(Msg.OverlayClose, Close);

        _onHostHidden = _overlay.MarkHiddenByHost;
        _surfaces.OverlayHidden += _onHostHidden;
        _onState = OnStateChanged;
        ((IOverlayState)_state).Changed += _onState;
        _onForeground = OnForegroundChanged;
        _tracker.ForegroundChanged += _onForeground;

        // The Host may already show the overlay (a restart of the controller): adopt what is true now.
        if (_surfaces.IsOverlayVisible && !_state.Current.Visible && _surfaces.CurrentOverlayMode is { } mode)
            _state.Set(new OverlayState(true, mode, null, _surfaces.OverlayToken, null, null));
        else
            OnStateChanged(_state.Current);
    }

    public void Stop()
    {
        lock (_gate) _stopped = true;
        if (_onHostHidden is not null) _surfaces.OverlayHidden -= _onHostHidden;
        if (_onState is not null) ((IOverlayState)_state).Changed -= _onState;
        if (_onForeground is not null) _tracker.ForegroundChanged -= _onForeground;
        _onHostHidden = null;
        _onState = null;
        _onForeground = null;
        lock (_gate) { _hideTimer?.Dispose(); _hideTimer = null; }
        ReleaseDock();
        _overlay.Dispose();
    }

    // ---- calls ----

    private Task Open(CallContext ctx, OverlayRequest request)
    {
        var validated = OverlayRequestValidator.Validate(request, _monitors.GetMonitors());
        try { _overlay.Show(validated, ctx.Surface); }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "overlay.open failed");
            throw ProtocolException.Failed("the overlay could not be shown");
        }
        return Task.CompletedTask;
    }

    private Task Close(CallContext ctx, OverlayCloseParams p)
    {
        // A stale exit animation (token of an older show) must not close the overlay the user just re-opened.
        if (p.Token != _surfaces.OverlayToken) return Task.CompletedTask;
        try
        {
            if (_surfaces.IsOverlayVisible) _overlay.Hide();
            else _overlay.MarkHiddenByHost();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "overlay.close failed");
            throw ProtocolException.Failed("the overlay could not be hidden");
        }
        return Task.CompletedTask;
    }

    // ---- state-driven side effects (run for every show/hide, whoever caused it) ----

    private void OnStateChanged(OverlayState state)
    {
        try
        {
            // Re-read the truth: Changed may be raised out of order by concurrent writers.
            var current = _state.Current;
            OverlayState previous;
            lock (_gate)
            {
                previous = _previous;
                _previous = current;
            }
            if (!current.Visible)
            {
                CancelHide();
            }
            else if (!previous.Visible || previous.Token != current.Token || previous.Mode != current.Mode)
            {
                CancelHide();
                Interlocked.Exchange(ref _baselineForeground, _tracker.Foreground?.Handle ?? 0);
            }
            SyncDock(current);
        }
        catch (Exception ex) { _log.LogDebug(ex, "Overlay state side effects failed"); }
    }

    /// <summary>Hold the dock revealed while one of its popovers is open (SetDockRevealed true), release otherwise.</summary>
    private void SyncDock(OverlayState state)
    {
        var want = state.Visible && state.Mode == OverlayMode.Popover && state.Popover is PopoverKind.DockMenu or PopoverKind.WindowList;
        bool change;
        lock (_gate)
        {
            if (_stopped && want) return;
            change = _dockHeld != want;
            _dockHeld = want;
        }
        if (change) _surfaces.SetDockRevealed(DockSurface, want);
    }

    private void ReleaseDock()
    {
        bool held;
        lock (_gate) { held = _dockHeld; _dockHeld = false; }
        if (held)
        {
            try { _surfaces.SetDockRevealed(DockSurface, false); }
            catch (Exception ex) { _log.LogDebug(ex, "Releasing the dock reveal failed"); }
        }
    }

    // ---- hide when another app takes the foreground ----

    private void OnForegroundChanged(TrackedWindow? window)
    {
        // Port-style event: return fast; only arm a timer.
        if (window is null || !_state.Current.Visible) return;
        if (window.Handle == Interlocked.Read(ref _baselineForeground)) return;
        ArmHide();
    }

    private void ArmHide()
    {
        lock (_gate)
        {
            if (_stopped) return;
            _hideTimer ??= _time.CreateTimer(_ => HideIfStillElsewhere(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _hideTimer.Change(ForegroundDebounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void CancelHide()
    {
        lock (_gate) _hideTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    private void HideIfStillElsewhere()
    {
        try
        {
            if (!_state.Current.Visible) return;
            var foreground = _tracker.Foreground;
            if (foreground is null || foreground.Handle == Interlocked.Read(ref _baselineForeground)) return; // it flipped back, or nothing is focused
            _overlay.Hide();
        }
        catch (Exception ex) { _log.LogDebug(ex, "Hiding the overlay after a foreground change failed"); }
    }
}
