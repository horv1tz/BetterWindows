using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;

namespace BetterWindows.Core.Config;

/// <summary>
/// THE settings validator (docs/IPC.md section 12), used for every load, reload, patch and update so all paths produce the same normalised value.
/// <see cref="Bind"/> turns RAW json into a typed value (missing/null/wrong-typed members take defaults, never failing the whole file) and <see cref="Normalize"/> applies the
/// value rules: numeric limits from <see cref="SettingsLimits"/> (clamped), list caps (truncated), strings trimmed and bounded, every path through
/// <see cref="InputPolicy.TryNormalizeLocalPath"/> (invalid entries dropped), unique ids, hotkey grammar, the web-search URL template and the accent/language patterns.
/// <see cref="Normalize"/> is idempotent.
/// </summary>
public static class SettingsValidator
{
    private const int MaxText = 512, MaxPath = 4096, MaxId = 128, MaxAppId = 4200, MaxMonitorId = 64, GridMax = 10000;
    private const int MaxWidgetConfigBytes = 64 * 1024, MaxTotalWidgetConfigBytes = 1024 * 1024;

    private static readonly Regex AccentPattern = new(SettingsLimits.All["AppearanceSettings.accent"].Pattern!, RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex LanguagePattern = new(SettingsLimits.All["GeneralSettings.language"].Pattern!, RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly HashSet<string> ProviderNames = new(Enum.GetValues<SearchProviderId>().Select(ProviderName), StringComparer.Ordinal);

    private static string ProviderName(SearchProviderId p) => char.ToLowerInvariant(p.ToString()[0]) + p.ToString()[1..];

    /// <summary>Tolerant bind of RAW json (a whole config.json root or a merged patch result) followed by <see cref="Normalize"/>. A non-object root yields defaults.</summary>
    public static Settings Bind(JsonNode? raw, List<string>? issues = null) => Normalize(SettingsBinder.Bind<Settings>(raw, issues));

    /// <summary>Applies all value rules. Idempotent; never throws.</summary>
    public static Settings Normalize(Settings s)
    {
        s ??= new Settings();
        var spotlight = NormalizeSpotlight(s.Spotlight ?? new SpotlightSettings());
        return s with
        {
            Appearance = NormalizeAppearance(s.Appearance ?? new AppearanceSettings()),
            Dock = NormalizeDock(s.Dock ?? new DockSettings()),
            MenuBar = s.MenuBar ?? new MenuBarSettings(),
            Spotlight = spotlight with { Hotkey = NormalizeOptionalHotkey(spotlight.Hotkey, new SpotlightSettings().Hotkey) },
            Hotkeys = NormalizeHotkeys(s.Hotkeys ?? new HotkeySettings()),
            HotCorners = NormalizeHotCorners(s.HotCorners ?? new HotCornerSettings()),
            Taskbar = s.Taskbar ?? new TaskbarSettings(),
            Desktop = NormalizeDesktop(s.Desktop ?? new DesktopSettings()),
            Weather = NormalizeWeather(s.Weather ?? new WeatherSettings()),
            Launchpad = NormalizeLaunchpad(s.Launchpad ?? new LaunchpadSettings()),
            General = NormalizeGeneral(s.General ?? new GeneralSettings()),
        };
    }

    // ---- helpers -------------------------------------------------------------------------------------------------------------------------------------

    private static FieldLimit Lim(string key) => SettingsLimits.All[key];

    private static int ClampInt(int v, string key)
    {
        var l = Lim(key);
        return Math.Clamp(v, (int)l.Min!.Value, (int)l.Max!.Value);
    }

    private static double ClampDouble(double v, string key, double fallback)
    {
        var l = Lim(key);
        return double.IsFinite(v) ? Math.Clamp(v, l.Min!.Value, l.Max!.Value) : fallback;
    }

    /// <summary>Display text: control characters become spaces, trimmed, cut at <paramref name="max"/>.</summary>
    private static string Text(string? s, int max) => InputPolicy.Truncate(s, max).Trim();

    /// <summary>An identifier: non-empty after trimming, at most <paramref name="max"/> chars, no control characters (otherwise null).</summary>
    private static string? Id(string? s, int max)
    {
        if (s is null) return null;
        var t = s.Trim();
        if (t.Length == 0 || t.Length > max) return null;
        foreach (var c in t) if (c < 0x20 || c == 0x7f) return null;
        return t;
    }

    private static string? MonitorId(string? s) => Id(s, MaxMonitorId);

    private static IReadOnlyList<T> Cap<T>(List<T> list, string key) => list.Count > (Lim(key).MaxItems ?? int.MaxValue) ? list.Take(Lim(key).MaxItems!.Value).ToList() : list;

    private static string? LocalPath(string? s, int max = MaxPath)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim();
        return t.Length <= max && InputPolicy.TryNormalizeLocalPath(t, out var p) ? p : null;
    }

    // ---- sections ------------------------------------------------------------------------------------------------------------------------------------

    private static AppearanceSettings NormalizeAppearance(AppearanceSettings a)
    {
        var accent = (a.Accent ?? "").Trim();
        return a with { Accent = AccentPattern.IsMatch(accent) ? accent : "system" };
    }

    private static DockSettings NormalizeDock(DockSettings d)
    {
        var icon = ClampInt(d.IconSize, "DockSettings.iconSize");
        // magnifiedSize is clamped to [iconSize, 192]; the generated minimum (32) is only the absolute floor.
        var magnified = Math.Max(icon, ClampInt(d.MagnifiedSize, "DockSettings.magnifiedSize"));
        return d with { IconSize = icon, MagnifiedSize = magnified, Pins = NormalizePins(d.Pins) };
    }

    private static IReadOnlyList<PinnedItem> NormalizePins(IReadOnlyList<PinnedItem>? pins)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<PinnedItem>();
        var maxTarget = Lim("PinnedItem.target").MaxLength ?? MaxPath;
        foreach (var p in pins ?? Array.Empty<PinnedItem>())
        {
            if (p is null) continue;
            var id = (p.Id ?? "").Trim();
            if (id.Length > MaxAppId || !InputPolicy.TryParseAppId(id, out var idKind, out _)) continue;
            var target = (p.Target ?? "").Trim();
            if (target.Length == 0 || target.Length > maxTarget) continue;
            foreach (var c in target) if (c < 0x20) { target = ""; break; }
            if (target.Length == 0) continue;
            // The id decides what the pin is; the target must then be consistent with it (a path for exe/dir pins).
            var kind = idKind == AppIdKind.Directory ? PinnedKind.Folder : PinnedKind.App;
            if (idKind != AppIdKind.Aumid)
            {
                var np = LocalPath(target, maxTarget);
                if (np is null) continue;
                target = np;
            }
            if (!seen.Add(id)) continue;
            var name = Text(p.Name, 256);
            result.Add(p with { Id = id, Kind = kind, Target = target, Name = name.Length == 0 ? null : name });
        }
        return Cap(result, "DockSettings.pins");
    }

