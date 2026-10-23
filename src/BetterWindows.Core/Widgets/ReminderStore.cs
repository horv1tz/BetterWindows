using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Core.Widgets;

/// <summary>A reminder as persisted: the protocol fields plus the scheduler's <c>notified</c> flag (reminders.due fires once) and the creation time.</summary>
public sealed record StoredReminder
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("title")] public string Title { get; init; } = "";
    [JsonPropertyName("notes")] public string? Notes { get; init; }
    /// <summary>Canonical local date-time (<see cref="ReminderRules.FormatDue"/>) or null.</summary>
    [JsonPropertyName("due")] public string? Due { get; init; }
    [JsonPropertyName("done")] public bool Done { get; init; }
    [JsonPropertyName("list")] public string? List { get; init; }
    [JsonPropertyName("notified")] public bool Notified { get; init; }
    [JsonPropertyName("createdAt")] public string? CreatedAt { get; init; }

    public Reminder ToProtocol() => new() { Id = Id, Title = Title, Notes = Notes, Due = Due, Done = Done, List = List };
}

/// <summary>
/// The reminders file: <c>{ "version": 1, "items": [...] }</c> at a path chosen by the caller (the controller passes <c>IAppPaths.DataDir/reminders.json</c>). Thread-safe;
/// every mutation is saved with <see cref="AtomicFile"/> BEFORE it becomes visible (a failed write rolls the change back and throws <see cref="IOException"/>). Loading is
/// tolerant: a corrupt file is moved aside (<c>reminders.corrupt-&lt;utc&gt;.json</c>), single bad items are dropped, at most <see cref="ReminderRules.MaxItems"/> are kept.
/// A file that exists but cannot be read right now (antivirus lock) is NEVER treated as empty: mutations fail until it can be read, so no data is overwritten.
/// </summary>
public sealed class ReminderStore
{
    private const int MaxFileBytes = 16 * 1024 * 1024;

    private readonly string _path;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly object _gate = new();
    private List<StoredReminder> _items = new();
    private bool _loaded;

    public ReminderStore(string path, TimeProvider time, ILogger? log = null)
    {
        _path = path;
        _time = time;
        _log = log ?? NullLogger.Instance;
    }

    public string Path => _path;

    /// <summary>Reads the file. Never throws; see the class comment for the unreadable-file rule.</summary>
    public void Load()
    {
        lock (_gate) LoadLocked();
    }

    private void LoadLocked()
    {
        _loaded = false;
        _items = new List<StoredReminder>();
        var exists = File.Exists(_path);
        var bytes = AtomicFile.TryRead(_path, MaxFileBytes, out var tooLarge);
        if (bytes is null)
        {
            if (tooLarge) { _log.LogWarning("reminders.json is larger than {Max} bytes; moved aside", MaxFileBytes); AtomicFile.Quarantine(_path, _time); _loaded = true; }
            else if (!exists) _loaded = true;
            else _log.LogWarning("reminders.json exists but cannot be read right now; changes are refused until it can");
            return;
        }

        JsonNode? root;
        try { root = JsonNode.Parse(bytes, null, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "reminders.json is corrupt; moved aside");
            AtomicFile.Quarantine(_path, _time);
            _loaded = true;
            return;
        }

        var array = root switch { JsonObject o => o["items"] as JsonArray, JsonArray a => a, _ => null };
        if (array is null)
        {
            if (root is not null) { AtomicFile.Quarantine(_path, _time); }
            _loaded = true;
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in array)
        {
            if (_items.Count >= ReminderRules.MaxItems) break;
            var item = TryNormalize(node);
            if (item is not null && seen.Add(item.Id)) _items.Add(item);
        }
        _loaded = true;
    }

