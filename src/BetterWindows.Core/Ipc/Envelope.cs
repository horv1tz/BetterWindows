using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

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
/// Results: {v,kind:"result",id,ok,payload|error}.
/// </summary>
public sealed class Envelope
{
    [JsonPropertyName("v")] public int V { get; init; } = Protocol.ProtocolInfo.Version;
    [JsonPropertyName("kind")] public string Kind { get; init; } = "";
    [JsonPropertyName("id")] public string? Id { get; init; }
    [JsonPropertyName("type")] public string? Type { get; init; }
    [JsonPropertyName("payload")] public JsonNode? Payload { get; init; }
    [JsonPropertyName("ok")] public bool? Ok { get; init; }
    [JsonPropertyName("error")] public ErrorInfo? Error { get; init; }
}
