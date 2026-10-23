using System.Text.Json.Nodes;
using BetterWindows.Core.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Core.Ipc;

/// <summary>
/// Host -> UI event publisher. Thread-safe and NEVER throws. Events are only posted to READY surfaces (a page that has not completed surface.ready has no listener:
/// it receives its current state through <see cref="IStatePublisher"/> right after the handshake). Use IStatePublisher for state events (ordering, dedupe,
/// coalescing); use this hub for transient events (dock.bounce, overlay.*, search.results, mc.thumbnail, reminders.due ...).
/// </summary>
public interface IEventHub
{
    /// <summary>Send to every ready surface.</summary>
    void Publish<T>(string type, T payload);
    void Publish(string type);
    /// <summary>Send to one surface (if it is ready).</summary>
    void PublishTo<T>(SurfaceId target, string type, T payload);
    void PublishTo(SurfaceId target, string type);
    /// <summary>Send to all ready surfaces of a kind (e.g. every menubar).</summary>
    void PublishToKind<T>(SurfaceKind kind, string type, T payload);
    void PublishToKind(SurfaceKind kind, string type);
}

public sealed class EventHub : IEventHub
{
    private readonly SurfaceRegistry _registry;
    private readonly ILogger _log;

    public EventHub(SurfaceRegistry registry, ILogger<EventHub>? log = null)
    {
        _registry = registry;
        _log = (ILogger?)log ?? NullLogger.Instance;
    }

    public void Publish<T>(string type, T payload) => Send(type, () => ProtocolJson.ToNode(payload), _registry.ReadyChannels());
    public void Publish(string type) => Send(type, () => null, _registry.ReadyChannels());
    public void PublishTo<T>(SurfaceId target, string type, T payload) => Send(type, () => ProtocolJson.ToNode(payload), One(target));
    public void PublishTo(SurfaceId target, string type) => Send(type, () => null, One(target));
    public void PublishToKind<T>(SurfaceKind kind, string type, T payload) => Send(type, () => ProtocolJson.ToNode(payload), _registry.ReadyChannels().Where(c => c.Id.Kind == kind).ToArray());
    public void PublishToKind(SurfaceKind kind, string type) => Send(type, () => null, _registry.ReadyChannels().Where(c => c.Id.Kind == kind).ToArray());

    private IReadOnlyList<ISurfaceChannel> One(SurfaceId id)
    {
        var ch = _registry.Get(id);
        return ch is not null && _registry.IsReady(ch) ? new[] { ch } : Array.Empty<ISurfaceChannel>();
    }

    private void Send(string type, Func<JsonNode?> payload, IReadOnlyList<ISurfaceChannel> targets)
    {
        if (targets.Count == 0) return;
        string json;
        try { json = Envelope.HostEvent(type, payload()).ToJson(); }
        catch (Exception ex)
        {
            _log.LogError(ex, "Could not serialise event {Type}", type);
            return;
        }
        foreach (var t in targets)
        {
            try { t.Post(json); }
            catch (Exception ex) { _log.LogDebug(ex, "Post of {Type} to {Surface} failed (webview gone?)", type, t.Id); }
        }
    }
}
