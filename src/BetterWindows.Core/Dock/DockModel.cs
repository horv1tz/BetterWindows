using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Core.WindowTracking;

namespace BetterWindows.Core.Dock;

/// <summary>Everything <see cref="DockModel.Build"/> depends on. All state is passed in: the model reads neither the clock nor any service.</summary>
public sealed record DockModelInput
{
    /// <summary>settings.dock.pins (user order; normalised by the model: apps first, unique).</summary>
    public IReadOnlyList<PinnedItem> Pins { get; init; } = Array.Empty<PinnedItem>();
    /// <summary>One consistent view of windows + foreground from the tracker.</summary>
    public WindowSnapshot Windows { get; init; } = WindowSnapshot.Empty;
    /// <summary>Catalog lookup by app id (IAppCatalog.FindById).</summary>
    public Func<string, AppEntry?> FindApp { get; init; } = _ => null;
    /// <summary>First-seen order of running apps (<see cref="RunningOrder"/>); apps missing from it come last, alphabetically.</summary>
    public IReadOnlyList<string> RunningOrder { get; init; } = Array.Empty<string>();
    /// <summary>App ids with a launch in flight (not yet running, not older than 20 s): pinned ones get <c>launching</c>, unpinned ones a transient item.</summary>
    public IReadOnlyList<string> Launching { get; init; } = Array.Empty<string>();
    /// <summary>Virtual desktop id of a window, asked only for windows that live on another desktop. null = unknown.</summary>
    public Func<long, string?>? DesktopOf { get; init; }
}

/// <summary>
/// Pure dock model (docs/IPC.md section 5): pinned apps in user order, then running-but-unpinned apps in first-seen order (transient "launching" items last),
/// then - only when both parts exist - <c>separator:1</c> and the pinned folders; plus the minimised windows (right-hand section). Deterministic: the same input
/// always yields the same <see cref="DockState"/>. Icons are requested at 256 px. A window on another virtual desktop counts as running.
/// </summary>
public static class DockModel
{
    public const string SeparatorId = "separator:1";
    private const int MaxTitleChars = 256;

    public static DockState Build(DockModelInput input)
    {
        var pins = PinsEditor.Normalize(input.Pins);
        var appPins = pins.Where(p => p.Kind != PinnedKind.Folder).ToList();
        var folderPins = pins.Where(p => p.Kind == PinnedKind.Folder).ToList();
        var snapshot = input.Windows;
        var foreground = snapshot.Foreground?.Handle ?? 0;
        var catalog = new Dictionary<string, AppEntry?>(StringComparer.OrdinalIgnoreCase);
        AppEntry? Find(string id)
        {
            if (!catalog.TryGetValue(id, out var e)) catalog[id] = e = input.FindApp(id);
            return e;
        }

        var matcher = new PinMatcher(appPins);
        var groups = new Dictionary<string, List<TrackedWindow>>(StringComparer.OrdinalIgnoreCase);
        foreach (var w in snapshot.Windows)
        {
            var key = matcher.KeyFor(w);
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<TrackedWindow>();
            list.Add(w);
        }
        var launching = new HashSet<string>(input.Launching, StringComparer.OrdinalIgnoreCase);

        var items = new List<DockItem>();

        // 1. pinned apps, user order
        foreach (var pin in appPins)
        {
            groups.Remove(pin.Id, out var wins);
            items.Add(AppItem(pin.Id, pin, wins ?? new List<TrackedWindow>(), pinned: true, launching.Contains(pin.Id)));
        }

        // 2. running, unpinned: first-seen order, then the rest alphabetically
        var order = input.RunningOrder.Select((id, i) => (id, i)).GroupBy(t => t.id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().i, StringComparer.OrdinalIgnoreCase);
        var remaining = groups
            .Select(g => (Key: g.Key, Windows: g.Value, Name: g.Value[0].AppName))
            .OrderBy(g => order.TryGetValue(g.Key, out var i) ? i : int.MaxValue)
            .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var g in remaining) items.Add(AppItem(g.Key, null, g.Windows, pinned: false, launching: false));

        // 2b. transient items of apps being launched
        var shown = new HashSet<string>(items.Select(i => i.Id), StringComparer.OrdinalIgnoreCase);
        foreach (var id in input.Launching)
        {
            if (string.IsNullOrWhiteSpace(id) || !shown.Add(id)) continue;
            items.Add(AppItem(id, null, new List<TrackedWindow>(), pinned: false, launching: true));
        }

