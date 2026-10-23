using BetterWindows.Core.Search;
using Xunit;

namespace BetterWindows.Core.Tests.Search;

public class FuzzyMatcherTests
{
    [Theory]
    [InlineData(null, "Safari")]
    [InlineData("", "Safari")]
    [InlineData("   ", "Safari")]
    [InlineData("saf", null)]
    [InlineData("saf", "")]
    [InlineData("zz", "Safari")]
    [InlineData("safarii", "Safari")]
    [InlineData("ba", "ab")]
    public void No_match_returns_null_for_empty_or_non_subsequence_inputs(string? query, string? candidate)
    {
        Assert.Null(FuzzyMatcher.Match(query, candidate));
        Assert.Equal(0, FuzzyMatcher.Score(query, candidate));
    }

    [Fact]
    public void Exact_match_scores_one_and_highlights_every_character()
    {
        var m = FuzzyMatcher.Match("Safari", "safari");
        Assert.NotNull(m);
        Assert.Equal(1.0, m!.Score);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, m.Indices);
    }

    [Fact]
    public void Indices_are_strictly_increasing_and_point_at_matching_characters()
    {
        var m = FuzzyMatcher.Match("gc", "GoogleChrome");
        Assert.NotNull(m);
        Assert.Equal(new[] { 0, 6 }, m!.Indices);
        Assert.True(m.Indices.Zip(m.Indices.Skip(1)).All(p => p.First < p.Second));
    }

    [Fact]
    public void Indices_have_one_entry_per_query_character_ignoring_whitespace()
    {
        var m = FuzzyMatcher.Match("wi fi", "Wi-Fi");
        Assert.NotNull(m);
        Assert.Equal(4, m!.Indices.Count);
    }

    [Fact]
    public void Whitespace_only_in_the_query_is_ignored()
    {
        Assert.NotNull(FuzzyMatcher.Match("wi fi", "Wi-Fi"));
        Assert.Equal(FuzzyMatcher.Score("wifi", "Wi-Fi"), FuzzyMatcher.Score("wi fi", "Wi-Fi"));
    }

    [Fact]
    public void Matching_is_case_insensitive_in_both_directions()
    {
        var upper = FuzzyMatcher.Match("SAF", "safari");
        var lower = FuzzyMatcher.Match("saf", "SAFARI");
        Assert.NotNull(upper);
        Assert.NotNull(lower);
        Assert.Equal(new[] { 0, 1, 2 }, upper!.Indices);
        Assert.Equal(new[] { 0, 1, 2 }, lower!.Indices);
    }

    [Fact]
    public void Matching_the_same_letters_in_the_same_case_ranks_higher_than_across_cases()
    {
        // The case bonus is a tie-breaker: identical letters in the typed case rank above a case-insensitive hit.
        Assert.True(FuzzyMatcher.Score("saf", "Safari") > FuzzyMatcher.Score("SAF", "safari"));
    }

    [Fact]
    public void Consecutive_matches_beat_gapped_matches()
    {
        Assert.True(FuzzyMatcher.Score("fox", "Fox Tool") > FuzzyMatcher.Score("fox", "Firefox"));
    }

    [Fact]
    public void Word_start_beats_a_match_inside_a_word()
    {
        // "ab" hits two word starts in the first candidate and two letters inside one word in the second.
        Assert.True(FuzzyMatcher.Score("ab", "Apple Banana") > FuzzyMatcher.Score("ab", "crabby"));
    }

    [Fact]
    public void CamelCase_humps_count_as_word_starts()
    {
        var camel = FuzzyMatcher.Match("gc", "GoogleChrome");
        var plain = FuzzyMatcher.Match("gc", "GargoyleXC"); // the C is neither a hump nor a word start
        Assert.NotNull(camel);
        Assert.NotNull(plain);
        Assert.True(camel!.Score > plain!.Score);
    }

    [Fact]
    public void A_consecutive_run_beats_scattered_characters()
    {
        Assert.True(FuzzyMatcher.Score("ab", "xab") > FuzzyMatcher.Score("ab", "xaxxb"));
    }

    [Fact]
    public void Shorter_gaps_score_higher_than_longer_gaps()
    {
        Assert.True(FuzzyMatcher.Score("ac", "abc") > FuzzyMatcher.Score("ac", "abbbbbbc"));
    }

    [Fact]
    public void Shorter_candidates_rank_above_longer_ones_with_the_same_match()
    {
        Assert.True(FuzzyMatcher.Score("mail", "Mail") > FuzzyMatcher.Score("mail", "Mail Manager Enterprise Edition"));
    }

    [Fact]
    public void Diacritics_are_ignored()
    {
        Assert.Equal(1.0, FuzzyMatcher.Score("cafe", "Café"));
        Assert.Equal(1.0, FuzzyMatcher.Score("café", "CAFE"));
    }

    [Fact]
    public void Cyrillic_queries_match_cyrillic_names()
    {
        Assert.Equal(1.0, FuzzyMatcher.Score("привет", "Привет"));
        Assert.True(FuzzyMatcher.Score("при", "Привет") > 0.5);
        Assert.True(FuzzyMatcher.Score("пт", "Привет") > 0);
    }

    [Fact]
    public void Russian_yo_matches_ye()
    {
        Assert.Equal(1.0, FuzzyMatcher.Score("елка", "Ёлка"));
        Assert.Equal(1.0, FuzzyMatcher.Score("ёлка", "елка"));
    }

    [Fact]
    public void Layout_fix_finds_a_word_typed_in_the_wrong_layout()
    {
        Assert.Null(FuzzyMatcher.Match("ghbdtn", "Привет"));
        var m = FuzzyMatcher.MatchWithLayout("ghbdtn", "Привет");
        Assert.NotNull(m);
        Assert.True(m!.Score > 0.8, $"score was {m.Score}");
    }

    [Fact]
    public void Layout_fix_works_the_other_way_round()
    {
        var m = FuzzyMatcher.MatchWithLayout("руддщ", "hello");
        Assert.NotNull(m);
        Assert.True(m!.Score > 0.5);
    }

    [Fact]
    public void Direct_match_wins_over_the_layout_swapped_match()
    {
        var direct = FuzzyMatcher.Match("Safari", "Safari");
        var layout = FuzzyMatcher.MatchWithLayout("Safari", "Safari");
        Assert.Equal(direct!.Score, layout!.Score);
    }

    [Fact]
    public void Layout_swap_is_discounted_below_an_exact_layout_match()
    {
        var typed = FuzzyMatcher.MatchWithLayout("привет", "Привет");
        var swapped = FuzzyMatcher.MatchWithLayout("ghbdtn", "Привет");
        Assert.True(typed!.Score > swapped!.Score);
    }

    [Fact]
    public void MatchWithLayout_returns_null_when_neither_reading_matches()
    {
        Assert.Null(FuzzyMatcher.MatchWithLayout("qqqq", "Safari"));
        Assert.Null(FuzzyMatcher.MatchWithLayout(null, "Safari"));
    }

    [Fact]
    public void Candidates_beyond_the_length_cap_are_not_matched()
    {
        var candidate = new string('x', 300) + "z";
        Assert.Null(FuzzyMatcher.Match("z", candidate));
        Assert.NotNull(FuzzyMatcher.Match("x", candidate));
    }

    [Fact]
    public void Very_long_queries_are_truncated_instead_of_failing()
    {
        var query = new string('a', 500);
        var candidate = new string('a', 200);
        var m = FuzzyMatcher.Match(query, candidate);
        Assert.NotNull(m);
        Assert.Equal(FuzzyMatcher.MaxQueryLength, m!.Indices.Count);
    }

    [Fact]
    public void Scores_always_stay_within_zero_and_one()
    {
        string[] queries = { "a", "ab", "set", "wifi", "lock screen", "привет", "ghbdtn", "xyz", "Ёлка" };
        string[] candidates = { "Settings", "Wi-Fi", "Lock Screen", "Привет", "", "a", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "Ёлка" };
        foreach (var q in queries)
        {
            foreach (var c in candidates)
            {
                var s = FuzzyMatcher.Score(q, c);
                Assert.InRange(s, 0, 1);
            }
        }
    }

    [Fact]
    public void Non_ascii_characters_without_decomposition_are_kept_as_they_are()
    {
        Assert.True(FuzzyMatcher.Score("日本", "日本語") > 0.5);
        Assert.Equal('x', FuzzyMatcher.Fold('X'));
    }

    [Fact]
    public void Scoring_is_deterministic()
    {
        var a = FuzzyMatcher.Match("calc", "Calculator Pro");
        var b = FuzzyMatcher.Match("calc", "Calculator Pro");
        Assert.Equal(a!.Score, b!.Score);
        Assert.Equal(a.Indices, b.Indices);
    }
}

