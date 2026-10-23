using System.Text.Json;
using System.Text.Json.Nodes;
using BetterWindows.Core.Ipc;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Tests.Support;

/// <summary>A fake WebView: records every envelope the host posts to it.</summary>
public sealed class FakeSurface : ISurfaceChannel
{
    private readonly List<JsonObject> _posted = new();
    private readonly object _gate = new();
    public FakeSurface(SurfaceId id) => Id = id;
    public FakeSurface(SurfaceKind kind, string? monitorId = null) : this(new SurfaceId(kind, monitorId)) { }

    public SurfaceId Id { get; }

    public void Post(string json)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        lock (_gate) _posted.Add(node);
    }

    public IReadOnlyList<JsonObject> All { get { lock (_gate) return _posted.ToArray(); } }

    /// <summary>Events of a type, oldest first.</summary>
    public IReadOnlyList<JsonObject> Events(string type) =>
        All.Where(e => (string?)e["kind"] == "event" && (string?)e["type"] == type).ToArray();

    /// <summary>Payload of the most recent event of a type, deserialised; default if none.</summary>
    public T? LastEvent<T>(string type)
    {
        var e = Events(type).LastOrDefault();
        return e is null ? default : ProtocolJson.Deserialize<T>(e["payload"]);
    }

    public JsonObject? ResultFor(string callId) =>
        All.LastOrDefault(e => (string?)e["kind"] == "result" && (string?)e["id"] == callId);

    public void Clear() { lock (_gate) _posted.Clear(); }
}
