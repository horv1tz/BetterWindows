using BetterWindows.Core.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Core.Ipc;

/// <summary>
/// Live surfaces, keyed by CHANNEL INSTANCE identity (a re-created surface with the same SurfaceId gets a new channel; the old window's late Closed handler
/// can therefore never unregister the new one). A channel is "ready" between the reply to its surface.ready and its next surface.ready/unregister; events are only
/// posted to ready channels. Each registered channel owns a session CancellationToken that is cancelled when the channel closes or sends surface.ready again.
/// </summary>
public sealed class SurfaceRegistry
{
    private sealed class Entry
    {
        public required ISurfaceChannel Channel;
        public volatile bool Ready;
        public CancellationTokenSource Session = new();
    }

    private readonly object _gate = new();
    private readonly Dictionary<SurfaceId, Entry> _byId = new();
    private readonly ILogger _log;

    public SurfaceRegistry(ILogger<SurfaceRegistry>? log = null) => _log = (ILogger?)log ?? NullLogger.Instance;

    /// <summary>Raised AFTER a surface completed surface.ready (the reply was posted and the channel is marked ready). Subscribers run in try/catch each.</summary>
    public event Action<SurfaceId>? SurfaceReady;
    /// <summary>Raised when a channel was unregistered (id = the channel's id). Runs on the caller's (UI) thread: handlers must be quick.</summary>
    public event Action<SurfaceId>? SurfaceClosed;
    /// <summary>Same moment as SurfaceClosed but with the channel object (per-channel state cleanup).</summary>
    public event Action<ISurfaceChannel>? ChannelClosed;

    public void Register(ISurfaceChannel channel)
    {
        Entry? replaced;
        lock (_gate)
        {
            _byId.TryGetValue(channel.Id, out replaced);
            _byId[channel.Id] = new Entry { Channel = channel };
        }
        if (replaced is not null) Retire(replaced);
    }

    /// <summary>Removes the channel only if it is still the registered one for its id. Safe to call from a late Closed handler of a replaced window.</summary>
    public bool Unregister(ISurfaceChannel channel)
    {
        Entry? removed = null;
        lock (_gate)
        {
            if (_byId.TryGetValue(channel.Id, out var e) && ReferenceEquals(e.Channel, channel)) { _byId.Remove(channel.Id); removed = e; }
        }
        if (removed is null) return false;
        Retire(removed);
        SafeInvoke(SurfaceClosed, channel.Id);
        return true;
    }

    private void Retire(Entry e)
    {
        e.Ready = false;
        try { e.Session.Cancel(); } catch { /* callbacks must not break retirement */ }
        e.Session.Dispose();
        var handler = ChannelClosed;
        if (handler is null) return;
        foreach (var d in handler.GetInvocationList())
        {
            try { ((Action<ISurfaceChannel>)d)(e.Channel); } catch (Exception ex) { _log.LogError(ex, "ChannelClosed subscriber failed"); }
        }
    }

    public ISurfaceChannel? Get(SurfaceId id) { lock (_gate) return _byId.GetValueOrDefault(id)?.Channel; }
    public IReadOnlyList<ISurfaceChannel> All() { lock (_gate) return _byId.Values.Select(e => e.Channel).ToArray(); }
    public IReadOnlyList<ISurfaceChannel> OfKind(SurfaceKind kind) { lock (_gate) return _byId.Values.Where(e => e.Channel.Id.Kind == kind).Select(e => e.Channel).ToArray(); }
    public IReadOnlyList<ISurfaceChannel> ReadyChannels() { lock (_gate) return _byId.Values.Where(e => e.Ready).Select(e => e.Channel).ToArray(); }

    public bool IsReady(ISurfaceChannel channel) { lock (_gate) return _byId.TryGetValue(channel.Id, out var e) && ReferenceEquals(e.Channel, channel) && e.Ready; }
    public bool IsReady(SurfaceId id) { lock (_gate) return _byId.TryGetValue(id, out var e) && e.Ready; }

    /// <summary>The session token of a registered channel (cancelled on close or when the page restarts its handshake). CancellationToken.None if unknown.</summary>
    public CancellationToken SessionToken(ISurfaceChannel channel)
    {
        lock (_gate)
        {
            if (!_byId.TryGetValue(channel.Id, out var e) || !ReferenceEquals(e.Channel, channel)) return CancellationToken.None;
            try { return e.Session.Token; } catch (ObjectDisposedException) { return new CancellationToken(true); }
        }
    }

    /// <summary>A surface.ready ARRIVED: not ready any more, in-flight work of the previous page session is cancelled.</summary>
    internal void BeginHandshake(ISurfaceChannel channel)
    {
        Entry? e;
        CancellationTokenSource? old = null;
        lock (_gate)
        {
            if (!_byId.TryGetValue(channel.Id, out e) || !ReferenceEquals(e.Channel, channel)) return;
            e.Ready = false;
            old = e.Session;
            e.Session = new CancellationTokenSource();
        }
        try { old.Cancel(); } catch { }
        old.Dispose();
    }

    /// <summary>The surface.ready reply was posted: events may flow, then subscribers push the initial state.</summary>
    internal void CompleteHandshake(ISurfaceChannel channel)
    {
        lock (_gate)
        {
            if (!_byId.TryGetValue(channel.Id, out var e) || !ReferenceEquals(e.Channel, channel)) return;
            e.Ready = true;
        }
        SafeInvoke(SurfaceReady, channel.Id);
    }

    /// <summary>Test/Host helper: mark a channel ready without a handshake.</summary>
    public void MarkReadyForTests(ISurfaceChannel channel) => CompleteHandshake(channel);

    private void SafeInvoke(Action<SurfaceId>? handler, SurfaceId id)
    {
        if (handler is null) return;
        foreach (var d in handler.GetInvocationList())
        {
            try { ((Action<SurfaceId>)d)(id); }
            catch (Exception ex) { _log.LogError(ex, "Surface event subscriber failed for {Surface}", id); }
        }
    }
}
