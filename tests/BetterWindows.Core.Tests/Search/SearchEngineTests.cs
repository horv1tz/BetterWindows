using BetterWindows.Core.Protocol;
using BetterWindows.Core.Search;
using BetterWindows.Core.Tests.Support;
using Xunit;
using static BetterWindows.Core.Tests.Search.SearchTestKit;

namespace BetterWindows.Core.Tests.Search;

public class SearchEngineTests
{
    private readonly ManualTimeProvider _time = new();
    private readonly List<SearchResults> _emitted = new();
    private readonly SearchEngine _engine;

    public SearchEngineTests()
    {
        _engine = new SearchEngine(_time);
    }

    private static SearchQuery Q(string text = "app") => new(text, "en");

    private Task Run(string requestId, IReadOnlyList<ISearchProvider> providers, CancellationToken ct = default, string text = "app", int max = 9) =>
        _engine.RunAsync(requestId, Q(text), providers, max, r => _emitted.Add(r), ct);

    [Fact]
    public async Task No_providers_emit_one_complete_empty_result()
    {
        await Run("r1", Array.Empty<ISearchProvider>());
        var only = Assert.Single(_emitted);
        Assert.True(only.Complete);
        Assert.Empty(only.Results);
        Assert.Equal("r1", only.RequestId);
    }

    [Fact]
    public async Task All_provider_results_are_merged_into_the_final_emission()
    {
        var apps = ScriptedProvider.Immediate(SearchProviderId.Apps, Hit(SearchProviderId.Apps, "App One", 0.9));
        var settings = ScriptedProvider.Immediate(SearchProviderId.Settings, Hit(SearchProviderId.Settings, "App Settings", 0.4));
        var task = Run("r1", new ISearchProvider[] { apps, settings });
        await PumpAsync(_time, task);

        var last = _emitted[^1];
        Assert.True(last.Complete);
        Assert.Equal(new[] { "App One", "App Settings" }, last.Results.Select(r => r.Title));
    }

    [Fact]
    public async Task Fast_providers_stream_before_slow_ones_finish()
    {
        var fast = ScriptedProvider.Immediate(SearchProviderId.Apps, Hit(SearchProviderId.Apps, "Quick App", 0.8));
        var (slow, gate) = ScriptedProvider.Gated(SearchProviderId.Files, TimeSpan.FromSeconds(5));
        var task = Run("r1", new ISearchProvider[] { fast, slow });
        await WaitStartedAsync(fast);

        await PumpUntilAsync(_time, () => _emitted.Any(e => !e.Complete));
        var first = _emitted.First(e => !e.Complete);
        Assert.Equal(new[] { "Quick App" }, first.Results.Select(r => r.Title));

        gate.SetResult(new[] { Hit(SearchProviderId.Files, "Slow File", 0.3) });
        await PumpAsync(_time, task);
        Assert.Equal(new[] { "Quick App", "Slow File" }, _emitted[^1].Results.Select(r => r.Title));
        Assert.True(_emitted[^1].Complete);
    }

    [Fact]
    public async Task A_provider_that_exceeds_its_timeout_is_cancelled_and_contributes_nothing()
    {
        var fast = ScriptedProvider.Immediate(SearchProviderId.Apps, Hit(SearchProviderId.Apps, "Keep", 0.5));
        var (slow, gate) = ScriptedProvider.Gated(SearchProviderId.Files, TimeSpan.FromMilliseconds(200));
        var task = Run("r1", new ISearchProvider[] { fast, slow });
        await WaitStartedAsync(fast);

        await PumpAsync(_time, task);
        Assert.Equal(new[] { "Keep" }, _emitted[^1].Results.Select(r => r.Title));
        Assert.True(_emitted[^1].Complete);
        // The provider observes cancellation on its own task: wait for it rather than assume the ordering.
        await PumpUntilAsync(_time, () => gate.Task.IsCanceled);
    }