    private static SpotlightSettings NormalizeSpotlight(SpotlightSettings s)
    {
        var defaults = new SpotlightSettings();
        var providers = new List<string>();
        foreach (var raw in s.Providers ?? Array.Empty<string>())
        {
            var name = (raw ?? "").Trim();
            var canonical = ProviderNames.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            if (canonical is not null && !providers.Contains(canonical)) providers.Add(canonical);
        }
        if (providers.Count == 0 && (s.Providers?.Count ?? 0) > 0) providers.AddRange(defaults.Providers); // all entries were garbage: do not silently disable search
        return s with
        {
            Providers = Cap(providers, "SpotlightSettings.providers"),
            WebSearchUrl = ValidWebSearchUrl(s.WebSearchUrl) ? s.WebSearchUrl.Trim() : defaults.WebSearchUrl,
            MaxResults = ClampInt(s.MaxResults, "SpotlightSettings.maxResults"),
        };
    }

    /// <summary>http(s) URL template with exactly one {query} placeholder (substituted with Uri.EscapeDataString by the search provider).</summary>
    public static bool ValidWebSearchUrl(string? url)
    {
        if (url is null) return false;
        var t = url.Trim();
        if (t.Length == 0 || t.Length > (Lim("SpotlightSettings.webSearchUrl").MaxLength ?? 512)) return false;
        var first = t.IndexOf("{query}", StringComparison.Ordinal);
        if (first < 0 || t.IndexOf("{query}", first + 1, StringComparison.Ordinal) >= 0) return false;
        return InputPolicy.IsSafeWebUrl(t, out _);
    }

    private static string NormalizeOptionalHotkey(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        return HotkeySyntax.TryCanonicalize(value, out var c) ? c : fallback;
    }

