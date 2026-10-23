namespace BetterWindows.Host.Lifecycle;

/// <summary>The Host's side effects, one per step of the ordered shutdown (docs/IPC.md section 13, rule 7). Each is supplied by the composition root.</summary>
public sealed record ShutdownActions(
    Action StopSurfaces,
    Action StopControllers,
    Action RestoreTaskbar,
    Action RestoreDesktopIcons,
    Action DisposeTray,
    Action DisposeWebView,
    Action MarkCleanExit,
    Action ReleaseInstance,
    Action Relaunch,
    Action ExitApplication);

/// <summary>
/// The fixed order of the Host's shutdown: surfaces first (their windows own AppBar reservations and WebViews), then the Core controllers, then the restores of
/// the user's shell (taskbar, desktop icons), then the rest. The restores come before the process exits, so a crash later in the sequence still leaves the shell
/// restored. Relaunch (restart only) runs after the single-instance mutex is released, so the new process does not see this one.
/// </summary>
public static class ShutdownPlan
{
    public const string Surfaces = "surfaces";
    public const string Controllers = "controllers";
    public const string Taskbar = "taskbar";
    public const string DesktopIcons = "desktop icons";
    public const string Tray = "tray";
    public const string WebView = "webview";
    public const string StartupState = "startup state";
    public const string SingleInstance = "single instance";
    public const string Relaunch = "relaunch";
    public const string Application = "application";

    /// <summary>The steps in the order they run.</summary>
    public static IReadOnlyList<ShutdownStep> Build(ShutdownActions a, bool relaunch)
    {
        var steps = new List<ShutdownStep>(10)
        {
            new(Surfaces, a.StopSurfaces),
            new(Controllers, a.StopControllers),
            new(Taskbar, a.RestoreTaskbar),
            new(DesktopIcons, a.RestoreDesktopIcons),
            new(Tray, a.DisposeTray),
            new(WebView, a.DisposeWebView),
            new(StartupState, a.MarkCleanExit),
            new(SingleInstance, a.ReleaseInstance),
        };
        if (relaunch) steps.Add(new(Relaunch, a.Relaunch));
        steps.Add(new(Application, a.ExitApplication));
        return steps;
    }
}
