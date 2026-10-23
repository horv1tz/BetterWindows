using System.Text.Json;
using System.Text.Json.Nodes;
using BetterWindows.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Core.Search;

/// <summary>
/// Launch counts and last-used times of apps, persisted as JSON (written atomically, default file DataDir\search-usage.json). Used to rank apps and to build the
/// empty-query "recent" list. A corrupt or oversized file is ignored (the store starts empty), never fatal.
/// </summary>
public sealed class UsageStore
{
    public const int MaxEntries = 500;
    private const int MaxFileBytes = 1024 * 1024;
    private static readonly TimeSpan RecentWindow = TimeSpan.FromDays(7);

    private readonly string _path;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly object _gate = new();
    private Dictionary<string, Usage>? _items;

    /// <summary>One app's usage.</summary>
    public sealed record Usage(int Count, DateTimeOffset Last);

    public UsageStore(string path, TimeProvider time, ILogger? log = null)
    {
        _path = path;
        _time = time;
        _log = log ?? NullLogger.Instance;
    }

    /// <summary>Counts one launch of an app and persists the store.</summary>
    public void RecordLaunch(string appId)
    {
        if (string.IsNullOrEmpty(appId) || appId.Length > 4200) return;
        lock (_gate)
        {
            var items = Items();
            int count = items.TryGetValue(appId, out var old) ? old.Count : 0;
            items[appId] = new Usage(count + 1, _time.GetUtcNow());
            Prune(items);
            Save(items);
        }
    }

    /// <summary>Ranking bonus in 0..0.16: grows with the launch count (log scale) and adds a little for apps used in the last week.</summary>
    public double Boost(string appId)
    {
        lock (_gate)
        {
            if (!Items().TryGetValue(appId, out var usage)) return 0;
            double count = Math.Min(0.12, 0.03 * Math.Log2(1 + usage.Count));
            double recent = _time.GetUtcNow() - usage.Last <= RecentWindow ? 0.04 : 0;
            return count + recent;
        }
    }

    /// <summary>App ids, most recently launched first.</summary>
    public IReadOnlyList<string> RecentAppIds(int limit)
    {
        if (limit <= 0) return Array.Empty<string>();
        lock (_gate)
        {
            return Items().OrderByDescending(kv => kv.Value.Last).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Take(limit).Select(kv => kv.Key).ToArray();
        }
    }

    /// <summary>Usage of one app, if it was ever launched through search or the dock.</summary>
    public Usage? Get(string appId)
    {
        lock (_gate) return Items().TryGetValue(appId, out var u) ? u : null;
    }

    private Dictionary<string, Usage> Items()
    {
        if (_items is { } loaded) return loaded;
        var items = _items = new Dictionary<string, Usage>(StringComparer.Ordinal);
        try
        {
            var bytes = AtomicFile.TryRead(_path, MaxFileBytes, out var tooLarge);
            if (bytes is null || tooLarge) return items;
            var root = JsonNode.Parse(bytes)?.AsObject();
            var apps = root?["apps"]?.AsObject();
            if (apps is null) return items;
            foreach (var kv in apps)
            {
                if (kv.Value is not JsonObject o || kv.Key.Length == 0 || kv.Key.Length > 4200) continue;
                int count = o["count"] is JsonValue cv && cv.TryGetValue<int>(out var c) ? Math.Max(0, c) : 0;
                if (o["last"] is not JsonValue lv || !lv.TryGetValue<DateTimeOffset>(out var last)) continue;
                if (count > 0) items[kv.Key] = new Usage(count, last);
            }
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or IOException)
        {
            _log.LogWarning(ex, "search usage file is unreadable; starting empty");
            items.Clear();
        }
        return items;
    }

    private void Prune(Dictionary<string, Usage> items)
    {
        if (items.Count <= MaxEntries) return;
        foreach (var victim in items.OrderBy(kv => kv.Value.Count).ThenBy(kv => kv.Value.Last).Take(items.Count - MaxEntries).Select(kv => kv.Key).ToArray())
        {
            items.Remove(victim);
        }
    }

    private void Save(Dictionary<string, Usage> items)
    {
        try
        {
            var apps = new JsonObject();
            foreach (var kv in items)
            {
                apps[kv.Key] = new JsonObject { ["count"] = kv.Value.Count, ["last"] = kv.Value.Last.ToString("O", System.Globalization.CultureInfo.InvariantCulture) };
            }
            var root = new JsonObject { ["version"] = 1, ["apps"] = apps };
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            AtomicFile.WriteAllText(_path, root.ToJsonString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "could not save search usage");
        }
    }
}
