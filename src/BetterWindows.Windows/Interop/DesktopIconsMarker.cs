using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetterWindows.Native.Interop;

/// <summary>
/// The record that BetterWindows hid Explorer's desktop icon list. Written BEFORE the list is hidden and deleted after it is shown again, so
/// <see cref="DesktopIconsRestorer.RestoreNow"/> can undo a hide left behind by a crash. Pure data; cross-platform and unit-tested.
/// </summary>
/// <param name="ProcessId">Process that hid the icons (diagnostics only).</param>
/// <param name="CreatedUtc">When the icons were hidden (diagnostics only).</param>
internal sealed record DesktopIconsMarker(
    [property: JsonPropertyName("processId")] int ProcessId,
    [property: JsonPropertyName("createdUtc")] DateTime CreatedUtc)
{
    /// <summary>Schema version; only version 1 is understood.</summary>
    [JsonPropertyName("version")]
    public int Version { get; init; } = 1;

    /// <summary>File name of the marker inside the BetterWindows state folder.</summary>
    public const string FileName = "desktop-icons-hidden.json";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>JSON text of the marker.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Parses a marker. Null for missing, malformed or unknown-version text (the caller then treats the state as unknown).</summary>
    public static DesktopIconsMarker? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var marker = JsonSerializer.Deserialize<DesktopIconsMarker>(json, Options);
            return marker is { Version: 1 } ? marker : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
