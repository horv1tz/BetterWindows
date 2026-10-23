using BetterWindows.Core.Services;

namespace BetterWindows.Host.Cli;

/// <summary>What this process was started to do.</summary>
public enum LaunchMode
{
    /// <summary>The normal shell (single instance per Windows session).</summary>
    Run,
    /// <summary><c>--restore-taskbar</c>: emergency restore, no UI, then exit.</summary>
    RestoreTaskbar,
    /// <summary><c>--diagnose [--json]</c>: read-only platform self-check printed to stdout.</summary>
    Diagnose,
    /// <summary><c>--smoke</c>: CI mode (hidden surfaces, real bridge, never touches the taskbar).</summary>
    Smoke,
    /// <summary><c>--guardian &lt;ownerPid&gt;</c> (legacy alias <c>--watchdog</c>): waits for the owner and restores the shell.</summary>
    Guardian,
}

/// <summary>
/// Parsed command line (docs/IPC.md section 15). Immutable; produced by <see cref="CommandLineParser"/>. Invalid values never throw: they land in
/// <see cref="Errors"/> and the corresponding option keeps its default, unknown arguments land in <see cref="Unknown"/> and are ignored.
/// </summary>
public sealed record CommandLineOptions
{
    public LaunchMode Mode { get; init; } = LaunchMode.Run;
    /// <summary><c>--json</c>: machine-readable <c>--diagnose</c> output.</summary>
    public bool Json { get; init; }
    /// <summary><c>--no-hide-taskbar</c>: the native taskbar is never hidden or auto-hidden.</summary>
    public bool NoHideTaskbar { get; init; }
    /// <summary><c>--safe-mode</c>: no taskbar hiding, no desktop layer, no hotkeys.</summary>
    public bool SafeMode { get; init; }
    /// <summary><c>--devtools</c>: WebView2 DevTools allowed.</summary>
    public bool DevTools { get; init; }
    /// <summary><c>--ui-url</c>: the dev-server ORIGIN (scheme://host[:port], no path) the surfaces navigate to instead of the bundled UI.</summary>
    public string? UiOrigin { get; init; }
    /// <summary><c>--autostart</c>: started by the Run key at logon: wait for Explorer's taskbar first.</summary>
    public bool Autostart { get; init; }
    /// <summary><c>--reset-settings</c>: move config.json (and its backup) aside before settings load.</summary>
    public bool ResetSettings { get; init; }
    /// <summary><c>--data-dir</c>: absolute local directory that replaces %LOCALAPPDATA%\BetterWindows (mapped to BETTERWINDOWS_DATA_DIR).</summary>
    public string? DataDir { get; init; }
    /// <summary><c>--guardian</c>/<c>--watchdog</c> value: the pid of the process whose shell state must be restored when it dies.</summary>
    public int GuardianOwnerPid { get; init; }
    /// <summary><c>--out</c>: additionally write the <c>--diagnose</c>/<c>--smoke</c> report to this file.</summary>
    public string? OutputFile { get; init; }
    /// <summary>Problems with recognised options (missing/invalid values, conflicting modes).</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    /// <summary>Arguments that were not recognised (ignored).</summary>
    public IReadOnlyList<string> Unknown { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Pure, side-effect free command-line parser. Options are case-insensitive, accept <c>--name value</c> and <c>--name=value</c>.
/// The first mode flag wins; a second, different mode is reported in <see cref="CommandLineOptions.Errors"/>.
/// </summary>
public static class CommandLineParser
{
    public static CommandLineOptions Parse(IEnumerable<string> args)
    {
        var o = new CommandLineOptions();
        var errors = new List<string>();
        var unknown = new List<string>();
        var list = args.ToList();
        var modeSet = false;

        void SetMode(LaunchMode mode, string flag)
        {
            if (!modeSet) { o = o with { Mode = mode }; modeSet = true; }
            else if (o.Mode != mode) errors.Add($"{flag}: conflicting mode (already {o.Mode})");
        }

        for (var i = 0; i < list.Count; i++)
        {
            var raw = list[i];
            if (raw is null || raw.Length < 2 || !raw.StartsWith("--", StringComparison.Ordinal))
            {
                unknown.Add(raw ?? "");
                continue;
            }
            string name = raw, inline = null!;
            var hasInline = false;
            var eq = raw.IndexOf('=');
            if (eq > 2) { name = raw[..eq]; inline = raw[(eq + 1)..]; hasInline = true; }
            name = name.ToLowerInvariant();

            // Fetches the option's value: "--x=v" or the next argument (which must not itself look like an option).
            bool TryValue(out string value)
            {
                if (hasInline) { value = inline; return true; }
                if (i + 1 < list.Count && !(list[i + 1] ?? "").StartsWith("--", StringComparison.Ordinal)) { value = list[++i]; return true; }
                value = "";
                errors.Add($"{name}: missing value");
                return false;
            }

            switch (name)
            {
                case "--restore-taskbar": SetMode(LaunchMode.RestoreTaskbar, name); break;
                case "--diagnose": SetMode(LaunchMode.Diagnose, name); break;
                case "--smoke": SetMode(LaunchMode.Smoke, name); break;
                case "--guardian":
                case "--watchdog":
                    if (TryValue(out var pidText))
                    {
                        if (int.TryParse(pidText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var pid) && pid > 0)
                        {
                            SetMode(LaunchMode.Guardian, name);
                            o = o with { GuardianOwnerPid = pid };
                        }
                        else errors.Add($"{name}: '{Clip(pidText)}' is not a process id");
                    }
                    break;
                case "--json": o = o with { Json = true }; break;
                case "--no-hide-taskbar": o = o with { NoHideTaskbar = true }; break;
                case "--safe-mode": o = o with { SafeMode = true }; break;
                case "--devtools": o = o with { DevTools = true }; break;
                case "--autostart": o = o with { Autostart = true }; break;
                case "--reset-settings": o = o with { ResetSettings = true }; break;
                case "--ui-url":
                    if (TryValue(out var url))
                    {
                        if (TryParseOrigin(url, out var origin)) o = o with { UiOrigin = origin };
                        else errors.Add($"{name}: '{Clip(url)}' is not an http(s) URL");
                    }
                    break;
                case "--data-dir":
                    if (TryValue(out var dir))
                    {
                        if (InputPolicy.TryNormalizeLocalPath(dir, out var normalized)) o = o with { DataDir = normalized };
                        else errors.Add($"{name}: '{Clip(dir)}' is not an absolute local path");
                    }
                    break;
                case "--out":
                    if (TryValue(out var outFile))
                    {
                        if (InputPolicy.TryNormalizeLocalPath(outFile, out var normalized)) o = o with { OutputFile = normalized };
                        else errors.Add($"{name}: '{Clip(outFile)}' is not an absolute local path");
                    }
                    break;
                default:
                    unknown.Add(raw);
                    break;
            }
        }
        return o with { Errors = errors, Unknown = unknown };
    }

    /// <summary>Reduces an http(s) URL to its origin (<c>scheme://host[:port]</c>). Rejects user info, other schemes and empty hosts.</summary>
    public static bool TryParseOrigin(string? url, out string origin)
    {
        origin = "";
        if (string.IsNullOrWhiteSpace(url) || url.Length > 2048) return false;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u)) return false;
        if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return false;
        if (string.IsNullOrEmpty(u.Host) || !string.IsNullOrEmpty(u.UserInfo)) return false;
        origin = u.GetLeftPart(UriPartial.Authority);
        return true;
    }

    private static string Clip(string s) => InputPolicy.Truncate(s, 80);
}
