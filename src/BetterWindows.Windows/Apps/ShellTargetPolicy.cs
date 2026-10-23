using System.Text.RegularExpressions;

namespace BetterWindows.Native;

/// <summary>What <see cref="ShellTargetPolicy.ClassifyOpenTarget"/> decided about a string handed to <c>IAppLauncher.Open</c>.</summary>
internal enum OpenTargetKind
{
    /// <summary>Not allowed; nothing may be executed.</summary>
    Rejected,
    /// <summary>http / https URL (normalised, canonical).</summary>
    Url,
    /// <summary>ms-settings: URI.</summary>
    SettingsUri,
    /// <summary>One of the two allow-listed shell namespace literals.</summary>
    ShellSpecial,
    /// <summary>Existing absolute local file.</summary>
    File,
    /// <summary>Existing absolute local folder.</summary>
    Folder,
}

/// <summary>
/// Defence-in-depth validation for everything the launcher passes to ShellExecute. Core validates call payloads too, but the UI is untrusted and the launcher is the
/// last line before the shell: only http(s) URLs, ms-settings: URIs, two shell-special literals and EXISTING ABSOLUTE LOCAL files/folders get through. Pure and testable:
/// file-system probes are injectable.
/// </summary>
internal static partial class ShellTargetPolicy
{
    public const string ThisPc = "shell:MyComputerFolder";
    public const string RecycleBin = "shell:RecycleBinFolder";
    public const int MaxTargetLength = 2048;
    public const int MaxArgumentsLength = 2048;

    /// <summary>
    /// Classifies and canonicalises an Open() target. <paramref name="canonical"/> is what must be handed to the shell (the URI's canonical form, the normalised path).
    /// </summary>
    public static OpenTargetKind ClassifyOpenTarget(string? target, out string canonical, Func<string, bool>? fileExists = null, Func<string, bool>? directoryExists = null)
    {
        canonical = "";
        if (string.IsNullOrWhiteSpace(target)) return OpenTargetKind.Rejected;
        var s = target.Trim();
        if (s.Length > MaxTargetLength || HasControlChars(s)) return OpenTargetKind.Rejected;

        // The two shell literals are matched EXACTLY (case-sensitive): not "any shell: URI".
        if (string.Equals(s, ThisPc, StringComparison.Ordinal) || string.Equals(s, RecycleBin, StringComparison.Ordinal))
        {
            canonical = s;
            return OpenTargetKind.ShellSpecial;
        }

        if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(s, UriKind.Absolute, out var uri)) return OpenTargetKind.Rejected;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return OpenTargetKind.Rejected;
            if (string.IsNullOrEmpty(uri.Host)) return OpenTargetKind.Rejected;
            canonical = uri.AbsoluteUri; // escaped, canonical form: nothing the shell could re-interpret
            return OpenTargetKind.Url;
        }

        if (s.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
        {
            if (!SettingsUri().IsMatch(s)) return OpenTargetKind.Rejected;
            canonical = s;
            return OpenTargetKind.SettingsUri;
        }

        // Anything else must be an existing absolute local path (no UNC, no device paths, no relative paths, no other URI scheme: "C:" drive paths only).
        var path = AppIdentity.NormalizeWindowsPath(s);
        if (path == null) return OpenTargetKind.Rejected;
        fileExists ??= File.Exists;
        directoryExists ??= Directory.Exists;
        if (fileExists(path)) { canonical = path; return OpenTargetKind.File; }
        if (directoryExists(path)) { canonical = path; return OpenTargetKind.Folder; }
        return OpenTargetKind.Rejected;
    }

    /// <summary>
    /// Parsing name (::{CLSID}) of the two allow-listed shell literals, for APIs that take parsing names (icons): the "shell:" moniker is understood by ShellExecute,
    /// not by SHCreateItemFromParsingName. Null for anything else.
    /// </summary>
    public static string? SpecialParsingName(string? target)
    {
        var s = target?.Trim();
        if (string.Equals(s, ThisPc, StringComparison.Ordinal)) return "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
        if (string.Equals(s, RecycleBin, StringComparison.Ordinal)) return "::{645FF040-5081-101B-9F08-00AA002F954E}";
        return null;
    }

    /// <summary>Arguments for an app launch: bounded, single line, no control characters.</summary>
    public static bool AreArgumentsSafe(string? arguments)
    {
        if (arguments == null || arguments.Length == 0) return true;
        return arguments.Length <= MaxArgumentsLength && !HasControlChars(arguments);
    }

    private static bool HasControlChars(string s)
    {
        foreach (var ch in s)
            if (ch < 0x20 || ch == 0x7f) return true;
        return false;
    }

    // "ms-settings:" followed by the page name and optional query: letters, digits and the punctuation real URIs use. No spaces, quotes, backslashes or shell metacharacters.
    [GeneratedRegex(@"^ms-settings:[A-Za-z0-9\-_.~:/?#&=%+@]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SettingsUri();
}
