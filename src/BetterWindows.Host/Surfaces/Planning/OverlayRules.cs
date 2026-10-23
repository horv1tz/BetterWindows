using BetterWindows.Core.Ipc;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Host.Surfaces;

/// <summary>
/// ShowOverlay arriving before the overlay page completed surface.ready (docs: "queued, latest wins, 3 s timeout, then log and drop"). Time is passed in so the rule is testable.
/// </summary>
internal sealed class PendingOverlayRequest
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    private OverlayRequest? _request;
    private SurfaceId? _source;
    private TimeSpan _queuedAt;

    public bool HasRequest => _request is not null;

    /// <summary>Queues a request; an older queued one is replaced (latest wins). Returns true when it replaced an older request.</summary>
    public bool Enqueue(OverlayRequest request, SurfaceId? source, TimeSpan now)
    {
        var replaced = _request is not null;
        _request = request;
        _source = source;
        _queuedAt = now;
        return replaced;
    }

    /// <summary>Takes the queued request if it is still fresh. An expired request is dropped (returns false and <paramref name="expired"/> is true).</summary>
    public bool TryTake(TimeSpan now, out OverlayRequest request, out SurfaceId? source, out bool expired)
    {
        request = null!;
        source = null;
        expired = false;
        if (_request is null) return false;
        var fresh = now - _queuedAt <= Timeout;
        var r = _request;
        var s = _source;
        _request = null;
        _source = null;
        if (!fresh) { expired = true; return false; }
        request = r;
        source = s;
        return true;
    }

    public void Clear() { _request = null; _source = null; }
}

/// <summary>What to do when the overlay window lost activation (debounced by 100 ms).</summary>
internal enum DeactivationDecision
{
    /// <summary>The overlay is the foreground window again (the deactivation was a flicker, e.g. focus moved to the WebView child): do nothing.</summary>
    Keep,
    /// <summary>Right after showing, the foreground lock may have denied activation: try to take the foreground again instead of dismissing.</summary>
    RetryActivation,
    /// <summary>Another window really took the foreground: dismiss the overlay and tell Core.</summary>
    Hide,
}

/// <summary>
/// Dismiss-on-deactivation policy. A debounce of 100 ms (applied by the caller) removes activation flicker; on top of that, activation failures within the
/// <see cref="Grace"/> window after the show are retried rather than treated as "the user went elsewhere" (otherwise a denied SetForegroundWindow would make Spotlight
/// vanish the moment it appears).
/// </summary>
internal static class OverlayActivationPolicy
{
    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(400);
    public const int MaxActivationRetries = 3;

    public static DeactivationDecision Decide(bool overlayIsForeground, TimeSpan sinceShown, int retriesSoFar)
    {
        if (overlayIsForeground) return DeactivationDecision.Keep;
        if (sinceShown < Grace && retriesSoFar < MaxActivationRetries) return DeactivationDecision.RetryActivation;
        return DeactivationDecision.Hide;
    }
}
