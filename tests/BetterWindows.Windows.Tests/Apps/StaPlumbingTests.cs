using BetterWindows.Native.Interop;

namespace BetterWindows.Native.Tests.Apps;

/// <summary>The queue / bounded-concurrency / timeout logic of the worker pool, run without apartments so it is verified on every OS.</summary>
public class StaPlumbingTests
{
    [Fact]
    public async Task Worker_runs_items_in_order_on_one_thread()
    {
        using var worker = new StaWorker("t", sta: false);
        var order = new List<int>();
        var tasks = Enumerable.Range(0, 20).Select(i => worker.RunAsync(() => { lock (order) order.Add(i); return Environment.CurrentManagedThreadId; })).ToList();
        var threads = await Task.WhenAll(tasks);
        order.Should().Equal(Enumerable.Range(0, 20));
        threads.Distinct().Should().HaveCount(1);
    }

    [Fact]
    public async Task Worker_survives_a_throwing_item()
    {
        using var worker = new StaWorker("t", sta: false);
        var bad = async () => await worker.RunAsync<int>(() => throw new InvalidOperationException("x"));
        await bad.Should().ThrowAsync<InvalidOperationException>();
        (await worker.RunAsync(() => 7)).Should().Be(7);
        await worker.RunAsync(() => { });
    }

    [Fact]
    public async Task Disposed_worker_fails_new_work_instead_of_hanging()
    {
        var worker = new StaWorker("t", sta: false);
        worker.Dispose();
        worker.Dispose();
        var act = async () => await worker.RunAsync(() => 1);
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task Pool_bounds_concurrency()
    {
        using var pool = new StaPool("p", 2, sta: false);
        int running = 0, maxRunning = 0;
        int Work()
        {
            int now = Interlocked.Increment(ref running);
            int seen;
            while ((seen = Volatile.Read(ref maxRunning)) < now && Interlocked.CompareExchange(ref maxRunning, now, seen) != seen) { }
            Thread.Sleep(60);
            Interlocked.Decrement(ref running);
            return 1;
        }
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => pool.RunAsync(Work, TimeSpan.FromSeconds(10), CancellationToken.None)));
        results.Should().OnlyContain(r => r == 1);
        maxRunning.Should().BeLessThanOrEqualTo(2);
        maxRunning.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Queue_wait_does_not_count_against_the_execution_timeout()
    {
        using var pool = new StaPool("p", 1, sta: false);
        // 6 items of 80 ms with a 250 ms timeout each: the last one waits ~400 ms in the queue but only runs 80 ms.
        var tasks = Enumerable.Range(0, 6).Select(_ => pool.RunAsync(() => { Thread.Sleep(80); return 1; }, TimeSpan.FromMilliseconds(250), CancellationToken.None)).ToList();
        (await Task.WhenAll(tasks)).Should().OnlyContain(r => r == 1);
    }

    [Fact]
    public async Task Hung_item_times_out_and_the_pool_recovers_with_a_new_worker()
    {
        using var pool = new StaPool("p", 1, sta: false);
        using var release = new ManualResetEventSlim(false);
        int hungThread = 0;
        var hung = async () => await pool.RunAsync(() => { hungThread = Environment.CurrentManagedThreadId; release.Wait(); return 0; }, TimeSpan.FromMilliseconds(150), CancellationToken.None);
        await hung.Should().ThrowAsync<TimeoutException>();

        // size is 1 and the only worker is stuck: the slot must still have been released, served by a replacement thread
        int next = await pool.RunAsync(() => Environment.CurrentManagedThreadId, TimeSpan.FromSeconds(5), CancellationToken.None);
        next.Should().NotBe(hungThread);

        release.Set(); // the stuck item finishes; its worker is retired (no exception, no deadlock)
        (await pool.RunAsync(() => 3, TimeSpan.FromSeconds(5), CancellationToken.None)).Should().Be(3);
    }

    [Fact]
    public async Task Workers_are_reused_between_calls()
    {
        using var pool = new StaPool("p", 1, sta: false);
        var a = await pool.RunAsync(() => Environment.CurrentManagedThreadId, TimeSpan.FromSeconds(5), CancellationToken.None);
        var b = await pool.RunAsync(() => Environment.CurrentManagedThreadId, TimeSpan.FromSeconds(5), CancellationToken.None);
        b.Should().Be(a);
    }

    [Fact]
    public async Task Cancellation_before_start_throws_and_leaves_the_pool_usable()
    {
        using var pool = new StaPool("p", 1, sta: false);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var act = async () => await pool.RunAsync(() => 1, TimeSpan.FromSeconds(5), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
        (await pool.RunAsync(() => 2, TimeSpan.FromSeconds(5), CancellationToken.None)).Should().Be(2);
    }

    [Fact]
    public async Task Cancellation_while_waiting_for_a_slot_throws_without_running_the_item()
    {
        using var pool = new StaPool("p", 1, sta: false);
        using var release = new ManualResetEventSlim(false);
        var blocker = pool.RunAsync(() => { release.Wait(); return 0; }, TimeSpan.FromSeconds(10), CancellationToken.None);
        using var cts = new CancellationTokenSource(100);
        bool ran = false;
        var waiting = async () => await pool.RunAsync(() => { ran = true; return 1; }, TimeSpan.FromSeconds(5), cts.Token);
        await waiting.Should().ThrowAsync<OperationCanceledException>();
        release.Set();
        await blocker;
        ran.Should().BeFalse();
        (await pool.RunAsync(() => 5, TimeSpan.FromSeconds(5), CancellationToken.None)).Should().Be(5);
    }

    [Fact]
    public async Task Cancellation_while_running_throws_but_the_item_finishes_and_the_worker_is_recycled()
    {
        using var pool = new StaPool("p", 1, sta: false);
        using var cts = new CancellationTokenSource();
        bool finished = false;
        var running = pool.RunAsync(() => { Thread.Sleep(300); finished = true; return 1; }, TimeSpan.FromSeconds(5), cts.Token);
        await Task.Delay(50);
        cts.Cancel();
        var act = async () => await running;
        await act.Should().ThrowAsync<OperationCanceledException>();
        // the next call waits for the slot until the abandoned item is done, then succeeds
        (await pool.RunAsync(() => 9, TimeSpan.FromSeconds(5), CancellationToken.None)).Should().Be(9);
        finished.Should().BeTrue();
    }

    [Fact]
    public async Task Exceptions_propagate_to_the_caller_and_free_the_slot()
    {
        using var pool = new StaPool("p", 1, sta: false);
        var act = async () => await pool.RunAsync<int>(() => throw new InvalidOperationException("boom"), TimeSpan.FromSeconds(5), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        (await pool.RunAsync(() => 1, TimeSpan.FromSeconds(5), CancellationToken.None)).Should().Be(1);
    }

    [Fact]
    public async Task Disposed_pool_refuses_work()
    {
        var pool = new StaPool("p", 1, sta: false);
        await pool.RunAsync(() => 1, TimeSpan.FromSeconds(5), CancellationToken.None);
        pool.Dispose();
        pool.Dispose();
        var act = async () => await pool.RunAsync(() => 1, TimeSpan.FromSeconds(5), CancellationToken.None);
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void Pool_size_must_be_positive()
    {
        var act = () => new StaPool("p", 0, sta: false);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
