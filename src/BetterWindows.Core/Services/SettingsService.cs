using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BetterWindows.Core.Config;
using BetterWindows.Core.Ipc;
using BetterWindows.Core.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Core.Services;

/// <summary>What the settings store knows about its file, for diagnostics (diag.report).</summary>
public sealed record SettingsStoreStatus(string ConfigFile, bool FileExists, bool PersistenceBlocked, string? BlockedReason, string LastLoad);

/// <summary>
/// <see cref="ISettingsService"/> over <c>config.json</c> (docs/IPC.md section 12). Design: one lock serialises every read-modify-write (Patch/Update/Reload), all work inside it is
/// synchronous and short, <see cref="Changed"/> is raised after the lock is released by a single-flight drain (in order, always with the newest value, no timers),
/// persistence is a debounced (300 ms) atomic write of the typed value overlaid on the raw document (unknown keys survive), a <see cref="FileSystemWatcher"/> reloads external
/// edits (own writes are recognised by content hash), and a corrupt file is quarantined and NEVER overwritten. Loading is lazy (first use), constructors do no I/O.
/// </summary>
public sealed class SettingsService : ISettingsService, IDisposable
{
    /// <summary>Quiet period before a changed value is written to disk.</summary>
    public static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(300);
    /// <summary>Longest a change may stay unsaved while edits keep arriving (the debounce is not extended past this).</summary>
    public static readonly TimeSpan MaxSaveDelay = TimeSpan.FromSeconds(2);
    /// <summary>Quiet period after a file-system event before the file is re-read.</summary>
    public static readonly TimeSpan ReloadDebounce = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan WriteRetryDelay = TimeSpan.FromSeconds(5);
    /// <summary>config.json is for humans too: indented, and non-ASCII text (city names, pin names) stays readable instead of \uXXXX escapes. Never served to a web page.</summary>
    private static readonly JsonSerializerOptions FileJson = new(ProtocolJson.IndentedOptions) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly IAppPaths _paths;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly IReadOnlyList<SettingsMigration> _migrations;
    private readonly int _targetVersion;

    private readonly object _lock = new();
    private readonly object _writeLock = new();
    private Settings? _current;
    private string _currentJson = "";
    private JsonObject _raw = new();
    private long _version, _writtenVersion;
    private bool _dirty, _persistBlocked, _disposed;
    private string? _blockedReason;
    private string _lastLoad = "not loaded";
    private string? _lastFileHash;
    private DateTimeOffset? _dirtySince;
    private ITimer? _saveTimer, _reloadTimer;
    private FileSystemWatcher? _watcher;
    private int _raising, _pending;

    public SettingsService(IAppPaths paths, TimeProvider time, ILogger<SettingsService>? log = null)
        : this(paths, time, log, SettingsMigrations.Default, SettingsMigrations.CurrentVersion) { }

    /// <summary>Tests inject their own migration table and schema version.</summary>
    internal SettingsService(IAppPaths paths, TimeProvider time, ILogger? log, IReadOnlyList<SettingsMigration> migrations, int targetVersion)
    {
        _paths = paths;
        _time = time;
        _log = log ?? NullLogger.Instance;
        _migrations = migrations;
        _targetVersion = targetVersion;
    }

    public event Action<Settings>? Changed;

    public Settings Current
    {
        get
        {
            var c = Volatile.Read(ref _current);
            if (c is not null) return c;
            lock (_lock) { EnsureLoadedLocked(); return _current!; }
        }
    }

    /// <summary>True while saving is suspended because config.json is unreadable or a corrupt copy could not be moved aside (the file is then left untouched).</summary>
    public bool PersistenceBlocked { get { lock (_lock) return _persistBlocked; } }

    public SettingsStoreStatus Status
    {
        get
        {
            lock (_lock) return new SettingsStoreStatus(_paths.ConfigFile, File.Exists(_paths.ConfigFile), _persistBlocked, _blockedReason, _lastLoad);
        }
    }

    // ---- public API ----------------------------------------------------------------------------------------------------------------------------------

