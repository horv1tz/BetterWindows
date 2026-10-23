using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Config;

/// <summary>One wire property of a protocol record: its JSON name and the CLR property.</summary>
internal sealed record SchemaProperty(string Name, PropertyInfo Info)
{
    public Type Type => Info.PropertyType;
}

/// <summary>
/// Reflection view of the generated protocol records (the schema of config.json): which types are objects, lists, maps or free-form JSON. The generated records ARE the schema
/// (protocol/settings.json is the single source), so patch-key validation, tolerant binding and unknown-key preservation need no second schema.
/// </summary>
internal static class SettingsSchema
{
    private static readonly ConcurrentDictionary<Type, SchemaProperty[]> PropertyCache = new();

    /// <summary>Generated protocol record: a class in the Protocol namespace with a parameterless constructor and [JsonPropertyName] properties.</summary>
    public static bool IsRecord(Type t) =>
        t.IsClass && t != typeof(string) && t.Namespace == typeof(Settings).Namespace && t.GetConstructor(Type.EmptyTypes) is not null && Properties(t).Length > 0;

    public static bool IsAny(Type t) => typeof(JsonNode).IsAssignableFrom(t);

    public static SchemaProperty[] Properties(Type t) => PropertyCache.GetOrAdd(t, static type =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is not null)
            .Select(p => (p, attr: p.GetCustomAttribute<JsonPropertyNameAttribute>()))
            .Where(x => x.attr is not null)
            .Select(x => new SchemaProperty(x.attr!.Name, x.p))
            .ToArray());

    public static SchemaProperty? Find(Type t, string wireName) => Properties(t).FirstOrDefault(p => p.Name == wireName);

    /// <summary><c>IReadOnlyList&lt;T&gt;</c> -> T.</summary>
    public static bool TryGetListElement(Type t, out Type element)
    {
        element = null!;
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)) { element = t.GetGenericArguments()[0]; return true; }
        return false;
    }

    /// <summary><c>IReadOnlyDictionary&lt;string, T&gt;</c> -> T.</summary>
    public static bool TryGetMapValue(Type t, out Type value)
    {
        value = null!;
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>) && t.GetGenericArguments()[0] == typeof(string)) { value = t.GetGenericArguments()[1]; return true; }
        return false;
    }

    /// <summary>Case-sensitive property lookup first, then case-insensitive (config.json may be hand-edited; the protocol itself is camelCase).</summary>
    public static JsonNode? GetChild(JsonObject o, string name)
    {
        if (o.TryGetPropertyValue(name, out var exact)) return exact;
        foreach (var kv in o) if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)) return kv.Value;
        return null;
    }
}

/// <summary>
/// Tolerant binding of a RAW <see cref="JsonNode"/> to a protocol record (docs/IPC.md section 12): a missing key or an explicit null becomes the member's DEFAULT; a value of the
/// wrong type or an unknown enum string resets only THAT member; invalid list elements are dropped. Never throws for bad data.
/// </summary>
internal static class SettingsBinder
{
    /// <summary>Binds <paramref name="node"/> to T; a node that is not an object yields a default T.</summary>
    public static T Bind<T>(JsonNode? node, List<string>? issues = null) where T : class, new()
    {
        TryBind(typeof(T), node, "", issues, out var v);
        return (T?)v ?? new T();
    }

    public static bool TryBind(Type type, JsonNode? node, out object? value) => TryBind(type, node, "", null, out value);

    private static bool TryBind(Type type, JsonNode? node, string path, List<string>? issues, out object? value)
    {
        value = null;
        if (node is null) return false;

        if (SettingsSchema.IsAny(type)) { value = node.DeepClone(); return true; }

        if (SettingsSchema.IsRecord(type))
        {
            if (node is not JsonObject obj) { issues?.Add($"{path}: expected an object"); return false; }
            var inst = Activator.CreateInstance(type)!;
            foreach (var p in SettingsSchema.Properties(type))
            {
                var child = SettingsSchema.GetChild(obj, p.Name);
                var childPath = path.Length == 0 ? p.Name : path + "." + p.Name;
                if (child is null) continue; // missing or null -> the record's default
                if (TryBind(p.Type, child, childPath, issues, out var v)) p.Info.SetValue(inst, v);
            }
            value = inst;
            return true;
        }

        if (SettingsSchema.TryGetListElement(type, out var elementType))
        {
            if (node is not JsonArray arr) { issues?.Add($"{path}: expected an array"); return false; }
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))!;
            for (var i = 0; i < arr.Count; i++)
            {
                if (arr[i] is null) continue;
                if (TryBind(elementType, arr[i], $"{path}[{i}]", issues, out var e) && e is not null) list.Add(e);
                else issues?.Add($"{path}[{i}]: element dropped");
            }
            value = list;
            return true;
        }

        if (SettingsSchema.TryGetMapValue(type, out var valueType))
        {
            if (node is not JsonObject map) { issues?.Add($"{path}: expected an object"); return false; }
            var dict = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), valueType))!;
            foreach (var kv in map)
            {
                if (kv.Value is null) continue;
                if (TryBind(valueType, kv.Value, $"{path}.{kv.Key}", issues, out var e) && e is not null) dict[kv.Key] = e;
                else issues?.Add($"{path}.{kv.Key}: entry dropped");
            }
            value = dict;
            return true;
        }

        try
        {
            value = node.Deserialize(type, ProtocolJson.Options);
            return value is not null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException or FormatException or OverflowException)
        {
            issues?.Add($"{path}: invalid value");
            value = null;
            return false;
        }
    }
}