    private static HotkeySettings NormalizeHotkeys(HotkeySettings h)
    {
        var d = new HotkeySettings();
        var exit = Required(h.Exit, d.Exit);
        var toggle = Required(h.ToggleTaskbar, d.ToggleTaskbar);
        // The escape-hatch hotkeys must differ: otherwise only one registration could succeed and the other path to restoring the taskbar would be fake.
        if (string.Equals(exit, toggle, StringComparison.Ordinal))
        {
            toggle = d.ToggleTaskbar;
            if (string.Equals(exit, toggle, StringComparison.Ordinal)) { exit = d.Exit; }
        }
        return h with
        {
            Launchpad = NormalizeOptionalHotkey(h.Launchpad, d.Launchpad),
            MissionControl = NormalizeOptionalHotkey(h.MissionControl, d.MissionControl),
            Exit = exit,
            ToggleTaskbar = toggle,
        };

        static string Required(string? value, string fallback) => HotkeySyntax.TryCanonicalize(value, out var c) ? c : fallback;
    }

    private static HotCornerSettings NormalizeHotCorners(HotCornerSettings h) => h with { DwellMs = ClampInt(h.DwellMs, "HotCornerSettings.dwellMs") };

    private static GeneralSettings NormalizeGeneral(GeneralSettings g)
    {
        var lang = (g.Language ?? "").Trim();
        return g with { Language = LanguagePattern.IsMatch(lang) ? lang : "auto", Features = g.Features ?? new FeatureFlags() };
    }

    private static WeatherSettings NormalizeWeather(WeatherSettings w)
    {
        static double? Coord(double? v, string key)
        {
            if (v is not { } d || !double.IsFinite(d)) return null;
            var l = Lim(key);
            return d >= l.Min!.Value && d <= l.Max!.Value ? d : null; // an out-of-range coordinate is garbage, not a place: forget it instead of clamping to a pole
        }
        return w with { LocationName = Text(w.LocationName, Lim("WeatherSettings.locationName").MaxLength ?? 256), Latitude = Coord(w.Latitude, "WeatherSettings.latitude"), Longitude = Coord(w.Longitude, "WeatherSettings.longitude") };
    }

