namespace BetterWindows.Host.Bridge;

/// <summary>
/// Health bookkeeping of one surface (docs/IPC.md section 16): unhealthy once it failed or did not complete surface.ready within its timeout; healthy again after the next
/// successful surface.ready. Automatic reloads (render/GPU crashes, hangs) are budgeted: at most 3 per 60 s. Thread-safe.
/// </summary>
public sealed class SurfaceHealth
{
    public const int MaxReloads = 3;
    public static readonly TimeSpan ReloadWindow = TimeSpan.FromSeconds(60);

    private readonly SlidingWindowLimiter _reloads;
    private readonly object _gate = new();
    private bool _ready, _failed, _readyTimedOut;

    public SurfaceHealth(TimeProvider? time = null) { _reloads = new SlidingWindowLimiter(MaxReloads, ReloadWindow, time); }

    /// <summary>False while failed or when the ready timeout elapsed.</summary>
    public bool IsHealthy { get { lock (_gate) return !_failed && !_readyTimedOut; } }
    public bool IsReady { get { lock (_gate) return _ready; } }

    /// <summary>A new document starts loading: not ready, not timed out (yet). A previous hard failure stays until ready.</summary>
    public void OnNavigationStarted() { lock (_gate) { _ready = false; _readyTimedOut = false; } }
    /// <summary>surface.ready completed: the surface is fully recovered.</summary>
    public void OnReady() { lock (_gate) { _ready = true; _failed = false; _readyTimedOut = false; } }
    public void OnReadyTimeout() { lock (_gate) { if (!_ready) _readyTimedOut = true; } }
    public void MarkFailed() { lock (_gate) { _failed = true; _ready = false; } }
    /// <summary>Spends one automatic reload from the budget; false = exhausted (the caller marks the surface failed).</summary>
    public bool TryBeginReload() => _reloads.TryAcquire();
}
