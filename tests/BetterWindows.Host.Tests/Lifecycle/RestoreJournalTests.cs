using BetterWindows.Host.Lifecycle;
using BetterWindows.Host.Tests.Support;

namespace BetterWindows.Host.Tests.Lifecycle;

public class RestoreJournalTests
{
    [Theory]
    [InlineData("taskbar.json", true)]
    [InlineData("taskbar.2.json", true)]
    [InlineData("desktopicons.json", true)]
    [InlineData("desktopicons.1.json", true)]
    [InlineData("TASKBAR.JSON", true)]
    [InlineData(".taskbar.123.abc.tmp", false)]   // AtomicFile temp files
    [InlineData("taskbar.corrupt-20261023.json", false)]
    [InlineData("taskbar.json.bak", false)]
    [InlineData("taskbar.json.tmp", false)]
    [InlineData("startup.json", false)]
    [InlineData("config.json", false)]
    [InlineData("taskbarx.json", false)]
    [InlineData("taskbar.txt", false)]
    public void Only_real_journal_names_are_journals(string name, bool expected)
    {
        RestoreJournalScanner.IsJournalName(name).Should().Be(expected);
    }

    [Fact]
    public void Scan_of_a_missing_directory_is_empty()
    {
        using var dir = new TempDir();
        RestoreJournalScanner.Scan(Path.Combine(dir.Path, "missing")).Should().BeEmpty();
    }

    [Fact]
    public void Scan_reads_owner_pid_and_start_time_and_ignores_other_files()
    {
        using var dir = new TempDir();
        dir.File("taskbar.1.json", """{ "ownerPid": 321, "ownerStartTime": "2026-10-23T10:00:00Z", "sessionId": 1 }""");
        dir.File("config.json", """{ "ownerPid": 9 }""");
        var found = RestoreJournalScanner.Scan(dir.Path);
        found.Should().ContainSingle();
        found[0].OwnerPid.Should().Be(321);
        found[0].Readable.Should().BeTrue();
        found[0].OwnerStartUtc.Should().Be(new DateTimeOffset(2026, 10, 23, 10, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Corrupt_journal_is_unreadable_and_therefore_stale()
    {
        using var dir = new TempDir();
        dir.File("taskbar.json", "{ not json");
        var probe = new FakeProbe();
        var stale = RestoreJournalScanner.FindStale(dir.Path, probe, currentPid: 1);
        stale.Should().ContainSingle().Which.Readable.Should().BeFalse();
    }

    [Fact]
    public void Oversized_journal_is_unreadable()
    {
        using var dir = new TempDir();
        dir.File("taskbar.json", "{\"ownerPid\":5,\"pad\":\"" + new string('x', 300 * 1024) + "\"}");
        RestoreJournalScanner.Scan(dir.Path).Should().ContainSingle().Which.Readable.Should().BeFalse();
    }

    [Fact]
    public void Journal_of_a_live_owner_is_not_stale_and_of_a_dead_owner_is()
    {
        using var dir = new TempDir();
        dir.File("taskbar.1.json", """{ "ownerPid": 500 }""");
        dir.File("desktopicons.1.json", """{ "ownerPid": 600 }""");
        var probe = new FakeProbe();
        probe.Alive.Add(500);
        var stale = RestoreJournalScanner.FindStale(dir.Path, probe, currentPid: 1);
        stale.Should().ContainSingle().Which.OwnerPid.Should().Be(600);
    }

    [Fact]
    public void Journal_owned_by_this_pid_without_start_time_is_stale_a_recycled_pid()
    {
        var j = new RestoreJournal("x", OwnerPid: 42, OwnerStartUtc: null, Readable: true);
        RestoreJournalScanner.IsStale(j, new FakeProbe { Alive = { 42 } }, currentPid: 42).Should().BeTrue();
    }

    [Fact]
    public void Journal_without_owner_pid_is_stale()
    {
        var j = new RestoreJournal("x", OwnerPid: 0, OwnerStartUtc: null, Readable: true);
        RestoreJournalScanner.IsStale(j, new FakeProbe(), currentPid: 1).Should().BeTrue();
    }

    [Theory]
    [InlineData("\"2026-10-23T10:00:00Z\"", 2026)]
    [InlineData("1700000000", 2023)]                 // seconds since 1970
    [InlineData("1700000000000", 2023)]             // milliseconds since 1970
    [InlineData("133000000000000000", 2022)]        // FILETIME (100 ns ticks since 1601)
    public void Start_time_formats_are_understood(string json, int year)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var parsed = RestoreJournalScanner.ParseStart(doc.RootElement);
        parsed.Should().NotBeNull();
        parsed!.Value.Year.Should().Be(year);
    }

    [Theory]
    [InlineData("\"not a date\"")]
    [InlineData("0")]
    [InlineData("-7")]
    [InlineData("true")]
    [InlineData("null")]
    public void Unknown_start_time_formats_are_null(string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        RestoreJournalScanner.ParseStart(doc.RootElement).Should().BeNull();
    }

    [Fact]
    public void Pid_given_as_string_is_accepted()
    {
        using var dir = new TempDir();
        dir.File("taskbar.json", """{ "ownerPid": "77" }""");
        RestoreJournalScanner.Scan(dir.Path).Single().OwnerPid.Should().Be(77);
    }
}
