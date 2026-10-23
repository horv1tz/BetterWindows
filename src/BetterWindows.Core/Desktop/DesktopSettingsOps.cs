using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Desktop;

/// <summary>Why <see cref="DesktopSettingsOps.AddShortcut"/> did not add anything.</summary>
public enum AddShortcutFailure { None, TooMany, DesktopFull }

public sealed record AddShortcutResult(Settings Settings, DesktopIcon? Icon, AddShortcutFailure Failure);

/// <summary>
/// Pure read-modify-write operations on <see cref="DesktopSettings"/> (shortcuts, icon placements, wallpapers). The controller runs them inside
/// <c>ISettingsService.UpdateAsync</c> so they always work on the freshest settings; being pure they are covered by plain unit tests. Every operation returns the SAME
/// <see cref="Settings"/> instance when nothing changed.
/// </summary>
public static class DesktopSettingsOps
{
    public static int MaxShortcuts => Limit("DesktopSettings.shortcuts", 500);
    public static int MaxPlacements => Limit("DesktopSettings.placements", 2000);
    public static int MaxWallpapers => Limit("DesktopSettings.wallpapers", 64);

    private static int Limit(string key, int fallback) => SettingsLimits.All.TryGetValue(key, out var l) && l.MaxItems is { } m ? m : fallback;

    /// <summary>The monitor an icon (by id) is shown on: a shortcut's own monitor (primary if unplugged), the primary monitor for desktop-folder items, null if unknown.</summary>
    public static MonitorInfo? MonitorOfIcon(Settings s, string id, IReadOnlyList<DesktopEntry> entries, IReadOnlyList<MonitorInfo> monitors)
    {
        if (id.StartsWith(DesktopLayout.ShortcutPrefix, StringComparison.Ordinal))
        {
            var sc = s.Desktop.Shortcuts.FirstOrDefault(x => DesktopLayout.ShortcutIconId(x) == id);
            return sc is null ? null : MonitorGeometry.Effective(monitors, sc.MonitorId);
        }
        if (!s.Desktop.ShowDesktopFolder || entries.All(e => e.Id != id)) return null;
        return MonitorGeometry.Primary(monitors);
    }

    /// <summary>Stores the cells that <see cref="DesktopLayout"/> auto-assigned to desktop-folder items, so they stay put once they exist.</summary>
    public static Settings ApplyNewPlacements(Settings s, IReadOnlyDictionary<string, GridCell> add)
    {
        if (add.Count == 0) return s;
        var map = new Dictionary<string, DesktopCell>(s.Desktop.Placements, StringComparer.Ordinal);
        var changed = false;
        foreach (var (id, c) in add)
        {
            if (!map.ContainsKey(id) && map.Count >= MaxPlacements) continue;
            var cell = new DesktopCell { X = c.Column, Y = c.Row };
            if (map.TryGetValue(id, out var old) && old.X == cell.X && old.Y == cell.Y) continue;
            map[id] = cell;
            changed = true;
        }
        return changed ? s with { Desktop = s.Desktop with { Placements = map } } : s;
    }

    /// <summary>Writes explicit cells: a shortcut keeps its cell in <c>shortcuts</c>, every other icon in <c>placements</c>.</summary>
    public static Settings SetCells(Settings s, IReadOnlyDictionary<string, GridCell> cells)
    {
        if (cells.Count == 0) return s;
        var shortcuts = s.Desktop.Shortcuts.Select(sc =>
            cells.TryGetValue(DesktopLayout.ShortcutIconId(sc), out var c) ? sc with { X = c.Column, Y = c.Row } : sc).ToArray();
        var other = cells.Where(kv => !kv.Key.StartsWith(DesktopLayout.ShortcutPrefix, StringComparison.Ordinal)).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        var next = s with { Desktop = s.Desktop with { Shortcuts = shortcuts } };
        return ApplyNewPlacements(next, other);
    }

    /// <summary>
    /// Moves an icon to <paramref name="target"/> (clamped into the grid). If another icon sits there the two SWAP places, so a drag never stacks icons and never needs a
    /// free-cell search. <c>Found</c> is false when the id is unknown (nothing changed).
    /// </summary>
    public static (Settings Settings, bool Found) MoveIcon(Settings s, string id, GridCell target, IReadOnlyList<DesktopEntry> entries, IReadOnlyList<MonitorInfo> monitors)
    {
        var monitor = MonitorOfIcon(s, id, entries, monitors);
        if (monitor is null) return (s, false);
        var layout = DesktopLayout.Resolve(s, entries, monitors, monitor);
        var current = layout.Icons.FirstOrDefault(i => i.Id == id);
        if (current is null) return (s, false);

        var to = layout.Grid.Clamp(target);
        var frozen = ApplyNewPlacements(s, layout.NewPlacements); // auto-placed items stay where the user just saw them
        if (to == current.Cell) return (frozen, true);

        var cells = new Dictionary<string, GridCell>(StringComparer.Ordinal) { [id] = to };
        var occupant = layout.Icons.FirstOrDefault(i => i.Id != id && i.Cell == to);
        if (occupant is not null) cells[occupant.Id] = current.Cell;
        return (SetCells(frozen, cells), true);
    }