        // 3. folders after a separator
        if (folderPins.Count > 0)
        {
            if (items.Count > 0) items.Add(new DockItem { Id = SeparatorId, Kind = DockItemKind.Separator });
            foreach (var f in folderPins) items.Add(FolderItem(f));
        }

        var minimized = snapshot.Windows.Where(w => w.Raw.IsMinimized).Select(w => Ref(w)).ToList();
        var active = items.FirstOrDefault(i => i.Active);
        return new DockState { Items = items, Minimized = minimized, ActiveItemId = active?.Id };

        DockItem AppItem(string id, PinnedItem? pin, List<TrackedWindow> wins, bool pinned, bool launching)
        {
            var entry = Find(id);
            var refs = wins.Select(w => Ref(w)).ToList();
            var isRunning = wins.Count > 0;
            return new DockItem
            {
                Id = id,
                Kind = DockItemKind.App,
                AppId = id,
                Name = !string.IsNullOrWhiteSpace(entry?.Name) ? entry!.Name
                    : !string.IsNullOrWhiteSpace(pin?.Name) ? pin!.Name!
                    : isRunning ? wins[0].AppName
                    : NameFromId(id, pin),
                IconUrl = IconFor(id, pin, entry is not null, wins),
                Pinned = pinned,
                Running = isRunning,
                Active = wins.Any(w => w.Handle == foreground),
                Attention = wins.Any(w => w.Raw.IsFlashing && w.Handle != foreground),
                Windows = refs,
                Launching = launching && !isRunning,
            };
        }

        DockItem FolderItem(PinnedItem f)
        {
            var path = f.Target;
            var name = !string.IsNullOrWhiteSpace(f.Name) ? f.Name!
                : AppNames.FileName(path) is { Length: > 0 } leaf ? leaf : path;
            return new DockItem
            {
                Id = f.Id,
                Kind = DockItemKind.Folder,
                Name = name,
                IconUrl = DynUrls.PathIcon(path, AppIconUrls.DockIconPx),
                Target = path,
                Pinned = true,
            };
        }

        WindowRef Ref(TrackedWindow w)
        {
            var onOtherDesktop = WindowFilter.IsOnOtherDesktop(w.Raw);
            string? desktop = null;
            if (onOtherDesktop && input.DesktopOf is not null)
            {
                try { desktop = input.DesktopOf(w.Handle); } catch (Exception) { desktop = null; }
            }
            return new WindowRef
            {
                Handle = Hwnd.ToWire(w.Handle),
                Title = InputPolicy.Truncate(w.Raw.Title, MaxTitleChars),
                AppId = w.AppId,
                IconUrl = AppIconUrls.ForApp(w.AppId, Find(w.AppId) is not null, w.Handle),
                Minimized = w.Raw.IsMinimized,
                Maximized = w.Raw.IsMaximized,
                Active = w.Handle == foreground,
                DesktopId = desktop,
                Elevated = w.Raw.IsElevated,
            };
        }
    }

    private static string IconFor(string id, PinnedItem? pin, bool inCatalog, List<TrackedWindow> wins)
    {
        if (inCatalog) return DynUrls.AppIcon(id, AppIconUrls.DockIconPx);
        // One stable window per item (the lowest handle) so the URL does not change while the user switches windows.
        if (wins.Count > 0) return DynUrls.WindowIcon(wins.Min(w => w.Handle), AppIconUrls.DockIconPx);
        if (pin is not null && id.StartsWith("exe:", StringComparison.OrdinalIgnoreCase) && AppNames.CleanPath(pin.Target) is { } path)
            return DynUrls.PathIcon(path, AppIconUrls.DockIconPx);
        return DynUrls.AppIcon(id, AppIconUrls.DockIconPx);
    }

    /// <summary>Best-effort display name for an item that is neither in the catalog, nor named by its pin, nor running.</summary>
    private static string NameFromId(string id, PinnedItem? pin)
    {
        if (pin is not null && !string.IsNullOrWhiteSpace(pin.Target))
            return id.StartsWith("aumid:", StringComparison.OrdinalIgnoreCase) ? AppNames.FromAumid(pin.Target) : AppNames.FromExePath(pin.Target);
        if (id.StartsWith("aumid:", StringComparison.OrdinalIgnoreCase)) return AppNames.FromAumid(id[6..]);
        if (id.StartsWith("exe:", StringComparison.OrdinalIgnoreCase)) return AppNames.FromExePath(id[4..]);
        return id;
    }
}
