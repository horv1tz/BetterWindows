using BetterWindows.Host.Cli;

namespace BetterWindows.Host.Lifecycle;

/// <summary>
/// The resolved, immutable runtime policy of this process: command line + environment + crash-loop detection. Registered in DI so the surfaces stream and the
/// platform decorators read ONE source of truth (the "safe-mode flag service" of docs/IPC.md section 15).
/// </summary>
public sealed class HostRuntimeOptions
{
    /// <summary>Environment variable that supplies the dev-server origin when <c>--ui-url</c> is absent.</summary>
    public const string UiUrlEnvVar = "BETTERWINDOWS_UI_URL";

    public HostRuntimeOptions(CommandLineOptions cli, bool crashLoopSafeMode = false, string? uiOriginFromEnvironment = null, bool debugBuild = false)
    {
        Cli = cli;
        Smoke = cli.Mode == LaunchMode.Smoke;
        SafeMode = cli.SafeMode || crashLoopSafeMode;
        SafeModeReason = crashLoopSafeMode ? "repeated unclean exits" : cli.SafeMode ? "--safe-mode" : null;
        NoHideTaskbar = cli.NoHideTaskbar || Smoke || SafeMode;
        DevTools = cli.DevTools || debugBuild;
        Autostart = cli.Autostart;
        UiOrigin = cli.UiOrigin ?? (CommandLineParser.TryParseOrigin(uiOriginFromEnvironment, out var env) ? env : null);
        ReadyTimeout = Smoke ? TimeSpan.FromSeconds(40) : TimeSpan.FromSeconds(10);
    }

    /// <summary>The parsed command line this policy was derived from.</summary>
    public CommandLineOptions Cli { get; }
    /// <summary>CI smoke mode: surfaces stay hidden, nothing on the user's desktop is hidden.</summary>
    public bool Smoke { get; }
    /// <summary>Explicit <c>--safe-mode</c> or three unclean starts in 10 minutes.</summary>
    public bool SafeMode { get; }
    /// <summary>Why safe mode is on (null when it is not).</summary>
    public string? SafeModeReason { get; }
    /// <summary>True when the native taskbar must stay visible (--no-hide-taskbar, safe mode, smoke).</summary>
    public bool NoHideTaskbar { get; }
    /// <summary>The surface windows must be created but not shown (smoke).</summary>
    public bool HideSurfaces => Smoke;
    /// <summary>No desktop layer in safe mode.</summary>
    public bool DesktopLayerDisabled => SafeMode;
    /// <summary>No global hotkeys in safe mode.</summary>
    public bool HotkeysDisabled => SafeMode;
    /// <summary>Started from the Run key: Explorer may not be up yet.</summary>
    public bool Autostart { get; }
    /// <summary>WebView2 DevTools are allowed (flag or debug build).</summary>
    public bool DevTools { get; }
    /// <summary>Dev-server origin; null = serve the bundled UI from <c>wwwroot</c>.</summary>
    public string? UiOrigin { get; }
    /// <summary>How long a freshly navigated surface may take to complete <c>surface.ready</c> before it is reported as failed (docs/IPC.md section 16).</summary>
    public TimeSpan ReadyTimeout { get; }
}
