namespace BetterWindows.Host.Bridge;

/// <summary>"At most N per window" (reload budget: 3 per 60 s; environment recreation: 1 per 60 s). Thread-safe, driven by an injected clock.</summary>
public sealed class SlidingWindowLimiter
{
    private readonly int _max;
    private readonly TimeSpan _window;
    private readonly TimeProvider _time;
    private readonly Queue<DateTimeOffset> _hits = new();
    private readonly object _gate = new();

    public SlidingWindowLimiter(int max, TimeSpan window, TimeProvider? time = null)
    {
        _max = max;
        _window = window;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Records a hit and returns true if it is within budget; a refused hit is not recorded.</summary>
    public bool TryAcquire()
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            while (_hits.Count > 0 && now - _hits.Peek() >= _window) _hits.Dequeue();
            if (_hits.Count >= _max) return false;
            _hits.Enqueue(now);
            return true;
        }
    }

    public void Reset() { lock (_gate) _hits.Clear(); }
}
