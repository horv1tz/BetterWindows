using System.Text;
using System.Text.RegularExpressions;

namespace BetterWindows.Native;

/// <summary>
/// Pure (OS independent) rules for app identifiers and Windows paths used by the apps stream: how an AUMID or an exe path becomes an
/// <c>AppEntry.Id</c>, how paths are normalised so that "exe:&lt;lowercased path&gt;" ids match the paths the window layer reports, and which
/// AUMIDs may be handed to the shell. Kept free of System.IO so it behaves identically on the Linux test runner.
/// </summary>
internal static partial class AppIdentity
{
    public const string AumidPrefix = "aumid:";
    public const string ExePrefix = "exe:";
    /// <summary>Real AUMIDs are short, but classic apps get shell-synthesised ones ("{GUID}\Folder\app.exe") that can be long.</summary>
    public const int MaxAumidLength = 512;
    public const int MaxPathLength = 32767;
    /// <summary>Prefix that turns an AUMID into a shell parsing name inside the Apps folder.</summary>
    public const string AppsFolderPrefix = @"shell:AppsFolder\";

    /// <summary>"aumid:&lt;AUMID&gt;" (case of the AUMID is preserved).</summary>
    public static string IdFromAumid(string aumid) => AumidPrefix + aumid.Trim();

    /// <summary>"exe:&lt;lowercased full path&gt;" for an already normalised path.</summary>
    public static string IdFromExePath(string normalizedPath) => ExePrefix + normalizedPath.ToLowerInvariant();

    /// <summary>Parsing name for ShellItem APIs / ShellExecute: <c>shell:AppsFolder\&lt;AUMID&gt;</c>.</summary>
    public static string AppsFolderParsingName(string aumid) => AppsFolderPrefix + aumid.Trim();

    /// <summary>True for MSIX/UWP style AUMIDs "&lt;PackageFamilyName&gt;!&lt;AppId&gt;".</summary>
    public static bool IsPackagedAumid(string aumid) => aumid.Contains('!');

    /// <summary>
    /// Extracts the AUMID from an Apps-folder parsing name ("shell:AppsFolder\X" -> "X"); null for anything else
    /// (file-system paths, other shell namespaces).
    /// </summary>
    public static string? AumidFromAppsFolderParsingName(string? parsingName)
    {
        if (string.IsNullOrWhiteSpace(parsingName)) return null;
        var s = parsingName.Trim();
        if (!s.StartsWith(AppsFolderPrefix, StringComparison.OrdinalIgnoreCase)) return null;
        var rest = s[AppsFolderPrefix.Length..];
        return IsValidAumid(rest) ? rest : null;
    }

    /// <summary>
    /// Whether <paramref name="aumid"/> may be appended to "shell:AppsFolder\". Rejects anything that could make the shell parse something other than an Apps-folder child:
    /// control characters, wildcards/quotes/pipes, drive or UNC rooted strings, leading separators, ".." segments and other shell namespaces ("shell:", "::{CLSID}").
    /// </summary>
    public static bool IsValidAumid(string? aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid)) return false;
        var s = aumid.Trim();
        if (s.Length > MaxAumidLength) return false;
        foreach (var ch in s)
        {
            if (ch < 0x20 || ch == 0x7f) return false;
            if (ch is '"' or '<' or '>' or '|' or '*' or '?') return false;
        }
        if (s[0] is '\\' or '/') return false;
        if (s.StartsWith("::", StringComparison.Ordinal)) return false;
        if (s.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return false;
        if (DriveRooted().IsMatch(s)) return false;
        foreach (var seg in s.Split('\\', '/'))
            if (seg == "..") return false;
        return true;
    }

    /// <summary>
    /// Normalises an absolute Windows path: expands %VARS% (callers pass the expanded string on Windows; unknown variables stay literal), trims whitespace and quotes,
    /// converts '/' to '\', collapses duplicate separators, resolves "." and ".." segments, removes a trailing separator (except for a drive root "C:\").
    /// Returns null for relative paths, UNC/device paths ("\\server\share", "\\?\") and over-long input. Purely string based so it is testable on any OS.
    /// </summary>
    public static string? NormalizeWindowsPath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim().Trim('"').Trim();
        if (s.Length < 3 || s.Length > MaxPathLength) return null;
        foreach (var ch in s)
            if (ch < 0x20) return null;
        s = s.Replace('/', '\\');
        if (!DriveRooted().IsMatch(s)) return null;

        var drive = char.ToUpperInvariant(s[0]);
        var segments = new List<string>();
        foreach (var seg in s[2..].Split('\\'))
        {
            if (seg.Length == 0 || seg == ".") continue;
            if (seg == "..")
            {
                if (segments.Count > 0) segments.RemoveAt(segments.Count - 1);
                continue;
            }
            // Windows silently drops trailing dots/spaces of a segment; do the same so ids are stable.
            var cleaned = seg.TrimEnd(' ', '.');
            if (cleaned.Length == 0) return null;
            segments.Add(cleaned);
        }
        var sb = new StringBuilder();
        sb.Append(drive).Append(":\\");
        sb.Append(string.Join('\\', segments));
        return sb.ToString();
    }

    /// <summary>File name (last segment) of a normalised or raw Windows path, independent of the host OS' separators.</summary>
    public static string FileNameOf(string path)
    {
        var i = path.LastIndexOfAny(['\\', '/']);
        return i < 0 ? path : path[(i + 1)..];
    }

    /// <summary>Extension including the dot, lower-cased; empty when none. Host-OS independent.</summary>
    public static string ExtensionOf(string path)
    {
        var name = FileNameOf(path);
        var i = name.LastIndexOf('.');
        return i <= 0 || i == name.Length - 1 ? "" : name[i..].ToLowerInvariant();
    }

    /// <summary>Directory part of a Windows path ("C:\a\b.exe" -> "C:\a"); null for roots.</summary>
    public static string? DirectoryOf(string path)
    {
        var i = path.LastIndexOf('\\');
        if (i < 0) return null;
        if (i == 2 && path.Length > 3) return path[..3]; // "C:\x.exe" -> "C:\"
        return i <= 2 ? null : path[..i];
    }

    [GeneratedRegex(@"^[A-Za-z]:[\\/]")]
    private static partial Regex DriveRooted();
}
