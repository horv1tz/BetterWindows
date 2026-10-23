using BetterWindows.Core.Protocol;

namespace BetterWindows.Native.Status;

/// <summary>Brightness scaling between the protocol's 0..1 level and WMI's 0..100 percent (unit-tested on any OS).</summary>
internal static class BrightnessMath
{
    /// <summary>0..1 level to whole percent. NaN becomes 0; out-of-range values are clamped.</summary>
    public static int ToPercent(double level01)
    {
        if (double.IsNaN(level01)) return 0;
        return (int)Math.Round(Math.Clamp(level01, 0.0, 1.0) * 100.0, MidpointRounding.AwayFromZero);
    }

    /// <summary>WMI percent (0..100; anything else is clamped) to the protocol's 0..1 level.</summary>
    public static double FromPercent(int percent) => Math.Clamp(percent, 0, 100) / 100.0;

    public static bool Same(BrightnessState a, BrightnessState b) => a.Supported == b.Supported && a.Level == b.Level;
}
