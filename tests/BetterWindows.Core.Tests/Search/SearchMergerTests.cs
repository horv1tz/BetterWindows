using BetterWindows.Core.Protocol;
using BetterWindows.Core.Search;
using Xunit;
using static BetterWindows.Core.Tests.Search.SearchTestKit;

namespace BetterWindows.Core.Tests.Search;

public class SearchMergerTests
{
    private static ResolvedHit R(string id, SearchProviderId provider, string title, double score) =>
        new(id, Hit(provider, title, score));

    private static IReadOnlyList<string> Ids(IEnumerable<ResolvedHit> hits) => hits.Select(h => h.Id).ToArray();

    [Fact]
    public void Empty_input_gives_an_empty_list()
    {
        Assert.Empty(SearchMerger.Merge(Array.Empty<ResolvedHit>(), "x", 9));
    }

    [Fact]
    public void Results_are_ordered_by_score_descending()
    {
        var hits = new[]
        {
            R("a", SearchProviderId.Apps, "Alpha", 0.4),
            R("b", SearchProviderId.Apps, "Beta", 0.9),
            R("c", SearchProviderId.Settings, "Gamma", 0.6),
        };
        Assert.Equal(new[] { "b", "c", "a" }, Ids(SearchMerger.Merge(hits, "x", 9)));
    }

    [Fact]
    public void The_calculator_answer_comes_first_even_with_a_lower_score()
    {
        var hits = new[]
        {
            R("a", SearchProviderId.Apps, "Calendar", 0.99),
            R("c", SearchProviderId.Calc, "= 4", 0.2),
        };
        Assert.Equal(new[] { "c", "a" }, Ids(SearchMerger.Merge(hits, "2+2", 9)));
    }

    [Fact]
    public void Web_results_always_come_last_even_with_the_highest_score()
    {
        var hits = new[]
        {
            R("w", SearchProviderId.Web, "Search the web for x", 1.0),
            R("a", SearchProviderId.Apps, "Xylophone", 0.2),
            R("c", SearchProviderId.Calc, "= 2", 0.5),
        };
        Assert.Equal(new[] { "c", "a", "w" }, Ids(SearchMerger.Merge(hits, "xy", 9)));
    }

    [Fact]
    public void Equal_scores_are_broken_by_provider_priority_then_title()
    {
        var hits = new[]
        {
            R("cmd", SearchProviderId.Commands, "Lock", 0.8),
            R("set", SearchProviderId.Settings, "Lock screen", 0.8),
            R("app", SearchProviderId.Apps, "Lockwise", 0.8),
            R("win", SearchProviderId.Windows, "Locking window", 0.8),
        };
        Assert.Equal(new[] { "app", "win", "set", "cmd" }, Ids(SearchMerger.Merge(hits, "lock", 9)));
    }

    [Fact]
    public void Equal_scores_and_priority_fall_back_to_title_order()
    {
        var hits = new[]
        {
            R("b", SearchProviderId.Apps, "Beta", 0.5),
            R("a", SearchProviderId.Apps, "Alpha", 0.5),
        };
        Assert.Equal(new[] { "a", "b" }, Ids(SearchMerger.Merge(hits, "x", 9)));
    }

    [Fact]
    public void At_most_three_results_per_provider_for_longer_queries()
    {
        var hits = Enumerable.Range(0, 6).Select(i => R("a" + i, SearchProviderId.Apps, "App " + i, 0.9 - i * 0.01)).ToArray();
        var merged = SearchMerger.Merge(hits, "app", 9);
        Assert.Equal(3, merged.Count);
    }

    [Fact]
    public void Short_queries_are_not_capped_per_provider()
    {
        var hits = Enumerable.Range(0, 6).Select(i => R("a" + i, SearchProviderId.Apps, "A" + i, 0.9 - i * 0.01)).ToArray();
        Assert.Equal(6, SearchMerger.Merge(hits, "a", 9).Count);
    }

    [Fact]
    public void The_total_is_limited_by_max_results()
    {
        var hits = Enumerable.Range(0, 20).Select(i => R("s" + i, i % 3 == 0 ? SearchProviderId.Settings : SearchProviderId.Apps, "T" + i, 0.5)).ToArray();
        Assert.Equal(5, SearchMerger.Merge(hits, "t", 5).Count);
    }

    [Fact]
    public void A_web_result_is_kept_when_the_list_is_full()
    {
        var hits = Enumerable.Range(0, 9).Select(i => R("a" + i, SearchProviderId.Apps, "Xa" + i, 0.9 - i * 0.01))
            .Append(R("w", SearchProviderId.Web, "Search the web", 0.1))
            .ToArray();
        var merged = SearchMerger.Merge(hits, "xa", 4);
        Assert.Equal(4, merged.Count);
        Assert.Equal("w", merged[^1].Id);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(9, 9)]
    [InlineData(100, SearchMerger.MaxResultsLimit)]
    public void Max_results_is_clamped_to_a_sane_range(int requested, int expectedCount)
    {
        var hits = Enumerable.Range(0, 40).Select(i => R("a" + i, SearchProviderId.Apps, "Q" + i, 0.5)).ToArray();
        Assert.Equal(expectedCount, SearchMerger.Merge(hits, "q", requested).Count);
    }

    [Fact]
    public void Priority_puts_apps_first_and_unknown_providers_last()
    {
        Assert.True(SearchMerger.Priority(SearchProviderId.Apps) < SearchMerger.Priority(SearchProviderId.Windows));
        Assert.True(SearchMerger.Priority(SearchProviderId.Files) < SearchMerger.Priority(SearchProviderId.Calc));
        Assert.True(SearchMerger.Priority((SearchProviderId)99) > SearchMerger.Priority(SearchProviderId.Web));
    }

    [Fact]
    public void Merging_does_not_mutate_the_input()
    {
        var hits = new List<ResolvedHit> { R("b", SearchProviderId.Apps, "B", 0.1), R("a", SearchProviderId.Apps, "A", 0.9) };
        _ = SearchMerger.Merge(hits, "x", 9);
        Assert.Equal(new[] { "b", "a" }, hits.Select(h => h.Id));
    }
}
