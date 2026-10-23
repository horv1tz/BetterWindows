using BetterWindows.Core.Services;
using BetterWindows.Host.Cli;

namespace BetterWindows.Host.Lifecycle;

/// <summary>
/// Process-wide environment that must be in place before ANY other code runs: the data directory override (read by <see cref="AppPaths"/> AND by the static
/// restorers in the Windows layer) and the WebView2 transparent default background (must exist before the first environment is created).
/// </summary>
public static class ProcessEnvironment
{
    public const string WebView2BackgroundVar = "WEBVIEW2_DEFAULT_BACKGROUND_COLOR";
    /// <summary>ARGB with alpha 0: the WebView2 control is fully transparent until the page paints (no white flash).</summary>
    public const string TransparentBackground = "00FFFFFF";

    /// <summary>Idempotent. Never throws.</summary>
    public static void Apply(CommandLineOptions options)
    {
        try
        {
            if (options.DataDir is { Length: > 0 } dir) Environment.SetEnvironmentVariable(AppPaths.EnvVar, dir);
            Environment.SetEnvironmentVariable(WebView2BackgroundVar, TransparentBackground);
        }
        catch (Exception) { /* environment block is process-local; failure is not actionable here */ }
    }
}
