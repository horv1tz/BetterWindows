using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Core.Overlay;

/// <summary>
/// The one place that shows and hides the overlay and keeps <see cref="IOverlayState"/> truthful: EVERY show and hide, whoever initiated it (overlay.open, a hotkey, a hot
/// corner, a command), goes through here so the state never lags behind. The visibility itself lives in the Host (<see cref="ISurfaceManager"/>); this class is stateless
/// apart from a one-shot timer, so any number of instances over the same singletons behave identically (OverlayController, HotkeyController and HotCornerController each create one).
/// <para>Hosts marshal <see cref="ISurfaceManager.ShowOverlay"/> to the UI thread, so right after the call the token getter may still be the previous one. The state is therefore
/// published optimistically and the token is re-synchronised shortly afterwards (<see cref="TokenResyncDelay"/>).</para>
/// Nothing here publishes overlay.show/overlay.hide: those events belong to the Host.
/// </summary>
public sealed class OverlayCoordinator : IDisposable
{
    /// <summary>How long after a show the host's token is read again, in case the host showed the overlay asynchronously.</summary>
    public static readonly TimeSpan TokenResyncDelay = TimeSpan.FromMilliseconds(100);

    private readonly ISurfaceManager _surfaces;
    private readonly OverlayStateService _state;
    private readonly ICursorProvider _cursor;
    private readonly IMonitorProvider _monitors;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private ITimer? _resync;
    private readonly object _gate = new();

    public OverlayCoordinator(ISurfaceManager surfaces, OverlayStateService state, ICursorProvider cursor, IMonitorProvider monitors, TimeProvider time, ILogger<OverlayCoordinator>? log = null)
    {
        _surfaces = surfaces;
        _state = state;
        _cursor = cursor;
        _monitors = monitors;
        _time = time;
        _log = (ILogger?)log ?? NullLogger.Instance;
    }

    /// <summary>The overlay as the Host reports it right now.</summary>
    public bool IsVisible => _surfaces.IsOverlayVisible;
    public OverlayMode? CurrentMode => _surfaces.CurrentOverlayMode;

    /// <summary>Id of the monitor under the mouse pointer; null when the pointer or the monitor cannot be determined (the Host then picks one itself).</summary>
    public string? MonitorUnderCursor()
    {
        try
        {
            if (!_cursor.TryGetCursorPosition(out var p)) return null;
            return _monitors.FromPoint(p.X, p.Y)?.Id;
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Could not resolve the monitor under the cursor");
            return null;
        }
    }

    /// <summary>Show (or re-show in another mode) the overlay. <paramref name="source"/> is the requesting surface, null for hotkeys / hot corners / commands.</summary>
    public void Show(OverlayRequest request, SurfaceId? source)
    {
        _surfaces.ShowOverlay(request, source);
        var monitor = request.MonitorId ?? source?.MonitorId ?? MonitorUnderCursor();
        _state.Set(new OverlayState(true, request.Mode, monitor, _surfaces.OverlayToken, request.Popover?.Kind, request.Popover?.ItemId));
        ArmTokenResync();
    }

    /// <summary>Hide the overlay (UI close, toggle, foreground change, other shell action).</summary>
    public void Hide()
    {
        CancelTokenResync();
        _surfaces.HideOverlay();
        _state.Set(OverlayState.Hidden);
    }

    /// <summary>The Host hid the overlay by itself (deactivation, monitor change): only the state needs to follow.</summary>
    public void MarkHiddenByHost()
    {
        CancelTokenResync();
        _state.Set(OverlayState.Hidden);
    }

    /// <summary>
    /// Toggle semantics for hotkeys and hot corners: visible in the SAME mode -> hide; hidden or another mode -> show this mode on <paramref name="monitorId"/> (default: the monitor
    /// under the cursor). Returns true if the overlay is now showing the mode.
    /// </summary>
    public bool Toggle(OverlayMode mode, string? monitorId = null)
    {
        if (_surfaces.IsOverlayVisible && _surfaces.CurrentOverlayMode == mode)
        {
            Hide();
            return false;
        }
        Show(new OverlayRequest { Mode = mode, MonitorId = monitorId ?? MonitorUnderCursor() }, null);
        return true;
    }

    private void ArmTokenResync()
    {
        lock (_gate)
        {
            _resync ??= _time.CreateTimer(_ => ResyncToken(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _resync.Change(TokenResyncDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void CancelTokenResync()
    {
        lock (_gate) _resync?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    private void ResyncToken()
    {
        try
        {
            var cur = _state.Current;
            if (!cur.Visible || !_surfaces.IsOverlayVisible) return; // hidden: OverlayHidden / Hide() are authoritative, never guess from a lagging getter
            var token = _surfaces.OverlayToken;
            if (token != cur.Token) _state.Set(cur with { Token = token });
        }
        catch (Exception ex) { _log.LogDebug(ex, "Overlay token resync failed"); }
    }

    public void Dispose()
    {
        lock (_gate) { _resync?.Dispose(); _resync = null; }
    }
}