    /// <summary>
    /// Adds a shortcut on <paramref name="monitor"/>. <paramref name="wanted"/> = an explicit cell (clamped; a taken cell goes to the nearest free one); null = first free
    /// cell (columns top-to-bottom, then left-to-right). <paramref name="storedMonitorId"/> is what is persisted (null = primary monitor).
    /// </summary>
    public static AddShortcutResult AddShortcut(Settings s, IReadOnlyList<DesktopEntry> entries, IReadOnlyList<MonitorInfo> monitors, MonitorInfo monitor,
        string? storedMonitorId, string id, string name, string path, GridCell? wanted)
    {
        if (s.Desktop.Shortcuts.Count >= MaxShortcuts) return new AddShortcutResult(s, null, AddShortcutFailure.TooMany);
        var layout = DesktopLayout.Resolve(s, entries, monitors, monitor);
        var occupied = layout.Icons.Select(i => i.Cell).ToHashSet();
        var cell = wanted is { } w ? layout.Grid.NearestFree(w, occupied) : layout.Grid.FirstFree(occupied);
        if (cell is not { } c) return new AddShortcutResult(s, null, AddShortcutFailure.DesktopFull);

        var shortcut = new DesktopShortcut { Id = id, Name = name, Target = path, X = c.Column, Y = c.Row, MonitorId = storedMonitorId };
        var next = ApplyNewPlacements(s, layout.NewPlacements);
        next = next with { Desktop = next.Desktop with { Shortcuts = next.Desktop.Shortcuts.Append(shortcut).ToArray() } };
        var icon = new DesktopIcon
        {
            Id = DesktopLayout.ShortcutIconId(shortcut), Name = name, Target = path, X = c.Column, Y = c.Row, Kind = DesktopIconKind.Shortcut,
            IconUrl = DynUrls.PathIcon(path, s.Desktop.IconSize),
        };
        return new AddShortcutResult(next, icon, AddShortcutFailure.None);
    }

    public static (Settings Settings, bool Found) RemoveShortcut(Settings s, string id)
    {
        var keep = s.Desktop.Shortcuts.Where(x => DesktopLayout.ShortcutIconId(x) != id).ToArray();
        if (keep.Length == s.Desktop.Shortcuts.Count) return (s, false);
        return (s with { Desktop = s.Desktop with { Shortcuts = keep } }, true);
    }

    /// <summary>
    /// Persists the cells of new desktop-folder items and prunes placements of items that no longer exist (only when the folder enumeration is non-empty: an empty one is
    /// indistinguishable from a transient failure and must not wipe the user's layout).
    /// </summary>
    public static Settings ReconcilePlacements(Settings s, IReadOnlyList<DesktopEntry> entries, IReadOnlyList<MonitorInfo> monitors)
    {
        if (!s.Desktop.Enabled || !s.Desktop.ShowDesktopFolder) return s;
        var primary = MonitorGeometry.Primary(monitors);
        if (primary is null) return s;

        var layout = DesktopLayout.Resolve(s, entries, monitors, primary);
        var next = ApplyNewPlacements(s, layout.NewPlacements);
        if (entries.Count > 0)
        {
            var live = entries.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
            var stale = next.Desktop.Placements.Keys.Where(k => !live.Contains(k)).ToArray();
            if (stale.Length > 0)
            {
                var map = new Dictionary<string, DesktopCell>(next.Desktop.Placements, StringComparer.Ordinal);
                foreach (var k in stale) map.Remove(k);
                next = next with { Desktop = next.Desktop with { Placements = map } };
            }
        }
        return next;
    }

    /// <summary>
    /// null monitor = every monitor: <c>wallpaperPath</c> becomes the default and the per-monitor overrides are cleared; otherwise only that monitor's override is set.
    /// </summary>
    public static Settings SetWallpaper(Settings s, string path, string? monitorId)
    {
        if (monitorId is null)
            return s with { Desktop = s.Desktop with { WallpaperPath = path, Wallpapers = new Dictionary<string, string>() } };
        var map = new Dictionary<string, string>(s.Desktop.Wallpapers, StringComparer.Ordinal) { [monitorId] = path };
        // The map is capped: when full, drop overrides of other monitors rather than refusing the user's choice.
        while (map.Count > MaxWallpapers) map.Remove(map.Keys.First(k => k != monitorId));
        return s with { Desktop = s.Desktop with { Wallpapers = map } };
    }

    /// <summary>The wallpaper file settings assign to a monitor: its override, else the default; null when neither is set.</summary>
    public static string? WallpaperOverride(DesktopSettings d, string monitorId)
    {
        if (d.Wallpapers.TryGetValue(monitorId, out var p) && !string.IsNullOrEmpty(p)) return p;
        return string.IsNullOrEmpty(d.WallpaperPath) ? null : d.WallpaperPath;
    }
}
