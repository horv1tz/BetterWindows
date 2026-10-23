using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Input;

/// <summary>The four corners of a monitor.</summary>
public enum ScreenCorner { TopLeft, TopRight, BottomLeft, BottomRight }

/// <summary>A monitor as the hot-corner logic sees it: id plus its bounds in virtual-screen physical pixels.</summary>
public readonly record struct HotCornerMonitor(string Id, NativeRect Bounds);

/// <summary>The action assigned to each corner (<see cref="HotCornerAction.None"/> = corner disabled).</summary>
public readonly record struct HotCornerActions(HotCornerAction TopLeft, HotCornerAction TopRight, HotCornerAction BottomLeft, HotCornerAction BottomRight)
{
    public HotCornerAction For(ScreenCorner corner) => corner switch
    {
        ScreenCorner.TopLeft => TopLeft,
        ScreenCorner.TopRight => TopRight,
        ScreenCorner.BottomLeft => BottomLeft,
        _ => BottomRight,
    };

    /// <summary>True if at least one corner has an action: only then does anything need to poll the cursor.</summary>
    public bool AnyAssigned => TopLeft != HotCornerAction.None || TopRight != HotCornerAction.None || BottomLeft != HotCornerAction.None || BottomRight != HotCornerAction.None;

    public static HotCornerActions From(HotCornerSettings s) => new(s.TopLeft, s.TopRight, s.BottomLeft, s.BottomRight);
}

/// <summary>A hot corner that was triggered.</summary>
public readonly record struct HotCornerTrigger(string MonitorId, ScreenCorner Corner, HotCornerAction Action);

/// <summary>
/// Pure hot-corner state machine (no clock, no OS): feed it one cursor sample per poll, it says when a corner fires.
/// <list type="bullet">
/// <item>A corner is USABLE only if it is on the OUTER edge of the virtual screen: none of the three pixels outward from the corner pixel (horizontal, vertical, diagonal)
/// belongs to another monitor. Corners where the cursor could slide onto a neighbouring monitor are ignored, so side-by-side, stacked, negative-coordinate and mixed-size layouts all work.</item>
/// <item>The pointer must be within <see cref="CornerTolerancePx"/> px (per axis) of the very corner pixel for the whole dwell time.</item>
/// <item>A corner fires once per entry: it then stays DISARMED until the pointer is farther than <see cref="RearmRadiusPx"/> px from the corner pixel.</item>
/// <item>A mouse button down inside the corner zone (or while entering it, e.g. dragging a window to snap it) disarms that corner the same way, so a drag released
/// in the corner does not pop Mission Control; while any button is down nothing fires and no dwell accumulates.</item>
/// <item>A fullscreen app on the monitor suppresses the trigger (checked lazily, only when a corner is about to fire) and also disarms the corner until the pointer leaves.</item>
/// <item>After any trigger there is a <see cref="RefractoryPeriod"/> during which no corner fires (the dwell of a corner entered meanwhile keeps running).</item>
/// </list>
/// Not thread-safe: call from one thread at a time (the controller serialises ticks).
/// </summary>
public sealed class HotCornerDetector
{
    /// <summary>The pointer counts as "in the corner" within this many pixels (per axis) of the corner pixel.</summary>
    public const int CornerTolerancePx = 2;
    /// <summary>A disarmed corner re-arms once the pointer is farther than this (Euclidean, px) from the corner pixel.</summary>
    public const int RearmRadiusPx = 24;
    /// <summary>No corner fires for this long after the previous trigger.</summary>
    public static readonly TimeSpan RefractoryPeriod = TimeSpan.FromMilliseconds(500);

    private sealed class CornerState
    {
        public bool Armed = true;
        public TimeSpan? EnteredAt;
    }

    private readonly record struct CornerSlot(string MonitorId, ScreenCorner Corner, int X, int Y, NativeRect Bounds);

    private readonly Dictionary<(string Monitor, ScreenCorner Corner), CornerState> _states = new();
    private IReadOnlyList<HotCornerMonitor>? _layoutSource;
    private CornerSlot[] _layout = Array.Empty<CornerSlot>();
    private TimeSpan? _lastFire;

    /// <summary>Forget all dwell/arming state (cursor unavailable, feature toggled, monitors changed).</summary>
    public void Reset()
    {
        _states.Clear();
        _lastFire = null;
    }

