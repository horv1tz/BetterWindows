using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Desktop;

/// <summary>One icon of the resolved desktop layout (before it becomes a protocol <see cref="DesktopIcon"/>).</summary>
public sealed record LayoutIcon(string Id, DesktopIconKind Kind, string Name, string Target, GridCell Cell);

/// <summary>The icons of one monitor plus the cells that were auto-assigned to desktop-folder items that had no stored placement yet.</summary>
public sealed record DesktopLayoutResult(IReadOnlyList<LayoutIcon> Icons, IReadOnlyDictionary<string, GridCell> NewPlacements, DesktopGrid Grid);

/// <summary>
/// Pure, deterministic icon layout of one monitor: the SAME function feeds <c>desktop.state</c> snapshots, <c>desktop.moveIcon</c> and the persistence of auto-placed
/// items, so what the UI shows is exactly what gets stored. Order of claiming cells: user shortcuts (settings order), desktop-folder items that have a stored placement,
/// then desktop-folder items without one (first free cell, columns first). A stored cell that is outside the grid is clamped; one that is already taken goes to the
/// nearest free cell. Nothing here writes: stale/clamped positions of shortcuts stay as the user stored them (a smaller resolution must not destroy the layout).
/// </summary>
public static class DesktopLayout
{
    public const string ShortcutPrefix = "sc:";
    public const string FolderPrefix = "fs:";
    public const string SpecialPrefix = "special:";
    public const string ThisPcId = "special:thisPc";
    public const string RecycleBinId = "special:recycleBin";
    public const string ThisPcTarget = "shell:MyComputerFolder";
    public const string RecycleBinTarget = "shell:RecycleBinFolder";

    /// <summary>Icon id of a stored shortcut ("sc:" prefix is added for hand-written ids that lack it).</summary>
    public static string ShortcutIconId(DesktopShortcut s) => s.Id.StartsWith(ShortcutPrefix, StringComparison.Ordinal) ? s.Id : ShortcutPrefix + s.Id;

    /// <summary>The only two shell: literals a special icon may open (the Windows launcher allowlists exactly these).</summary>
    public static string? SpecialTarget(string id) => id switch { ThisPcId => ThisPcTarget, RecycleBinId => RecycleBinTarget, _ => null };

    public static DesktopIconKind KindOf(DesktopEntry e) => e.IsSpecial ? DesktopIconKind.Special : e.IsFolder ? DesktopIconKind.Folder : DesktopIconKind.File;

    /// <summary>Grid of a monitor for the current settings.</summary>
    public static DesktopGrid GridFor(MonitorInfo monitor, int iconSize) => new(iconSize, MonitorGeometry.WorkAreaDip(monitor));

    /// <summary>The user shortcuts shown on <paramref name="monitor"/> (a shortcut of an unplugged monitor falls back to the primary one).</summary>
    public static IEnumerable<DesktopShortcut> ShortcutsOn(Settings settings, IReadOnlyList<MonitorInfo> monitors, MonitorInfo monitor) =>
        settings.Desktop.Shortcuts.Where(s => MonitorGeometry.Effective(monitors, s.MonitorId)?.Id == monitor.Id);

    public static DesktopLayoutResult Resolve(Settings settings, IReadOnlyList<DesktopEntry> folderEntries, IReadOnlyList<MonitorInfo> monitors, MonitorInfo monitor)
    {
        var d = settings.Desktop;
        var grid = GridFor(monitor, d.IconSize);
        var isPrimary = MonitorGeometry.Primary(monitors)?.Id == monitor.Id;
        var occupied = new HashSet<GridCell>();
        var icons = new List<LayoutIcon>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var newPlacements = new Dictionary<string, GridCell>(StringComparer.Ordinal);

        void Place(string id, DesktopIconKind kind, string name, string target, GridCell? stored)
        {
            GridCell? cell;
            if (stored is { } s)
            {
                var clamped = grid.Clamp(s);
                cell = occupied.Contains(clamped) ? grid.NearestFree(clamped, occupied) : clamped;
            }
            else cell = grid.FirstFree(occupied);
            // A completely full grid: stack on the last cell rather than losing the icon.
            var final = cell ?? new GridCell(grid.Columns - 1, grid.Rows - 1);
            occupied.Add(final);
            icons.Add(new LayoutIcon(id, kind, name, target, final));
            if (stored is null) newPlacements[id] = final;
        }

        foreach (var sc in ShortcutsOn(settings, monitors, monitor))
        {
            var id = ShortcutIconId(sc);
            if (!seen.Add(id)) continue;
            Place(id, DesktopIconKind.Shortcut, sc.Name, sc.Target, new GridCell(sc.X, sc.Y)); // shortcuts always carry their own cell: never a new placement
        }

        if (isPrimary && d.ShowDesktopFolder)
        {
            var entries = folderEntries.Where(e => !string.IsNullOrEmpty(e.Id)).ToList();
            foreach (var e in entries)
                if (d.Placements.TryGetValue(e.Id, out var p) && seen.Add(e.Id))
                    Place(e.Id, KindOf(e), e.Name, e.Target, new GridCell(p.X, p.Y));
            foreach (var e in entries)
                if (seen.Add(e.Id))
                    Place(e.Id, KindOf(e), e.Name, e.Target, null);
        }

        return new DesktopLayoutResult(icons, newPlacements, grid);
    }
}
