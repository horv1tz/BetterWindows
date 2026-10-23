using System.Globalization;
using System.Text;

namespace BetterWindows.Core.Search;

/// <summary>A successful fuzzy match: score in 0..1 (higher is better) and the matched candidate indices, for highlighting.</summary>
public sealed record FuzzyMatch(double Score, IReadOnlyList<int> Indices);

/// <summary>
/// fzf-style subsequence matcher shared by every search provider. Case- and diacritic-insensitive for Latin and Cyrillic; whitespace in the query is ignored
/// ("wi fi" finds "Wi-Fi"). Scores reward word starts, camelCase humps, prefixes and consecutive runs and penalise gaps. The alignment is optimal (dynamic programming,
/// O(query x candidate)); a greedy subsequence pre-check rejects non-matches without allocating.
/// </summary>
public static class FuzzyMatcher
{
    /// <summary>Longer queries are truncated for matching (the cost is quadratic in the query length).</summary>
    public const int MaxQueryLength = 128;
    /// <summary>Longer candidates are truncated for matching.</summary>
    public const int MaxCandidateLength = 256;

    private const int MatchScore = 16;
    private const int PrefixBonus = 12;
    private const int WordStartBonus = 10;
    private const int CamelBonus = 8;
    private const int ConsecutiveBonus = 10;
    private const int CaseBonus = 2;
    private const int MaxLeadingPenalty = 15;
    /// <summary>Upper bound of the score one query character can earn; normalises the raw score.</summary>
    private const int MaxPerChar = MatchScore + PrefixBonus + ConsecutiveBonus + CaseBonus;
    private const int Unreachable = int.MinValue / 4;

    /// <summary>Score only (0 when there is no match).</summary>
    public static double Score(string? query, string? candidate) => Match(query, candidate)?.Score ?? 0;

    /// <summary>Matches a query against a candidate; null when the query is not a subsequence of the candidate.</summary>
    public static FuzzyMatch? Match(string? query, string? candidate)
    {
        if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(candidate)) return null;

        var q = StripWhitespace(query);
        if (q.Length == 0) return null;
        if (q.Length > MaxQueryLength) q = q[..MaxQueryLength];
        var t = candidate.Length > MaxCandidateLength ? candidate[..MaxCandidateLength] : candidate;

        int n = q.Length, m = t.Length;
        if (n > m) return null;

        var qf = new char[n];
        for (int i = 0; i < n; i++) qf[i] = Fold(q[i]);
        var tf = new char[m];
        for (int j = 0; j < m; j++) tf[j] = Fold(t[j]);

        // Cheap pre-check: greedy leftmost subsequence test. Most candidates fail here without any allocation beyond the folded arrays.
        int k = 0;
        for (int j = 0; j < m && k < n; j++) if (tf[j] == qf[k]) k++;
        if (k < n) return null;

        bool exact = n == m;
        for (int i = 0; exact && i < n; i++) exact = qf[i] == tf[i];
        if (exact) return new FuzzyMatch(1.0, Enumerable.Range(0, m).ToArray());

        var parent = new int[n * m];
        var prev = new int[m];
        var cur = new int[m];

        // Row 0: the first query character may match any position; leading skips are penalised (capped, so long names are not punished absurdly).
        for (int j = 0; j < m; j++)
        {
            prev[j] = Unreachable;
            parent[j] = -1;
            if (tf[j] != qf[0]) continue;
            prev[j] = MatchScore + Bonus(t, j) + CaseMatch(q[0], t[j]) - Math.Min(j, MaxLeadingPenalty);
        }

        for (int i = 1; i < n; i++)
        {
            int bestPlus = Unreachable;
            int bestPlusIndex = -1;
            for (int j = 0; j < m; j++)
            {
                cur[j] = Unreachable;
                // Gap transitions come from any j' <= j-2; the gap costs one point per skipped candidate character, so
                // prev[j'] - (j - j' - 1) == (prev[j'] + j') - j + 1, which lets us keep a running maximum.
                int jp = j - 2;
                if (jp >= 0 && prev[jp] > Unreachable && prev[jp] + jp > bestPlus)
                {
                    bestPlus = prev[jp] + jp;
                    bestPlusIndex = jp;
                }

                if (tf[j] != qf[i]) continue;

                int best = Unreachable, par = -1;
                if (j >= 1 && prev[j - 1] > Unreachable)
                {
                    best = prev[j - 1] + ConsecutiveBonus;
                    par = j - 1;
                }
                if (bestPlusIndex >= 0)
                {
                    int gap = bestPlus - j + 1;
                    if (gap > best) { best = gap; par = bestPlusIndex; }
                }
                if (par < 0) continue;

                cur[j] = best + MatchScore + Bonus(t, j) + CaseMatch(q[i], t[j]);
                parent[i * m + j] = par;
            }
            (prev, cur) = (cur, prev);
        }

        int bestEnd = -1, bestScore = Unreachable;
        for (int j = 0; j < m; j++)
        {
            if (prev[j] > bestScore) { bestScore = prev[j]; bestEnd = j; }
        }
        if (bestEnd < 0) return null;

        var indices = new int[n];
        indices[n - 1] = bestEnd;
        for (int i = n - 1, at = bestEnd; i >= 1; i--)
        {
            at = parent[i * m + at];
            indices[i - 1] = at;
        }

        double raw = bestScore / (double)(n * MaxPerChar);
        double score = Math.Clamp(raw, 0.01, 0.99) * (0.8 + 0.2 * n / m);
        return new FuzzyMatch(Math.Round(score, 4), indices);
    }

    /// <summary>
    /// Matches the query as typed and, if that fails or scores lower, the query with its characters swapped between the Russian (ЙЦУКЕН) and US (QWERTY) layouts
    /// (someone typing "ghbdtn" for "привет"). The swapped match is slightly discounted so an exact-layout match always ranks first.
    /// </summary>
    public static FuzzyMatch? MatchWithLayout(string? query, string? candidate)
    {
        var direct = Match(query, candidate);
        if (string.IsNullOrEmpty(query)) return direct;
        var swapped = KeyboardLayout.Swap(query);
        if (string.Equals(swapped, query, StringComparison.Ordinal)) return direct;
        var alt = Match(swapped, candidate);
        if (alt is null) return direct;
        var discounted = new FuzzyMatch(Math.Round(alt.Score * 0.9, 4), alt.Indices);
        return direct is null || discounted.Score > direct.Score ? discounted : direct;
    }

    /// <summary>Lower-cased base letter with diacritics removed ('é' -> 'e', 'ё' -> 'е'). Non-letters are only lower-cased.</summary>
    internal static char Fold(char c)
    {
        if (c < 0x80) return char.ToLowerInvariant(c);
        var d = c.ToString().Normalize(NormalizationForm.FormD);
        foreach (var ch in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) return char.ToLowerInvariant(ch);
        }
        return char.ToLowerInvariant(c);
    }

    private static string StripWhitespace(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s) if (!char.IsWhiteSpace(c) && !char.IsControl(c)) sb.Append(c);
        return sb.ToString();
    }

    /// <summary>Position bonus: prefix, start of a word (after a non-alphanumeric), or a camelCase hump.</summary>
    private static int Bonus(string t, int j)
    {
        if (j == 0) return PrefixBonus;
        char prev = t[j - 1], cur = t[j];
        if (!char.IsLetterOrDigit(prev) && char.IsLetterOrDigit(cur)) return WordStartBonus;
        if (char.IsLower(prev) && char.IsUpper(cur)) return CamelBonus;
        return 0;
    }

    private static int CaseMatch(char query, char candidate) => query == candidate ? CaseBonus : 0;
}
