using System.Globalization;
using System.IO;
using System.Text.Json;

namespace BetterWindows.Host.Lifecycle;

/// <summary>One restore journal found in the state directory (docs/IPC.md section 15: <c>taskbar.&lt;sessionId&gt;.json</c>, <c>desktopicons.&lt;sessionId&gt;.json</c>).</summary>
/// <param name="File">Full path.</param>
/// <param name="OwnerPid">The pid recorded as owner (0 when missing/unreadable).</param>
/// <param name="OwnerStartUtc">Recorded start time of the owner, null when absent or in an unknown format.</param>
/// <param name="Readable">False when the file is corrupt or too large: the owner cannot be proven alive.</param>
public sealed record RestoreJournal(string File, int OwnerPid, DateTimeOffset? OwnerStartUtc, bool Readable);

/// <summary>
/// Finds restore journals whose owner process is dead (a previous run crashed or was killed while the taskbar / desktop icons were modified). Read-only and
/// tolerant: the exact journal schema is owned by the Windows layer, only <c>ownerPid</c> and <c>ownerStartTime</c> are consulted.
/// </summary>
public static class RestoreJournalScanner
{
    private const int MaxJournalBytes = 256 * 1024;
    private static readonly string[] Prefixes = { "taskbar", "desktopicons" };

    /// <summary>All journals in <paramref name="stateDir"/> (missing directory = none). Never throws.</summary>
    public static IReadOnlyList<RestoreJournal> Scan(string stateDir)
    {
        var result = new List<RestoreJournal>();
        try
        {
            if (!Directory.Exists(stateDir)) return result;
            foreach (var file in Directory.EnumerateFiles(stateDir, "*.json"))
            {
                var name = Path.GetFileName(file);
                if (!IsJournalName(name)) continue;
                result.Add(Read(file));
            }
        }
        catch (Exception) { /* unreadable directory: nothing we can prove */ }
        return result;
    }

    /// <summary>Journals whose owner is not a live process other than <paramref name="currentPid"/>. Unreadable journals count as stale (restoring is idempotent).</summary>
    public static IReadOnlyList<RestoreJournal> FindStale(string stateDir, IProcessProbe probe, int currentPid) =>
        Scan(stateDir).Where(j => IsStale(j, probe, currentPid)).ToArray();

    internal static bool IsStale(RestoreJournal j, IProcessProbe probe, int currentPid) =>
        !j.Readable
        || j.OwnerPid <= 0
        || (j.OwnerPid == currentPid && j.OwnerStartUtc is null) // our pid but no start time to prove it is us: a recycled pid of a dead run
        || !probe.IsAlive(j.OwnerPid, j.OwnerStartUtc);

    /// <summary><c>taskbar.json</c>, <c>taskbar.2.json</c>, <c>desktopicons.1.json</c>; never backups, temp files or quarantined copies.</summary>
    public static bool IsJournalName(string fileName)
    {
        if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return false;
        if (fileName.StartsWith('.')) return false; // AtomicFile temp files are ".<name>.<pid>.<guid>.tmp"
        if (fileName.Contains(".corrupt-", StringComparison.OrdinalIgnoreCase) || fileName.Contains(".bak", StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var prefix in Prefixes)
        {
            if (fileName.Equals(prefix + ".json", StringComparison.OrdinalIgnoreCase)) return true;
            if (fileName.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static RestoreJournal Read(string file)
    {
        try
        {
            var info = new FileInfo(file);
            if (info.Length > MaxJournalBytes) return new RestoreJournal(file, 0, null, false);
            using var doc = JsonDocument.Parse(System.IO.File.ReadAllBytes(file), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return new RestoreJournal(file, 0, null, false);
            var pid = 0;
            if (TryGet(doc.RootElement, "ownerPid", out var p))
            {
                if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n)) pid = n;
                else if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var s)) pid = s;
            }
            DateTimeOffset? start = TryGet(doc.RootElement, "ownerStartTime", out var st) ? ParseStart(st) : null;
            return new RestoreJournal(file, pid, start, true);
        }
        catch (Exception) { return new RestoreJournal(file, 0, null, false); }
    }

    private static bool TryGet(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var prop in obj.EnumerateObject())
            if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { value = prop.Value; return true; }
        value = default;
        return false;
    }

    /// <summary>ISO-8601 string, FILETIME (100 ns since 1601), Unix milliseconds or Unix seconds; anything else = unknown (compare by pid only).</summary>
    internal static DateTimeOffset? ParseStart(JsonElement e)
    {
        try
        {
            if (e.ValueKind == JsonValueKind.String)
                return DateTimeOffset.TryParse(e.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d) ? d : null;
            if (e.ValueKind != JsonValueKind.Number || !e.TryGetInt64(out var n) || n <= 0) return null;
            if (n > 100_000_000_000_000L) return DateTimeOffset.FromFileTime(n).ToUniversalTime();      // FILETIME (~1.3e17 today)
            if (n > 100_000_000_000L) return DateTimeOffset.FromUnixTimeMilliseconds(n);               // ms since 1970 (~1.7e12)
            return DateTimeOffset.FromUnixTimeSeconds(n);                                               // s since 1970 (~1.7e9)
        }
        catch (ArgumentOutOfRangeException) { return null; }
    }
}
