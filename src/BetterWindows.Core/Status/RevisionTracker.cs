namespace BetterWindows.Core.Status;

/// <summary>
/// Hands out a cache-busting revision number that increments ONLY when the observed key changes (media art: title/artist/album/art marker; tray icons: the icon's identity).
/// Observing the same key repeatedly is idempotent, so it is safe to call from a state-topic snapshot function that runs once per surface.
/// </summary>
public sealed class RevisionTracker
{
    private readonly object _gate = new();
    private string? _key;
    private int _rev;

    /// <summary>The revision for <paramref name="key"/>; the first observation yields 1.</summary>
    public int Observe(string key)
    {
        lock (_gate)
        {
            if (!string.Equals(_key, key, StringComparison.Ordinal))
            {
                _key = key;
                _rev++;
            }
            return _rev;
        }
    }

    /// <summary>The last revision handed out (0 before the first observation).</summary>
    public int Current { get { lock (_gate) return _rev; } }
}
