using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Ipc;

public static class EnvelopeKind
{
    public const string Event = "event";
    public const string Call = "call";
    public const string Result = "result";
}

public sealed class ErrorInfo
{
    [JsonPropertyName("code")] public string Code { get; init; } = "";
    [JsonPropertyName("message")] public string Message { get; init; } = "";
}

/// <summary>
/// Wire envelope. Events: {v,kind:"event",type,payload}. Calls: {v,kind:"call",id,type,payload}.
/// Results: {v,kind:"result",id,ok,payload|error}. V defaults to 0 so a peer that omits it is recognised as a protocol mismatch.
/// </summary>
public sealed class Envelope
{
    [JsonPropertyName("v")] public int V { get; init; }
    [JsonPropertyName("kind")] public string Kind { get; init; } = "";
    [JsonPropertyName("id")] public string? Id { get; init; }
    [JsonPropertyName("type")] public string? Type { get; init; }
    [JsonPropertyName("payload")] public JsonNode? Payload { get; init; }
    [JsonPropertyName("ok")] public bool? Ok { get; init; }
    [JsonPropertyName("error")] public ErrorInfo? Error { get; init; }

    public static Envelope HostEvent(string type, JsonNode? payload) => new() { V = ProtocolInfo.Version, Kind = EnvelopeKind.Event, Type = type, Payload = payload };
    public static Envelope Ok_(string id, JsonNode? payload) => new() { V = ProtocolInfo.Version, Kind = EnvelopeKind.Result, Id = id, Ok = true, Payload = payload };
    public static Envelope Fail(string id, string code, string message) => new()
    {
        V = ProtocolInfo.Version, Kind = EnvelopeKind.Result, Id = id, Ok = false, Error = new ErrorInfo { Code = code, Message = message },
    };

    public string ToJson() => JsonSerializer.Serialize(this, ProtocolJson.Options);
}
