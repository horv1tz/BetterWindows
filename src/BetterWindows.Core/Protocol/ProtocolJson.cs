using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace BetterWindows.Core.Protocol;

/// <summary>The one JSON configuration used for the wire protocol AND for config.json.</summary>
public static class ProtocolJson
{
    public static readonly JsonSerializerOptions Options = Create(indented: false);
    public static readonly JsonSerializerOptions IndentedOptions = Create(indented: true);

    private static JsonSerializerOptions Create(bool indented) => new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = indented,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static JsonNode? ToNode<T>(T value) => JsonSerializer.SerializeToNode(value, Options);
    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
    public static T? Deserialize<T>(JsonNode? node) => node is null ? default : node.Deserialize<T>(Options);
}

/// <summary>Error thrown by a call handler; becomes { ok:false, error:{ code, message } } on the wire.</summary>
public sealed class ProtocolException : Exception
{
    public string Code { get; }
    public ProtocolException(string code, string message) : base(message) => Code = code;

    public static ProtocolException BadRequest(string message) => new("badRequest", message);
    public static ProtocolException NotFound(string message) => new("notFound", message);
    public static ProtocolException Unsupported(string message) => new("unsupported", message);
    public static ProtocolException Failed(string message) => new("failed", message);
}

public static class Hwnd
{
    /// <summary>HWND/window handle -> wire string (decimal).</summary>
    public static string ToWire(long handle) => handle.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public static bool TryParse(string? wire, out long handle) =>
        long.TryParse(wire, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out handle);
    public static long Parse(string? wire) =>
        TryParse(wire, out var h) ? h : throw ProtocolException.BadRequest($"invalid window handle '{wire}'");
}
