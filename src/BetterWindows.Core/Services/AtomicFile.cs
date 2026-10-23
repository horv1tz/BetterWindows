using System.Text;

namespace BetterWindows.Core.Services;

/// <summary>
/// Crash-safe file helpers for every Core store (settings, reminders, usage, journals): write a temp file in the SAME directory, flush to disk, then File.Replace
/// (keeping a .bak) or File.Move; retries IOException (antivirus/indexer locks) with backoff up to ~2 s; reads are size-capped.
/// </summary>
public static class AtomicFile
{
    public static void WriteAllBytes(string path, byte[] data, bool keepBackup = false)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, $".{Path.GetFileName(path)}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                fs.Write(data, 0, data.Length);
                fs.Flush(true);
            }
            Retry(() =>
            {
                if (File.Exists(path))
                    File.Replace(tmp, path, keepBackup ? path + ".bak" : null, ignoreMetadataErrors: true);
                else
                    File.Move(tmp, path);
            });
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
        }
    }

    public static void WriteAllText(string path, string text, bool keepBackup = false) => WriteAllBytes(path, new UTF8Encoding(false).GetBytes(text), keepBackup);

    /// <summary>Reads at most <paramref name="maxBytes"/>; returns null if missing, unreadable or larger than the cap (<paramref name="tooLarge"/> tells which).</summary>
    public static byte[]? TryRead(string path, int maxBytes, out bool tooLarge)
    {
        tooLarge = false;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            if (info.Length > maxBytes) { tooLarge = true; return null; }
            return Retry(() => File.ReadAllBytes(path));
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>Moves a corrupt file aside as &lt;name&gt;.corrupt-&lt;utc&gt;&lt;ext&gt; and keeps only the newest <paramref name="keep"/> quarantined copies. Returns the new path or null.</summary>
    public static string? Quarantine(string path, TimeProvider time, int keep = 5)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            var target = Path.Combine(dir, $"{name}.corrupt-{time.GetUtcNow():yyyyMMddTHHmmssfff}{ext}");
            Retry(() => File.Move(path, target, overwrite: true));
            var old = Directory.GetFiles(dir, $"{name}.corrupt-*{ext}").OrderByDescending(f => f, StringComparer.Ordinal).Skip(keep);
            foreach (var f in old) { try { File.Delete(f); } catch { } }
            return target;
        }
        catch { return null; }
    }

    private static void Retry(Action action) => Retry<object?>(() => { action(); return null; });

    private static T Retry<T>(Func<T> action)
    {
        var delay = 25;
        for (var attempt = 0; ; attempt++)
        {
            try { return action(); }
            catch (IOException) when (attempt < 6) { Thread.Sleep(delay); delay *= 2; }
            catch (UnauthorizedAccessException) when (attempt < 6) { Thread.Sleep(delay); delay *= 2; }
        }
    }
}
