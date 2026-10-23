using BetterWindows.Core.Input;
using BetterWindows.Core.Tests.Status;

namespace BetterWindows.Core.Tests.Input;

public class SerialRunnerTests
{
    [Fact]
    public void RunInline_executes_on_the_calling_thread()
    {
        var thread = -1;
        var runner = new SerialRunner(() => thread = Environment.CurrentManagedThreadId);
        runner.RunInline();
        thread.Should().Be(Environment.CurrentManagedThreadId);
        runner.Busy.Should().BeFalse();
    }

    [Fact]
    public async Task Trigger_runs_on_the_thread_pool_not_on_the_caller()
    {
        var thread = -1;
        var runner = new SerialRunner(() => thread = Environment.CurrentManagedThreadId);
        runner.Trigger();
        await Kit.Eventually(() => thread != -1);
        thread.Should().NotBe(Environment.CurrentManagedThreadId);
    }

    [Fact]
    public async Task Triggers_during_a_run_collapse_into_exactly_one_more_run()
    {
        var runs = 0;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var runner = new SerialRunner(() =>
        {
            if (Interlocked.Increment(ref runs) == 1) { entered.Set(); release.Wait(5000); }
        });
        runner.Trigger();
        entered.Wait(5000).Should().BeTrue();
        for (var i = 0; i < 25; i++) runner.Trigger();
        runner.Busy.Should().BeTrue();
        release.Set();
        await Kit.Eventually(() => !runner.Busy);
        runs.Should().Be(2);
    }

    [Fact]
    public async Task Runs_never_overlap()
    {
        var concurrent = 0;
        var maxConcurrent = 0;
        var runs = 0;
        var runner = new SerialRunner(() =>
        {
            var now = Interlocked.Increment(ref concurrent);
            InterlockedMax(ref maxConcurrent, now);
            Thread.Sleep(2);
            Interlocked.Decrement(ref concurrent);
            Interlocked.Increment(ref runs);
        });
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => { for (var i = 0; i < 50; i++) runner.Trigger(); })));
        await Kit.Eventually(() => !runner.Busy);
        maxConcurrent.Should().Be(1);
        runs.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task A_throwing_action_does_not_break_the_runner()
    {
        var runs = 0;
        var runner = new SerialRunner(() => { if (Interlocked.Increment(ref runs) == 1) throw new InvalidOperationException("boom"); });
        runner.RunInline();
        runner.RunInline();
        runs.Should().Be(2);
        runner.Trigger();
        await Kit.Eventually(() => Volatile.Read(ref runs) == 3);
    }

    [Fact]
    public async Task Stop_prevents_further_runs()
    {
        var runs = 0;
        var runner = new SerialRunner(() => Interlocked.Increment(ref runs));
        runner.RunInline();
        runner.Stop();
        runner.RunInline();
        runner.Trigger();
        await Kit.Settle();
        runs.Should().Be(1);
        runner.Busy.Should().BeFalse();
    }

    [Fact]
    public void RunInline_while_another_run_is_in_flight_queues_instead_of_running_concurrently()
    {
        var runs = 0;
        SerialRunner? runner = null;
        var nestedBusy = false;
        runner = new SerialRunner(() =>
        {
            if (Interlocked.Increment(ref runs) == 1)
            {
                runner!.RunInline();          // re-entrant request from inside the action: queued, returns immediately
                nestedBusy = runner.Busy;
            }
        });
        runner.RunInline();
        nestedBusy.Should().BeTrue();
        runs.Should().Be(2);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        do { current = Volatile.Read(ref target); if (value <= current) return; }
        while (Interlocked.CompareExchange(ref target, value, current) != current);
    }
}