    private static DesktopSettings NormalizeDesktop(DesktopSettings d)
    {
        var wallpapers = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in d.Wallpapers ?? new Dictionary<string, string>())
        {
            var key = MonitorId(kv.Key);
            var path = LocalPath(kv.Value);
            if (key is not null && path is not null) wallpapers[key] = path;
        }
        var placements = new SortedDictionary<string, DesktopCell>(StringComparer.Ordinal);
        foreach (var kv in d.Placements ?? new Dictionary<string, DesktopCell>())
        {
            var key = Id(kv.Key, 512);
            if (key is null || kv.Value is null) continue;
            placements[key] = new DesktopCell { X = Math.Clamp(kv.Value.X, 0, GridMax), Y = Math.Clamp(kv.Value.Y, 0, GridMax) };
        }
        return d with
        {
            WallpaperPath = LocalPath(d.WallpaperPath),
            IconSize = ClampInt(d.IconSize, "DesktopSettings.iconSize"),
            Shortcuts = NormalizeShortcuts(d.Shortcuts),
            Widgets = NormalizeWidgets(d.Widgets),
            Wallpapers = CapMap(wallpapers, "DesktopSettings.wallpapers"),
            Placements = CapMap(placements, "DesktopSettings.placements"),
        };
    }

    private static IReadOnlyDictionary<string, T> CapMap<T>(SortedDictionary<string, T> map, string key)
    {
        var max = Lim(key).MaxItems ?? int.MaxValue;
        return map.Count <= max ? new Dictionary<string, T>(map) : map.Take(max).ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    private static IReadOnlyList<DesktopShortcut> NormalizeShortcuts(IReadOnlyList<DesktopShortcut>? shortcuts)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<DesktopShortcut>();
        foreach (var sc in shortcuts ?? Array.Empty<DesktopShortcut>())
        {
            if (sc is null) continue;
            var id = Id(sc.Id, MaxId);
            if (id is null || !seen.Add(id)) continue;
            var target = (sc.Target ?? "").Trim();
            string? good = InputPolicy.IsSafeWebUrl(target, out _) ? target : LocalPath(target, Lim("DesktopShortcut.target").MaxLength ?? MaxPath);
            if (good is null) { seen.Remove(id); continue; }
            result.Add(sc with
            {
                Id = id,
                Name = Text(sc.Name, Lim("DesktopShortcut.name").MaxLength ?? 256),
                Target = good,
                X = Math.Clamp(sc.X, 0, GridMax),
                Y = Math.Clamp(sc.Y, 0, GridMax),
                MonitorId = MonitorId(sc.MonitorId),
            });
        }
        return Cap(result, "DesktopSettings.shortcuts");
    }

    private static IReadOnlyList<WidgetInstance> NormalizeWidgets(IReadOnlyList<WidgetInstance>? widgets)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<WidgetInstance>();
        foreach (var w in widgets ?? Array.Empty<WidgetInstance>())
        {
            if (w is null) continue;
            var id = Id(w.Id, MaxId);
            if (id is null || !seen.Add(id)) continue;
            result.Add(w with
            {
                Id = id,
                X = ClampDouble(w.X, "WidgetInstance.x", 0),
                Y = ClampDouble(w.Y, "WidgetInstance.y", 0),
                Config = NormalizeWidgetConfig(w.Config),
                MonitorId = MonitorId(w.MonitorId),
            });
        }
        var capped = Cap(result, "DesktopSettings.widgets").ToList();
        // Bound the total size of free-form widget configs so config.json can never outgrow its 2 MB cap through widget notes.
        var total = capped.Sum(w => w.Config is null ? 0 : w.Config.ToJsonString().Length);
        while (total > MaxTotalWidgetConfigBytes)
        {
            var biggest = capped.Select((w, i) => (i, size: w.Config?.ToJsonString().Length ?? 0)).OrderByDescending(x => x.size).First();
            capped[biggest.i] = capped[biggest.i] with { Config = null };
            total -= biggest.size;
        }
        return capped;
    }

    /// <summary>Widget config must be a JSON object of bounded size; anything else is dropped (the widget then uses its defaults).</summary>
    private static JsonNode? NormalizeWidgetConfig(JsonNode? config)
    {
        if (config is not JsonObject o) return null;
        return o.ToJsonString().Length <= MaxWidgetConfigBytes ? o : null;
    }

    private static LaunchpadSettings NormalizeLaunchpad(LaunchpadSettings l)
    {
        var folders = new List<LaunchpadFolder>();
        var folderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inFolder = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var appCap = Lim("LaunchpadFolder.appIds").MaxItems ?? 200;
        foreach (var f in l.Folders ?? Array.Empty<LaunchpadFolder>())
        {
            if (f is null) continue;
            var id = Id(f.Id, MaxId);
            if (id is null || !folderIds.Add(id)) continue;
            var apps = new List<string>();
            foreach (var a in f.AppIds ?? Array.Empty<string>())
            {
                var app = (a ?? "").Trim();
                if (app.Length > MaxAppId || !InputPolicy.TryParseAppId(app, out _, out _)) continue;
                if (apps.Count >= appCap) break;
                if (inFolder.Add(app)) apps.Add(app); // an app lives in at most one folder
            }
            folders.Add(f with { Id = id, Name = Text(f.Name, Lim("LaunchpadFolder.name").MaxLength ?? 128), AppIds = apps });
        }

        var hidden = new List<string>();
        var hiddenSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in l.Hidden ?? Array.Empty<string>())
        {
            var app = (h ?? "").Trim();
            if (app.Length <= MaxAppId && InputPolicy.TryParseAppId(app, out _, out _) && hiddenSet.Add(app)) hidden.Add(app);
        }

        var order = new List<string>();
        var orderSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in l.Order ?? Array.Empty<string>())
        {
            var item = (o ?? "").Trim();
            if (item.Length == 0 || item.Length > MaxAppId) continue;
            var isFolder = folderIds.Contains(item);
            var isApp = !isFolder && InputPolicy.TryParseAppId(item, out _, out _);
            if (!isFolder && !isApp) continue;
            if (isApp && (inFolder.Contains(item) || hiddenSet.Contains(item))) continue; // members of folders / hidden apps never appear at top level
            if (orderSet.Add(item)) order.Add(item);
        }

        return l with
        {
            Folders = Cap(folders, "LaunchpadSettings.folders"),
            Hidden = Cap(hidden, "LaunchpadSettings.hidden"),
            Order = Cap(order, "LaunchpadSettings.order"),
        };
    }
}
