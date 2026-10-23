using BetterWindows.Core.Ipc;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Tests.Support;

namespace BetterWindows.Core.Tests.Ipc;

public class StatePublisherTests
{
    [Fact]
    public void Initial_state_goes_only_to_surface_kinds_in_the_initial_list_after_the_handshake()
    {
        using var h = new CoreHarness();
        var battery = new BatteryState { Present = true, Percent = 50 };
        h.State.Declare(Msg.StatusBattery, _ => battery, TimeSpan.Zero);
        var dock = h.AddSurface(SurfaceKind.Dock, ready: false);
        var bar = h.AddSurface(SurfaceKind.Menubar, "1", ready: false);

        h.Registry.MarkReadyForTests(dock);
        h.Registry.MarkReadyForTests(bar);

        dock.Events(Msg.StatusBattery).Should().BeEmpty(); // dock does not consume battery
        bar.Events(Msg.StatusBattery).Should().HaveCount(1);
    }

    [Fact]
    public void Identical_snapshots_are_not_reposted()
    {
        using var h = new CoreHarness();
        var level = 50;
        var topic = h.State.Declare(Msg.StatusBattery, _ => new BatteryState { Present = true, Percent = level }, TimeSpan.Zero);
        var bar = h.AddSurface(SurfaceKind.Menubar, "1");

        topic.Invalidate(); topic.Invalidate(); topic.Invalidate();
        bar.Events(Msg.StatusBattery).Should().HaveCount(1);
        level = 49;
        topic.Invalidate();
        bar.Events(Msg.StatusBattery).Should().HaveCount(2);
    }

    [Fact]
    public void Bursts_are_coalesced_trailing_edge_on_the_injected_clock()
    {
        using var h = new CoreHarness();
        var level = 1;
        var topic = h.State.Declare(Msg.StatusBattery, _ => new BatteryState { Present = true, Percent = level }, TimeSpan.FromMilliseconds(50));
        var bar = h.AddSurface(SurfaceKind.Menubar, "1");
        bar.Clear();

        for (var i = 1; i <= 20; i++) { level = i; topic.Invalidate(); h.Time.Advance(TimeSpan.FromMilliseconds(1)); }
        bar.Events(Msg.StatusBattery).Should().BeEmpty();
        h.Time.Advance(TimeSpan.FromMilliseconds(60));

        var events = bar.Events(Msg.StatusBattery);
        events.Should().HaveCount(1);
        events[0]["payload"]!["percent"]!.GetValue<int>().Should().Be(20); // the NEWEST snapshot
    }

    [Fact]
    public void Newest_snapshot_is_always_the_last_one_posted_even_under_concurrency()
    {
        using var h = new CoreHarness();
        var current = 0;
        var topic = h.State.Declare(Msg.StatusBattery, _ => new BatteryState { Present = true, Percent = Volatile.Read(ref current) }, TimeSpan.Zero);
        var bar = h.AddSurface(SurfaceKind.Menubar, "1");

        Parallel.For(1, 400, i => { Volatile.Write(ref current, i % 100); topic.Invalidate(); });
        Volatile.Write(ref current, 77);
        topic.Invalidate();

        bar.Events(Msg.StatusBattery).Last()["payload"]!["percent"]!.GetValue<int>().Should().Be(77);
    }

    [Fact]
    public void A_null_snapshot_sends_nothing_to_that_surface()
    {
        using var h = new CoreHarness();
        h.State.Declare<DesktopState>(Msg.DesktopState, id => id.MonitorId == "1" ? new DesktopState { MonitorId = "1" } : null, TimeSpan.Zero);
        var d1 = h.AddSurface(SurfaceKind.Desktop, "1");
        var d2 = h.AddSurface(SurfaceKind.Desktop, "2");
        d1.Events(Msg.DesktopState).Should().HaveCount(1);
        d2.Events(Msg.DesktopState).Should().BeEmpty();
    }

    [Fact]
    public void A_new_channel_for_the_same_surface_gets_the_state_again()
    {
        using var h = new CoreHarness();
        h.State.Declare(Msg.StatusBattery, _ => new BatteryState { Present = true, Percent = 10 }, TimeSpan.Zero);
        var first = h.AddSurface(SurfaceKind.Menubar, "1");
        h.Registry.Unregister(first);
        var second = h.AddSurface(SurfaceKind.Menubar, "1");
        second.Events(Msg.StatusBattery).Should().HaveCount(1);
    }

    [Fact]
    public void Declaring_a_non_state_event_or_a_second_producer_is_rejected()
    {
        using var h = new CoreHarness();
        h.State.Invoking(s => s.Declare(Msg.DockBounce, _ => new DockBounce(), null)).Should().Throw<ArgumentException>();
        h.State.Declare(Msg.StatusBattery, _ => new BatteryState(), TimeSpan.Zero);
        h.State.Invoking(s => s.Declare(Msg.StatusBattery, _ => new BatteryState(), TimeSpan.Zero)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_throwing_snapshot_function_does_not_break_other_topics_or_surfaces()
    {
        using var h = new CoreHarness();
        h.State.Declare<BatteryState>(Msg.StatusBattery, _ => throw new InvalidOperationException("boom"), TimeSpan.Zero);
        h.State.Declare(Msg.StatusNetwork, _ => new NetworkState { Connected = true }, TimeSpan.Zero);
        var bar = h.AddSurface(SurfaceKind.Menubar, "1");
        bar.Events(Msg.StatusNetwork).Should().HaveCount(1);
    }
}
