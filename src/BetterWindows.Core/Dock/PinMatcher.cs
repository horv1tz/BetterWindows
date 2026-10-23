using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Core.WindowTracking;

namespace BetterWindows.Core.Dock;

/// <summary>
/// Decides which dock item a window belongs to. A window belongs to a pinned app when (1) its tracked app id equals the pin id, or - for windows whose identity
/// was resolved differently than the pin (a pinned exe that the catalog knows by AUMID, a Chrome window with its own AUMID) - (2) one of the window's aliases
/// (app id, "aumid:&lt;window AUMID&gt;", "exe:&lt;process image path&gt;") equals one of the pin's (id, "aumid:&lt;target&gt;" or "exe:&lt;target&gt;"). Exact ids win over aliases.
/// Windows that match no pin belong to the item named by their own app id. Only app pins take part; comparisons are case-insensitive.
/// </summary>
public sealed class PinMatcher
{
    private readonly Dictionary<string, string> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _byAlias = new(StringComparer.OrdinalIgnoreCase);

    public PinMatcher(IEnumerable<PinnedItem> pins)
    {
        var list = pins.Where(p => p.Kind != PinnedKind.Folder && !string.IsNullOrWhiteSpace(p.Id)).ToList();
        foreach (var p in list) _byId.TryAdd(p.Id, p.Id);
        foreach (var p in list)
        {
            foreach (var alias in AliasesOf(p)) _byAlias.TryAdd(alias, p.Id);
        }
    }

    private static IEnumerable<string> AliasesOf(PinnedItem p)
    {
        yield return p.Id;
        if (string.IsNullOrWhiteSpace(p.Target)) yield break;
        if (LooksLikePath(p.Target)) { var key = AppNames.PathKey(p.Target); if (key is not null) yield return "exe:" + key; }
        else yield return "aumid:" + p.Target.Trim();
    }

    private static bool LooksLikePath(string s) => s.Length >= 3 && char.IsAsciiLetter(s[0]) && s[1] == ':' && (s[2] == '\\' || s[2] == '/') || s.StartsWith(@"\\", StringComparison.Ordinal);

    /// <summary>The id of the dock item that shows <paramref name="w"/>: the matching pin's id, else the window's own app id.</summary>
    public string KeyFor(TrackedWindow w)
    {
        if (_byId.TryGetValue(w.AppId, out var exact)) return exact;
        if (_byAlias.Count > 0)
        {
            if (_byAlias.TryGetValue(w.AppId, out var a)) return a;
            var aumid = w.Raw.Aumid?.Trim();
            if (!string.IsNullOrEmpty(aumid) && _byAlias.TryGetValue("aumid:" + aumid, out a)) return a;
            var exe = AppNames.PathKey(w.Raw.ExePath);
            if (exe is not null && _byAlias.TryGetValue("exe:" + exe, out a)) return a;
        }
        return w.AppId;
    }

    /// <summary>The windows (in snapshot order) that dock item <paramref name="itemId"/> shows.</summary>
    public IReadOnlyList<TrackedWindow> WindowsOf(string itemId, IEnumerable<TrackedWindow> windows) =>
        windows.Where(w => string.Equals(KeyFor(w), itemId, StringComparison.OrdinalIgnoreCase)).ToList();
}
