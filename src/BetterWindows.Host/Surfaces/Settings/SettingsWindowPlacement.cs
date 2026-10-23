using System.Text.Json;
using System.Text.Json.Serialization;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Host.Surfaces;

/// <summary>Remembered placement of the settings window (state\window-settings.json): the RESTORED rectangle in physical px plus the DPI it was measured at.</summary>
internal sealed record SettingsWindowState
{
    [JsonPropertyName("left")] public int Left { get; init; }
    [JsonPropertyName("top")] public int Top { get; init; }
    [JsonPropertyName("width")] public int Width { get; init; }
    [JsonPropertyName("height")] public int Height { get; init; }
    [JsonPropertyName("dpi")] public int Dpi { get; init; } = 96;
    [JsonPropertyName("maximized")] public bool Maximized { get; init; }
}

/// <summary>Where the settings window opens: a pixel rectangle on a monitor, whether it starts maximised, and that monitor's scale.</summary>
internal readonly record struct SettingsWindowLayout(NativeRect BoundsPx, bool Maximized, double Scale);

/// <summary>
/// Pure placement rules of the settings window: 980x680 DIP, minimum 760x520 DIP, centred on first open; later opens restore the remembered rectangle but only if it is
/// still reasonably visible on a connected monitor (a window remembered on an unplugged monitor must not open off-screen), re-scaled when the monitor's DPI changed.
/// </summary>
internal static class SettingsWindowPlacement
{
    public const double DefaultWidthDip = 980, DefaultHeightDip = 680, MinWidthDip = 760, MinHeightDip = 520;
    /// <summary>At least this much of the remembered window must overlap a work area to be considered "visible".</summary>
    private const int MinVisibleWidthPx = 160, MinVisibleHeightPx = 100;

    public static SettingsWindowLayout Resolve(SettingsWindowState? saved, IReadOnlyList<MonitorInfo> monitors, MonitorInfo? preferredForDefault = null)
    {
        if (monitors.Count == 0)
        {
            return new SettingsWindowLayout(new NativeRect(0, 0, (int)DefaultWidthDip, (int)DefaultHeightDip), false, 1.0);
        }
        if (saved is not null && IsSane(saved))
        {
            var rect = new NativeRect(saved.Left, saved.Top, saved.Left + saved.Width, saved.Top + saved.Height);
            var target = BestMonitor(rect, monitors);
            if (target is not null)
            {
                var scale = MonitorGeometry.Scale(target);
                var savedScale = DpiMath.SanitizeScale(saved.Dpi / DpiMath.BaseDpi);
                var work = MonitorGeometry.WorkAreaPx(target);
                var wDip = Math.Max(MinWidthDip, saved.Width / savedScale);
                var hDip = Math.Max(MinHeightDip, saved.Height / savedScale);
                var w = Math.Min(DpiMath.DipToPxCeil(wDip, scale), work.Width);
                var h = Math.Min(DpiMath.DipToPxCeil(hDip, scale), work.Height);
                int left, top;
                if (Math.Abs(scale - savedScale) < 0.005) { left = saved.Left; top = saved.Top; }
                else
                {
                    // DPI of the monitor changed since the window was saved: keep its centre, not its corner.
                    left = saved.Left + saved.Width / 2 - w / 2;
                    top = saved.Top + saved.Height / 2 - h / 2;
                }
                left = Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - w));
                top = Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - h));
                return new SettingsWindowLayout(new NativeRect(left, top, left + w, top + h), saved.Maximized, scale);
            }
        }
        return Default(preferredForDefault ?? MonitorGeometry.Primary(monitors)!);
    }

    public static SettingsWindowLayout Default(MonitorInfo monitor)
    {
        var scale = MonitorGeometry.Scale(monitor);
        var work = MonitorGeometry.WorkAreaPx(monitor);
        var w = Math.Min(DpiMath.DipToPxCeil(DefaultWidthDip, scale), work.Width);
        var h = Math.Min(DpiMath.DipToPxCeil(DefaultHeightDip, scale), work.Height);
        var left = work.Left + (work.Width - w) / 2;
        var top = work.Top + (work.Height - h) / 2;
        return new SettingsWindowLayout(new NativeRect(left, top, left + w, top + h), false, scale);
    }

    /// <summary>Minimum tracking size in physical px for a given scale (WM_GETMINMAXINFO / MinWidth).</summary>
    public static (int Width, int Height) MinSizePx(double scale) => (DpiMath.DipToPxCeil(MinWidthDip, scale), DpiMath.DipToPxCeil(MinHeightDip, scale));

    private static bool IsSane(SettingsWindowState s) =>
        s.Width >= 200 && s.Height >= 150 && s.Width <= 32000 && s.Height <= 32000 && s.Dpi is >= 48 and <= 768
        && s.Left > -64000 && s.Left < 64000 && s.Top > -64000 && s.Top < 64000;

    private static MonitorInfo? BestMonitor(NativeRect rect, IReadOnlyList<MonitorInfo> monitors)
    {
        MonitorInfo? best = null;
        long bestArea = 0;
        foreach (var m in monitors)
        {
            var inter = MonitorGeometry.Intersect(rect, MonitorGeometry.WorkAreaPx(m));
            if (inter.Width < Math.Min(MinVisibleWidthPx, rect.Width) || inter.Height < Math.Min(MinVisibleHeightPx, rect.Height)) continue;
            long area = (long)inter.Width * inter.Height;
            if (area > bestArea) { bestArea = area; best = m; }
        }
        return best;
    }
}

/// <summary>(De)serialisation of <see cref="SettingsWindowState"/>; a damaged file simply means "no remembered placement".</summary>
internal static class SettingsWindowStateFile
{
    public const string FileName = "window-settings.json";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Serialize(SettingsWindowState state) => JsonSerializer.Serialize(state, Options);

    public static SettingsWindowState? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 4096) return null;
        try { return JsonSerializer.Deserialize<SettingsWindowState>(json, Options); }
        catch (JsonException) { return null; }
        catch (NotSupportedException) { return null; }
    }
}

/// <summary>Localised title of the settings window: settings.general.language 'en' | 'ru' | 'auto' (follows the Windows UI language).</summary>
internal static class SettingsWindowTitle
{
    public const string En = "BetterWindows Settings";
    public const string Ru = "Настройки BetterWindows";

    public static string For(string? language, string? uiCultureName)
    {
        var lang = (language ?? "auto").Trim().ToLowerInvariant();
        if (lang == "ru") return Ru;
        if (lang == "en") return En;
        return uiCultureName is not null && uiCultureName.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ? Ru : En;
    }

    /// <summary>Pages the settings route accepts (settings.openWindow / settings.navigate); anything else opens the default page.</summary>
    private static readonly HashSet<string> Pages = new(StringComparer.Ordinal)
    {
        "general", "appearance", "dock", "menubar", "spotlight", "hotCorners", "desktop", "widgets", "shortcuts", "about", "onboarding",
    };

    public static string? NormalizePage(string? page) => page is not null && Pages.Contains(page) ? page : null;
}
