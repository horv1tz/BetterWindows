using System.Text.Json;
using System.Text.Json.Nodes;
using BetterWindows.Core.Ipc;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Tests.Support;

/// <summary>
/// A fake WebView: records every envelope the host posts to it AND validates it against the protocol catalog: a controller that publishes the wrong payload under an
/// event type, a misspelt property, or a ui->host event type is recorded in <see cref="Violations"/>; CoreHarness.Dispose fails the test if there are any.
/// </summary>
public sealed class FakeSurface : ISurfaceChannel
{
    private readonly List<JsonObject> _posted = new();
    private readonly List<string> _violations = new();
    private readonly object _gate = new();
    public FakeSurface(SurfaceId id) => Id = id;
    public FakeSurface(SurfaceKind kind, string? monitorId = null) : this(new SurfaceId(kind, monitorId)) { }

    public SurfaceId Id { get; }
    /// <summary>When true, Post throws (simulates a dead WebView).</summary>
    public bool Broken { get; set; }

    public void Post(string json)
    {
        if (Broken) throw new ObjectDisposedException("webview");
        var node = JsonNode.Parse(json)!.AsObject();
        lock (_gate)
        {
            _posted.Add(node);
            Validate(node);
        }
    }

    private void Validate(JsonObject env)
    {
        var kind = (string?)env["kind"];
        if ((int?)env["v"] != ProtocolInfo.Version) _violations.Add($"bad protocol version in {env}");
        if (kind == "event")
        {
            var type = (string?)env["type"] ?? "";
            if (!MessageCatalog.ByType.TryGetValue(type, out var def)) { _violations.Add($"event '{type}' is not in the catalog"); return; }
            if (def.Kind != MessageKind.HostEvent) { _violations.Add($"'{type}' is not a host->ui event"); return; }
            if (def.State && !def.Initial!.Contains(Id.Kind)) _violations.Add($"state event '{type}' sent to {Id.Kind}, which is not in its initial list");
            var payload = env["payload"];
            if (def.Payload is null) { if (payload is not null) _violations.Add($"event '{type}' must have no payload"); return; }
            if (payload is null) { _violations.Add($"event '{type}' needs a {def.Payload.Name} payload"); return; }
            try
            {
                var obj = payload.Deserialize(def.Payload, ProtocolJson.Options);
                var round = JsonSerializer.SerializeToNode(obj, def.Payload, ProtocolJson.Options);
                if (!JsonNode.DeepEquals(round, payload)) _violations.Add($"event '{type}' payload does not match {def.Payload.Name} (misspelt/extra properties?): {payload}");
            }
            catch (Exception ex) { _violations.Add($"event '{type}' payload is not a {def.Payload.Name}: {ex.Message}"); }
        }
        else if (kind == "result")
        {
            if ((string?)env["id"] is null) _violations.Add("result without id");
        }
        else _violations.Add($"host posted an envelope of kind '{kind}'");
    }

    public IReadOnlyList<JsonObject> All { get { lock (_gate) return _posted.ToArray(); } }
    public IReadOnlyList<string> Violations { get { lock (_gate) return _violations.ToArray(); } }

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
