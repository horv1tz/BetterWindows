using BetterWindows.Native.Interop;

namespace BetterWindows.Native.Tests.Shell;

/// <summary>The per-window 50 ms limiter (leading + trailing edge); fake clock, runs everywhere.</summary>
public class TrailingThrottleTests
{
    private long _now = 1000;

    private TrailingThrottle<int> Create(int interval = 50) => new(interval, () => _now);

    [Fact]
    public void First_event_is_delivered_immediately()
    {
        var t = Create();
        t.Offer(1).Should().Be(ThrottleDecision.Deliver);
        t.HasPending.Should().BeFalse();
    }

    [Fact]
    public void Burst_inside_the_interval_is_deferred_and_collapsed_into_one_trailing_delivery()
    {
        var t = Create();
        t.Offer(1);
        _now += 10;
        t.Offer(1).Should().Be(ThrottleDecision.Deferred);
        _now += 10;
        t.Offer(1).Should().Be(ThrottleDecision.Deferred);
        t.HasPending.Should().BeTrue();

        var delivered = new List<int>();
        _now += 10;
        t.Flush(delivered.Add).Should().Be(0); // 30 ms since the leading delivery: not due yet
        _now += 30;
        t.Flush(delivered.Add).Should().Be(1);
        delivered.Should().Equal(1);
        t.HasPending.Should().BeFalse();
        t.Flush(delivered.Add).Should().Be(0);
    }

    [Fact]
    public void Event_after_the_interval_is_a_new_leading_edge()
    {
        var t = Create();
        t.Offer(1);
        _now += 50;
        t.Offer(1).Should().Be(ThrottleDecision.Deliver);
    }

    [Fact]
    public void Keys_are_independent()
    {
        var t = Create();
        t.Offer(1);
        t.Offer(2).Should().Be(ThrottleDecision.Deliver);
        t.Offer(1).Should().Be(ThrottleDecision.Deferred);
        t.Offer(2).Should().Be(ThrottleDecision.Deferred);
        _now += 60;
        var delivered = new List<int>();
        t.Flush(delivered.Add).Should().Be(2);
        delivered.Should().BeEquivalentTo(new[] { 1, 2 });
    }

    [Fact]
    public void Remove_drops_a_pending_trailing_event()
    {
        var t = Create();
        t.Offer(7);
        t.Offer(7);
        t.HasPending.Should().BeTrue();
        t.Remove(7);
        t.HasPending.Should().BeFalse();
        _now += 100;
        t.Flush(_ => throw new InvalidOperationException("must not deliver")).Should().Be(0);
    }

    [Fact]
    public void Idle_keys_are_pruned_so_the_table_does_not_grow_forever()
    {
        var t = Create();
        for (int i = 0; i < 100; i++)
        {
            t.Offer(i);
        }

        _now += 50 * 41;
        t.Flush(_ => { });
        // After pruning, the first offer of any key is a fresh leading edge again and nothing is pending.
        t.Offer(1).Should().Be(ThrottleDecision.Deliver);
        t.HasPending.Should().BeFalse();
    }

    [Fact]
    public void A_delivery_that_throws_does_not_lose_the_other_due_keys_state()
    {
        var t = Create();
        t.Offer(1);
        t.Offer(1);
        _now += 60;
        var act = () => t.Flush(_ => throw new InvalidOperationException());
        act.Should().Throw<InvalidOperationException>();
        t.HasPending.Should().BeFalse(); // state was already updated under the lock; nothing is stuck pending
    }

    [Fact]
    public void Interval_must_be_positive()
    {
        var act = () => new TrailingThrottle<int>(0, () => 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
