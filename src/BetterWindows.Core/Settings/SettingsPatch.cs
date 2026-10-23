using System.Text;
using System.Text.Json.Nodes;
using BetterWindows.Core.Ipc;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Config;

/// <summary>
/// RFC 7386 JSON Merge Patch for <c>settings.patch</c> (docs/IPC.md section 12): the patch is checked against the schema first (unknown key -> badRequest naming the full path,
/// <c>version</c> is never patchable, wrong-typed values and unknown enum strings -> badRequest naming the path), then merged into the serialised current settings: objects merge
/// recursively (map fields too), arrays and primitives replace, <c>null</c> deletes the key (the tolerant binder then restores the default / null / removes the map entry).
/// </summary>
internal static class SettingsPatch
{
    private const int MaxProblemsReported = 8;

    /// <summary>Returns the patch as an object or throws <see cref="ProtocolException"/> (badRequest) naming every offending path (first few).</summary>
    public static JsonObject Validate(JsonNode? patch)
    {
        if (patch is not JsonObject root) throw ProtocolException.BadRequest("settings.patch: 'patch' must be a JSON object");
        if (Encoding.UTF8.GetByteCount(root.ToJsonString()) > Limits.MaxPatchBytes) throw ProtocolException.BadRequest($"settings.patch: patch exceeds {Limits.MaxPatchBytes / 1024} KB");

        var unknown = new List<string>();
        var invalid = new List<string>();
        foreach (var kv in root)
        {
            if (kv.Key == "version") { invalid.Add("'version' is not patchable"); continue; }
            var prop = SettingsSchema.Find(typeof(Protocol.Settings), kv.Key);
            if (prop is null) { unknown.Add(kv.Key); continue; }
            Check(prop.Type, kv.Value, kv.Key, unknown, invalid);
        }
        if (unknown.Count > 0) throw ProtocolException.BadRequest("unknown settings key(s): " + string.Join(", ", unknown.Take(MaxProblemsReported)));
        if (invalid.Count > 0) throw ProtocolException.BadRequest(string.Join("; ", invalid.Take(MaxProblemsReported)));
        return root;
    }

    private static void Check(Type type, JsonNode? node, string path, List<string> unknown, List<string> invalid)
    {
        if (node is null) return; // null = reset to default / delete
        if (SettingsSchema.IsAny(type)) return; // free-form JSON (widget config)

        if (SettingsSchema.IsRecord(type))
        {
            if (node is not JsonObject obj) { invalid.Add($"'{path}' must be an object"); return; }
            foreach (var kv in obj)
            {
                var prop = SettingsSchema.Find(type, kv.Key);
                var childPath = path + "." + kv.Key;
                if (prop is null) { unknown.Add(childPath); continue; }
                Check(prop.Type, kv.Value, childPath, unknown, invalid);
            }
            return;
        }

        if (SettingsSchema.TryGetListElement(type, out var elementType))
        {
            if (node is not JsonArray arr) { invalid.Add($"'{path}' must be an array"); return; }
            for (var i = 0; i < arr.Count; i++) Check(elementType, arr[i], $"{path}[{i}]", unknown, invalid);
            return;
        }

        if (SettingsSchema.TryGetMapValue(type, out var valueType))
        {
            if (node is not JsonObject map) { invalid.Add($"'{path}' must be an object"); return; }
            foreach (var kv in map) Check(valueType, kv.Value, path + "." + kv.Key, unknown, invalid);
            return;
        }

        if (!SettingsBinder.TryBind(type, node, out _)) invalid.Add($"invalid value for '{path}'");
    }

    /// <summary>RFC 7386 MergePatch(target, patch). Neither argument is mutated; the result shares no nodes with them.</summary>
    public static JsonObject Merge(JsonObject target, JsonObject patch)
    {
        var result = (JsonObject)target.DeepClone();
        foreach (var kv in patch)
        {
            if (kv.Value is null) { result.Remove(kv.Key); continue; }
            if (kv.Value is JsonObject po)
            {
                var existing = result[kv.Key] as JsonObject ?? new JsonObject();
                result[kv.Key] = Merge(existing, po);
            }
            else result[kv.Key] = kv.Value.DeepClone();
        }
        return result;
    }
}
