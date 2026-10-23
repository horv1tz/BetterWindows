namespace BetterWindows.Core.Desktop;

/// <summary>
/// Existence checks for paths the user picked (shortcut targets, wallpaper files). A seam, because Core runs on Linux in tests where "C:\Users\me\a.png" can never
/// exist; the default implementation is plain System.IO and is used when nothing else is registered.
/// </summary>
public interface IPathProbe
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
}

/// <summary>System.IO implementation; never throws (an inaccessible path simply does not exist).</summary>
public sealed class SystemPathProbe : IPathProbe
{
    public bool FileExists(string path) { try { return File.Exists(path); } catch (Exception) { return false; } }
    public bool DirectoryExists(string path) { try { return Directory.Exists(path); } catch (Exception) { return false; } }
}

/// <summary>Validation shared by the desktop calls for UI-supplied paths.</summary>
public static class DesktopPathRules
{
    public const int MaxPathChars = 4096;
    public static readonly IReadOnlySet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".gif" };
    /// <summary>Extensions that start a program: launched through <c>IAppLauncher.Launch</c>; everything else goes to <c>IAppLauncher.Open</c>.</summary>
    public static readonly IReadOnlySet<string> LaunchableExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".exe", ".lnk" };

    public static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path));
    public static bool IsLaunchable(string path) => LaunchableExtensions.Contains(Path.GetExtension(path));

    /// <summary>Display name of a shortcut target: the file name without the extension for launchers, else the full file/folder name; drive roots keep "C:\".</summary>
    public static string DisplayName(string normalizedPath)
    {
        var trimmed = normalizedPath.TrimEnd('\\');
        var slash = trimmed.LastIndexOf('\\');
        if (slash < 0) return normalizedPath; // "C:" (drive root): keep "C:\"
        var name = trimmed[(slash + 1)..];
        var ext = Path.GetExtension(name);
        if (ext.Length > 0 && (ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) || ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) || ext.Equals(".url", StringComparison.OrdinalIgnoreCase)))
            name = name[..^ext.Length];
        return name.Length == 0 ? normalizedPath : name;
    }
}
