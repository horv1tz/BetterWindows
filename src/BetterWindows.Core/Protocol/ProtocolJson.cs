using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace BetterWindows.Core.Protocol;

/// <summary>
/// The one JSON configuration used for the wire protocol AND for config.json.
/// Hardening (docs/IPC.md section 1): non-finite doubles serialise as 0; int fields accept any JSON number (rounded half away from zero);
/// an explicit JSON null never overwrites a non-nullable member (it keeps the default); missing [JsonRequired] fields of UI input types -> JsonException.
/// </summary>
public static class ProtocolJson
{
    public static readonly JsonSerializerOptions Options = Create(indented: false);
    public static readonly JsonSerializerOptions IndentedOptions = Create(indented: true);

    private static JsonSerializerOptions Create(bool indented)
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = indented,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { IgnoreExplicitNullForNonNullable } },
        };
        o.Converters.Add(new FiniteDoubleConverter());
        o.Converters.Add(new LenientIntConverter());
        return o;
    }

    private static void IgnoreExplicitNullForNonNullable(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object) return;
        var ctx = new NullabilityInfoContext();
        foreach (var p in typeInfo.Properties)
        {
            if (p.Set is null || p.AttributeProvider is not PropertyInfo pi) continue;
            if (ctx.Create(pi).WriteState != NullabilityState.NotNull) continue;
            var original = p.Set;
            p.Set = (obj, value) => { if (value is not null) original(obj, value); };
        }
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static JsonNode? ToNode<T>(T value) => JsonSerializer.SerializeToNode(value, Options);
    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
    public static T? Deserialize<T>(JsonNode? node) => node is null ? default : node.Deserialize<T>(Options);
}

/// <summary>NaN/Infinity from COM/WinRT must never break serialisation: they become 0.</summary>
public sealed class FiniteDoubleConverter : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetDouble();
    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) => writer.WriteNumberValue(double.IsFinite(value) ? value : 0);
}

/// <summary>JS numbers are doubles: accept 1.5, 2.0, 1e2 and numeric strings for int fields (rounded half away from zero, clamped to the int range).</summary>
public sealed class LenientIntConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        double d;
        if (reader.TokenType == JsonTokenType.Number) d = reader.GetDouble();
        else if (reader.TokenType == JsonTokenType.String && double.TryParse(reader.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)) d = parsed;
        else throw new JsonException("expected a number");
        if (double.IsNaN(d)) throw new JsonException("NaN is not a number");
        d = Math.Round(d, MidpointRounding.AwayFromZero);
        return d >= int.MaxValue ? int.MaxValue : d <= int.MinValue ? int.MinValue : (int)d;
    }
    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
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
    public static ProtocolException Forbidden(string message) => new("forbidden", message);
    public static ProtocolException Busy(string message) => new("busy", message);
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
