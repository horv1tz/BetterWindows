namespace BetterWindows.Core.Overlay;

/// <summary>
/// What a global hotkey or hot corner can make the shell do. A closed set of enum values: ShellActions never turns a UI- or settings-supplied string into behaviour.
/// </summary>
public enum ShellAction
{
    Spotlight,
    Launchpad,
    MissionControl,
    ShowDesktop,
    LockScreen,
    Sleep,
}
