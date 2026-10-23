using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;

namespace BetterWindows.Core.Search.Providers;

/// <summary>
/// Files and folders from the Windows Search index. Needs at least two characters; waits for 120 ms of typing idle first (a newer query cancels the wait),
/// and runs at most one index query at a time across requests. Paths come from the index only; the actions open, reveal or copy them.
/// </summary>
public sealed class FilesProvider : ISearchProvider
{
    public const int MinQueryLength = 2;
    public const int MaxResults = 8;
    public static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(120);

    private readonly IFileSearch _search;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _single = new(1, 1);

    public FilesProvider(IFileSearch search, TimeProvider time)
    {
        _search = search;
        _time = time;
    }

    public SearchProviderId Id => SearchProviderId.Files;
    public TimeSpan Timeout => TimeSpan.FromMilliseconds(1500);

    public async Task<IReadOnlyList<SearchHit>> QueryAsync(SearchQuery query, CancellationToken ct)
    {
        var text = query.Text.Trim();
        if (text.Length < MinQueryLength || !_search.IsAvailable) return Array.Empty<SearchHit>();

        await TimedWait.After(_time, IdleDelay, ct).ConfigureAwait(false);

        await _single.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var hits = new List<SearchHit>();
            await foreach (var file in _search.SearchAsync(text, MaxResults, ct).ConfigureAwait(false))
            {
                if (ct.IsCancellationRequested) break;
                if (!InputPolicy.TryNormalizeLocalPath(file.Path, out var path) || string.IsNullOrWhiteSpace(file.Name)) continue;
                hits.Add(ToHit(path, InputPolicy.Truncate(file.Name, 200), text, query.Locale));
                if (hits.Count >= MaxResults) break;
            }
            return hits;
        }
        finally
        {
            _single.Release();
        }
    }

    private SearchHit ToHit(string path, string name, string text, string locale)
    {
        // Index hits arrive ranked; the score keeps them below apps and windows and orders them by name similarity.
        double score = Math.Clamp((FuzzyMatcher.Score(text, name) * 0.7) + 0.2, 0.2, 0.7);
        var actions = new List<HitAction>
        {
            new("open", Loc.Pick(locale, "Open", "Открыть"), true, new OpenPathOperation(path)),
            new("reveal", Loc.Pick(locale, "Show in Explorer", "Показать в проводнике"), false, new RevealPathOperation(path)),
            new("copy", Loc.Pick(locale, "Copy path", "Скопировать путь"), false, new CopyTextOperation(path)),
        };
        return new SearchHit(
            Id,
            Title: name,
            Subtitle: InputPolicy.Truncate(TextUtil.ParentFolder(path), 200),
            IconUrl: DynUrls.PathIcon(path, 64),
            Score: Math.Round(score, 4),
            Actions: actions,
            PreviewPath: path,
            PreviewIsApp: false);
    }
}
