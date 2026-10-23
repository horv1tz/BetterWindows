using BetterWindows.Core.Platform;

namespace BetterWindows.Host.Surfaces;

/// <summary>Pure menu bar geometry: a full-monitor-width bar of 28 DIP on the top edge of its monitor.</summary>
internal static class MenuBarPlacement
{
    public const double HeightDip = 28.0;

    public static int HeightPx(double scale) => Math.Max(1, DpiMath.DipToPxCeil(HeightDip, scale));

    /// <summary>The bar rectangle (also the AppBar band) for a monitor, before an AppBar adjusted it.</summary>
    public static NativeRect Compute(NativeRect monitorBoundsPx, double scale)
    {
        var h = Math.Min(HeightPx(scale), Math.Max(1, monitorBoundsPx.Height));
        return new NativeRect(monitorBoundsPx.Left, monitorBoundsPx.Top, monitorBoundsPx.Right, monitorBoundsPx.Top + h);
    }
}
