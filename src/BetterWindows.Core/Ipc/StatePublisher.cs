using System.Text.Json.Nodes;
using BetterWindows.Core.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Core.Ipc;

/// <summary>
/// THE way to publish state events (events flagged state:true in protocol/*.json). One producer declares a topic per event type; consumers never hand-write
/// "push on surface.ready" or coalescing. Guarantees: (1) the LAST posted snapshot is the NEWEST (snapshots are computed and posted under a per-topic lock),
/// (2) identical consecutive snapshots are not re-posted (compared as serialised JSON: never compare protocol records with ==), (3) every ready surface whose kind
/// is in the event's "initial" list gets the current snapshot right after its surface.ready, (4) coalescing is trailing-edge on the injected TimeProvider.
/// </summary>
public interface IStatePublisher
{
    /// <summary>
    /// Declare the single producer of state event <paramref name="type"/>. Call in IShellController.Start(). <paramref name="snapshot"/> returns the CURRENT full state for
    /// that surface, or null to send nothing to it (other monitor, unsupported); it must be cheap, pure and must not call the hub/publisher. coalesce == null -> 30 ms,
    /// TimeSpan.Zero -> publish inline (deterministic tests).
    /// </summary>
    IStateTopic Declare<T>(string type, Func<SurfaceId, T?> snapshot, TimeSpan? coalesce = null) where T : class;
}

public interface IStateTopic
{
    /// <summary>The state changed. Thread-safe, never throws, cheap (call it from port-event handlers; it never blocks on I/O).</summary>
    void Invalidate();
    /// <summary>Re-send to one surface only (inline).</summary>
    void Invalidate(SurfaceId only);
}

public sealed class StatePublisher : IStatePublisher, IDisposable
{
    private static readonly TimeSpan DefaultCoalesce = TimeSpan.FromMilliseconds(30);
    private readonly SurfaceRegistry _registry;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly List<Topic> _topics = new();
    private readonly object _gate = new();

    public StatePublisher(SurfaceRegistry registry, TimeProvider time, ILogger<StatePublisher>? log = null)
    {
        _registry = registry;
        _time = time;
        _log = (ILogger?)log ?? NullLogger.Instance;
        _registry.SurfaceReady += OnReady;
        _registry.ChannelClosed += OnClosed;
    }

    public IStateTopic Declare<T>(string type, Func<SurfaceId, T?> snapshot, TimeSpan? coalesce = null) where T : class
    {
        if (!InitialState.IsState(type)) throw new ArgumentException($"'{type}' is not a state event in protocol/*.json", nameof(type));
        var topic = new Topic(this, type, id => snapshot(id), coalesce ?? DefaultCoalesce);
        lock (_gate)
        {
            if (_topics.Any(t => t.Type == type)) throw new InvalidOperationException($"state event '{type}' already has a producer");
            _topics.Add(topic);
        }
        return topic;
    }

    private void OnReady(SurfaceId id)
    {
        Topic[] topics;
        lock (_gate) topics = _topics.ToArray();
        foreach (var t in topics)
        {
            if (!t.Kinds.Contains(id.Kind)) continue;
            try { t.Flush(id); } catch (Exception ex) { _log.LogError(ex, "Initial push of {Type} to {Surface} failed", t.Type, id); }
        }
    }

    private void OnClosed(ISurfaceChannel channel)
    {
        Topic[] topics;
        lock (_gate) topics = _topics.ToArray();
        foreach (var t in topics) t.Forget(channel);
    }

    public void Dispose()
    {
        _registry.SurfaceReady -= OnReady;
        _registry.ChannelClosed -= OnClosed;
        lock (_gate) foreach (var t in _topics) t.Dispose();
    }

    private sealed class Topic : IStateTopic, IDisposable
    {
        private readonly StatePublisher _owner;
        private readonly Func<SurfaceId, object?> _snapshot;
        private readonly TimeSpan _coalesce;
        private readonly object _lock = new();
        private readonly Dictionary<ISurfaceChannel, string> _last = new(ReferenceEqualityComparer.Instance as IEqualityComparer<ISurfaceChannel>);
        private ITimer? _timer;
        private bool _armed, _disposed;

        public Topic(StatePublisher owner, string type, Func<SurfaceId, object?> snapshot, TimeSpan coalesce)
        {
            _owner = owner; Type = type; _snapshot = snapshot; _coalesce = coalesce;
            Kinds = new HashSet<SurfaceKind>(InitialState.KindsOf(type));
        }

        public string Type { get; }
        public HashSet<SurfaceKind> Kinds { get; }

        public void Invalidate()
        {
            try
            {
                if (_coalesce <= TimeSpan.Zero) { Flush(null); return; }
                lock (_lock)
                {
                    if (_disposed || _armed) return;
                    _armed = true;
                    _timer ??= _owner._time.CreateTimer(_ => { lock (_lock) _armed = false; try { Flush(null); } catch (Exception ex) { _owner._log.LogError(ex, "State flush of {Type} failed", Type); } }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                    _timer.Change(_coalesce, Timeout.InfiniteTimeSpan);
                }
            }
            catch (Exception ex) { _owner._log.LogError(ex, "Invalidate({Type}) failed", Type); }
        }

        public void Invalidate(SurfaceId only)
        {
            try { Flush(only); } catch (Exception ex) { _owner._log.LogError(ex, "Invalidate({Type}, {Surface}) failed", Type, only); }
        }

        public void Forget(ISurfaceChannel channel) { lock (_lock) _last.Remove(channel); }

        /// <summary>Compute + post under the topic lock so the newest snapshot is always the last one posted.</summary>
        public void Flush(SurfaceId? only)
        {
            lock (_lock)
            {
                if (_disposed) return;
                foreach (var ch in _owner._registry.ReadyChannels())
                {
                    if (!Kinds.Contains(ch.Id.Kind)) continue;
                    if (only is { } o && o != ch.Id) continue;
                    object? snap;
                    try { snap = _snapshot(ch.Id); }
                    catch (Exception ex) { _owner._log.LogError(ex, "Snapshot of {Type} for {Surface} failed", Type, ch.Id); continue; }
                    if (snap is null) continue;
                    string json;
                    JsonNode? node;
                    try { node = ProtocolJson.ToNode(snap); json = node?.ToJsonString(ProtocolJson.Options) ?? "null"; }
                    catch (Exception ex) { _owner._log.LogError(ex, "Could not serialise {Type}", Type); continue; }
                    if (_last.TryGetValue(ch, out var prev) && prev == json) continue;
                    try
                    {
                        ch.Post(Envelope.HostEvent(Type, node).ToJson());
                        _last[ch] = json;
                    }
                    catch (Exception ex) { _owner._log.LogDebug(ex, "Post of {Type} to {Surface} failed", Type, ch.Id); }
                }
            }
        }

        public void Dispose() { lock (_lock) { _disposed = true; _timer?.Dispose(); } }
    }
}
