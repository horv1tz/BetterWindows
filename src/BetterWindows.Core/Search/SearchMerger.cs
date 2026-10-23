using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Search;

/// <summary>A provider result with the request-scoped id the host assigned to it (what the UI sends back on search.perform).</summary>
public sealed record ResolvedHit(string Id, SearchHit Hit);

/// <summary>
/// The ordering rules of the merged list (pure, so they are unit-tested directly):
/// 1. the calculator answer comes first; 2. then every other provider's results by score, ties broken by provider priority (apps, windows, settings,
/// commands, files) and then by title; 3. web results come last, always. At most <c>maxResults</c> results; at most three per provider unless the query is
/// two characters or fewer; a web result is never pushed out by the cap.
/// </summary>
public static class SearchMerger
{
    /// <summary>Upper bound for the result count, whatever the settings say.</summary>
    public const int MaxResultsLimit = 30;
    public const int PerProviderCap = 3;
    public const int ShortQueryLength = 2;

    public static IReadOnlyList<ResolvedHit> Merge(IEnumerable<ResolvedHit> hits, string query, int maxResults)
    {
        int max = Math.Clamp(maxResults, 1, MaxResultsLimit);
        int cap = (query?.Trim().Length ?? 0) <= ShortQueryLength ? int.MaxValue : PerProviderCap;
        var all = hits as IReadOnlyCollection<ResolvedHit> ?? hits.ToArray();

        var calc = Capped(all.Where(h => h.Hit.Provider == SearchProviderId.Calc).OrderByDescending(h => h.Hit.Score).ThenBy(h => h.Hit.Title, StringComparer.Ordinal), cap);
        var web = Capped(all.Where(h => h.Hit.Provider == SearchProviderId.Web).OrderByDescending(h => h.Hit.Score).ThenBy(h => h.Hit.Title, StringComparer.Ordinal), cap);
        var rest = all.Where(h => h.Hit.Provider is not (SearchProviderId.Calc or SearchProviderId.Web))
            .OrderByDescending(h => h.Hit.Score)
            .ThenBy(h => Priority(h.Hit.Provider))
            .ThenBy(h => h.Hit.Title, StringComparer.Ordinal)
            .ToArray();

        var webKept = web.Take(max).ToArray();
        int headRoom = max - webKept.Length;
        var head = new List<ResolvedHit>(Math.Min(headRoom, all.Count));
        var counts = new Dictionary<SearchProviderId, int>();
        foreach (var h in calc.Concat(CappedPerProvider(rest, cap, counts)))
        {
            if (head.Count >= headRoom) break;
            head.Add(h);
        }
        return head.Concat(webKept).ToArray();
    }

    /// <summary>Position of a provider in the tie-breaking order (lower wins).</summary>
    public static int Priority(SearchProviderId provider) => provider switch
    {
        SearchProviderId.Apps => 0,
        SearchProviderId.Windows => 1,
        SearchProviderId.Settings => 2,
        SearchProviderId.Commands => 3,
        SearchProviderId.Files => 4,
        SearchProviderId.Calc => 5,
        SearchProviderId.Web => 6,
        _ => 7,
    };

    private static IEnumerable<ResolvedHit> Capped(IEnumerable<ResolvedHit> ordered, int cap) => ordered.Take(cap);

    private static IEnumerable<ResolvedHit> CappedPerProvider(IEnumerable<ResolvedHit> ordered, int cap, Dictionary<SearchProviderId, int> counts)
    {
        foreach (var h in ordered)
        {
            counts.TryGetValue(h.Hit.Provider, out int n);
            if (n >= cap) continue;
            counts[h.Hit.Provider] = n + 1;
            yield return h;
        }
    }
}
