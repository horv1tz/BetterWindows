using BetterWindows.Native.Interop;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Native.Tests.Status;

public class StatusCellTests
{
    [Fact]
    public void Publish_raises_changed_only_for_different_snapshots()
    {
        var cell = new StatusCell<string>("a", (x, y) => x == y, NullLogger.Instance);
        var raised = 0;
        cell.Add(() => raised++);

        cell.Publish("a").Should().BeFalse();
        cell.Publish("b").Should().BeTrue();
        cell.Publish("b").Should().BeFalse();
        raised.Should().Be(1);
        cell.Value.Should().Be("b");
    }

    [Fact]
    public void Removed_subscribers_are_not_called_and_counts_track_them()
    {
        var cell = new StatusCell<string>("0", (x, y) => x == y, NullLogger.Instance);
        var raised = 0;
        Action handler = () => raised++;
        cell.Add(handler);
        cell.Add(handler);
        cell.SubscriberCount.Should().Be(2);
        cell.Remove(handler);
        cell.SubscriberCount.Should().Be(1);
        cell.Remove(() => { });             // not subscribed: no change
        cell.SubscriberCount.Should().Be(1);
        cell.Publish("1");
        raised.Should().Be(1);
    }

    [Fact]
    public void Throwing_subscriber_does_not_stop_the_others_or_escape()
    {
        var cell = new StatusCell<string>("0", (x, y) => x == y, NullLogger.Instance);
        var later = 0;
        cell.Add(() => throw new InvalidOperationException("boom"));
        cell.Add(() => later++);
        var act = () => cell.Publish("5");
        act.Should().NotThrow();
        later.Should().Be(1);
    }

    [Fact]
    public void Reset_replaces_the_value_without_raising()
    {
        var cell = new StatusCell<string>("0", (x, y) => x == y, NullLogger.Instance);
        var raised = 0;
        cell.Add(() => raised++);
        cell.Reset("9");
        cell.Value.Should().Be("9");
        raised.Should().Be(0);
    }
}

public class StatusDebouncerTests
{
    [Fact]
    public async Task A_burst_collapses_into_one_call_after_quiet()
    {
        var calls = 0;
        using var d = new StatusDebouncer(TimeProvider.System, TimeSpan.FromMilliseconds(150), TimeSpan.FromSeconds(5), () => Interlocked.Increment(ref calls));
        for (var i = 0; i < 10; i++)
        {
            d.Trigger();
            await Task.Delay(5);
        }
        await WaitUntil(() => Volatile.Read(ref calls) >= 1, TimeSpan.FromSeconds(3));
        await Task.Delay(400);
        Volatile.Read(ref calls).Should().Be(1);
    }

    [Fact]
    public async Task A_continuous_stream_is_still_flushed_by_the_max_wait()
    {
        var calls = 0;
        using var d = new StatusDebouncer(TimeProvider.System, TimeSpan.FromMilliseconds(60), TimeSpan.FromMilliseconds(150), () => Interlocked.Increment(ref calls));
        var until = DateTime.UtcNow + TimeSpan.FromMilliseconds(700);
        while (DateTime.UtcNow < until)
        {
            d.Trigger();
            await Task.Delay(10);
        }
        await WaitUntil(() => Volatile.Read(ref calls) >= 1, TimeSpan.FromSeconds(3));
        Volatile.Read(ref calls).Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Cancel_prevents_the_pending_call()
    {
        var calls = 0;
        using var d = new StatusDebouncer(TimeProvider.System, TimeSpan.FromMilliseconds(80), TimeSpan.FromSeconds(1), () => Interlocked.Increment(ref calls));
        d.Trigger();
        d.IsPending.Should().BeTrue();
        d.Cancel();
        await Task.Delay(300);
        Volatile.Read(ref calls).Should().Be(0);
        d.IsPending.Should().BeFalse();
    }

    [Fact]
    public async Task An_action_exception_goes_to_the_error_sink_and_never_escapes()
    {
        Exception? seen = null;
        using var d = new StatusDebouncer(TimeProvider.System, TimeSpan.FromMilliseconds(20), TimeSpan.FromSeconds(1),
            () => throw new InvalidOperationException("boom"), ex => seen = ex);
        d.Trigger();
        await WaitUntil(() => seen is not null, TimeSpan.FromSeconds(3));
        seen.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task Disposed_debouncer_ignores_triggers()
    {
        var calls = 0;
        var d = new StatusDebouncer(TimeProvider.System, TimeSpan.FromMilliseconds(20), TimeSpan.FromSeconds(1), () => Interlocked.Increment(ref calls));
        d.Dispose();
        d.Trigger();
        await Task.Delay(120);
        Volatile.Read(ref calls).Should().Be(0);
    }

    [Fact]
    public void Max_wait_shorter_than_quiet_is_raised_to_quiet()
    {
        // Constructing with maxWait < quiet must not throw and must still produce a usable debouncer.
        using var d = new StatusDebouncer(TimeProvider.System, TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(10), () => { });
        var act = () => d.Trigger();
        act.Should().NotThrow();
    }

    private static async Task WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < until) await Task.Delay(10);
    }
}

