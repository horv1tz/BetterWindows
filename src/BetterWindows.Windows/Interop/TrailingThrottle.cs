namespace BetterWindows.Native.Interop;

/// <summary>Outcome of <see cref="TrailingThrottle{TKey}.Offer"/>.</summary>
internal enum ThrottleDecision
{
    /// <summary>Deliver the event now (leading edge).</summary>
    Deliver,
    /// <summary>Within the interval: remembered, will be returned by <see cref="TrailingThrottle{TKey}.Flush"/> once the interval has elapsed (trailing edge).</summary>
    Deferred,
}

/// <summary>
/// Per-key rate limiter with leading AND trailing edge: the first event of a burst is delivered immediately, further events inside
/// <c>interval</c> collapse into one delivery when the interval has elapsed, so the consumer never misses the final state (e.g. the last
/// title of a window whose title changes rapidly) yet never sees more than one event per key per interval. Pure logic with an injected
/// clock; the owner arms a timer for <see cref="Flush"/> after a <see cref="ThrottleDecision.Deferred"/>. Thread-safe.
/// </summary>
internal sealed class TrailingThrottle<TKey> where TKey : notnull
{
    private sealed class State
    {
        public long LastDeliveredMs;
        public bool Pending;
    }

    private readonly Dictionary<TKey, State> _states = new();
    private readonly object _gate = new();
    private readonly long _intervalMs;
    private readonly Func<long> _nowMs;
    private int _pendingCount;

    /// <param name="intervalMs">Minimum spacing between deliveries of the same key.</param>
    /// <param name="nowMs">Monotonic millisecond clock (Environment.TickCount64 in production).</param>
    public TrailingThrottle(int intervalMs, Func<long> nowMs)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(intervalMs);
        _intervalMs = intervalMs;
        _nowMs = nowMs;
    }

    /// <summary>True while at least one key waits for its trailing delivery (the owner must keep a flush timer armed).</summary>
    public bool HasPending
    {
        get
        {
            lock (_gate)
            {
                return _pendingCount > 0;
            }
        }
    }

    /// <summary>Registers an occurrence of <paramref name="key"/>.</summary>
    public ThrottleDecision Offer(TKey key)
    {
        lock (_gate)
        {
            long now = _nowMs();
            if (!_states.TryGetValue(key, out var s))
            {
                _states[key] = new State { LastDeliveredMs = now };
                return ThrottleDecision.Deliver;
            }

            if (now - s.LastDeliveredMs >= _intervalMs)
            {
                s.LastDeliveredMs = now;
                if (s.Pending)
                {
                    s.Pending = false;
                    _pendingCount--;
                }

                return ThrottleDecision.Deliver;
            }

            if (!s.Pending)
            {
                s.Pending = true;
                _pendingCount++;
            }

            return ThrottleDecision.Deferred;
        }
    }

    /// <summary>
    /// Delivers (through <paramref name="deliver"/>) every key whose trailing event is due and prunes idle keys.
    /// The callback runs OUTSIDE the internal lock. Returns the number of deliveries.
    /// </summary>
    public int Flush(Action<TKey> deliver)
    {
        List<TKey>? due = null;
        lock (_gate)
        {
            long now = _nowMs();
            List<TKey>? stale = null;
            foreach (var (key, s) in _states)
            {
                if (s.Pending)
                {
                    if (now - s.LastDeliveredMs >= _intervalMs)
                    {
                        s.Pending = false;
                        s.LastDeliveredMs = now;
                        _pendingCount--;
                        (due ??= new List<TKey>()).Add(key);
                    }
                }
                else if (now - s.LastDeliveredMs > _intervalMs * 40)
                {
                    // Idle for a long time: forget the key so the table does not grow with every window that ever existed.
                    (stale ??= new List<TKey>()).Add(key);
                }
            }

            if (stale != null)
            {
                foreach (var k in stale)
                {
                    _states.Remove(k);
                }
            }
        }

        if (due == null)
        {
            return 0;
        }

        foreach (var k in due)
        {
            deliver(k);
        }

        return due.Count;
    }

    /// <summary>Forgets a key (e.g. its window was destroyed) together with any pending trailing event.</summary>
    public void Remove(TKey key)
    {
        lock (_gate)
        {
            if (_states.Remove(key, out var s) && s.Pending)
            {
                _pendingCount--;
            }
        }
    }

    /// <summary>Forgets everything.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _states.Clear();
            _pendingCount = 0;
        }
    }
}
