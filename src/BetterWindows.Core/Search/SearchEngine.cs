using BetterWindows.Core.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Core.Search;

/// <summary>
/// Runs the enabled providers of one search concurrently, each under its own timeout, and streams the COMPLETE merged list after every batch of finished
/// providers (see <see cref="SearchMerger"/> for the order). Emissions are at least 30 ms apart; the last one always has Complete = true unless the run was
/// cancelled. Each request keeps its id -> hit table; the engine keeps the last three request tables (search and recent lists) so that a click on a list
/// that a newer query just replaced still resolves. Thread-safe.
/// </summary>
public sealed class SearchEngine
{
    /// <summary>How many request tables stay resolvable.</summary>
    public const int MaxTables = 3;
    /// <summary>Minimum spacing between two emissions of one run.</summary>
    public static readonly TimeSpan MinEmitInterval = TimeSpan.FromMilliseconds(30);

    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly object _gate = new();
    private readonly List<ResultTable> _tables = new();
    private long _recentSeq;

    public SearchEngine(TimeProvider time, ILogger<SearchEngine>? log = null)
    {
        _time = time;
        _log = (ILogger?)log ?? NullLogger.Instance;
    }

    /// <summary>
    /// Runs one search. <paramref name="emit"/> receives the merged list each time (never after cancellation). Returns when the last provider finished or timed
    /// out (or the token was cancelled). Never throws for provider failures.
    /// </summary>
    public async Task RunAsync(string requestId, SearchQuery query, IReadOnlyList<ISearchProvider> providers, int maxResults, Action<SearchResults> emit, CancellationToken ct)
    {
        var table = OpenTable(requestId);
        var collected = new List<ResolvedHit>();
        var pending = new List<Task<IReadOnlyList<SearchHit>>>(providers.Count);
        foreach (var provider in providers) pending.Add(RunProviderAsync(provider, query, ct));

        DateTimeOffset? lastEmit = null;
        try
        {
            while (true)
            {
                if (pending.Count > 0)
                {
                    // Wait for at least one provider, then collect every provider that has finished by now (coalesces bursts).
                    await Task.WhenAny(pending).WaitAsync(ct).ConfigureAwait(false);
                    for (int i = pending.Count - 1; i >= 0; i--)
                    {
                        if (!pending[i].IsCompleted) continue;
                        foreach (var hit in pending[i].Result) collected.Add(AddHit(table, hit));
                        pending.RemoveAt(i);
                    }
                }

                bool complete = pending.Count == 0;
                if (lastEmit is { } last)
                {
                    var wait = last + MinEmitInterval - _time.GetUtcNow();
                    if (wait > TimeSpan.Zero) await TimedWait.After(_time, wait, ct).ConfigureAwait(false);
                }
                ct.ThrowIfCancellationRequested();

                lastEmit = _time.GetUtcNow();
                var merged = SearchMerger.Merge(collected, query.Text, maxResults);
                emit(new SearchResults
                {
                    RequestId = requestId,
                    Results = merged.Select(ToDto).ToArray(),
                    Complete = complete,
                });
                if (complete) return;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The newer query (or the surface) superseded this run: nothing more is emitted.
        }
    }

    /// <summary>Registers a list of recent results and returns it with its own request id (resolvable like a search).</summary>
    public SearchRecent RegisterRecent(IReadOnlyList<SearchHit> hits)
    {
        var requestId = "recent-" + Interlocked.Increment(ref _recentSeq).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var table = OpenTable(requestId);
        var results = hits.Select(h => ToDto(AddHit(table, h))).ToArray();
        return new SearchRecent { RequestId = requestId, Results = results };
    }

    /// <summary>The hit a UI id refers to in a request, or null when the request or the id is unknown.</summary>
    public SearchHit? TryResolve(string requestId, string resultId)
    {
        if (string.IsNullOrEmpty(requestId) || string.IsNullOrEmpty(resultId)) return null;
        lock (_gate)
        {
            for (int i = _tables.Count - 1; i >= 0; i--)
            {
                if (_tables[i].RequestId != requestId) continue;
                if (_tables[i].Hits.TryGetValue(resultId, out var hit)) return hit;
            }
            return null;
        }
    }

    /// <summary>Wraps one provider call with its timeout. The provider is cancelled when the timeout fires or the call finishes; errors become "no results".</summary>
    private async Task<IReadOnlyList<SearchHit>> RunProviderAsync(ISearchProvider provider, SearchQuery query, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            var work = Task.Run(() => provider.QueryAsync(query, cts.Token), cts.Token);
            var timeout = TimedWait.After(_time, provider.Timeout, cts.Token);
            var winner = await Task.WhenAny(work, timeout).ConfigureAwait(false);
            if (winner != work)
            {
                _log.LogDebug("search provider {Provider} timed out", provider.Id);
                return Array.Empty<SearchHit>();
            }
            if (work.IsCompletedSuccessfully) return work.Result ?? Array.Empty<SearchHit>();
            if (work.Exception is { } ex) _log.LogWarning(ex.GetBaseException(), "search provider {Provider} failed", provider.Id);
            return Array.Empty<SearchHit>();
        }
        catch (OperationCanceledException)
        {
            return Array.Empty<SearchHit>();
        }
        finally
        {
            cts.Cancel(); // stops a timed-out provider and releases the timeout timer
        }
    }

    private ResolvedHit AddHit(ResultTable table, SearchHit hit)
    {
        lock (_gate) return table.Add(hit);
    }

    private ResultTable OpenTable(string requestId)
    {
        lock (_gate)
        {
            var table = new ResultTable(requestId);
            _tables.Add(table);
            while (_tables.Count > MaxTables) _tables.RemoveAt(0);
            return table;
        }
    }

    /// <summary>Maps one hit to the protocol DTO: clamped score, at most one primary action, truncated texts.</summary>
    internal static SearchResult ToDto(ResolvedHit resolved)
    {
        var hit = resolved.Hit;
        bool primarySeen = false;
        var actions = new List<SearchAction>(hit.Actions.Count);
        foreach (var action in hit.Actions)
        {
            bool primary = action.Primary && !primarySeen;
            primarySeen |= primary;
            actions.Add(new SearchAction { Id = action.Id, Label = action.Label, Primary = primary });
        }
        return new SearchResult
        {
            Id = resolved.Id,
            Provider = hit.Provider,
            Title = hit.Title,
            Subtitle = hit.Subtitle,
            IconUrl = hit.IconUrl,
            Score = double.IsFinite(hit.Score) ? Math.Clamp(hit.Score, 0, 1) : 0,
            Actions = actions,
            Preview = hit.Preview,
        };
    }

    /// <summary>The hits of one request: ids "r1", "r2", ... unique within the request.</summary>
    private sealed class ResultTable(string requestId)
    {
        private int _seq;

        public string RequestId { get; } = requestId;
        public Dictionary<string, SearchHit> Hits { get; } = new(StringComparer.Ordinal);

        public ResolvedHit Add(SearchHit hit)
        {
            var id = "r" + (++_seq).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Hits[id] = hit;
            return new ResolvedHit(id, hit);
        }
    }
}
