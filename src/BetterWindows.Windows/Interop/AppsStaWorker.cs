using System.Collections.Concurrent;

namespace BetterWindows.Native.Interop;

/// <summary>
/// A dedicated single-threaded apartment (STA) worker thread with a work queue. Shell COM objects (IShellItem, IShellLink, IDesktopWallpaper, ShellExecuteEx handlers)
/// must be created and used on an STA thread; the thread pool is MTA. Work items run strictly one after another on the same thread, so COM objects
/// created by an item may safely be used by later items. The thread is a background thread (never keeps the process alive) and blocks on a managed wait,
/// which the CLR pumps for COM calls on STA threads, so cross-apartment shell calls cannot deadlock against it.
/// </summary>
internal sealed class StaWorker : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;
    private int _disposed;

    /// <param name="name">Thread name (diagnostics).</param>
    /// <param name="sta">Always true in production. False exists only so the queue/timeout logic can be unit tested on operating systems without apartments.</param>
    public StaWorker(string name, bool sta = true)
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = name };
        // Must be set before Start: the CLR then calls CoInitializeEx(COINIT_APARTMENTTHREADED) when the thread starts and CoUninitialize when it ends.
        if (sta) _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>Queues <paramref name="func"/>; the task completes with its result or exception. Never throws synchronously.</summary>
    public Task<T> RunAsync<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _queue.Add(() =>
            {
                try { tcs.TrySetResult(func()); }
                catch (Exception ex) { tcs.TrySetException(ex); }
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            tcs.TrySetException(new ObjectDisposedException(nameof(StaWorker)));
        }
        return tcs.Task;
    }

    /// <summary>Queues <paramref name="action"/> (no result).</summary>
    public Task RunAsync(Action action) => RunAsync<object?>(() =>
    {
        action();
        return null;
    });

    private void Loop()
    {
        try
        {
            foreach (var item in _queue.GetConsumingEnumerable())
            {
                try { item(); }
                catch (Exception) { /* items report their own failures through their TaskCompletionSource */ }
            }
        }
        catch (Exception) { /* queue disposed while waiting: the worker is being torn down */ }
    }

    /// <summary>Lets the thread finish the item it is running (if any) and exit. Does not block: a hung shell extension must not hang shutdown.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { _queue.CompleteAdding(); }
        catch (ObjectDisposedException) { }
    }
}

/// <summary>
/// A bounded pool of <see cref="StaWorker"/>s: at most <c>size</c> items run concurrently, each with an execution timeout. A worker whose item times out is
/// considered hung (a misbehaving thumbnail/icon handler): it is retired, a fresh worker takes its place, and the caller gets a <see cref="TimeoutException"/>.
/// Time spent waiting for a free slot does NOT count towards the timeout (a Launchpad burst of 200 icon requests must not time out in the queue).
/// </summary>
internal sealed class StaPool : IDisposable
{
    private readonly string _name;
    private readonly bool _sta;
    private readonly SemaphoreSlim _gate;
    private readonly ConcurrentStack<StaWorker> _idle = new();
    private int _counter;
    private int _disposed;

    public StaPool(string name, int size, bool sta = true)
    {
        if (size < 1) throw new ArgumentOutOfRangeException(nameof(size));
        _name = name;
        _sta = sta;
        _gate = new SemaphoreSlim(size, size);
    }

    /// <exception cref="TimeoutException">The item did not finish within <paramref name="timeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled (the item itself, if already running, still finishes).</exception>
    public async Task<T> RunAsync<T>(Func<T> func, TimeSpan timeout, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _gate.WaitAsync(ct).ConfigureAwait(false);

        StaWorker worker;
        Task<T> work;
        try
        {
            if (!_idle.TryPop(out worker!))
                worker = new StaWorker($"{_name}-{Interlocked.Increment(ref _counter)}", _sta);
            work = worker.RunAsync(func);
        }
        catch
        {
            _gate.Release();
            throw;
        }

        // 0 = running, 1 = timed out (worker retired), 2 = completed (worker returned to the pool).
        int state = 0;
        _ = work.ContinueWith(t =>
        {
            _ = t.Exception; // observe so an abandoned faulted item never becomes an unobserved task exception
            if (Interlocked.CompareExchange(ref state, 2, 0) == 0)
            {
                if (Volatile.Read(ref _disposed) != 0) worker.Dispose();
                else _idle.Push(worker);
                _gate.Release();
            }
            else
            {
                worker.Dispose(); // it had timed out; the slot was already released
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var delay = Task.Delay(timeout, delayCts.Token);
        var finished = await Task.WhenAny(work, delay).ConfigureAwait(false);
        if (finished == work)
        {
            delayCts.Cancel();
            return await work.ConfigureAwait(false);
        }

        ct.ThrowIfCancellationRequested(); // cancelled by the caller: leave the item running, the continuation recycles the worker
        if (Interlocked.CompareExchange(ref state, 1, 0) == 0)
        {
            _gate.Release();
            throw new TimeoutException($"STA work item exceeded {timeout.TotalMilliseconds:0} ms.");
        }
        return await work.ConfigureAwait(false); // finished in the same instant
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        while (_idle.TryPop(out var w)) w.Dispose();
    }
}
