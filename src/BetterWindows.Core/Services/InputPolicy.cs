using System.Text.RegularExpressions;

namespace BetterWindows.Core.Services;

public enum AppIdKind { Aumid, ExePath, Directory }

/// <summary>
/// The ONE place that decides whether a UI-supplied string is acceptable (docs/IPC.md section 14, AGENTS.md security rules). Pure and Linux-testable.
/// Every controller uses these; nobody invents their own path/URL/id rules. The UI is untrusted: never pass a UI string to ShellExecute/Process.Start.
/// </summary>
public static partial class InputPolicy
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Canonicalises a LOCAL, drive-letter absolute path. Rejects: null/empty, > 32767 chars, control characters, UNC (\\server\share, //), extended-length (\\?\) and
    /// device-namespace (\\.\) prefixes, any ':' beyond the drive colon (alternate data streams), wildcards '*' '?', '&lt;' '&gt;' '|' '"', reserved device names in any segment, and
    /// '..' escaping above the drive root. Does not touch the file system.
    /// </summary>
    public static bool TryNormalizeLocalPath(string? s, out string path)
    {
        path = "";
        if (string.IsNullOrWhiteSpace(s) || s.Length > 32767) return false;
        foreach (var c in s) if (c < 0x20 || c == '*' || c == '?' || c == '<' || c == '>' || c == '|' || c == '"') return false;
        var t = s.Replace('/', '\\');
        if (t.StartsWith(@"\\", StringComparison.Ordinal)) return false; // UNC, \\?\, \\.\
        if (t.Length < 3 || !char.IsAsciiLetter(t[0]) || t[1] != ':' || t[2] != '\\') return false;
        if (t.IndexOf(':', 2) >= 0) return false;
        // collapse . and .. segments without touching the disk (Path.GetFullPath is platform dependent)
        var parts = new List<string>();
        foreach (var seg in t[3..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (seg == ".") continue;
            if (seg == "..") { if (parts.Count == 0) return false; parts.RemoveAt(parts.Count - 1); continue; }
            var stem = seg.TrimEnd(' ', '.');
            if (stem.Length == 0) return false;
            var dot = stem.IndexOf('.');
            var baseName = dot >= 0 ? stem[..dot] : stem;
            if (ReservedNames.Contains(baseName)) return false;
            parts.Add(seg);
        }
        path = char.ToUpperInvariant(t[0]) + @":\" + string.Join('\\', parts);
        return true;
    }

    /// <summary>http(s) URL: non-empty host, no user info, at most 2048 chars, no control characters.</summary>
    public static bool IsSafeWebUrl(string? s, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrEmpty(s) || s.Length > 2048) return false;
        foreach (var c in s) if (c < 0x20 || c == 0x7f) return false;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var u)) return false;
        if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return false;
        if (string.IsNullOrEmpty(u.Host) || !string.IsNullOrEmpty(u.UserInfo)) return false;
        uri = u;
        return true;
    }

    [GeneratedRegex(@"^ms-settings:[a-z0-9-]+([?#][!-~]*)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex MsSettingsRegex();

    public static bool IsMsSettingsUri(string? s) => s is { Length: <= 512 } && MsSettingsRegex().IsMatch(s);

    /// <summary>"aumid:&lt;AUMID&gt;" | "exe:&lt;path&gt;" | "dir:&lt;path&gt;" (paths are normalised and lower-cased by the caller where ids are built).</summary>
    public static bool TryParseAppId(string? s, out AppIdKind kind, out string value)
    {
        kind = default; value = "";
        if (string.IsNullOrEmpty(s) || s.Length > 4200) return false;
        foreach (var c in s) if (c < 0x20) return false;
        if (s.StartsWith("aumid:", StringComparison.Ordinal) && s.Length > 6) { kind = AppIdKind.Aumid; value = s[6..]; return true; }
        if (s.StartsWith("exe:", StringComparison.Ordinal) && TryNormalizeLocalPath(s[4..], out var e)) { kind = AppIdKind.ExePath; value = e; return true; }
        if (s.StartsWith("dir:", StringComparison.Ordinal) && TryNormalizeLocalPath(s[4..], out var d)) { kind = AppIdKind.Directory; value = d; return true; }
        return false;
    }

    /// <summary>Cuts to <paramref name="max"/> chars and removes control characters (log-injection safe).</summary>
    public static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new System.Text.StringBuilder(Math.Min(s.Length, max));
        foreach (var c in s)
        {
            if (sb.Length >= max) break;
            sb.Append(c < 0x20 || c == 0x7f ? ' ' : c);
        }
        return sb.ToString();
    }

    /// <summary>Finite and within [min, max], else <paramref name="fallback"/> (NaN/Infinity/out-of-range never pass through).</summary>
    public static double FiniteOr(double v, double min, double max, double fallback) => double.IsFinite(v) && v >= min && v <= max ? v : fallback;
}
