using System.Collections.Concurrent;
using BetterWindows.Core.Platform;
using Microsoft.Extensions.Logging;

namespace BetterWindows.Core.Resources;

/// <summary>
/// Window thumbnails for Mission Control (docs/IPC.md section 8, docs/DECISIONS.md #6). A capture result is cached per (handle, width bucket, rev) with LRU 64 entries /
/// 64 MB / 30 s lifetime; at most 3 captures run at once and identical requests share one capture. Windows that cannot be captured now (cloaked on another virtual desktop,
/// minimised, protected) are served from the LAST GOOD image of that window, whatever rev is asked for; those images are bounded to 64 windows and dropped when the window is
/// gone. A global capture counter orders captures (diagnostics) — the rev that busts the browser cache is chosen by the URL builder.
/// </summary>
internal sealed class ThumbnailStore
{
    public const int MaxEntries = 64, MaxLastGood = 64, MaxConcurrentCaptures = 3, PrewarmWidth = 480;
    public const long MaxBytes = 64L * 1024 * 1024;
    private const int MaxThumbBytes = 8 * 1024 * 1024;
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan QueueTimeout = TimeSpan.FromSeconds(5), PruneEvery = TimeSpan.FromSeconds(2);

    private readonly record struct Key(long Handle, int Width, string Rev);
    private sealed record LastGood(byte[] Data, long Serial);

    private readonly TimeProvider _time;
    private readonly Func<IWindowCapture?> _capture;
    private readonly Func<long, bool> _isTracked;
    private readonly ILogger _log;
    private readonly LruCache<Key, byte[]> _cache;
    private readonly Dictionary<long, LastGood> _lastGood = new();
    private readonly object _lock = new();
    private readonly SemaphoreSlim _gate = new(MaxConcurrentCaptures, MaxConcurrentCaptures);
    private readonly ConcurrentDictionary<Key, Lazy<Task<byte[]?>>> _inflight = new();
    private long _serial, _lastPrune;

    public ThumbnailStore(TimeProvider time, Func<IWindowCapture?> capture, Func<long, bool> isTracked, ILogger log)
    {
        _time = time;
        _capture = capture;
        _isTracked = isTracked;
        _log = log;
        _cache = new LruCache<Key, byte[]>(MaxEntries, MaxBytes, time, Lifetime);
        _lastPrune = time.GetTimestamp();
    }

    public int CachedCount => _cache.Count;
    public int LastGoodCount { get { lock (_lock) return _lastGood.Count; } }
    public long Captures => Interlocked.Read(ref _serial);
    public bool HasLastGood(long handle) { lock (_lock) return _lastGood.ContainsKey(handle); }

    /// <summary>A thumbnail for the window: from the cache, from a fresh capture, or (capture impossible) the last good image. Null if there never was one.</summary>
    public async Task<byte[]?> GetAsync(long handle, int width, string? rev, CancellationToken ct)
    {
        PruneIfDue();
        var key = new Key(handle, width, rev ?? "");
        if (_cache.TryGet(key, out var hit)) return hit;
        var lazy = _inflight.GetOrAdd(key, k => new Lazy<Task<byte[]?>>(() => CaptureAsync(k)));
        return await lazy.Value.WaitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Captures now and remembers the result as the window's last good image (bounded, never throws).</summary>
    public async Task PrewarmAsync(long handle, CancellationToken ct)
    {
        try
        {
            PruneIfDue();
            if (!_isTracked(handle)) return;
            var bytes = await CaptureCoreAsync(handle, PrewarmWidth, ct).ConfigureAwait(false);
            if (bytes is not null) RememberGood(handle, bytes);
        }
        catch (OperationCanceledException) { /* caller gave up */ }
        catch (Exception ex) { _log.LogDebug(ex, "Thumbnail prewarm for {Handle} failed", handle); }
    }

    /// <summary>Runs once per in-flight key (shared by all waiting requests); removes itself from the in-flight table when done, after the result was cached.</summary>
    private async Task<byte[]?> CaptureAsync(Key key)
    {
        try
        {
            byte[]? bytes = null;
            try { bytes = await CaptureCoreAsync(key.Handle, key.Width, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) { _log.LogDebug(ex, "Thumbnail capture for {Handle} failed", key.Handle); }
            if (bytes is not null)
            {
                _cache.Set(key, bytes, bytes.Length);
                RememberGood(key.Handle, bytes);
                return bytes;
            }
            lock (_lock) return _lastGood.TryGetValue(key.Handle, out var g) ? g.Data : null;
        }
        finally { _inflight.TryRemove(key, out _); }
    }

    /// <summary>One capture under the global concurrency limit and a timeout; returns validated image bytes or null.</summary>
    private async Task<byte[]?> CaptureCoreAsync(long handle, int width, CancellationToken ct)
    {
        var capture = _capture();
        if (capture is null) return null;
        using var timeout = new CancellationTokenSource(QueueTimeout, _time);
        using var queueCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try { await _gate.WaitAsync(queueCts.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { _log.LogDebug("Thumbnail capture queue timed out for {Handle}", handle); return null; }
        try
        {
            using var captureTimeout = new CancellationTokenSource(CaptureTimeout, _time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, captureTimeout.Token);
            var work = Task.Run(() => capture.CaptureAsync(handle, width, linked.Token), CancellationToken.None);
            byte[]? bytes;
            try { bytes = await work.WaitAsync(linked.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { _ = work.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted); return null; }
            if (bytes is null || bytes.Length == 0 || bytes.Length > MaxThumbBytes || ImageFormats.Sniff(bytes) is null) return null;
            Interlocked.Increment(ref _serial);
            return bytes;
        }
        finally { _gate.Release(); }
    }

    private void RememberGood(long handle, byte[] bytes)
    {
        lock (_lock)
        {
            _lastGood[handle] = new LastGood(bytes, Interlocked.Read(ref _serial));
            while (_lastGood.Count > MaxLastGood)
            {
                var oldest = _lastGood.MinBy(kv => kv.Value.Serial).Key;
                _lastGood.Remove(oldest);
            }
        }
    }

    /// <summary>Forgets images of windows that no longer exist (at most every 2 s; one tracker lookup per remembered window).</summary>
    private void PruneIfDue()
    {
        var now = _time.GetTimestamp();
        if (_time.GetElapsedTime(Interlocked.Read(ref _lastPrune), now) < PruneEvery) return;
        Interlocked.Exchange(ref _lastPrune, now);
        long[] handles;
        lock (_lock) handles = _lastGood.Keys.ToArray();
        var dead = handles.Where(h => !_isTracked(h)).ToArray();
        if (dead.Length > 0)
        {
            lock (_lock) foreach (var h in dead) _lastGood.Remove(h);
        }
        _cache.RemoveWhere(k => !_isTracked(k.Handle));
    }

    /// <summary>Test hook: forces the next call to prune.</summary>
    internal void ForcePrune() => Interlocked.Exchange(ref _lastPrune, _time.GetTimestamp() - _time.TimestampFrequency * 60);
}
