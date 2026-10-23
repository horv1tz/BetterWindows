namespace BetterWindows.Core.Resources;

/// <summary>
/// Small thread-safe LRU cache bounded by entry count AND total bytes, with an optional lifetime measured on the injected <see cref="TimeProvider"/> (tests drive it).
/// Used for decoded icons (256 entries / 16 MB) and window thumbnails (64 entries / 64 MB / 30 s).
/// </summary>
internal sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private sealed class Node
    {
        public required TKey Key;
        public required TValue Value;
        public long Bytes;
        public long Stamp;
    }

    private readonly object _lock = new();
    private readonly Dictionary<TKey, LinkedListNode<Node>> _map;
    private readonly LinkedList<Node> _order = new(); // first = most recently used
    private readonly int _maxEntries;
    private readonly long _maxBytes;
    private readonly TimeSpan? _lifetime;
    private readonly TimeProvider _time;
    private long _bytes;

    public LruCache(int maxEntries, long maxBytes, TimeProvider time, TimeSpan? lifetime = null, IEqualityComparer<TKey>? comparer = null)
    {
        _maxEntries = maxEntries;
        _maxBytes = maxBytes;
        _time = time;
        _lifetime = lifetime;
        _map = new Dictionary<TKey, LinkedListNode<Node>>(comparer);
    }

    public int Count { get { lock (_lock) return _map.Count; } }
    public long Bytes { get { lock (_lock) return _bytes; } }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var n))
            {
                if (_lifetime is { } life && _time.GetElapsedTime(n.Value.Stamp) > life) { RemoveNode(n); }
                else { _order.Remove(n); _order.AddFirst(n); value = n.Value.Value; return true; }
            }
        }
        value = default!;
        return false;
    }

    /// <summary>Inserts or replaces; an item larger than the whole byte budget is not cached at all.</summary>
    public void Set(TKey key, TValue value, long bytes)
    {
        if (bytes > _maxBytes) return;
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var old)) RemoveNode(old);
            var node = new LinkedListNode<Node>(new Node { Key = key, Value = value, Bytes = bytes, Stamp = _time.GetTimestamp() });
            _order.AddFirst(node);
            _map[key] = node;
            _bytes += bytes;
            while ((_map.Count > _maxEntries || _bytes > _maxBytes) && _order.Last is { } last) RemoveNode(last);
        }
    }

    public bool Remove(TKey key)
    {
        lock (_lock)
        {
            if (!_map.TryGetValue(key, out var n)) return false;
            RemoveNode(n);
            return true;
        }
    }

    public void Clear() { lock (_lock) { _map.Clear(); _order.Clear(); _bytes = 0; } }

    /// <summary>Removes every entry whose key matches the predicate; returns how many.</summary>
    public int RemoveWhere(Func<TKey, bool> predicate)
    {
        lock (_lock)
        {
            var doomed = _map.Where(kv => predicate(kv.Key)).Select(kv => kv.Value).ToArray();
            foreach (var n in doomed) RemoveNode(n);
            return doomed.Length;
        }
    }

    public TKey[] Keys() { lock (_lock) return _order.Select(n => n.Key).ToArray(); }

    private void RemoveNode(LinkedListNode<Node> n)
    {
        _order.Remove(n);
        _map.Remove(n.Value.Key);
        _bytes -= n.Value.Bytes;
    }
}
