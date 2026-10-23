using BetterWindows.Core.Protocol;
using BetterWindows.Core.Search.Providers;
using BetterWindows.Core.Services;

namespace BetterWindows.Core.Search;

/// <summary>
/// The empty-query list: recently launched apps (from the usage store, newest first), then pinned dock apps that are not in that list yet. Ids are the catalog
/// app ids, so they resolve through the same path as search results.
/// </summary>
public static class SearchRecents
{
    public const int DefaultLimit = 8;

    public static IReadOnlyList<SearchHit> Build(UsageStore usage, IAppCatalog catalog, IEnumerable<PinnedItem> pins, string locale, int limit = DefaultLimit)
    {
        limit = Math.Clamp(limit, 1, DefaultLimit);
        var hits = new List<SearchHit>(limit);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var appId in usage.RecentAppIds(limit))
        {
            if (hits.Count >= limit) break;
            var entry = catalog.FindById(appId);
            if (entry is null || !seen.Add(entry.Id)) continue;
            hits.Add(AppsProvider.ToHit(entry, 1.0, locale));
        }

        foreach (var pin in pins)
        {
            if (hits.Count >= limit) break;
            if (pin.Kind != PinnedKind.App) continue;
            var entry = catalog.FindById(pin.Id);
            if (entry is null || !seen.Add(entry.Id)) continue;
            hits.Add(AppsProvider.ToHit(entry, 1.0, locale));
        }
        return hits;
    }
}
