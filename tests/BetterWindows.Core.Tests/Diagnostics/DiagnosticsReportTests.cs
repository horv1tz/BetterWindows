using BetterWindows.Core.Diagnostics;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using Xunit;

namespace BetterWindows.Core.Tests.Diagnostics;

public class DiagnosticsReportTests
{
    private static DiagInputs Healthy(TaskbarMode requested = TaskbarMode.Visible, TaskbarMode applied = TaskbarMode.Visible, bool autostart = false) => new(
        Version: "1.2.3", Os: "Windows 11", WebView2Version: "120.0", ConfigFile: @"C:\data\config.json", ConfigFileExists: true,
        PersistenceBlocked: false, PersistenceBlockedReason: null, LastLoad: "ok", DataRoot: @"C:\data", DataRootExists: true,
        Hotkeys: new[] { new HotkeyRegistration("exit", "Ctrl+Alt+Q", true, null), new HotkeyRegistration("spotlight", "Alt+Space", false, "taken") },
        EscapeHotkeyRegistered: true, RequestedTaskbar: requested, AppliedTaskbar: applied, TaskbarReason: "applied",
        AutostartWanted: autostart, AutostartEnabled: autostart, MonitorCount: 2, DockReady: true, MenubarReady: true,
        EnabledFeatures: new[] { "liveThumbnails" });

    private static DiagCheck Find(DiagReport r, string name) => r.Checks.Single(c => c.Name == name);

    [Fact]
    public void A_healthy_machine_reports_every_check_ok()
    {
        var r = DiagnosticsReport.Build(Healthy());
        Assert.Equal("1.2.3", r.Version);
        Assert.Equal("Windows 11", r.Os);
        Assert.All(r.Checks.Where(c => c.Name is not ("hotkey.spotlight")), c => Assert.True(c.Ok, $"{c.Name}: {c.Detail}"));
    }

    [Fact]
    public void Each_hotkey_is_listed_and_a_failed_one_explains_itself()
    {
        var r = DiagnosticsReport.Build(Healthy());
        Assert.True(Find(r, "hotkey.exit").Ok);
        var spotlight = Find(r, "hotkey.spotlight");
        Assert.False(spotlight.Ok);
        Assert.Contains("Alt+Space not registered", spotlight.Detail);
        Assert.Contains("taken", spotlight.Detail);
    }

    [Fact]
    public void A_taskbar_that_could_not_be_hidden_is_flagged_with_the_reason()
    {
        var inputs = Healthy(requested: TaskbarMode.Hidden, applied: TaskbarMode.Visible) with { TaskbarReason = "the taskbar stays visible: neither the exit nor the toggle-taskbar hotkey is registered" };
        var taskbar = Find(DiagnosticsReport.Build(inputs), "taskbar");
        Assert.False(taskbar.Ok);
        Assert.Contains("requested hidden, applied visible", taskbar.Detail);
        Assert.Contains("toggle-taskbar", taskbar.Detail);
    }

    [Fact]
    public void A_hidden_taskbar_that_was_applied_is_ok()
    {
        Assert.True(Find(DiagnosticsReport.Build(Healthy(TaskbarMode.Hidden, TaskbarMode.Hidden)), "taskbar").Ok);
    }

    [Fact]
    public void Autostart_mismatch_is_reported_in_both_directions()
    {
        var off = Find(DiagnosticsReport.Build(Healthy(autostart: false) with { AutostartWanted = true }), "autostart");
        Assert.False(off.Ok);
        Assert.Contains("setting is on but the Run entry is off", off.Detail);

        var agree = Find(DiagnosticsReport.Build(Healthy(autostart: true)), "autostart");
        Assert.True(agree.Ok);
        Assert.Equal("enabled", agree.Detail);
    }

    [Fact]
    public void Blocked_persistence_is_the_first_thing_to_look_at()
    {
        var inputs = Healthy() with { PersistenceBlocked = true, PersistenceBlockedReason = "config.json is unreadable" };
        var c = Find(DiagnosticsReport.Build(inputs), "config.persistence");
        Assert.False(c.Ok);
        Assert.Contains("config.json is unreadable", c.Detail);
    }

    [Fact]
    public void Missing_dock_or_menubar_and_no_monitors_are_flagged()
    {
        var r = DiagnosticsReport.Build(Healthy() with { DockReady = false, MenubarReady = false, MonitorCount = 0 });
        Assert.False(Find(r, "surface.dock").Ok);
        Assert.False(Find(r, "surface.menubar").Ok);
        Assert.False(Find(r, "monitors").Ok);
    }

    [Fact]
    public void Unknown_versions_and_empty_features_are_described_not_hidden()
    {
        var r = DiagnosticsReport.Build(Healthy() with { WebView2Version = " ", EnabledFeatures = Array.Empty<string>() });
        Assert.False(Find(r, "webview2.runtime").Ok);
        Assert.Equal("none enabled", Find(r, "features").Detail);
    }
}
