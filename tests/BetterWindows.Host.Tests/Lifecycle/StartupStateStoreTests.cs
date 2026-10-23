using BetterWindows.Host.Lifecycle;
using BetterWindows.Host.Tests.Support;

namespace BetterWindows.Host.Tests.Lifecycle;

public class StartupStateStoreTests
{
    private static StartupStateStore Store(string stateDir, FakeTime time, FakeProbe probe, int pid) =>
        new(stateDir, time, probe, pid);

    [Fact]
    public void First_start_is_not_unclean()
    {
        using var dir = new TempDir();
        var d = Store(dir.Path, new FakeTime(), new FakeProbe(), 100).BeginRun();
        d.PreviousExitUnclean.Should().BeFalse();
        d.RecentUncleanCount.Should().Be(0);
        d.CrashLoop.Should().BeFalse();
    }

    [Fact]
    public void A_clean_exit_is_not_counted()
    {
        using var dir = new TempDir();
        var time = new FakeTime();
        var probe = new FakeProbe();
        var first = Store(dir.Path, time, probe, 100);
        first.BeginRun();
        first.MarkCleanExit();

        var next = Store(dir.Path, time, probe, 101).BeginRun();
        next.PreviousExitUnclean.Should().BeFalse();
        next.RecentUncleanCount.Should().Be(0);
    }

    [Fact]
    public void Three_unclean_starts_within_ten_minutes_trigger_safe_mode_on_the_next_start()
    {
        using var dir = new TempDir();
        var time = new FakeTime();
        var probe = new FakeProbe();
        // Each run dies without a clean exit: its pid is gone when the next one starts.
        var pid = 100;
        var counts = new List<StartupDecision>();
        for (var i = 0; i < 4; i++)
        {
            counts.Add(Store(dir.Path, time, probe, pid).BeginRun());
            probe.Alive.Clear();
            pid++;
            time.Advance(TimeSpan.FromMinutes(1));
        }
        counts[0].CrashLoop.Should().BeFalse();
        counts[1].RecentUncleanCount.Should().Be(1);
        counts[2].RecentUncleanCount.Should().Be(2);
        counts[3].RecentUncleanCount.Should().Be(3);
        counts[3].CrashLoop.Should().BeTrue();
        counts[2].CrashLoop.Should().BeFalse();
    }

    [Fact]
    public void Unclean_starts_outside_the_ten_minute_window_do_not_count()
    {
        using var dir = new TempDir();
        var time = new FakeTime();
        var probe = new FakeProbe();
        Store(dir.Path, time, probe, 100).BeginRun();
        probe.Alive.Clear();
        time.Advance(TimeSpan.FromMinutes(11));
        var second = Store(dir.Path, time, probe, 101).BeginRun();
        second.PreviousExitUnclean.Should().BeTrue();
        second.RecentUncleanCount.Should().Be(0, "the first unclean start is older than the window");
    }

    [Fact]
    public void A_run_that_became_healthy_resets_the_counter()
    {
        using var dir = new TempDir();
        var time = new FakeTime();
        var probe = new FakeProbe();
        Store(dir.Path, time, probe, 100).BeginRun();
        probe.Alive.Clear();
        Store(dir.Path, time, probe, 101).BeginRun().RecentUncleanCount.Should().Be(1);

        // Run 101 stays alive for 60 s and becomes healthy: its list is cleared, so when it dies the next start sees no streak.
        probe.Alive.Add(101);
        Store(dir.Path, time, probe, 101).MarkHealthy();
        probe.Alive.Clear();
        var next = Store(dir.Path, time, probe, 102).BeginRun();
        next.PreviousExitUnclean.Should().BeTrue();
        next.RecentUncleanCount.Should().Be(0);
        next.CrashLoop.Should().BeFalse();
    }

    [Fact]
    public void Mark_healthy_clears_the_list_for_the_owning_run()
    {
        using var dir = new TempDir();
        var time = new FakeTime();
        var probe = new FakeProbe();
        var run = Store(dir.Path, time, probe, 100);
        run.BeginRun();
        probe.Alive.Clear();
        var second = Store(dir.Path, time, probe, 101);
        second.BeginRun();          // 101 counts 100 as unclean
        second.MarkHealthy();       // 101 became healthy: its list is cleared
        probe.Alive.Clear();
        var third = Store(dir.Path, time, probe, 102).BeginRun();
        third.PreviousExitUnclean.Should().BeTrue();
        third.RecentUncleanCount.Should().Be(0, "a healthy run clears the streak it carried");
    }

    [Fact]
    public void A_store_never_modifies_a_record_written_by_another_pid()
    {
        using var dir = new TempDir();
        var time = new FakeTime();
        var probe = new FakeProbe { Alive = { 100 } };
        Store(dir.Path, time, probe, 100).BeginRun();
        // pid 200 tries to mark a record it does not own: nothing changes, so the next start still sees 100 alive and clean=false.
        Store(dir.Path, time, probe, 200).MarkCleanExit();
        var next = Store(dir.Path, time, probe, 300).BeginRun();
        next.PreviousExitUnclean.Should().BeFalse("100 is still alive; the record was not clean");
    }

    [Fact]
    public void Corrupt_state_file_is_treated_as_first_start()
    {
        using var dir = new TempDir();
        dir.File("startup.json", "{{{ garbage");
        var d = Store(dir.Path, new FakeTime(), new FakeProbe(), 5).BeginRun();
        d.PreviousExitUnclean.Should().BeFalse();
        d.CrashLoop.Should().BeFalse();
    }
}