    /// <summary>
    /// Process one cursor sample. <paramref name="now"/> is a monotonic clock; <paramref name="isFullscreen"/> (monitor id -> fullscreen app active) is consulted only when a corner is
    /// ready to fire. Returns the trigger or null.
    /// </summary>
    public HotCornerTrigger? Update(TimeSpan now, NativePoint cursor, bool anyButtonDown, IReadOnlyList<HotCornerMonitor> monitors, HotCornerActions actions, TimeSpan dwell,
        Func<string, bool>? isFullscreen = null)
    {
        var layout = LayoutFor(monitors);
        foreach (var slot in layout)
        {
            if (actions.For(slot.Corner) == HotCornerAction.None) continue;
            var state = StateFor(slot);
            var dx = cursor.X - slot.X;
            var dy = cursor.Y - slot.Y;
            var inZone = Math.Abs(dx) <= CornerTolerancePx && Math.Abs(dy) <= CornerTolerancePx && slot.Bounds.Contains(cursor.X, cursor.Y);

            if (!inZone)
            {
                state.EnteredAt = null; // leaving the zone cancels the dwell
                if (!state.Armed && (long)dx * dx + (long)dy * dy > (long)RearmRadiusPx * RearmRadiusPx) state.Armed = true;
                continue;
            }

            if (!state.Armed) continue; // already fired / dragged in: wait for the pointer to leave the re-arm radius

            if (anyButtonDown)
            {
                state.Armed = false;
                state.EnteredAt = null;
                continue;
            }

            state.EnteredAt ??= now;
            if (now - state.EnteredAt.Value < dwell) continue;
            if (_lastFire is { } last && now - last < RefractoryPeriod) continue;

            state.EnteredAt = null;
            state.Armed = false; // once per entry, whatever happens next
            if (isFullscreen?.Invoke(slot.MonitorId) == true) continue;

            _lastFire = now;
            return new HotCornerTrigger(slot.MonitorId, slot.Corner, actions.For(slot.Corner));
        }
        return null;
    }

    private CornerState StateFor(CornerSlot slot)
    {
        var key = (slot.MonitorId, slot.Corner);
        if (!_states.TryGetValue(key, out var s)) _states[key] = s = new CornerState();
        return s;
    }

    /// <summary>The usable corners of a monitor layout. Cached per list instance (the controller passes the same list until monitors change).</summary>
    private CornerSlot[] LayoutFor(IReadOnlyList<HotCornerMonitor> monitors)
    {
        if (ReferenceEquals(monitors, _layoutSource)) return _layout;
        _layoutSource = monitors;
        _layout = BuildLayout(monitors);
        var valid = _layout.Select(s => (s.MonitorId, s.Corner)).ToHashSet();
        foreach (var stale in _states.Keys.Where(k => !valid.Contains(k)).ToArray()) _states.Remove(stale);
        return _layout;
    }

    /// <summary>Exposed for tests: the usable (outer) corner pixels of a layout.</summary>
    internal static IReadOnlyList<(string MonitorId, ScreenCorner Corner, int X, int Y)> UsableCorners(IReadOnlyList<HotCornerMonitor> monitors) =>
        BuildLayout(monitors).Select(s => (s.MonitorId, s.Corner, s.X, s.Y)).ToArray();

    private static CornerSlot[] BuildLayout(IReadOnlyList<HotCornerMonitor> monitors)
    {
        var slots = new List<CornerSlot>();
        var seen = new HashSet<(int, int)>(); // mirrored displays share corner pixels: one slot per pixel so a corner cannot fire twice
        for (var i = 0; i < monitors.Count; i++)
        {
            var b = monitors[i].Bounds;
            if (b.Width <= 0 || b.Height <= 0) continue;
            Consider(ScreenCorner.TopLeft, b.Left, b.Top, -1, -1);
            Consider(ScreenCorner.TopRight, b.Right - 1, b.Top, 1, -1);
            Consider(ScreenCorner.BottomLeft, b.Left, b.Bottom - 1, -1, 1);
            Consider(ScreenCorner.BottomRight, b.Right - 1, b.Bottom - 1, 1, 1);

            void Consider(ScreenCorner corner, int x, int y, int outX, int outY)
            {
                // Outer edge of the virtual screen: stepping outward (sideways, up/down and diagonally) must not land on another monitor.
                for (var j = 0; j < monitors.Count; j++)
                {
                    if (j == i) continue;
                    var o = monitors[j].Bounds;
                    if (o.Contains(x + outX, y) || o.Contains(x, y + outY) || o.Contains(x + outX, y + outY)) return;
                }
                if (!seen.Add((x, y))) return;
                slots.Add(new CornerSlot(monitors[i].Id, corner, x, y, b));
            }
        }
        return slots.ToArray();
    }
}
