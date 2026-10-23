using BetterWindows.Core.Platform;

namespace BetterWindows.Host.Surfaces;

/// <summary>
/// Pure maths of the menu bar auto-hide slide. The window is moved UP by an offset and the part that would leave the monitor is clipped away with the window region
/// (a window moved above the top edge of a monitor would otherwise show up on the monitor above in a vertical multi-monitor layout).
/// </summary>
internal static class SlideAnimation
{
    public static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(140);

    /// <summary>Ease-out cubic progress 0..1 for the elapsed time.</summary>
    public static double Progress(TimeSpan elapsed, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) return 1.0;
        var t = Math.Clamp(elapsed.TotalMilliseconds / duration.TotalMilliseconds, 0.0, 1.0);
        var inv = 1.0 - t;
        return 1.0 - inv * inv * inv;
    }

    /// <summary>
    /// Offset (px the bar is shifted up) at <paramref name="progress"/> of a slide from <paramref name="fromPx"/> to <paramref name="toPx"/>. Interpolating from the
    /// CURRENT offset means reversing a half-finished slide continues smoothly instead of jumping to the other end.
    /// </summary>
    public static int Between(int fromPx, int toPx, double progress)
    {
        var p = Math.Clamp(double.IsFinite(progress) ? progress : 1.0, 0.0, 1.0);
        return (int)Math.Round(fromPx + (toPx - fromPx) * p, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The part of a window (screen px) that lies on its own monitor, in WINDOW-LOCAL coordinates; null when nothing is visible. Used as the clip of the window region.
    /// </summary>
    public static NativeRect? LocalClip(NativeRect windowPx, NativeRect monitorBoundsPx)
    {
        var inter = MonitorGeometry.Intersect(windowPx, monitorBoundsPx);
        if (MonitorGeometry.IsEmpty(inter)) return null;
        return new NativeRect(inter.Left - windowPx.Left, inter.Top - windowPx.Top, inter.Right - windowPx.Left, inter.Bottom - windowPx.Top);
    }
}
