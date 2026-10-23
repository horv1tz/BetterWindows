namespace BetterWindows.Native.Status;

/// <summary>Immutable personalisation snapshot read from the registry and system settings.</summary>
internal sealed record ThemeSnapshot(bool AppsUseLightTheme, string AccentColorHex, bool ReduceMotion, bool ReduceTransparency);

/// <summary>Colour conversions and defaults for <see cref="ThemeSnapshot"/> (unit-tested on any OS).</summary>
internal static class ThemeMath
{
    /// <summary>Windows 10/11 default accent (used when neither UISettings nor the registry yields one).</summary>
    public const string DefaultAccentHex = "#0078d4";

    /// <summary>"#rrggbb" lower-case from channel values; alpha is not part of the protocol colour.</summary>
    public static string Hex(byte r, byte g, byte b) => $"#{r:x2}{g:x2}{b:x2}";

    /// <summary>
    /// Converts the registry <c>HKCU\Software\Microsoft\Windows\DWM\AccentColor</c> DWORD. Despite the name its byte order is ABGR:
    /// the low byte is red. The alpha byte is ignored.
    /// </summary>
    public static string FromAbgr(uint abgr) =>
        Hex((byte)(abgr & 0xFF), (byte)((abgr >> 8) & 0xFF), (byte)((abgr >> 16) & 0xFF));

    /// <summary>Accepts only "#rrggbb" (case-insensitive) and returns it normalised; anything else returns null.</summary>
    public static string? NormalizeHex(string? value)
    {
        if (value is null || value.Length != 7 || value[0] != '#') return null;
        for (var i = 1; i < 7; i++)
        {
            if (!Uri.IsHexDigit(value[i])) return null;
        }
        return value.ToLowerInvariant();
    }

    /// <summary>Combined reduce-transparency rule: the user turned transparency off OR high-contrast mode is on.</summary>
    public static bool ReduceTransparency(int? enableTransparency, bool highContrast) => enableTransparency == 0 || highContrast;

    /// <summary>
    /// SPI_GETCLIENTAREAANIMATION: TRUE means animations are on. A failed query (null) keeps animations on, so an unreadable setting never disables them.
    /// </summary>
    public static bool ReduceMotion(bool? clientAreaAnimationEnabled) => clientAreaAnimationEnabled == false;

    public static bool Same(ThemeSnapshot a, ThemeSnapshot b) =>
        a.AppsUseLightTheme == b.AppsUseLightTheme && a.AccentColorHex == b.AccentColorHex
        && a.ReduceMotion == b.ReduceMotion && a.ReduceTransparency == b.ReduceTransparency;
}
