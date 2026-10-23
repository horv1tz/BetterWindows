namespace BetterWindows.Host.Bridge;

/// <summary>One queued outgoing message.</summary>
public sealed record OutboundMessage(OutboundKind Kind, string? Type, string Json);

/// <summary>
/// Bounded FIFO between Core threads and the UI thread (docs/IPC.md section 11): state events coalesce to the LATEST per type (the older snapshot is removed and the new one
/// goes to the back, keeping the order of what the page would have seen last), transient events keep the newest <see cref="MaxTransientEvents"/>, results are never dropped
/// unless the hard cap <see cref="MaxTotal"/> is reached (then the oldest message of any kind goes). Thread-safe.
/// </summary>
public sealed class OutboundQueue
{
    public const int MaxTransientEvents = 64;
    public const int MaxTotal = 512;

    private readonly object _gate = new();
    private readonly LinkedList<OutboundMessage> _items = new();
    private int _transient;

    /// <summary>Messages dropped because of the bounds (diagnostics).</summary>
    public long Dropped { get; private set; }
    /// <summary>State events replaced by a newer snapshot (diagnostics).</summary>
    public long Coalesced { get; private set; }

    public int Count { get { lock (_gate) return _items.Count; } }

    public void Enqueue(OutboundMessage m)
    {
        lock (_gate)
        {
            switch (m.Kind)
            {
                case OutboundKind.StateEvent when m.Type is not null:
                    for (var node = _items.First; node is not null; node = node.Next)
                    {
                        if (node.Value.Kind == OutboundKind.StateEvent && node.Value.Type == m.Type)
                        {
                            _items.Remove(node);
                            Coalesced++;
                            break;
                        }
                    }
                    break;
                case OutboundKind.TransientEvent:
                    _transient++;
                    break;
            }
            _items.AddLast(m);
            while (_transient > MaxTransientEvents && DropOldest(OutboundKind.TransientEvent)) { }
            while (_items.Count > MaxTotal) DropFirst();
        }
    }

    public bool TryDequeue(out OutboundMessage message)
    {
        lock (_gate)
        {
            var first = _items.First;
            if (first is null) { message = null!; return false; }
            _items.RemoveFirst();
            if (first.Value.Kind == OutboundKind.TransientEvent) _transient--;
            message = first.Value;
            return true;
        }
    }

    public void Clear()
    {
        lock (_gate) { _items.Clear(); _transient = 0; }
    }

    private bool DropOldest(OutboundKind kind)
    {
        for (var node = _items.First; node is not null; node = node.Next)
        {
            if (node.Value.Kind != kind) continue;
            _items.Remove(node);
            if (kind == OutboundKind.TransientEvent) _transient--;
            Dropped++;
            return true;
        }
        return false;
    }

    private void DropFirst()
    {
        var first = _items.First;
        if (first is null) return;
        if (first.Value.Kind == OutboundKind.TransientEvent) _transient--;
        _items.RemoveFirst();
        Dropped++;
    }
}