    [Fact]
    public async Task Each_provider_gets_its_own_timeout_and_a_fast_provider_is_not_delayed_by_a_slow_one()
    {
        var calc = ScriptedProvider.Immediate(SearchProviderId.Calc, Hit(SearchProviderId.Calc, "= 4", 1.0));
        var (commands, gate) = ScriptedProvider.Gated(SearchProviderId.Commands, TimeSpan.FromMilliseconds(200));
        var task = Run("r1", new ISearchProvider[] { calc, commands });
        await WaitStartedAsync(calc);
        await PumpUntilAsync(_time, () => _emitted.Count > 0);
        Assert.Equal("= 4", _emitted[0].Results.Single().Title);
        Assert.False(_emitted[0].Complete);
        await PumpAsync(_time, task);
        Assert.True(_emitted[^1].Complete);
        await PumpUntilAsync(_time, () => gate.Task.IsCanceled);
    }

    [Fact]
    public async Task A_throwing_provider_is_treated_as_no_results()
    {
        var broken = new ScriptedProvider(SearchProviderId.Files, (_, _) => throw new InvalidOperationException("index broke"));
        var ok = ScriptedProvider.Immediate(SearchProviderId.Apps, Hit(SearchProviderId.Apps, "Still Here", 0.5));
        var task = Run("r1", new ISearchProvider[] { broken, ok });
        await PumpAsync(_time, task);
        Assert.Equal(new[] { "Still Here" }, _emitted[^1].Results.Select(r => r.Title));
        Assert.True(_emitted[^1].Complete);
    }

    [Fact]
    public async Task A_provider_that_faults_asynchronously_is_treated_as_no_results()
    {
        var faulted = new ScriptedProvider(SearchProviderId.Apps, (_, _) => Task.FromException<IReadOnlyList<SearchHit>>(new IOException("disk")));
        var task = Run("r1", new ISearchProvider[] { faulted });
        await PumpAsync(_time, task);
        Assert.Empty(_emitted[^1].Results);
        Assert.True(_emitted[^1].Complete);
    }

    [Fact]
    public async Task Cancelling_the_run_stops_emissions_and_returns_quietly()
    {
        var (slow, gate) = ScriptedProvider.Gated(SearchProviderId.Files, TimeSpan.FromSeconds(5));
        using var cts = new CancellationTokenSource();
        var task = Run("r1", new ISearchProvider[] { slow }, cts.Token);
        await WaitStartedAsync(slow);
        cts.Cancel();
        await PumpAsync(_time, task);
        Assert.Empty(_emitted);
        await PumpUntilAsync(_time, () => gate.Task.IsCanceled);
    }

    [Fact]
    public async Task Emissions_are_at_least_thirty_milliseconds_apart()
    {
        var first = ScriptedProvider.Immediate(SearchProviderId.Apps, Hit(SearchProviderId.Apps, "One", 0.9));
        var (second, gate) = ScriptedProvider.Gated(SearchProviderId.Settings, TimeSpan.FromSeconds(5));
        var stamps = new List<DateTimeOffset>();
        var firstEmitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEmitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = _engine.RunAsync("r1", Q(), new ISearchProvider[] { first, second }, 9, r =>
        {
            stamps.Add(_time.GetUtcNow());
            _emitted.Add(r);
            if (stamps.Count == 1) firstEmitted.SetResult(); else secondEmitted.SetResult();
        }, default);

        // The clock stays still until the first emission has been stamped: a background pump could otherwise move it between the engine's
        // record of that emission and this stamp. After that only this thread advances the clock, and the engine never emits sooner than
        // 30 ms after its previous record, so the stamps must be at least 30 ms apart.
        await firstEmitted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        gate.SetResult(new[] { Hit(SearchProviderId.Settings, "Two", 0.4) });
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!secondEmitted.Task.IsCompleted)
        {
            if (sw.ElapsedMilliseconds > 10_000) throw new Xunit.Sdk.XunitException("second emission never happened");
            _time.Advance(TimeSpan.FromMilliseconds(5));
            await Task.Delay(2);
        }
        await task;