public class KeyboardLayoutTests
{
    [Theory]
    [InlineData("ghbdtn", "привет")]
    [InlineData("Ghbdtn", "Привет")]
    [InlineData("Safari", "Ыфафкш")]
    [InlineData("", "")]
    [InlineData("123 .", "123 ю")]
    public void Swap_maps_characters_between_the_layouts(string input, string expected)
    {
        Assert.Equal(expected, KeyboardLayout.Swap(input));
    }

    [Fact]
    public void Swap_is_an_involution_for_the_mapped_alphabet()
    {
        const string all = "qwertyuiop[]asdfghjkl;'zxcvbnm,`.ЙЦУКЕНГШЩЗХЪФЫВАПРОЛДЖЭЯЧСМИТЬБЁЮ";
        Assert.Equal(all, KeyboardLayout.Swap(KeyboardLayout.Swap(all)));
    }

    [Fact]
    public void Swap_of_null_is_empty()
    {
        Assert.Equal("", KeyboardLayout.Swap(null));
    }

    [Fact]
    public void Counterpart_gives_the_key_on_the_other_layout()
    {
        Assert.Equal('й', KeyboardLayout.Counterpart('q'));
        Assert.Equal('q', KeyboardLayout.Counterpart('й'));
        Assert.Equal('ю', KeyboardLayout.Counterpart('.'));
        Assert.Null(KeyboardLayout.Counterpart('1'));
        Assert.Null(KeyboardLayout.Counterpart(' '));
    }

    [Fact]
    public void Digits_and_unmapped_symbols_are_unchanged()
    {
        Assert.Equal("2024 + 7 = 2031 @ #", KeyboardLayout.Swap("2024 + 7 = 2031 @ #"));
    }
}
