using BetterWindows.Core.Protocol;

namespace BetterWindows.Native;

/// <summary>
/// Pure parsing of the virtual desktop registry values and of desktop ids (no OS calls, so it is unit-tested on any OS).
/// Layout (HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops): VirtualDesktopIDs = REG_BINARY, 16-byte GUIDs in order;
/// CurrentVirtualDesktop = REG_BINARY, one GUID (absent until the user first switches desktops); Desktops\{GUID}\Name = REG_SZ (absent or empty = default name).
/// </summary>
public static class DesktopRegistry
{
    /// <summary>Size of one GUID in the binary registry values.</summary>
    public const int GuidBlobSize = 16;

    /// <summary>Upper bound for a desktop name taken from the registry (names are user-owned data).</summary>
    public const int MaxNameLength = 256;

    /// <summary>
    /// Parses a concatenation of 16-byte GUIDs in little-endian field order (the layout <see cref="Guid(ReadOnlySpan{byte})"/> expects).
    /// Returns an empty list for a null, empty or misaligned blob; drops Guid.Empty and duplicates while keeping the order.
    /// </summary>
    public static IReadOnlyList<Guid> ParseGuidBlob(byte[]? blob)
    {
        if (blob is null || blob.Length == 0 || blob.Length % GuidBlobSize != 0) return Array.Empty<Guid>();
        var result = new List<Guid>(blob.Length / GuidBlobSize);
        for (int offset = 0; offset < blob.Length; offset += GuidBlobSize)
        {
            var id = new Guid(blob.AsSpan(offset, GuidBlobSize));
            if (id != Guid.Empty && !result.Contains(id)) result.Add(id);
        }
        return result;
    }

    /// <summary>The current desktop from the CurrentVirtualDesktop value, or null when the value is missing or malformed.</summary>
    public static Guid? ParseCurrent(byte[]? blob)
    {
        if (blob is null || blob.Length != GuidBlobSize) return null;
        var id = new Guid(blob);
        return id == Guid.Empty ? null : id;
    }

    /// <summary>
    /// Accepts only the canonical "D" (hyphenated) and "B" (braced) forms, which is what <see cref="FormatId"/> produces. Anything else (whitespace,
    /// other formats, hostile strings) is rejected.
    /// </summary>
    public static bool TryParseId(string? text, out Guid id)
    {
        id = Guid.Empty;
        if (string.IsNullOrEmpty(text) || text.Length is not (36 or 38)) return false;
        return Guid.TryParseExact(text, "D", out id) || Guid.TryParseExact(text, "B", out id);
    }

    /// <summary>Public desktop id: the lower-case hyphenated GUID.</summary>
    public static string FormatId(Guid id) => id.ToString("D");

    /// <summary>Default name for a desktop without a Name value. English on purpose: the UI localises names that match this pattern.</summary>
    public static string DefaultName(int zeroBasedIndex) => "Desktop " + (zeroBasedIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Index of the current desktop in <paramref name="ids"/>. When the current id is unknown (value absent, or stale), the first desktop is
    /// reported as current, which matches Explorer's behaviour before the first switch. Returns -1 only when there are no desktops.
    /// </summary>
    public static int ResolveCurrentIndex(IReadOnlyList<Guid> ids, Guid? current)
    {
        if (ids.Count == 0) return -1;
        if (current is Guid c)
        {
            int index = -1;
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == c) { index = i; break; }
            }
            if (index >= 0) return index;
        }
        return 0;
    }

    /// <summary>Builds the protocol list. <paramref name="nameOf"/> returns the raw registry Name (or null); blank names become the default pattern.</summary>
    public static IReadOnlyList<DesktopInfo> BuildDesktops(IReadOnlyList<Guid> ids, Func<Guid, string?> nameOf, Guid? current)
    {
        int currentIndex = ResolveCurrentIndex(ids, current);
        var result = new List<DesktopInfo>(ids.Count);
        for (int i = 0; i < ids.Count; i++)
        {
            string? raw = nameOf(ids[i]);
            string name = string.IsNullOrWhiteSpace(raw) ? DefaultName(i) : Truncate(raw.Trim(), MaxNameLength);
            result.Add(new DesktopInfo
            {
                Id = FormatId(ids[i]),
                Name = name,
                Index = i,
                Current = i == currentIndex,
            });
        }
        return result;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}

/// <summary>Result of planning a desktop switch: <see cref="Steps"/> presses of Win+Ctrl+Left (Direction -1) or Win+Ctrl+Right (Direction +1).</summary>
public readonly record struct DesktopSwitchPlan(bool Valid, int Direction, int Steps);

/// <summary>Computes how many Win+Ctrl+Arrow presses move from one desktop index to another. Pure and deterministic.</summary>
public static class DesktopSwitchPlanner
{
    /// <summary>Invalid when an index is outside [0, count) or the count is not positive.</summary>
    public static DesktopSwitchPlan Plan(int currentIndex, int targetIndex, int desktopCount)
    {
        if (desktopCount <= 0 || currentIndex < 0 || currentIndex >= desktopCount || targetIndex < 0 || targetIndex >= desktopCount)
            return new DesktopSwitchPlan(false, 0, 0);
        int diff = targetIndex - currentIndex;
        return new DesktopSwitchPlan(true, Math.Sign(diff), Math.Abs(diff));
    }
}
