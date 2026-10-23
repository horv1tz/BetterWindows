using System.Text.Json;
using System.Text.Json.Serialization;
var o = new JsonSerializerOptions(JsonSerializerDefaults.Web);
void T(string n, Func<object?> f) { try { Console.WriteLine($"{n}: {f()}"); } catch (Exception e) { Console.WriteLine($"{n}: THROWS {e.GetType().Name}: {e.Message}"); } }
T("missing required", () => JsonSerializer.Deserialize<VolumeParams>("{}", o)!.Volume);
T("misnamed required", () => JsonSerializer.Deserialize<VolumeParams>("{\"volum\":0.5}", o)!.Volume);
T("present required", () => JsonSerializer.Deserialize<VolumeParams>("{\"volume\":0.5}", o)!.Volume);
T("null required string", () => JsonSerializer.Deserialize<IdP>("{\"id\":null}", o)!.Id is null ? "null accepted" : "x");
T("Activator", () => Activator.CreateInstance<VolumeParams>().Volume);
public sealed record VolumeParams { [JsonPropertyName("volume")] public required double Volume { get; init; } = 0.0; }
public sealed record IdP { [JsonPropertyName("id")] public required string Id { get; init; } = ""; }
