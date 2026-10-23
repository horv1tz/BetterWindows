using BetterWindows.Core.Protocol;
using BetterWindows.Core.Widgets;

namespace BetterWindows.Core.Tests.Widgets;

/// <summary>Input rules for reminders: due-time parsing and canonical form, text limits, ids and DST handling.</summary>
public class ReminderRulesTests
{
    [Theory]
    [InlineData("2026-10-23T14:30", 2026, 10, 23, 14, 30, 0)]
    [InlineData("2026-10-23T14:30:15", 2026, 10, 23, 14, 30, 15)]
    [InlineData("1970-01-01T00:00", 1970, 1, 1, 0, 0, 0)]
    [InlineData("2999-12-31T23:59", 2999, 12, 31, 23, 59, 0)]
    public void Valid_local_date_times_parse(string s, int y, int mo, int d, int h, int mi, int sec)
    {
        ReminderRules.TryParseDue(s, out var local).Should().BeTrue();
        local.Should().Be(new DateTime(y, mo, d, h, mi, sec));
        local.Kind.Should().Be(DateTimeKind.Unspecified, "due times are wall-clock, not instants");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tomorrow")]
    [InlineData("2026-10-23")]
    [InlineData("2026-10-23 14:30")]
    [InlineData("2026-10-23T14:30Z")]
    [InlineData("2026-10-23T14:30+02:00")]
    [InlineData("2026-13-01T10:00")]
    [InlineData("2026-02-30T10:00")]
    [InlineData("2026-10-23T25:00")]
    [InlineData("1969-12-31T23:59")]
    [InlineData("3000-01-01T00:00")]
    [InlineData("2026-10-23T14:30:15.123456789")]
    public void Malformed_or_out_of_range_due_times_are_rejected(string? s)
    {
        ReminderRules.TryParseDue(s, out _).Should().BeFalse();
    }

    [Fact]
    public void Due_times_longer_than_the_limit_are_rejected()
    {
        ReminderRules.TryParseDue("2026-10-23T14:30" + new string(' ', 40), out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("2026-10-23T14:30", "2026-10-23T14:30")]
    [InlineData("2026-10-23T14:30:00", "2026-10-23T14:30")]
    [InlineData("2026-10-23T14:30:15", "2026-10-23T14:30:15")]
    [InlineData("2026-10-23T14:30:15.9", "2026-10-23T14:30:15")]
    public void Canonical_form_drops_zero_seconds_and_fractions(string input, string canonical)
    {
        ReminderRules.TryParseDue(input, out var local);
        ReminderRules.FormatDue(local).Should().Be(canonical);
    }

    [Fact]
    public void OptionalDue_treats_empty_as_none_and_canonicalises_valid_input()
    {
        ReminderRules.OptionalDue(null).Should().BeNull();
        ReminderRules.OptionalDue("").Should().BeNull();
        ReminderRules.OptionalDue("2026-10-23T14:30:00").Should().Be("2026-10-23T14:30");
        Action bad = () => ReminderRules.OptionalDue("someday");
        bad.Should().Throw<ProtocolException>().Which.Code.Should().Be("badRequest");
    }

    [Fact]
    public void RequireLine_trims_and_enforces_length_and_control_characters()
    {
        ReminderRules.RequireLine("  Buy milk \t", 50, "title").Should().Be("Buy milk");
        ReminderRules.RequireLine(new string('a', 50), 50, "title").Should().HaveLength(50);
        Action tooLong = () => ReminderRules.RequireLine(new string('a', 51), 50, "title");
        tooLong.Should().Throw<ProtocolException>();
        Action empty = () => ReminderRules.RequireLine("   ", 50, "title");
        empty.Should().Throw<ProtocolException>();
        Action control = () => ReminderRules.RequireLine("a\nb", 50, "title");
        control.Should().Throw<ProtocolException>();
        Action del = () => ReminderRules.RequireLine("a\u007fb", 50, "title");
        del.Should().Throw<ProtocolException>();
    }

    [Fact]
    public void OptionalLine_treats_whitespace_as_none()
    {
        ReminderRules.OptionalLine("   ", 100, "list").Should().BeNull();
        ReminderRules.OptionalLine(null, 100, "list").Should().BeNull();
        ReminderRules.OptionalLine(" Work ", 100, "list").Should().Be("Work");
    }

    [Fact]
    public void Notes_may_be_multi_line_but_not_contain_other_control_characters()
    {
        ReminderRules.OptionalNotes("line one\r\nline two\tend").Should().Be("line one\r\nline two\tend");
        ReminderRules.OptionalNotes("   ").Should().BeNull();
        Action bell = () => ReminderRules.OptionalNotes("ding\u0007");
        bell.Should().Throw<ProtocolException>();
        Action huge = () => ReminderRules.OptionalNotes(new string('n', ReminderRules.MaxNotesChars + 1));
        huge.Should().Throw<ProtocolException>();
    }

    [Fact]
    public void Ids_must_be_guids_and_are_compared_in_canonical_form()
    {
        var id = Guid.NewGuid().ToString("D");
        ReminderRules.IsValidId(id).Should().BeTrue();
        ReminderRules.IsValidId(id.ToUpperInvariant()).Should().BeTrue();
        ReminderRules.IsValidId(id.Replace("-", "")).Should().BeFalse();
        ReminderRules.IsValidId("../../etc/passwd").Should().BeFalse();
        ReminderRules.IsValidId(null).Should().BeFalse();
    }

    [Fact]
    public void ToUtc_converts_with_the_zone_offset()
    {
        var utc = ReminderRules.ToUtc(new DateTime(2026, 10, 23, 14, 30, 0), TimeZoneInfo.Utc);
        utc.Should().Be(new DateTimeOffset(2026, 10, 23, 14, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void ToUtc_moves_a_nonexistent_spring_forward_time_to_the_next_hour()
    {
        if (!TryZone("Europe/Berlin", out var berlin)) return; // no time zone database on this machine
        // 2026-03-29 02:30 does not exist in Berlin (clocks jump 02:00 -> 03:00 CET -> CEST); it becomes 03:30 CEST = 01:30 UTC.
        var utc = ReminderRules.ToUtc(new DateTime(2026, 3, 29, 2, 30, 0), berlin);
        utc.Should().Be(new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void ToUtc_of_an_ambiguous_autumn_time_takes_the_first_occurrence()
    {
        if (!TryZone("Europe/Berlin", out var berlin)) return;
        // 2026-10-25 02:30 happens twice in Berlin; the first (CEST, +2) is 00:30 UTC.
        var utc = ReminderRules.ToUtc(new DateTime(2026, 10, 25, 2, 30, 0), berlin);
        utc.Should().Be(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero));
    }

    private static bool TryZone(string id, out TimeZoneInfo zone) => TimeZoneInfo.TryFindSystemTimeZoneById(id, out zone!);
}
