using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Core.Services;

/// <summary>
/// The installed-app catalog over <see cref="IAppCatalogSource"/>: entries get their <c>IconUrl</c> (<see cref="DynUrls.AppIcon"/>), are de-duplicated by id and sorted by name
/// (culture-aware, case-insensitive), and are indexed by id / AUMID / exe path (all case-insensitive). The first access starts a background scan; the source's
/// <c>Changed</c> event (start-menu folders) triggers a re-scan after a 500 ms quiet period. Scans are single-flight (a request during a scan schedules exactly one more),
/// a failing scan keeps the previous list, and <see cref="Changed"/> is only raised when the list really changed.
/// </summary>
public sealed class AppCatalog : IAppCatalog, IDisposable
{
    /// <summary>Quiet period after a start-menu change before re-scanning.</summary>
    public static readonly TimeSpan RescanDebounce = TimeSpan.FromMilliseconds(500);

    private sealed record Snapshot(
        IReadOnlyList<AppEntry> Apps, IReadOnlyDictionary<string, AppEntry> ById, IReadOnlyDictionary<string, AppEntry> ByAumid, IReadOnlyDictionary<string, AppEntry> ByPath, string Fingerprint)
    {
        public static readonly Snapshot Empty = new(Array.Empty<AppEntry>(), new Dictionary<string, AppEntry>(), new Dictionary<string, AppEntry>(), new Dictionary<string, AppEntry>(), "");
    }

    private readonly IAppCatalogSource _source;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly object _lock = new();
    private readonly CancellationTokenSource _cts = new();
    private volatile Snapshot _snap = Snapshot.Empty;
    private Task? _running;
    private bool _rerun, _disposed;
    private int _started;
    private ITimer? _debounce;

    public AppCatalog(IAppCatalogSource source, TimeProvider time, ILogger<AppCatalog>? log = null)
    {
        _source = source;
        _time = time;
        _log = (ILogger?)log ?? NullLogger.Instance;
    }

    public event Action? Changed;

    public IReadOnlyList<AppEntry> Apps { get { EnsureStarted(); return _snap.Apps; } }

    public AppEntry? FindById(string id)
    {
        EnsureStarted();
        return string.IsNullOrEmpty(id) ? null : _snap.ById.GetValueOrDefault(id.Trim());
    }

    public AppEntry? FindByAumid(string aumid)
    {
        EnsureStarted();
        return string.IsNullOrEmpty(aumid) ? null : _snap.ByAumid.GetValueOrDefault(aumid.Trim());
    }

    public AppEntry? FindByExePath(string exePath)
    {
        EnsureStarted();
        var key = PathKey(exePath);
        return key is null ? null : _snap.ByPath.GetValueOrDefault(key);
    }

    /// <summary>Scans now. Completes when a scan that started at or after this call has finished (joins one in flight and runs one more if the call arrived mid-scan).</summary>
    public Task RefreshAsync(CancellationToken ct = default)
    {
        EnsureSubscribed();
        Task joined;
        lock (_lock)
        {
            if (_disposed) return Task.CompletedTask;
            if (_running is null) _running = joined = Task.Run(RunScansAsync);
            else { _rerun = true; joined = _running; }
        }
        return joined.WaitAsync(ct);
    }

    public void Dispose()
    {
        ITimer? t;
        lock (_lock) { _disposed = true; t = _debounce; _debounce = null; }
        try { _source.Changed -= OnSourceChanged; } catch { /* port already gone */ }
        t?.Dispose();
        try { _cts.Cancel(); } catch { /* disposed concurrently */ }
    }

    // ---- internals -----------------------------------------------------------------------------------------------------------------------------------

    private void EnsureStarted()
    {
        if (EnsureSubscribed()) _ = RefreshAsync();
    }

    /// <summary>Returns true exactly once: the caller that subscribed is the one that triggers the initial scan.</summary>
    private bool EnsureSubscribed()
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0) return false;
        try { _source.Changed += OnSourceChanged; }
        catch (Exception ex) { _log.LogWarning(ex, "Could not subscribe to the app catalog source"); }
        return true;
    }

    /// <summary>Raised on an OS thread (file watcher): only arms the debounce timer.</summary>
    private void OnSourceChanged()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _debounce ??= _time.CreateTimer(_ => { try { _ = RefreshAsync(); } catch (Exception ex) { _log.LogError(ex, "Scheduled app catalog refresh failed"); } }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _debounce.Change(RescanDebounce, Timeout.InfiniteTimeSpan);
        }
    }

    private async Task RunScansAsync()
    {
        while (true)
        {
            await ScanOnceAsync().ConfigureAwait(false);
            lock (_lock)
            {
                if (!_rerun || _disposed) { _running = null; _rerun = false; return; }
                _rerun = false;
            }
        }
    }

    private async Task ScanOnceAsync()
    {
        try
        {
            var raw = await _source.ScanAsync(_cts.Token).ConfigureAwait(false);
            var next = Build(raw);
            bool changed;
            lock (_lock)
            {
                changed = next.Fingerprint != _snap.Fingerprint;
                if (changed) _snap = next;
            }
            if (changed) RaiseChanged();
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested) { /* shutting down */ }
        catch (Exception ex) { _log.LogError(ex, "App catalog scan failed; keeping the previous list ({Count} apps)", _snap.Apps.Count); }
    }

    private void RaiseChanged()
    {
        var handlers = Changed;
        if (handlers is null) return;
        foreach (var d in handlers.GetInvocationList())
        {
            try { ((Action)d)(); }
            catch (Exception ex) { _log.LogError(ex, "An app catalog Changed subscriber threw"); }
        }
    }

    private Snapshot Build(IReadOnlyList<AppEntry>? raw)
    {
        var byId = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in raw ?? Array.Empty<AppEntry>())
        {
            var id = e?.Id?.Trim();
            var name = e?.Name?.Trim();
            if (e is null || string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name)) { _log.LogDebug("Skipping an app catalog entry without id or name"); continue; }
            if (byId.TryGetValue(id, out var existing))
            {
                // the same app found in two start-menu folders: keep one entry, but do not lose information the first copy lacked
                byId[id] = existing with { Path = existing.Path ?? e.Path, Aumid = existing.Aumid ?? e.Aumid, Category = existing.Category ?? e.Category };
                continue;
            }
            byId[id] = e with { Id = id, Name = name, IconUrl = DynUrls.AppIcon(id) };
        }

        var sorted = byId.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(a => a.Id, StringComparer.Ordinal).ToArray();
        var ids = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
        var aumids = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
        var paths = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in sorted)
        {
            ids[a.Id] = a;
            if (!string.IsNullOrWhiteSpace(a.Aumid)) aumids.TryAdd(a.Aumid.Trim(), a);
            if (PathKey(a.Path) is { } k) paths.TryAdd(k, a);
        }
        var fingerprint = string.Join('\n', sorted.Select(a => string.Join('\t', a.Id, a.Name, a.Path, a.Aumid, a.Packaged, a.Category)));
        return new Snapshot(sorted, ids, aumids, paths, fingerprint);
    }

    /// <summary>Lookup key of an exe path: the canonical local form when it is one, else the trimmed text with forward slashes unified.</summary>
    private static string? PathKey(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var t = path.Trim();
        return InputPolicy.TryNormalizeLocalPath(t, out var n) ? n : t.Replace('/', '\\');
    }
}
