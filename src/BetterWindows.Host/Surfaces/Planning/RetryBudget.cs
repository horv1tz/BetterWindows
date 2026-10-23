namespace BetterWindows.Host.Surfaces;

/// <summary>
/// "At most N events per window" limiter (a surface that keeps failing must not be re-created forever). Time is passed in (monotonic TimeSpan), so tests need no clock.
/// </summary>
internal sealed class RetryBudget
{
    private readonly int _max;
    private readonly TimeSpan _window;
    private readonly Queue<TimeSpan> _stamps = new();

    public RetryBudget(int max, TimeSpan window)
    {
        _max = Math.Max(1, max);
        _window = window;
    }

    /// <summary>Consumes one attempt; false when the budget of the sliding window is used up.</summary>
    public bool TryAcquire(TimeSpan now)
    {
        while (_stamps.Count > 0 && now - _stamps.Peek() >= _window) _stamps.Dequeue();
        if (_stamps.Count >= _max) return false;
        _stamps.Enqueue(now);
        return true;
    }

    public void Reset() => _stamps.Clear();
}