        Assert.Equal(2, stamps.Count);
        Assert.True(stamps[1] - stamps[0] >= SearchEngine.MinEmitInterval, $"gap was {stamps[1] - stamps[0]}");
        Assert.True(_emitted[^1].Complete);
    }

    [Fact]
    public async Task Hits_get_unique_ids_within_one_request_and_resolve_to_their_hits()
    {
        var apps = ScriptedProvider.Immediate(SearchProviderId.Apps,
            Hit(SearchProviderId.Apps, "A", 0.9), Hit(SearchProviderId.Apps, "B", 0.8));
        var task = Run("r1", new ISearchProvider[] { apps });
        await PumpAsync(_time, task);

        var results = _emitted[^1].Results;
        Assert.Equal(results.Count, results.Select(r => r.Id).Distinct().Count());
        var a = results.Single(r => r.Title == "A");
        Assert.Equal("A", _engine.TryResolve("r1", a.Id)!.Title);
    }

    [Fact]
    public async Task Ids_are_resolvable_only_for_their_own_request()
    {
        var apps = ScriptedProvider.Immediate(SearchProviderId.Apps, Hit(SearchProviderId.Apps, "A", 0.9));
        await PumpAsync(_time, Run("r1", new ISearchProvider[] { apps }));
        var id = _emitted[^1].Results.Single().Id;

        Assert.NotNull(_engine.TryResolve("r1", id));
        Assert.Null(_engine.TryResolve("other", id));
        Assert.Null(_engine.TryResolve("r1", "nope"));
        Assert.Null(_engine.TryResolve("", id));
    }

    [Fact]
    public async Task Only_the_last_three_request_tables_stay_resolvable()
    {
        var ids = new List<string>();
        for (int i = 1; i <= 4; i++)
        {
            var provider = ScriptedProvider.Immediate(SearchProviderId.Apps, Hit(SearchProviderId.Apps, "Item " + i, 0.5));
            await PumpAsync(_time, Run("req" + i, new ISearchProvider[] { provider }));
            ids.Add(_emitted[^1].Results.Single().Id);
        }
        Assert.Null(_engine.TryResolve("req1", ids[0]));
        Assert.NotNull(_engine.TryResolve("req2", ids[1]));
        Assert.NotNull(_engine.TryResolve("req4", ids[3]));
    }

    [Fact]
    public void Recent_lists_get_their_own_request_id_and_stay_resolvable()
    {
        var recent = _engine.RegisterRecent(new[] { Hit(SearchProviderId.Apps, "Notes", 1.0), Hit(SearchProviderId.Apps, "Paint", 1.0) });
        Assert.StartsWith("recent-", recent.RequestId);
        Assert.Equal(2, recent.Results.Count);
        var first = recent.Results[0];
        Assert.NotNull(_engine.TryResolve(recent.RequestId, first.Id));
        Assert.Equal("Notes", _engine.TryResolve(recent.RequestId, first.Id)!.Title);
    }

    [Fact]
    public void Two_recent_lists_have_different_request_ids()
    {
        var a = _engine.RegisterRecent(Array.Empty<SearchHit>());
        var b = _engine.RegisterRecent(Array.Empty<SearchHit>());
        Assert.NotEqual(a.RequestId, b.RequestId);
    }

    [Fact]
    public void To_dto_clamps_scores_and_keeps_only_the_first_primary_action()
    {
        var hit = new SearchHit(SearchProviderId.Apps, "T", "S", "/dyn/x", double.NaN, new[]
        {
            new HitAction("one", "One", true, new CopyTextOperation("x")),
            new HitAction("two", "Two", true, new CopyTextOperation("y")),
            new HitAction("three", "Three", false, new CopyTextOperation("z")),
        });
        var dto = SearchEngine.ToDto(new ResolvedHit("r9", hit));
        Assert.Equal(0, dto.Score);
        Assert.Equal(new[] { true, false, false }, dto.Actions.Select(a => a.Primary));
        Assert.Equal("r9", dto.Id);
        Assert.Equal("/dyn/x", dto.IconUrl);

        var high = SearchEngine.ToDto(new ResolvedHit("r1", hit with { Score = 7 }));
        Assert.Equal(1, high.Score);
    }

    [Fact]
    public async Task Zero_maximum_is_treated_as_one_result()
    {
        var apps = ScriptedProvider.Immediate(SearchProviderId.Apps, Hit(SearchProviderId.Apps, "A", 0.9), Hit(SearchProviderId.Apps, "B", 0.8));
        await PumpAsync(_time, Run("r1", new ISearchProvider[] { apps }, max: 0));
        Assert.Single(_emitted[^1].Results);
    }
}
