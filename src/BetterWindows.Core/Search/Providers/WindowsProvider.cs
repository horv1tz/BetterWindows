using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;

namespace BetterWindows.Core.Search.Providers;

/// <summary>
/// Open windows (the tracker's eligible windows) matched on the window title and the app name. The primary action activates the window; the operation carries
/// the handle and the process id it had, which the action runner re-checks before touching it (docs/IPC.md section 14).
/// </summary>
public sealed class WindowsProvider : ISearchProvider
{
    public const double MinScore = 0.3;
    public const int MaxResults = 8;

    private readonly IWindowTracker _tracker;

    public WindowsProvider(IWindowTracker tracker) => _tracker = tracker;

    public SearchProviderId Id => SearchProviderId.Windows;
    public TimeSpan Timeout => TimeSpan.FromMilliseconds(200);

    public Task<IReadOnlyList<SearchHit>> QueryAsync(SearchQuery query, CancellationToken ct)
    {
        var scored = new List<(double Score, TrackedWindow Window)>();
        foreach (var window in _tracker.Snapshot.Windows)
        {
            if (ct.IsCancellationRequested) break;
            if (string.IsNullOrWhiteSpace(window.Raw.Title)) continue;
            double score = Math.Max(
                FuzzyMatcher.MatchWithLayout(query.Text, window.Raw.Title)?.Score ?? 0,
                0.9 * (FuzzyMatcher.MatchWithLayout(query.Text, window.AppName)?.Score ?? 0));
            if (score < MinScore) continue;
            scored.Add((Math.Round(score, 4), window));
        }

        IReadOnlyList<SearchHit> hits = scored
            .OrderByDescending(s => s.Score)
            .Take(MaxResults)
            .Select(s => new SearchHit(
                Id,
                Title: InputPolicy.Truncate(s.Window.Raw.Title, 200),
                Subtitle: InputPolicy.Truncate(s.Window.AppName, 120),
                IconUrl: DynUrls.WindowIcon(s.Window.Handle, 64),
                Score: s.Score,
                Actions: new[]
                {
                    new HitAction("activate", Loc.Pick(query.Locale, "Switch to window", "Перейти к окну"), true,
                        new ActivateWindowOperation(s.Window.Handle, s.Window.Raw.ProcessId)),
                }))
            .ToArray();
        return Task.FromResult(hits);
    }
}
