namespace BetterWindows.Core.Tests.Support;

/// <summary>Deterministic clock: time only moves when the test calls <see cref="Advance"/>, which fires due timers synchronously in due order.</summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = new();
    private DateTimeOffset _now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    private long _ticks;

    public override DateTimeOffset GetUtcNow() { lock (_gate) return _now; }
    public override long GetTimestamp() { lock (_gate) return _ticks; }
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var t = new ManualTimer(this, callback, state);
        t.Change(dueTime, period);
        return t;
    }

    public void Advance(TimeSpan by)
    {
        var target = GetTimestamp() + by.Ticks;
        while (true)
        {
            ManualTimer? next;
            lock (_gate)
            {
                next = _timers.Where(t => t.Armed && t.Due <= target).OrderBy(t => t.Due).FirstOrDefault();
                if (next is null)
                {
                    _now += TimeSpan.FromTicks(target - _ticks);
                    _ticks = target;
                    return;
                }
                _now += TimeSpan.FromTicks(next.Due - _ticks);
                _ticks = next.Due;
                next.Fired();
            }
            next.Invoke();
        }
    }

    private sealed class ManualTimer : ITimer
    {
        private readonly ManualTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        public bool Armed;
        public long Due;
        private long _period;

        public ManualTimer(ManualTimeProvider owner, TimerCallback cb, object? state) { _owner = owner; _callback = cb; _state = state; lock (owner._gate) owner._timers.Add(this); }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (_owner._gate)
            {
                if (dueTime == Timeout.InfiniteTimeSpan) { Armed = false; return true; }
                Armed = true;
                Due = _owner._ticks + dueTime.Ticks;
                _period = period == Timeout.InfiniteTimeSpan ? 0 : period.Ticks;
                return true;
            }
        }

        public void Fired() { if (_period > 0) Due += _period; else Armed = false; }
        public void Invoke() => _callback(_state);
        public void Dispose() { lock (_owner._gate) { Armed = false; _owner._timers.Remove(this); } }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
