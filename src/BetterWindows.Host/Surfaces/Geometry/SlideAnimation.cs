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

    /// <summary>How many px the bar is shifted up: 0 = fully shown, <paramref name="heightPx"/> = fully hidden. <paramref name="revealing"/> runs the animation backwards.</summary>
    public static int OffsetPx(double progress, int heightPx, bool revealing)
    {
        var p = Math.Clamp(progress, 0.0, 1.0);
        var shown = revealing ? p : 1.0 - p;
        return (int)Math.Round((1.0 - shown) * Math.Max(0, heightPx), MidpointRounding.AwayFromZero);
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
