using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Input;

/// <summary>The global hotkeys BetterWindows registers. The ids are what <see cref="Services.IHotkeyStatus"/> reports (exit / toggleTaskbar are the escape hatches).</summary>
public static class HotkeyIds
{
    public const string Spotlight = "spotlight";
    public const string Launchpad = "launchpad";
    public const string MissionControl = "missionControl";
    public const string Exit = "exit";
    public const string ToggleTaskbar = "toggleTaskbar";

    /// <summary>Registration priority: the escape hatches first, so a duplicate gesture never steals the way back to the taskbar.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Exit, ToggleTaskbar, Spotlight, Launchpad, MissionControl };
}

/// <summary>Maps the settings to (hotkey id, gesture string) pairs. An empty string means "unassigned".</summary>
public static class HotkeyBindings
{
    public static IReadOnlyList<KeyValuePair<string, string>> From(Settings s) => new[]
    {
        new KeyValuePair<string, string>(HotkeyIds.Exit, s.Hotkeys.Exit ?? ""),
        new KeyValuePair<string, string>(HotkeyIds.ToggleTaskbar, s.Hotkeys.ToggleTaskbar ?? ""),
        new KeyValuePair<string, string>(HotkeyIds.Spotlight, s.Spotlight.Hotkey ?? ""),
        new KeyValuePair<string, string>(HotkeyIds.Launchpad, s.Hotkeys.Launchpad ?? ""),
        new KeyValuePair<string, string>(HotkeyIds.MissionControl, s.Hotkeys.MissionControl ?? ""),
    };
}
