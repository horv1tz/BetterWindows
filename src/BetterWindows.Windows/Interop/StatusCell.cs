using Microsoft.Extensions.Logging;

namespace BetterWindows.Native.Interop;

/// <summary>
/// Holds the latest immutable snapshot of one status source and raises <c>Changed</c> when a new snapshot is not equal to the previous one.
/// <para>
/// Contract shared by every status port (docs: Core/Platform/Ports.Status.cs): <see cref="Value"/> is a cheap, lock-free read that is safe from any thread;
/// <c>Changed</c> is raised on whatever thread called <see cref="Publish"/> (a WinRT/COM/thread-pool thread, never while a lock of this class is held), through
/// <see cref="SafeEvent"/> so a faulty subscriber can never unwind into an OS callback; identical consecutive snapshots are swallowed (deduplication).
/// </para>
/// </summary>
internal sealed class StatusCell<T> where T : class
{
    private readonly Func<T, T, bool> _equals;
    private readonly ILogger _log;
    private readonly object _gate = new();
    private T _value;
    private Action? _changed;
    private int _subscribers;

    public StatusCell(T initial, Func<T, T, bool> equals, ILogger log)
    {
        _value = initial;
        _equals = equals;
        _log = log;
    }

    /// <summary>Latest snapshot (volatile read).</summary>
    public T Value => Volatile.Read(ref _value);

    /// <summary>Number of current <c>Changed</c> subscribers.</summary>
    public int SubscriberCount => Volatile.Read(ref _subscribers);

    /// <summary>Adds a subscriber. Returns the new subscriber count.</summary>
    public int Add(Action? handler)
    {
        if (handler is null) return SubscriberCount;
        lock (_gate)
        {
            _changed += handler;
            _subscribers++;
            return _subscribers;
        }
    }

    /// <summary>Removes a subscriber. Returns the new subscriber count.</summary>
    public int Remove(Action? handler)
    {
        if (handler is null) return SubscriberCount;
        lock (_gate)
        {
            var before = _changed;
            _changed -= handler;
            if (!ReferenceEquals(before, _changed) && _subscribers > 0) _subscribers--;
            return _subscribers;
        }
    }

    /// <summary>
    /// Stores <paramref name="next"/> and raises <c>Changed</c> if it differs from the current snapshot. Returns true when a change was published.
    /// Concurrent publishers are serialised for the compare-and-store; the event itself is raised outside the lock.
    /// </summary>
    public bool Publish(T next)
    {
        Action? handler;
        lock (_gate)
        {
            if (_equals(_value, next)) return false;
            Volatile.Write(ref _value, next);
            handler = _changed;
        }
        SafeEvent.Raise(handler, ex => _log.LogWarning(ex, "Status Changed subscriber threw"));
        return true;
    }

    /// <summary>Replaces the snapshot without raising (used by Dispose to reset to a safe default).</summary>
    public void Reset(T value)
    {
        lock (_gate) Volatile.Write(ref _value, value);
    }
}
