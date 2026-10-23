namespace BetterWindows.Core.Ipc;

/// <summary>Resource limits of the IPC fabric (docs/IPC.md section 11). Enforced by MessageRouter / SurfaceRegistry / controllers.</summary>
public static class Limits
{
    /// <summary>Longest accepted web message (characters). Larger messages are dropped and logged.</summary>
    public const int MaxMessageChars = 1 << 20;
    public const int MaxInFlightCallsPerSurface = 32;
    /// <summary>Per-surface token bucket for ALL messages.</summary>
    public const double MessagesPerSecond = 200, MessageBurst = 400;
    public const double LogPerSecond = 20, ShapePerSecond = 30, ResizePerSecond = 60;
    public const int MaxLogMessageChars = 2048, MaxLogDataChars = 4096;
    public const int MaxShapeRects = 64;
    public const int MaxSearchQueryChars = 256;
    public const int MaxPatchBytes = 256 * 1024, MaxConfigBytes = 2 * 1024 * 1024;
}

/// <summary>Classic token bucket on a TimeProvider (thread-safe).</summary>
public sealed class TokenBucket
{
    private readonly double _rate, _burst;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private double _tokens;
    private long _last;

    public TokenBucket(double ratePerSecond, double burst, TimeProvider time)
    {
        _rate = ratePerSecond; _burst = burst; _time = time; _tokens = burst; _last = time.GetTimestamp();
    }

    public bool TryTake()
    {
        lock (_gate)
        {
            var now = _time.GetTimestamp();
            var dt = _time.GetElapsedTime(_last, now).TotalSeconds;
            _last = now;
            _tokens = Math.Min(_burst, _tokens + dt * _rate);
            if (_tokens < 1) return false;
            _tokens -= 1;
            return true;
        }
    }
}
