using System.Text;

namespace BetterWindows.Native.Interop;

/// <summary>
/// Small marker files that record "BetterWindows changed this system state" so a fresh process (crash recovery, rescue command) can undo it.
/// Location: <c>%LOCALAPPDATA%\BetterWindows\state\</c>. Every read and write is defensive: when the folder cannot be resolved or written,
/// callers get false/null and must fail safe (not change the system state they could not record).
/// </summary>
internal static class ShellStateStore
{
    /// <summary>The state folder, or null when the local application data folder cannot be resolved.</summary>
    public static string? Directory
    {
        get
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return string.IsNullOrEmpty(local) ? null : Path.Combine(local, "BetterWindows", "state");
        }
    }

    /// <summary>Full path of a marker file inside the state folder, or null when there is no state folder.</summary>
    public static string? PathOf(string fileName)
    {
        string? dir = Directory;
        return dir == null ? null : Path.Combine(dir, fileName);
    }

    /// <summary>True when the marker file exists. Never throws.</summary>
    public static bool Exists(string fileName)
    {
        try
        {
            string? path = PathOf(fileName);
            return path != null && File.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Reads the marker text, or null when absent or unreadable. Never throws.</summary>
    public static string? TryReadText(string fileName)
    {
        try
        {
            string? path = PathOf(fileName);
            return path != null && File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes the marker atomically (temp file flushed to disk, then renamed over the target) so a reader sees either the old or the new
    /// complete file. False on any I/O failure.
    /// </summary>
    public static bool TryWriteText(string fileName, string text)
    {
        string? path = PathOf(fileName);
        if (path == null)
        {
            return false;
        }

        string tmp = path + "." + Environment.ProcessId + ".tmp";
        try
        {
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            TryDelete(tmp);
            return false;
        }
    }

    /// <summary>Deletes the marker; never throws.</summary>
    public static void Delete(string fileName)
    {
        try
        {
            string? path = PathOf(fileName);
            if (path != null)
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Left behind: the next restore finds a restored desktop and removes it.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Nothing more to do.
        }
    }
}
