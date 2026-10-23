using System.Text.Json.Nodes;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Config;

/// <summary>
/// The persisted form of config.json: the typed, validated <see cref="Settings"/> overlaid on the RAW root that was loaded from disk, so keys this build does not know
/// (written by a newer version or by the user) survive every save (docs/IPC.md section 12). Known keys always take the typed value (a key whose typed value is null/absent is
/// removed from the file); unknown keys are kept as they were, up to <see cref="MaxUnknownChars"/> in total.
/// </summary>
internal static class SettingsDocument
{
    /// <summary>Unknown content preserved across saves (docs/IPC.md section 12: 256 KB).</summary>
    public const int MaxUnknownChars = 256 * 1024;

    /// <summary>Deep clone of <paramref name="raw"/> with every schema-known member replaced by the typed value.</summary>
    public static JsonObject Compose(JsonObject? raw, Settings typed)
    {
        var result = raw is null ? new JsonObject() : (JsonObject)raw.DeepClone();
        var typedNode = (JsonObject)ProtocolJson.ToNode(typed)!;
        Overlay(result, typedNode, typeof(Settings));
        return result;
    }

    private static void Overlay(JsonObject target, JsonObject typed, Type recordType)
    {
        foreach (var p in SettingsSchema.Properties(recordType))
        {
            typed.TryGetPropertyValue(p.Name, out var value);
            var key = target.Select(kv => kv.Key).FirstOrDefault(k => string.Equals(k, p.Name, StringComparison.OrdinalIgnoreCase)) ?? p.Name;
            if (value is null) { target.Remove(key); continue; }
            if (SettingsSchema.IsRecord(p.Type) && value is JsonObject child)
            {
                if (target[key] is JsonObject existing) Overlay(existing, child, p.Type);
                else
                {
                    var created = new JsonObject();
                    Overlay(created, child, p.Type);
                    target[key] = created;
                }
            }
            else target[key] = value.DeepClone();
        }
    }

    /// <summary>Total serialised length of the members of <paramref name="raw"/> that the schema does not know (at any record level).</summary>
    public static int UnknownSize(JsonObject raw, Type recordType)
    {
        var total = 0;
        foreach (var kv in raw)
        {
            var prop = SettingsSchema.Properties(recordType).FirstOrDefault(p => string.Equals(p.Name, kv.Key, StringComparison.OrdinalIgnoreCase));
            if (prop is null) total += kv.Key.Length + (kv.Value?.ToJsonString().Length ?? 4);
            else if (kv.Value is JsonObject child && SettingsSchema.IsRecord(prop.Type)) total += UnknownSize(child, prop.Type);
        }
        return total;
    }

    /// <summary>Removes every member the schema does not know (used when the unknown content exceeds its budget).</summary>
    public static void RemoveUnknown(JsonObject raw, Type recordType)
    {
        foreach (var key in raw.Select(kv => kv.Key).ToArray())
        {
            var prop = SettingsSchema.Properties(recordType).FirstOrDefault(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase));
            if (prop is null) raw.Remove(key);
            else if (raw[key] is JsonObject child && SettingsSchema.IsRecord(prop.Type)) RemoveUnknown(child, prop.Type);
        }
    }
}
