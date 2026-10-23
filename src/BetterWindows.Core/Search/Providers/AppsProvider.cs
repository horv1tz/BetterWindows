using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;

namespace BetterWindows.Core.Search.Providers;

/// <summary>
/// Installed apps from the catalog, matched on the display name and on the executable's file name (an alias: "msedge" finds Microsoft Edge), with a ranking
/// boost from launch counts and recency. Primary action launches the app by its catalog id; "Show in Explorer" is secondary when the app has a file.
/// </summary>
public sealed class AppsProvider : ISearchProvider
{
    /// <summary>Lowest score that is shown (noise filter for long subsequence matches).</summary>
    public const double MinScore = 0.3;
    public const int MaxResults = 8;

    private readonly IAppCatalog _catalog;
    private readonly UsageStore _usage;

    public AppsProvider(IAppCatalog catalog, UsageStore usage)
    {
        _catalog = catalog;
        _usage = usage;
    }

    public SearchProviderId Id => SearchProviderId.Apps;
    public TimeSpan Timeout => TimeSpan.FromMilliseconds(200);

    public Task<IReadOnlyList<SearchHit>> QueryAsync(SearchQuery query, CancellationToken ct)
    {
        var scored = new List<(double Score, AppEntry Entry)>();
        foreach (var entry in _catalog.Apps)
        {
            if (ct.IsCancellationRequested) break;
            double score = Math.Max(
                FuzzyMatcher.MatchWithLayout(query.Text, entry.Name)?.Score ?? 0,
                entry.Path is null ? 0 : 0.9 * (FuzzyMatcher.MatchWithLayout(query.Text, TextUtil.FileStem(entry.Path))?.Score ?? 0));
            if (score <= 0) continue;
            score = Math.Min(1.0, score + _usage.Boost(entry.Id));
            if (score < MinScore) continue;
            scored.Add((Math.Round(score, 4), entry));
        }

        IReadOnlyList<SearchHit> hits = scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Entry.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxResults)
            .Select(s => ToHit(s.Entry, s.Score, query.Locale))
            .ToArray();
        return Task.FromResult(hits);
    }

    /// <summary>The result row for one app (also used for the recent list).</summary>
    internal static SearchHit ToHit(AppEntry entry, double score, string locale)
    {
        var actions = new List<HitAction>
        {
            new("open", Loc.Pick(locale, "Open", "Открыть"), true, new LaunchAppOperation(entry.Id)),
        };
        if (entry.Path is not null && InputPolicy.TryNormalizeLocalPath(entry.Path, out var path))
        {
            actions.Add(new HitAction("reveal", Loc.Pick(locale, "Show in Explorer", "Показать в проводнике"), false, new RevealPathOperation(path)));
        }
        return new SearchHit(
            SearchProviderId.Apps,
            Title: InputPolicy.Truncate(entry.Name, 200),
            Subtitle: Loc.Pick(locale, "Application", "Приложение"),
            IconUrl: DynUrls.AppIcon(entry.Id, 64),
            Score: score,
            Actions: actions,
            PreviewPath: entry.Path,
            PreviewIsApp: true);
    }
}
