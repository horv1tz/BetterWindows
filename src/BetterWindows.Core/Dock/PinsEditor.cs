using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Dock;

/// <summary>
/// Pure edits of <c>settings.dock.pins</c>. Invariants kept by every operation: ids are unique (case-insensitive) and non-empty, app pins come before folder pins
/// (the dock draws folders after a separator), relative order is otherwise preserved, an index is clamped into the section of the item's kind.
/// </summary>
public static class PinsEditor
{
    /// <summary>Same limit as <c>DockSettings.pins</c> in protocol/settings.json.</summary>
    public const int MaxPins = 100;

    public static IReadOnlyList<PinnedItem> Normalize(IEnumerable<PinnedItem>? pins)
    {
        var apps = new List<PinnedItem>();
        var folders = new List<PinnedItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in pins ?? Array.Empty<PinnedItem>())
        {
            if (p is null || string.IsNullOrWhiteSpace(p.Id) || !seen.Add(p.Id)) continue;
            (p.Kind == PinnedKind.Folder ? folders : apps).Add(p);
        }
        apps.AddRange(folders);
        return apps;
    }

    public static bool Contains(IEnumerable<PinnedItem> pins, string id) => pins.Any(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    public static PinnedItem? Find(IEnumerable<PinnedItem> pins, string id) => pins.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Adds <paramref name="item"/> at <paramref name="index"/> (clamped; null = end of its section). An id that is already pinned leaves the list unchanged.</summary>
    public static IReadOnlyList<PinnedItem> Insert(IReadOnlyList<PinnedItem> pins, PinnedItem item, int? index)
    {
        var list = Normalize(pins).ToList();
        if (Contains(list, item.Id) || list.Count >= MaxPins) return list;
        var appCount = list.Count(p => p.Kind != PinnedKind.Folder);
        int at;
        if (item.Kind == PinnedKind.Folder) at = Math.Clamp(index ?? list.Count, appCount, list.Count);
        else at = Math.Clamp(index ?? appCount, 0, appCount);
        list.Insert(at, item);
        return list;
    }

    public static IReadOnlyList<PinnedItem> Remove(IReadOnlyList<PinnedItem> pins, string id) =>
        Normalize(pins).Where(p => !string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>
    /// New order = <paramref name="ids"/> (duplicates ignored), then every pin that was not mentioned in its old relative order (a reorder never deletes a pin).
    /// Returns false and the first unknown id when an id is not pinned.
    /// </summary>
    public static bool TryReorder(IReadOnlyList<PinnedItem> pins, IEnumerable<string> ids, out IReadOnlyList<PinnedItem> result, out string? unknownId)
    {
        var current = Normalize(pins);
        var ordered = new List<PinnedItem>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
        {
            var pin = Find(current, id);
            if (pin is null) { result = current; unknownId = id; return false; }
            if (used.Add(pin.Id)) ordered.Add(pin);
        }
        ordered.AddRange(current.Where(p => !used.Contains(p.Id)));
        result = Normalize(ordered);
        unknownId = null;
        return true;
    }
}
