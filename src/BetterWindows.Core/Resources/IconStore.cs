using System.Security.Cryptography;
using System.Text;
using BetterWindows.Core.Services;
using Microsoft.Extensions.Logging;

namespace BetterWindows.Core.Resources;

/// <summary>
/// Two-level cache of generated icon images: in-memory LRU (256 entries / 16 MB) in front of a PNG cache on disk under <c>IAppPaths.CacheDir\icons\</c>, keyed by
/// sha1(key) where the key already contains the size and a staleness stamp (file mtime/length for path-based icons). Disk entries older than 14 days are purged once per
/// process. Everything is best effort: a broken cache directory only costs speed.
/// </summary>
internal sealed class IconStore
{
    private const int MaxEntries = 256, MaxBytes = 16 * 1024 * 1024, MaxDiskFileBytes = 2 * 1024 * 1024;
    private static readonly TimeSpan MaxDiskAge = TimeSpan.FromDays(14);
    private readonly LruCache<string, byte[]> _memory;
    private readonly string _dir;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private int _purged;

    public IconStore(IAppPaths paths, TimeProvider time, ILogger log)
    {
        _memory = new LruCache<string, byte[]>(MaxEntries, MaxBytes, time, comparer: StringComparer.Ordinal);
        _dir = Path.Combine(paths.CacheDir, "icons");
        _time = time;
        _log = log;
    }

    public int MemoryCount => _memory.Count;

    public static string FileNameFor(string key) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant() + ".png";

    public byte[]? TryGet(string key, bool useDisk)
    {
        if (_memory.TryGet(key, out var hit)) return hit;
        if (!useDisk) return null;
        try
        {
            var file = Path.Combine(_dir, FileNameFor(key));
            var bytes = AtomicFile.TryRead(file, MaxDiskFileBytes, out _);
            if (bytes is null) return null;
            if (ImageFormats.Sniff(bytes) is null) { TryDelete(file); return null; } // a damaged cache file must never be served
            _memory.Set(key, bytes, bytes.Length);
            return bytes;
        }
        catch (Exception ex) { _log.LogDebug(ex, "Icon disk cache read failed"); return null; }
    }

    public void Put(string key, byte[] png, bool useDisk)
    {
        _memory.Set(key, png, png.Length);
        if (!useDisk || png.Length > MaxDiskFileBytes) return;
        try
        {
            PurgeOldOnce();
            AtomicFile.WriteAllBytes(Path.Combine(_dir, FileNameFor(key)), png);
        }
        catch (Exception ex) { _log.LogDebug(ex, "Icon disk cache write failed"); }
    }

    public void ClearMemory() => _memory.Clear();

    private void PurgeOldOnce()
    {
        if (Interlocked.Exchange(ref _purged, 1) != 0 || !Directory.Exists(_dir)) return;
        try
        {
            foreach (var f in Directory.EnumerateFiles(_dir, "*.png"))
                if (_time.GetUtcNow().UtcDateTime - File.GetLastWriteTimeUtc(f) > MaxDiskAge) TryDelete(f); // the injected clock; file-system timestamps are UTC
        }
        catch (Exception ex) { _log.LogDebug(ex, "Icon disk cache purge failed"); }
    }

    private static void TryDelete(string file) { try { File.Delete(file); } catch { /* best effort */ } }
}