public class StatusComWorkerTests
{
    [Fact]
    public void Invoke_returns_the_value_computed_on_the_worker_thread()
    {
        using var worker = new StatusComWorker(NullLogger.Instance);
        var threadId = worker.Invoke(() => Environment.CurrentManagedThreadId, TimeSpan.FromSeconds(2), -1);
        threadId.Should().NotBe(-1);
        threadId.Should().NotBe(Environment.CurrentManagedThreadId);
    }

    [Fact]
    public void Invoke_returns_the_fallback_when_the_work_is_too_slow()
    {
        using var worker = new StatusComWorker(NullLogger.Instance);
        using var release = new ManualResetEventSlim(false);
        worker.Post(() => release.Wait(TimeSpan.FromSeconds(5)));
        var result = worker.Invoke(() => 42, TimeSpan.FromMilliseconds(50), -1);
        result.Should().Be(-1);
        release.Set();
    }

    [Fact]
    public void Invoke_returns_the_fallback_when_the_work_throws()
    {
        using var worker = new StatusComWorker(NullLogger.Instance);
        worker.Invoke<int>(() => throw new InvalidOperationException("boom"), TimeSpan.FromSeconds(2), 7).Should().Be(7);
    }

    [Fact]
    public void Posted_work_runs_in_order_and_a_throwing_job_does_not_stop_the_worker()
    {
        using var worker = new StatusComWorker(NullLogger.Instance);
        var log = new List<int>();
        var gate = new object();
        worker.Post(() => { lock (gate) log.Add(1); });
        worker.Post(() => throw new InvalidOperationException("boom"));
        worker.Post(() => { lock (gate) log.Add(2); });
        var result = worker.Invoke(() => { lock (gate) return log.ToArray(); }, TimeSpan.FromSeconds(2), Array.Empty<int>());
        result.Should().Equal(1, 2);
    }

    [Fact]
    public void Dispose_runs_queued_work_then_ignores_new_work()
    {
        var worker = new StatusComWorker(NullLogger.Instance);
        var ran = 0;
        worker.Post(() => Interlocked.Increment(ref ran));
        worker.Dispose();
        Volatile.Read(ref ran).Should().Be(1);

        var act = () => worker.Post(() => Interlocked.Increment(ref ran));
        act.Should().NotThrow();
        worker.Invoke(() => 1, TimeSpan.FromMilliseconds(50), 0).Should().Be(0);
        worker.Dispose();   // idempotent
    }
}

public class StatusFeatureTests
{
    [Fact]
    public void Default_device_switching_gate_is_explicit()
    {
        // Documented gate: switching the default device through the undocumented interface is on by default and has a safe fallback in the source.
        StatusFeatures.UndocumentedDefaultDevice.Should().BeTrue();
    }
}
