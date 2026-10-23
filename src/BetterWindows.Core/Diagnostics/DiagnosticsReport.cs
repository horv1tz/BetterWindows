using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;

namespace BetterWindows.Core.Diagnostics;

/// <summary>A read-only snapshot of everything diag.report explains. Collected by SystemController; nothing here changes system state.</summary>
public sealed record DiagInputs(
    string Version,
    string Os,
    string WebView2Version,
    string ConfigFile,
    bool ConfigFileExists,
    bool PersistenceBlocked,
    string? PersistenceBlockedReason,
    string LastLoad,
    string DataRoot,
    bool DataRootExists,
    IReadOnlyList<HotkeyRegistration> Hotkeys,
    bool EscapeHotkeyRegistered,
    TaskbarMode RequestedTaskbar,
    TaskbarMode AppliedTaskbar,
    string TaskbarReason,
    bool AutostartWanted,
    bool AutostartEnabled,
    int MonitorCount,
    bool DockReady,
    bool MenubarReady,
    IReadOnlyList<string> EnabledFeatures);

/// <summary>Turns <see cref="DiagInputs"/> into the protocol's <see cref="DiagReport"/>. Pure: every check is a plain statement about the collected state.</summary>
public static class DiagnosticsReport
{
    public static DiagReport Build(DiagInputs i)
    {
        var checks = new List<DiagCheck>
        {
            Check("host.version", true, $"{i.Version}"),
            Check("host.os", true, i.Os),
            Check("webview2.runtime", !string.IsNullOrWhiteSpace(i.WebView2Version), string.IsNullOrWhiteSpace(i.WebView2Version) ? "WebView2 runtime version unknown" : i.WebView2Version),
            Check("config.persistence", !i.PersistenceBlocked, i.PersistenceBlocked ? $"saving is suspended: {i.PersistenceBlockedReason ?? "unknown reason"} ({i.ConfigFile})" : $"{i.ConfigFile} ({(i.ConfigFileExists ? "present" : "not yet written")}); last load: {i.LastLoad}"),
            Check("data.root", i.DataRootExists, i.DataRootExists ? i.DataRoot : $"{i.DataRoot} does not exist yet"),
            Check("surface.dock", i.DockReady, i.DockReady ? "running" : "not running"),
            Check("surface.menubar", i.MenubarReady, i.MenubarReady ? "running" : "not running"),
            Check("monitors", i.MonitorCount > 0, $"{i.MonitorCount} monitor(s)"),
            Check("features", true, i.EnabledFeatures.Count == 0 ? "none enabled" : string.Join(", ", i.EnabledFeatures)),
            Check("hotkeys.escape", i.EscapeHotkeyRegistered, i.EscapeHotkeyRegistered ? "exit or toggle-taskbar hotkey is registered" : "neither exit nor toggle-taskbar is registered: the taskbar cannot be hidden"),
            Check("taskbar", i.RequestedTaskbar == TaskbarMode.Visible || i.AppliedTaskbar == i.RequestedTaskbar,
                $"requested {i.RequestedTaskbar.ToString().ToLowerInvariant()}, applied {i.AppliedTaskbar.ToString().ToLowerInvariant()}: {i.TaskbarReason}"),
            Check("autostart", i.AutostartWanted == i.AutostartEnabled,
                i.AutostartWanted == i.AutostartEnabled ? (i.AutostartEnabled ? "enabled" : "disabled") : $"setting is {(i.AutostartWanted ? "on" : "off")} but the Run entry is {(i.AutostartEnabled ? "on" : "off")}"),
        };
        foreach (var h in i.Hotkeys)
        {
            var detail = h.Registered ? h.Gesture : $"{h.Gesture} not registered" + (string.IsNullOrEmpty(h.Error) ? "" : $": {h.Error}");
            checks.Add(Check($"hotkey.{h.Id}", h.Registered, detail));
        }
        return new DiagReport { Version = i.Version, Os = i.Os, Checks = checks };
    }

    private static DiagCheck Check(string name, bool ok, string detail) => new() { Name = name, Ok = ok, Detail = detail };
}
