using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace BetterWindows.Native.Interop;

/// <summary>
/// A dedicated background thread that owns COM objects of the audio status source (NAudio's MMDevice tree, the undocumented IPolicyConfig). Work is
/// posted and runs in arrival order; callers never wait for it except through <see cref="Invoke{T}"/>, which is bounded by a timeout and is used only
/// for short, user-triggered calls. The thread is a background thread so it can never keep the process alive.
/// <para>
/// Apartment: the worker is a plain thread that never pumps messages and is never an STA, so COM calls on it use the MTA. NAudio's enumerator and its
/// notification callbacks are free-threaded and do not need a message loop.
/// </para>
/// </summary>
internal sealed class StatusComWorker : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly ILogger _log;
    private readonly object _gate = new();
    private Thread? _thread;
    private bool _disposed;

    public StatusComWorker(ILogger log)
    {
        _log = log;
    }

    /// <summary>Queues <paramref name="work"/>. Never blocks. Returns false (and drops the work) after <see cref="Dispose"/>.</summary>
    public bool Post(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (!EnsureStarted()) return false;
        try
        {
            _queue.Add(work);
            return true;
        }
        catch (InvalidOperationException)
        {
            // Completed by Dispose: the work is dropped on purpose, the process is shutting down.
            return false;
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/> on the worker and waits at most <paramref name="timeout"/>. Returns <paramref name="fallback"/> on timeout, after
    /// Dispose, or when the work throws. Must not be called from the worker itself (it would wait on its own queue).
    /// </summary>
    public T Invoke<T>(Func<T> work, TimeSpan timeout, T fallback)
    {
        ArgumentNullException.ThrowIfNull(work);
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!Post(() =>
            {
                try { done.TrySetResult(work()); }
                catch (Exception ex) { done.TrySetException(ex); }
            }))
        {
            return fallback;   // disposed: answer at once instead of waiting out the timeout
        }
        try
        {
            // Wait returned true only after the task completed, so the Result read never blocks.
            return done.Task.Wait(timeout) ? done.Task.Result : fallback;
        }
        catch (AggregateException ex)
        {
            _log.LogWarning(ex.InnerException ?? ex, "COM work on the status worker failed");
            return fallback;
        }
    }

    private bool EnsureStarted()
    {
        lock (_gate)
        {
            if (_disposed) return false;
            if (_thread is not null) return true;
            var t = new Thread(Run) { IsBackground = true, Name = "BetterWindows.StatusCom" };
            _thread = t;
            t.Start();
            return true;
        }
    }

    private void Run()
    {
        foreach (var work in _queue.GetConsumingEnumerable())
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                // A COM failure in one job must not stop the worker: every later job still needs the apartment.
                _log.LogWarning(ex, "COM work on the status worker threw");
            }
        }
    }

    /// <summary>Stops accepting work, runs what is already queued (so COM objects are released on their own thread) and waits up to 2 s for it.</summary>
    public void Dispose()
    {
        Thread? t;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            t = _thread;
        }
        try { _queue.CompleteAdding(); } catch (ObjectDisposedException) { }
        if (t is not null && t != Thread.CurrentThread) t.Join(TimeSpan.FromSeconds(2));
    }
}
