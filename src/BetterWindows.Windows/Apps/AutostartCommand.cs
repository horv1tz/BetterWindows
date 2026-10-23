namespace BetterWindows.Native;

/// <summary>
/// Pure helpers for the autostart registry value: building the command line we register and recovering the executable from a stored command line.
/// Format: <c>"C:\path\BetterWindows.exe" --autostart</c> (the exe quoted so paths with spaces are safe; the flag tells the process it was started by the system).
/// </summary>
internal static class AutostartCommand
{
    public const string Argument = "--autostart";
    public const string ValueName = "BetterWindows";

    /// <summary>Builds the registry value for <paramref name="exePath"/>.</summary>
    public static string Build(string exePath) => $"\"{exePath}\" {Argument}";

    /// <summary>
    /// Extracts the executable path from a Run-key command line: either a quoted path at the start, or (legacy/unquoted) everything before the first
    /// " --" / " /" switch. Null for empty input.
    /// </summary>
    public static string? ExtractExePath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var s = command.Trim();
        if (s[0] == '"')
        {
            int end = s.IndexOf('"', 1);
            return end > 1 ? s[1..end] : null;
        }
        int cut = IndexOfSwitch(s);
        return (cut < 0 ? s : s[..cut]).Trim();
    }

    private static int IndexOfSwitch(string s)
    {
        for (int i = 1; i + 1 < s.Length; i++)
            if (s[i] == ' ' && (s[i + 1] == '-' || s[i + 1] == '/')) return i;
        return -1;
    }

    /// <summary>Path equality the way Windows compares file names: case-insensitive, '/' == '\', trailing separators ignored.</summary>
    public static bool SamePath(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        static string N(string p) => p.Trim().Replace('/', '\\').TrimEnd('\\');
        return string.Equals(N(a), N(b), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Interprets a StartupApproved\Run value (Task Manager's "Startup" tab / Settings > Apps > Startup toggle). The first byte is a state flag:
    /// even (0x02, 0x06) = enabled, odd (0x03, 0x07 ...) = disabled; the following bytes hold the time it was changed. Missing/short data = enabled
    /// (Explorer treats absence as enabled).
    /// </summary>
    public static bool IsApprovedByUser(byte[]? startupApproved)
        => startupApproved == null || startupApproved.Length == 0 || (startupApproved[0] & 1) == 0;
}
