using System.Text.Json;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Ipc;

/// <summary>Host -> UI event publisher. Thread-safe; payloads are serialised once per publish.</summary>
public interface IEventHub
{
    /// <summary>Send to every connected surface.</summary>
    void Publish<T>(string type, T payload);
    void Publish(string type);
    /// <summary>Send to one surface (e.g. initial state after surface.ready).</summary>
    void PublishTo<T>(SurfaceId target, string type, T payload);
    void PublishTo(SurfaceId target, string type);
    /// <summary>Send to all surfaces of a kind (e.g. every menubar).</summary>
    void PublishToKind<T>(SurfaceKind kind, string type, T payload);
    void PublishToKind(SurfaceKind kind, string type);
}

public sealed class EventHub : IEventHub
{
    private readonly SurfaceRegistry _registry;
    public EventHub(SurfaceRegistry registry) => _registry = registry;

    public void Publish<T>(string type, T payload) => Send(_registry.All(), type, ProtocolJson.ToNode(payload));
    public void Publish(string type) => Send(_registry.All(), type, null);
    public void PublishTo<T>(SurfaceId target, string type, T payload) => SendOne(target, type, ProtocolJson.ToNode(payload));
    public void PublishTo(SurfaceId target, string type) => SendOne(target, type, null);
    public void PublishToKind<T>(SurfaceKind kind, string type, T payload) => Send(_registry.OfKind(kind), type, ProtocolJson.ToNode(payload));
    public void PublishToKind(SurfaceKind kind, string type) => Send(_registry.OfKind(kind), type, null);

    private void SendOne(SurfaceId target, string type, System.Text.Json.Nodes.JsonNode? payload)
    {
        var ch = _registry.Get(target);
        if (ch is not null) Send(new[] { ch }, type, payload);
    }

    private static void Send(IReadOnlyList<ISurfaceChannel> targets, string type, System.Text.Json.Nodes.JsonNode? payload)
    {
        if (targets.Count == 0) return;
        var env = new Envelope { Kind = EnvelopeKind.Event, Type = type, Payload = payload };
        var json = JsonSerializer.Serialize(env, ProtocolJson.Options);
        foreach (var t in targets)
        {
            try { t.Post(json); } catch { /* a dying WebView must not break the others */ }
        }
    }
}
