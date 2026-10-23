using System.Text;

namespace BetterWindows.Core.WindowTracking;

/// <summary>
/// Path and display-name helpers for apps that are not (yet) in the catalog. Pure and platform independent: Windows paths are parsed by hand because
/// <c>System.IO.Path</c> follows the host OS (on Linux a backslash is not a separator).
/// </summary>
public static class AppNames
{
    /// <summary>
    /// Trims whitespace and quotes, converts '/' to '\', removes the extended-length prefix (\\?\C:\...), and returns null for empty input.
    /// The result keeps its case (display names depend on it); lower-case it for ids and comparisons.
    /// </summary>
    public static string? CleanPath(string? path)
    {
        if (path is null) return null;
        var p = path.Trim().Trim('"', '\'').Trim();
        if (p.Length == 0) return null;
        p = p.Replace('/', '\\');
        if (p.StartsWith(@"\\?\", StringComparison.Ordinal) && p.Length > 6 && char.IsAsciiLetter(p[4]) && p[5] == ':') p = p[4..];
        return p;
    }

    /// <summary>The lower-cased, cleaned path used inside ids ("exe:&lt;this&gt;") and for comparisons; null for empty input.</summary>
    public static string? PathKey(string? path) => CleanPath(path)?.ToLowerInvariant();

    /// <summary>Last path segment (Windows separators), without trailing separators.</summary>
    public static string FileName(string path)
    {
        var p = path.TrimEnd('\\', '/');
        var i = p.LastIndexOfAny(new[] { '\\', '/' });
        return i >= 0 ? p[(i + 1)..] : p;
    }

    private static readonly string[] StrippedExtensions = { ".exe", ".lnk", ".com", ".bat", ".cmd", ".scr" };

    public static string StripExtension(string fileName)
    {
        foreach (var ext in StrippedExtensions)
            if (fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase) && fileName.Length > ext.Length) return fileName[..^ext.Length];
        return fileName;
    }

    /// <summary>True when the file name of the path equals <paramref name="fileName"/> (case-insensitive).</summary>
    public static bool IsFile(string? path, string fileName) => path is not null && string.Equals(FileName(CleanPath(path) ?? ""), fileName, StringComparison.OrdinalIgnoreCase);

    // Display names of common executables, used until the catalog knows the app (first seconds after start) or for apps that have no Start menu entry.
    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["explorer"] = "File Explorer", ["msedge"] = "Microsoft Edge", ["chrome"] = "Google Chrome", ["firefox"] = "Firefox", ["notepad"] = "Notepad",
        ["calc"] = "Calculator", ["calculatorapp"] = "Calculator", ["cmd"] = "Command Prompt", ["powershell"] = "Windows PowerShell", ["pwsh"] = "PowerShell",
        ["wt"] = "Windows Terminal", ["mspaint"] = "Paint", ["winword"] = "Microsoft Word", ["excel"] = "Microsoft Excel", ["powerpnt"] = "Microsoft PowerPoint",
        ["outlook"] = "Microsoft Outlook", ["onenote"] = "Microsoft OneNote", ["code"] = "Visual Studio Code", ["devenv"] = "Visual Studio", ["taskmgr"] = "Task Manager",
        ["mmc"] = "Microsoft Management Console", ["regedit"] = "Registry Editor", ["control"] = "Control Panel", ["snippingtool"] = "Snipping Tool",
        ["conhost"] = "Console Window", ["systemsettings"] = "Settings", ["ms-teams"] = "Microsoft Teams", ["teams"] = "Microsoft Teams", ["vlc"] = "VLC media player",
        ["7zfm"] = "7-Zip File Manager", ["winrar"] = "WinRAR", ["steam"] = "Steam", ["spotify"] = "Spotify", ["discord"] = "Discord",
    };

    /// <summary>"C:\Program Files\Foo\Bar_baz.exe" -> "Bar Baz"; well-known executables get their product name.</summary>
    public static string FromExePath(string path)
    {
        var file = FileName(CleanPath(path) ?? path);
        var stem = StripExtension(file);
        if (Known.TryGetValue(stem, out var known)) return known;
        var pretty = Prettify(stem);
        return pretty.Length > 0 ? pretty : file;
    }

    /// <summary>"Microsoft.WindowsCalculator_8wekyb3d8bbwe!App" -> "Windows Calculator"; "Chrome" -> "Chrome".</summary>
    public static string FromAumid(string aumid)
    {
        var s = aumid.Trim();
        var bang = s.IndexOf('!');
        if (bang > 0) s = s[..bang];
        var us = s.IndexOf('_');
        if (us > 0) s = s[..us];
        var dot = s.LastIndexOf('.');
        if (dot >= 0 && dot < s.Length - 1) s = s[(dot + 1)..];
        var pretty = Prettify(s);
        return pretty.Length > 0 ? pretty : aumid.Trim();
    }

    /// <summary>
    /// Splits camelCase / PascalCase and '_' '-' separators into words and capitalises all-lower-case words:
    /// "WindowsTerminal" -> "Windows Terminal", "my_cool-app" -> "My Cool App", "XMLParser" -> "XML Parser", "VSCode" -> "VS Code", "iTunes" stays "iTunes".
    /// </summary>
    public static string Prettify(string stem)
    {
        var sb = new StringBuilder(stem.Length + 4);
        for (var i = 0; i < stem.Length; i++)
        {
            var c = stem[i];
            if (c is '_' or '-') { AppendSpace(sb); continue; }
            if (i > 0 && char.IsUpper(c) && !IsSeparator(stem[i - 1]))
            {
                var prev = stem[i - 1];
                // lower -> Upper boundary, but only after a run of >= 2 lower-case letters ("iTunes" / "eBay" are brand spellings, not words)
                var lowerRun = 0;
                for (var j = i - 1; j >= 0 && char.IsLower(stem[j]); j--) lowerRun++;
                if (char.IsLower(prev) && lowerRun >= 2) AppendSpace(sb);
                // Upper -> Upper+lower boundary: "XMLParser" -> "XML Parser"
                else if (char.IsUpper(prev) && i + 1 < stem.Length && char.IsLower(stem[i + 1])) AppendSpace(sb);
            }
            sb.Append(c);
        }
        var words = sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < words.Length; i++)
        {
            var w = words[i];
            if (w.Length > 0 && char.IsLower(w[0]) && w.All(ch => !char.IsUpper(ch))) words[i] = char.ToUpperInvariant(w[0]) + w[1..];
        }
        return string.Join(' ', words);
    }

    private static bool IsSeparator(char c) => c is '_' or '-' or ' ';

    private static void AppendSpace(StringBuilder sb)
    {
        if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
    }
}
