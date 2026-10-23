namespace BetterWindows.Native.Interop;

/// <summary>
/// Trailing-edge event debouncer with a maximum wait: bursts of <see cref="Trigger"/> calls collapse into ONE call of the action, fired
/// <c>quiet</c> after the last trigger, but never later than <c>maxWait</c> after the first trigger of the burst (so a continuous stream, e.g. a volume slider
/// drag or an RSSI flood, still updates the UI regularly). Thread-safe; the action runs on a timer (thread-pool) thread, never under the internal lock,
/// and an exception thrown by it is swallowed and reported through <c>onError</c> (a timer callback must never crash the process).
/// </summary>
public sealed class StatusDebouncer : IDisposable
{
    private readonly TimeProvider _time;
    private readonly TimeSpan _quiet;
    private readonly TimeSpan _maxWait;
    private readonly Action _action;
    private readonly Action<Exception>? _onError;
    private readonly object _gate = new();
    private ITimer? _timer;
    private long _burstStart;   // timestamp of the first trigger of the pending burst; 0 when idle
    private bool _pending;
    private bool _disposed;

    public StatusDebouncer(TimeProvider time, TimeSpan quiet, TimeSpan maxWait, Action action, Action<Exception>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(action);
        if (quiet < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(quiet));
        _time = time;
        _quiet = quiet;
        _maxWait = maxWait < quiet ? quiet : maxWait;
        _action = action;
        _onError = onError;
    }

    /// <summary>True while a call of the action is scheduled.</summary>
    public bool IsPending { get { lock (_gate) return _pending; } }

    /// <summary>Requests the action; coalesced with other triggers of the same burst.</summary>
    public void Trigger()
    {
        lock (_gate)
        {
            if (_disposed) return;
            var now = _time.GetTimestamp();
            if (!_pending)
            {
                _pending = true;
                _burstStart = now;
            }
            var elapsed = _time.GetElapsedTime(_burstStart, now);
            var remainingCap = _maxWait - elapsed;
            var due = _quiet < remainingCap ? _quiet : remainingCap;
            if (due < TimeSpan.Zero) due = TimeSpan.Zero;
            if (_timer is null) _timer = _time.CreateTimer(OnTimer, null, due, Timeout.InfiniteTimeSpan);
            else _timer.Change(due, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Cancels a scheduled call without running it.</summary>
    public void Cancel()
    {
        lock (_gate)
        {
            _pending = false;
            _burstStart = 0;
            _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnTimer(object? state)
    {
        lock (_gate)
        {
            if (_disposed || !_pending) return;
            _pending = false;
            _burstStart = 0;
        }
        try { _action(); }
        catch (Exception ex)
        {
            try { _onError?.Invoke(ex); } catch { /* the error sink itself must not throw out of a timer callback */ }
        }
    }

    public void Dispose()
    {
        ITimer? t;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _pending = false;
            t = _timer;
            _timer = null;
        }
        t?.Dispose();
    }
}
