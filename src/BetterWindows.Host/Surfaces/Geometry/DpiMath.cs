using BetterWindows.Core.Platform;

namespace BetterWindows.Host.Surfaces;

/// <summary>
/// DIP &lt;-&gt; physical pixel conversion used by every surface. The UI speaks DIPs (CSS px at the surface's own scale); Win32 placement (SetWindowPos, AppBar,
/// regions) speaks physical pixels. Rounding policy (WHY): SIZES round UP so that content is never clipped by a fraction of a pixel; POSITIONS and edge coordinates round
/// to nearest so that a window centred on an edge stays centred. A tiny epsilon absorbs floating point noise (56 * 1.25 = 70.00000000000001 must stay 70, not 71).
/// </summary>
internal static class DpiMath
{
    public const double BaseDpi = 96.0;
    private const double Epsilon = 1e-6;

    /// <summary>GetDpiForWindow / GetDpiForMonitor value -&gt; scale factor (1.0 = 96 dpi). 0 (failure) maps to 1.0.</summary>
    public static double ScaleFromDpi(uint dpi) => dpi == 0 ? 1.0 : dpi / BaseDpi;

    /// <summary>Guards against NaN/zero/absurd scales from broken drivers; the UI is never asked to render at scale 0.</summary>
    public static double SanitizeScale(double scale) => double.IsFinite(scale) && scale >= 0.25 && scale <= 16.0 ? scale : 1.0;

    /// <summary>DIP extent -&gt; pixels, rounded up (sizes).</summary>
    public static int DipToPxCeil(double dip, double scale)
    {
        var v = Clamp(dip) * SanitizeScale(scale);
        return ToIntClamped(Math.Ceiling(v - Epsilon));
    }

    /// <summary>DIP coordinate -&gt; pixels, rounded to nearest, halves away from zero (positions).</summary>
    public static int DipToPxRound(double dip, double scale) => ToIntClamped(Math.Round(Clamp(dip) * SanitizeScale(scale), MidpointRounding.AwayFromZero));

    /// <summary>Pixels -&gt; DIPs (no rounding: the UI works with fractional DIPs).</summary>
    public static double PxToDip(double px, double scale) => px / SanitizeScale(scale);

    /// <summary>Rounds a double pixel value (e.g. a MonitorInfo.boundsPx member) to an int.</summary>
    public static int RoundPx(double px) => ToIntClamped(Math.Round(Clamp(px), MidpointRounding.AwayFromZero));

    private static double Clamp(double v) => double.IsFinite(v) ? v : 0.0;

    private static int ToIntClamped(double v) => v >= int.MaxValue / 2 ? int.MaxValue / 2 : v <= int.MinValue / 2 ? int.MinValue / 2 : (int)v;
}