    /// <summary>Drops items that cannot be repaired (no valid id or title); repairs the rest field by field (bad due -> none, wrong-typed or over-long text -> cut/ignored).</summary>
    private static StoredReminder? TryNormalize(JsonNode? node)
    {
        if (node is not JsonObject o) return null;
        var id = Str(o, "id");
        if (!ReminderRules.IsValidId(id)) return null;
        var title = InputPolicy.Truncate((Str(o, "title") ?? "").Trim(), ReminderRules.MaxTitleChars).Trim();
        if (title.Length == 0) return null;
        var rawDue = Str(o, "due");
        string? due = rawDue is not null && ReminderRules.TryParseDue(rawDue, out var local) ? ReminderRules.FormatDue(local) : null;
        var notes = Str(o, "notes");
        var list = Str(o, "list");
        return new StoredReminder
        {
            Id = id!.ToLowerInvariant(),
            Title = title,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : (notes.Length > ReminderRules.MaxNotesChars ? notes[..ReminderRules.MaxNotesChars] : notes),
            Due = due,
            Done = Bool(o, "done"),
            List = string.IsNullOrWhiteSpace(list) ? null : InputPolicy.Truncate(list.Trim(), ReminderRules.MaxListChars),
            Notified = Bool(o, "notified"),
            CreatedAt = Str(o, "createdAt") is { Length: <= 40 } c ? c : null,
        };
    }

    private static string? Str(JsonObject o, string name) => o[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    private static bool Bool(JsonObject o, string name) => o[name] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    public IReadOnlyList<StoredReminder> Snapshot()
    {
        lock (_gate) return _items.ToArray();
    }

    public int Count { get { lock (_gate) return _items.Count; } }

    /// <summary>Adds the reminder; null when the store is full. Throws <see cref="IOException"/> when the file cannot be written (nothing changed).</summary>
    public StoredReminder? Add(StoredReminder item)
    {
        lock (_gate)
        {
            EnsureLoadedLocked();
            if (_items.Count >= ReminderRules.MaxItems) return null;
            var next = new List<StoredReminder>(_items) { item };
            SaveLocked(next);
            _items = next;
            return item;
        }
    }

    /// <summary>Applies <paramref name="change"/> to the reminder; null when the id is unknown.</summary>
    public StoredReminder? Update(string id, Func<StoredReminder, StoredReminder> change)
    {
        lock (_gate)
        {
            EnsureLoadedLocked();
            var i = _items.FindIndex(r => r.Id == id);
            if (i < 0) return null;
            var updated = change(_items[i]);
            var next = new List<StoredReminder>(_items) { [i] = updated };
            SaveLocked(next);
            _items = next;
            return updated;
        }
    }

    public bool Remove(string id)
    {
        lock (_gate)
        {
            EnsureLoadedLocked();
            var next = _items.Where(r => r.Id != id).ToList();
            if (next.Count == _items.Count) return false;
            SaveLocked(next);
            _items = next;
            return true;
        }
    }

    /// <summary>
    /// Atomically flags the given reminders as notified and returns ONLY those that were still un-notified, so two concurrent scheduler passes can never both fire the
    /// same reminder. Throws <see cref="IOException"/> (nothing changed) if the file cannot be written.
    /// </summary>
    public IReadOnlyList<StoredReminder> MarkNotified(IReadOnlyCollection<string> ids)
    {
        lock (_gate)
        {
            EnsureLoadedLocked();
            var flipped = new List<StoredReminder>();
            var next = new List<StoredReminder>(_items.Count);
            foreach (var r in _items)
            {
                if (!r.Notified && ids.Contains(r.Id)) { var n = r with { Notified = true }; flipped.Add(n); next.Add(n); }
                else next.Add(r);
            }
            if (flipped.Count == 0) return flipped;
            SaveLocked(next);
            _items = next;
            return flipped;
        }
    }

    private void EnsureLoadedLocked()
    {
        if (_loaded) return;
        LoadLocked();
        if (!_loaded) throw new IOException("the reminders file exists but cannot be read");
    }

    private void SaveLocked(List<StoredReminder> items)
    {
        var json = JsonSerializer.Serialize(new { version = 1, items }, ProtocolJson.IndentedOptions);
        AtomicFile.WriteAllText(_path, json);
    }
}
