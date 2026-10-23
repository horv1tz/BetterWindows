using System.Text;

namespace BetterWindows.Native;

/// <summary>
/// What to run for one file search: how many hits the caller may receive in total, and the statements as a list of VARIANTS (most capable first). Every variant holds the
/// same phases in the same order (phase 1 = name starts with the phrase, phase 2 = name contains every word). The executor uses the first variant the provider accepts:
/// the index provider is only reachable on a real Windows machine, so the more exotic clauses (relevance ordering, file scope) have plain fallbacks instead of a dead feature.
/// </summary>
internal sealed record FileSearchPlan(int Limit, IReadOnlyList<IReadOnlyList<string>> Variants)
{
    /// <summary>The full-featured variant (variant 0).</summary>
    public IReadOnlyList<string> Statements => Variants[0];
}

/// <summary>
/// Builds Windows Search (SYSTEMINDEX) SQL from user input. User text only ever appears inside single-quoted LIKE literals and passes through <see cref="EscapeLiteral"/>:
/// single quotes are doubled (the only way to leave a string literal), LIKE wildcard/set characters are removed (Windows Search SQL has no ESCAPE clause, so they cannot be
/// escaped; dropping them only ever broadens a match) and control characters are dropped. Pure, so the escaping is unit tested on any OS.
/// </summary>
internal static class FileSearchQuery
{
    public const int MaxQueryLength = 128;
    public const int MaxTokens = 6;
    public const int MaxLimit = 100;
    /// <summary>System folders whose contents are never useful as "files": they are filtered after the query (a SQL NOT LIKE on the path would defeat the index).</summary>
    private static readonly string[] ExcludedSegments =
    [
        @"\appdata\", @"\$recycle.bin\", @"\windows\", @"\program files\", @"\program files (x86)\", @"\programdata\",
        @"\node_modules\", @"\.git\", @"\system volume information\", @"\windowsapps\",
    ];

    /// <summary>Makes <paramref name="text"/> safe to place between single quotes in a Windows Search SQL LIKE literal.</summary>
    public static string EscapeLiteral(string text)
    {
        var sb = new StringBuilder(text.Length + 4);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\'':
                    sb.Append("''");
                    break;
                case '%':  // LIKE wildcard
                case '[':  // LIKE character set
                case ']':
                case '^':
                    break;
                default:
                    if (ch >= 0x20 && ch != 0x7f) sb.Append(ch);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Splits the input into escaped, non-empty tokens (whitespace separated, at most <see cref="MaxTokens"/>).</summary>
    public static IReadOnlyList<string> Tokenize(string? query)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(query)) return result;
        var q = query.Trim();
        if (q.Length > MaxQueryLength) q = q[..MaxQueryLength];
        foreach (var raw in q.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var t = EscapeLiteral(raw);
            if (t.Length == 0) continue;
            result.Add(t);
            if (result.Count == MaxTokens) break;
        }
        return result;
    }

    /// <summary>
    /// Phase 1: names that START with the whole phrase (best matches first, index friendly). Phase 2: names that contain every token anywhere.
    /// Each is ordered by relevance (System.Search.Rank, then newest first). Returns null for input that is empty after sanitising.
    /// </summary>
    public static FileSearchPlan? Plan(string? query, int limit)
    {
        var tokens = Tokenize(query);
        if (tokens.Count == 0) return null;
        limit = Math.Clamp(limit, 1, MaxLimit);
        // Over-fetch: hidden/system items and excluded system folders are removed after the query, and phase 2 repeats phase-1 hits (de-duplicated by path).
        int top = Math.Min(MaxLimit * 3, limit * 3 + 20);
        var phrase = string.Join(' ', tokens);

        var like1 = $"System.FileName LIKE '{phrase}%'";
        var like2 = string.Join(" AND ", tokens.Select(t => $"System.FileName LIKE '%{t}%'"));
        var variants = new List<IReadOnlyList<string>>(3)
        {
            // 0: file scope + relevance order
            new[] { Select(top, like1, scoped: true, ranked: true), Select(top * 2, like2, scoped: true, ranked: true) },
            // 1: without System.Search.Rank (a provider that rejects ranking for plain LIKE predicates)
            new[] { Select(top, like1, scoped: true, ranked: false), Select(top * 2, like2, scoped: true, ranked: false) },
            // 2: minimal - no scope clause either; non-file items are dropped from the results by their (non-rooted) path
            new[] { Select(top, like1, scoped: false, ranked: false), Select(top * 2, like2, scoped: false, ranked: false) },
        };
        return new FileSearchPlan(limit, variants);
    }

    /// <summary>The cheap availability probe statement.</summary>
    public const string ProbeStatement = "SELECT TOP 1 System.ItemPathDisplay FROM SYSTEMINDEX";

    private static string Select(int top, string where, bool scoped, bool ranked)
    {
        var columns = ranked
            ? "System.ItemPathDisplay, System.ItemNameDisplay, System.DateModified, System.FileAttributes, System.Search.Rank"
            : "System.ItemPathDisplay, System.ItemNameDisplay, System.DateModified, System.FileAttributes";
        var scope = scoped ? "SCOPE='file:' AND " : "";
        var order = ranked ? "ORDER BY System.Search.Rank DESC, System.DateModified DESC" : "ORDER BY System.DateModified DESC";
        return $"SELECT TOP {top} {columns} FROM SYSTEMINDEX WHERE {scope}{where} {order}";
    }

    /// <summary>True for paths inside system / tooling folders that must not be offered as search results.</summary>
    public static bool IsExcludedPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return true;
        var p = path.Replace('/', '\\').ToLowerInvariant();
        foreach (var seg in ExcludedSegments)
            if (p.Contains(seg, StringComparison.Ordinal)) return true;
        return false;
    }

    public const uint AttributeHidden = 0x2;
    public const uint AttributeSystem = 0x4;
    public const uint AttributeDirectory = 0x10;
}
