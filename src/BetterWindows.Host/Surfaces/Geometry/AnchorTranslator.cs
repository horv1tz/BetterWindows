using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Host.Surfaces;

/// <summary>
/// Popover anchors: the source surface (dock/menubar/desktop) reports the anchor in ITS client DIPs; the overlay wants it in ITS client DIPs
/// (docs/IPC.md section 4). Both windows are placed in the same physical virtual-screen space, so the translation goes through pixels:
/// anchorOverlay = ((sourceOrigin + anchor x sourceScale) - overlayOrigin) / overlayScale. Pure and unit-tested; mixed-DPI monitors are the reason this is not a plain subtraction.
/// </summary>
internal static class AnchorTranslator
{
    public static Rect Translate(Rect anchorDip, NativePoint sourceOriginPx, double sourceScale, NativePoint overlayOriginPx, double overlayScale)
    {
        var ss = DpiMath.SanitizeScale(sourceScale);
        var os = DpiMath.SanitizeScale(overlayScale);
        var ax = Finite(anchorDip.X);
        var ay = Finite(anchorDip.Y);
        var aw = Math.Max(0, Finite(anchorDip.Width));
        var ah = Math.Max(0, Finite(anchorDip.Height));
        var xPx = sourceOriginPx.X + ax * ss;
        var yPx = sourceOriginPx.Y + ay * ss;
        return new Rect
        {
            X = Clean((xPx - overlayOriginPx.X) / os),
            Y = Clean((yPx - overlayOriginPx.Y) / os),
            Width = Clean(aw * ss / os),
            Height = Clean(ah * ss / os),
        };
    }

    private static double Finite(double v) => double.IsFinite(v) ? v : 0.0;

    /// <summary>Normalises -0.0 and removes floating point dust (the UI compares anchors; 1e-12 noise is not a position).</summary>
    private static double Clean(double v) => Math.Abs(v) < 1e-9 ? 0.0 : Math.Round(v, 6);
}

/// <summary>Chooses the monitor of an overlay.show: request.MonitorId, else the source surface's monitor, else the monitor under the cursor, else the primary one.</summary>
internal static class OverlayMonitorResolver
{
    public static MonitorInfo? Resolve(IReadOnlyList<MonitorInfo> monitors, string? requestMonitorId, string? sourceMonitorId, string? cursorMonitorId)
    {
        return MonitorGeometry.ById(monitors, requestMonitorId)
            ?? MonitorGeometry.ById(monitors, sourceMonitorId)
            ?? MonitorGeometry.ById(monitors, cursorMonitorId)
            ?? MonitorGeometry.Primary(monitors);
    }
}