    public Task<Settings> PatchAsync(JsonNode patch, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            Settings result;
            bool changed;
            lock (_lock)
            {
                EnsureLoadedLocked();
                var checkedPatch = SettingsPatch.Validate(patch);
                var merged = SettingsPatch.Merge((JsonObject)ProtocolJson.ToNode(_current!)!, checkedPatch);
                var next = SettingsValidator.Bind(merged) with { Version = _current!.Version };
                changed = StoreLocked(next);
                result = _current!;
            }
            if (changed) RaiseChanged();
            return Task.FromResult(result);
        }
        catch (Exception ex) { return Task.FromException<Settings>(ex); }
    }

    public Task<Settings> UpdateAsync(Func<Settings, Settings> mutate, CancellationToken ct = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(mutate);
            ct.ThrowIfCancellationRequested();
            Settings result;
            bool changed;
            lock (_lock)
            {
                EnsureLoadedLocked();
                var cur = _current!;
                var proposed = mutate(cur) ?? throw new InvalidOperationException("the settings mutation returned null");
                var next = SettingsValidator.Normalize(proposed with { Version = cur.Version });
                changed = StoreLocked(next);
                result = _current!;
            }
            if (changed) RaiseChanged();
            return Task.FromResult(result);
        }
        catch (Exception ex) { return Task.FromException<Settings>(ex); }
    }

    public void Reload()
    {
        var changed = false;
        lock (_lock)
        {
            if (_disposed) return;
            if (_current is null) { EnsureLoadedLocked(); return; }
            changed = ReloadLocked();
        }
        if (changed) RaiseChanged();
    }

    /// <summary>Writes any pending change now (called on exit, by <see cref="Dispose"/>, and by the debounce timer). Never throws.</summary>
    public void Flush()
    {
        JsonObject doc;
        long ver;
        lock (_lock)
        {
            _saveTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            if (_current is null || !_dirty) return;
            if (_persistBlocked) { _log.LogDebug("Settings not saved: persistence is blocked ({Reason})", _blockedReason); return; }
            doc = SettingsDocument.Compose(_raw, _current);
            _raw = doc;
            ver = _version;
            _dirty = false;
            _dirtySince = null;
        }

        byte[] bytes;
        try { bytes = new UTF8Encoding(false).GetBytes(doc.ToJsonString(FileJson)); }
        catch (Exception ex) { _log.LogError(ex, "Settings could not be serialised; nothing was written"); return; }
        if (bytes.Length > Limits.MaxConfigBytes)
        {
            _log.LogError("Settings are {Size} bytes, above the {Max} byte cap; nothing was written", bytes.Length, Limits.MaxConfigBytes);
            return;
        }

        lock (_writeLock)
        {
            if (ver <= _writtenVersion) return; // a newer snapshot has already been written
            try
            {
                AtomicFile.WriteAllBytes(_paths.ConfigFile, bytes, keepBackup: true);
                _writtenVersion = ver;
                lock (_lock) _lastFileHash = Hash(bytes);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Writing {File} failed; will retry", _paths.ConfigFile);
                lock (_lock) { _dirty = true; _dirtySince ??= _time.GetUtcNow(); ArmSaveTimerLocked(WriteRetryDelay); }
            }
        }
    }

    public void Dispose()
    {
        ITimer? save, reload;
        FileSystemWatcher? watcher;
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            save = _saveTimer; reload = _reloadTimer; watcher = _watcher;
            _saveTimer = null; _reloadTimer = null; _watcher = null;
        }
        try { watcher?.Dispose(); } catch { /* shutting down */ }
        reload?.Dispose();
        save?.Dispose();
        Flush(); // _disposed only stops new scheduling and reloads; the final flush must still run.
    }

    // ---- load / reload -------------------------------------------------------------------------------------------------------------------------------

    private sealed record Loaded(JsonObject Raw, Settings Settings, bool Dirty, bool Blocked, string? BlockedReason, string Note, string? Hash, bool KeepCurrent);

    private void EnsureLoadedLocked()
    {
        if (_current is not null) return;
        var loaded = ReadConfig(reload: false);
        _raw = loaded.Raw;
        _current = loaded.Settings;
        _currentJson = Serialize(loaded.Settings);
        _version = 1;
        _lastFileHash = loaded.Hash;
        _persistBlocked = loaded.Blocked;
        _blockedReason = loaded.BlockedReason;
        _lastLoad = loaded.Note;
        if (loaded.Dirty) MarkDirtyLocked();
        _log.LogInformation("Settings loaded: {Note}", loaded.Note);
        StartWatcherLocked();
    }

    /// <summary>Re-reads the file under the lock. Returns true if the effective value changed.</summary>
    private bool ReloadLocked()
    {
        var bytes = AtomicFile.TryRead(_paths.ConfigFile, Limits.MaxConfigBytes, out _);
        if (bytes is not null && Hash(bytes) == _lastFileHash) return false; // our own write (or nothing new)

        var loaded = ReadConfig(reload: true);
        if (loaded.KeepCurrent)
        {
            // Missing / corrupt (quarantined) / unreadable during a reload: keep what is in memory. A missing or quarantined file is re-created from memory.
            _lastLoad = loaded.Note;
            if (loaded.Blocked) { _persistBlocked = true; _blockedReason = loaded.BlockedReason; }
            if (loaded.Dirty) { _persistBlocked = false; _blockedReason = null; MarkDirtyLocked(); }
            return false;
        }

        _raw = loaded.Raw;
        _lastFileHash = loaded.Hash;
        _lastLoad = loaded.Note;
        _persistBlocked = false;
        _blockedReason = null;
        if (loaded.Dirty) MarkDirtyLocked();
        else { _dirty = false; _dirtySince = null; _saveTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan); } // the file on disk is newer than anything pending
        return StoreLocked(loaded.Settings, markDirty: false);
    }

    private Loaded ReadConfig(bool reload)
    {
        var path = _paths.ConfigFile;
        var defaults = SettingsValidator.Normalize(new Settings { Version = _targetVersion });
        var bytes = AtomicFile.TryRead(path, Limits.MaxConfigBytes, out var tooLarge);

        if (bytes is null)
        {
            if (tooLarge) return HandleCorrupt(reload, defaults, $"larger than {Limits.MaxConfigBytes / (1024 * 1024)} MB");
            if (!File.Exists(path))
                return new Loaded(new JsonObject(), defaults, Dirty: reload, Blocked: false, null, reload ? "config.json disappeared; recreating from memory" : "no config.json; using defaults", null, KeepCurrent: reload);
            // Exists but unreadable (locked, no permission): never write over what we could not read.
            var reason = "config.json exists but cannot be read";
            _log.LogError("{Reason}; settings will not be saved until it can be read", reason);
            return new Loaded(new JsonObject(), defaults, Dirty: false, Blocked: true, reason, reason, null, KeepCurrent: reload);
        }

        if (!TryParseRoot(bytes, out var root, out var error)) return HandleCorrupt(reload, defaults, error);
        var hash = Hash(bytes);
        return FromRoot(root, bytes, hash, "config.json");
    }

    private Loaded FromRoot(JsonObject root, byte[] originalBytes, string? hash, string source)
    {
        var dirty = false;
        var working = root;
        var version = SettingsMigrations.ReadVersion(root, _targetVersion);

        if (version < _targetVersion)
        {
            WriteVersionBackup(version, originalBytes);
            var clone = (JsonObject)root.DeepClone();
            try
            {
                SettingsMigrations.Migrate(clone, version, _targetVersion, _migrations, _log);
                working = clone;
                dirty = true;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Migrating settings from version {Version} failed; using the file as it is (backup config.v{Version}.bak was kept)", version, version);
            }
        }
        else if (version > _targetVersion)
        {
            _log.LogWarning("config.json has version {Version}, newer than this build ({Target}); loading what is understood and keeping the rest", version, _targetVersion);
        }

        if (SettingsDocument.UnknownSize(working, typeof(Settings)) > SettingsDocument.MaxUnknownChars)
        {
            _log.LogWarning("config.json carries more than {Max} chars of unknown settings; dropping them", SettingsDocument.MaxUnknownChars);
            if (ReferenceEquals(working, root)) working = (JsonObject)root.DeepClone();
            SettingsDocument.RemoveUnknown(working, typeof(Settings));
            dirty = true;
        }

        var issues = new List<string>();
        var settings = SettingsValidator.Bind(working, issues) with { Version = Math.Max(version, _targetVersion) };
        if (issues.Count > 0) _log.LogWarning("config.json had {Count} invalid value(s) that were reset: {First}", issues.Count, issues[0]);
        return new Loaded(working, settings, dirty, Blocked: false, null, $"{source}{(dirty ? " (migrated)" : "")}{(issues.Count > 0 ? $", {issues.Count} value(s) reset" : "")}", hash, KeepCurrent: false);
    }

    private Loaded HandleCorrupt(bool reload, Settings defaults, string reason)
    {
        var path = _paths.ConfigFile;
        _log.LogError("config.json is corrupt ({Reason}); moving it aside", reason);
        var quarantined = AtomicFile.Quarantine(path, _time, 5);
        if (quarantined is null)
        {
            var why = $"config.json is corrupt ({reason}) and could not be moved aside";
            _log.LogError("{Why}; settings will not be saved so the file is not overwritten", why);
            return new Loaded(new JsonObject(), defaults, Dirty: false, Blocked: true, why, why, null, KeepCurrent: reload);
        }

        if (reload)
        {
            // The running app keeps its in-memory settings; config.json is re-created from them and the broken copy stays in quarantine.
            return new Loaded(new JsonObject(), defaults, Dirty: true, Blocked: false, null, $"corrupt config.json moved to {Path.GetFileName(quarantined)}; recreating from memory", null, KeepCurrent: true);
        }

        var backup = _paths.ConfigFile + ".bak";
        var backupBytes = AtomicFile.TryRead(backup, Limits.MaxConfigBytes, out _);
        if (backupBytes is not null && TryParseRoot(backupBytes, out var backupRoot, out _))
        {
            var recovered = FromRoot(backupRoot, backupBytes, null, "config.json.bak (after corrupt config.json)");
            _log.LogWarning("Recovered settings from {Backup}; the corrupt file was kept as {Quarantine}", backup, quarantined);
            return recovered with { Dirty = true };
        }
        return new Loaded(new JsonObject(), defaults, Dirty: false, Blocked: false, null, $"corrupt config.json moved to {Path.GetFileName(quarantined)}; using defaults", null, KeepCurrent: false);
    }

    private static bool TryParseRoot(byte[] bytes, out JsonObject root, out string error)
    {
        root = null!;
        error = "";
        try
        {
            // Notepad and friends write a UTF-8 BOM; Utf8JsonReader rejects it. An empty or whitespace-only file holds nothing to lose: it is an empty document.
            var start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            if (bytes.AsSpan(start).IsEmpty || Encoding.UTF8.GetString(bytes, start, bytes.Length - start).AsSpan().Trim().IsEmpty) { root = new JsonObject(); return true; }
            var node = JsonNode.Parse(bytes.AsSpan(start), new JsonNodeOptions { PropertyNameCaseInsensitive = false }, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, MaxDepth = 64 });
            if (node is JsonObject o) { root = o; return true; }
            error = "the root is not a JSON object";
            return false;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            error = InputPolicy.Truncate(ex.Message, 200);
            return false;
        }
    }

    private void WriteVersionBackup(int version, byte[] original)
    {
        try
        {
            var backup = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_paths.ConfigFile))!, $"config.v{version}.bak");
            if (!File.Exists(backup)) AtomicFile.WriteAllBytes(backup, original);
        }
        catch (Exception ex) { _log.LogWarning(ex, "Could not write the pre-migration backup config.v{Version}.bak", version); }
    }

    // ---- state ---------------------------------------------------------------------------------------------------------------------------------------

    private static string Serialize(Settings s) => ProtocolJson.Serialize(s);

    /// <summary>Stores <paramref name="next"/> if it differs (compared as JSON, never with ==). Returns true if the value changed.</summary>
    private bool StoreLocked(Settings next, bool markDirty = true)
    {
        var json = Serialize(next);
        if (json == _currentJson) return false;
        _current = next;
        _currentJson = json;
        _version++;
        if (markDirty) MarkDirtyLocked();
        return true;
    }

    private void MarkDirtyLocked()
    {
        _version++; // a re-creation of the file from unchanged memory must also be newer than the last write
        _dirty = true;
        if (_disposed) return;
        _dirtySince ??= _time.GetUtcNow();
        var age = _time.GetUtcNow() - _dirtySince.Value;
        // Debounce: every change restarts the quiet period, but never beyond MaxSaveDelay since the first unsaved change (then the armed timer is left alone).
        if (_saveTimer is null || age < MaxSaveDelay) ArmSaveTimerLocked(SaveDebounce);
    }

    private void ArmSaveTimerLocked(TimeSpan delay)
    {
        if (_disposed) return;
        _saveTimer ??= _time.CreateTimer(_ => { try { Flush(); } catch (Exception ex) { _log.LogError(ex, "Debounced settings save failed"); } }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _saveTimer.Change(delay, Timeout.InfiniteTimeSpan);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    // ---- Changed (single-flight drain) ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Delivers the newest value to subscribers in order, outside every lock. If another thread is already delivering, it will pick the new value up before it returns, so
    /// subscribers always end on the newest value and never see two deliveries in parallel; bursts of writes collapse into fewer events.
    /// </summary>
    private void RaiseChanged()
    {
        Volatile.Write(ref _pending, 1);
        while (true)
        {
            if (Interlocked.CompareExchange(ref _raising, 1, 0) != 0) return;
            try
            {
                while (Interlocked.Exchange(ref _pending, 0) == 1)
                {
                    var handlers = Changed;
                    var value = Volatile.Read(ref _current);
                    if (handlers is null || value is null) continue;
                    foreach (var d in handlers.GetInvocationList())
                    {
                        try { ((Action<Settings>)d)(value); }
                        catch (Exception ex) { _log.LogError(ex, "A settings Changed subscriber threw"); }
                    }
                }
            }
            finally { Volatile.Write(ref _raising, 0); }
            if (Volatile.Read(ref _pending) == 0) return;
        }
    }

    // ---- file watcher --------------------------------------------------------------------------------------------------------------------------------

    private void StartWatcherLocked()
    {
        if (_watcher is not null || _disposed) return;
        try
        {
            var full = Path.GetFullPath(_paths.ConfigFile);
            var dir = Path.GetDirectoryName(full)!;
            Directory.CreateDirectory(dir);
            var w = new FileSystemWatcher(dir, Path.GetFileName(full))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
                IncludeSubdirectories = false,
            };
            w.Changed += (_, _) => OnFileSystemEvent();
            w.Created += (_, _) => OnFileSystemEvent();
            w.Deleted += (_, _) => OnFileSystemEvent();
            w.Renamed += (_, _) => OnFileSystemEvent();
            w.Error += (_, e) => { _log.LogWarning(e.GetException(), "Settings file watcher error; scheduling a reload"); OnFileSystemEvent(); };
            w.EnableRaisingEvents = true;
            _watcher = w;
        }
        catch (Exception ex) { _log.LogWarning(ex, "Could not watch {File} for external edits", _paths.ConfigFile); }
    }

    /// <summary>Called on a file-system thread: only arms the debounce timer (the re-read happens on the timer thread).</summary>
    internal void OnFileSystemEvent()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _reloadTimer ??= _time.CreateTimer(_ => { try { Reload(); } catch (Exception ex) { _log.LogError(ex, "Settings reload failed"); } }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _reloadTimer.Change(ReloadDebounce, Timeout.InfiniteTimeSpan);
        }
    }
}
