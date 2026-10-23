using System.Text.Json.Nodes;
using BetterWindows.Core.Controllers;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Core.Tests.Config;
using BetterWindows.Core.Tests.Support;
using BetterWindows.Core.Tests.Support.Fakes;
using Xunit;
using static BetterWindows.Core.Tests.Config.ControllerRig;

namespace BetterWindows.Core.Tests.Diagnostics;

public class SystemControllerTests : IDisposable
{
    private readonly CoreHarness _h;

    public SystemControllerTests() => _h = Start<SystemController>();

    public void Dispose() => _h.Dispose();

    private FakeAppLauncher Launcher => _h.Get<FakeAppLauncher>();
    private FakeTaskbarController Taskbar => _h.Get<FakeTaskbarController>();
    private FakeAutostartService Autostart => _h.Get<FakeAutostartService>();
    private SystemController Controller => _h.Get<SystemController>();
    private IHotkeyStatus Hotkeys => _h.Get<IHotkeyStatus>();

    private static JsonNode Json(string text) => JsonNode.Parse(text)!;

    private void EscapeHotkey(bool registered)
    {
        if (registered) Hotkeys.Report(new HotkeyRegistration("exit", "Ctrl+Alt+Q", true, null));
        else Hotkeys.Clear("exit");
    }

    private async Task PatchSettings(string json) => await _h.Get<ISettingsService>().PatchAsync(Json(json));

    /// <summary>Dock and menu bar running, as the handshake leaves them.</summary>
    private void BarsRunning()
    {
        _h.AddSurface(SurfaceKind.Dock, "m1");
        _h.AddSurface(SurfaceKind.Menubar, "m1");
    }

    // ---- system.open / quit / power / about / monitors ------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("https://example.com/a?b=1")]
    [InlineData("http://intranet/page")]
    [InlineData("ms-settings:display")]
    [InlineData("ms-settings:windowsupdate")]
    public async Task Open_accepts_http_https_and_ms_settings(string target)
    {
        var overlay = _h.AddSurface(SurfaceKind.Overlay);
        var r = await _h.CallRawAsync(overlay, Msg.SystemOpen, new OpenParams { Target = target });
        Assert.True(r["ok"]!.GetValue<bool>(), r.ToJsonString());
        Assert.Single(Launcher.Opened);
    }

    [Theory]
    [InlineData("file:///C:/Windows/system32/cmd.exe")]
    [InlineData(@"C:\Windows\system32\cmd.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:secret@example.com/")]
    [InlineData("ms-settings:../../x")]
    [InlineData("ms-settings:display\r\nnotepad")]
    [InlineData("")]
    public async Task Open_refuses_everything_else_before_the_shell_sees_it(string target)
    {
        var overlay = _h.AddSurface(SurfaceKind.Overlay);
        var r = await _h.CallRawAsync(overlay, Msg.SystemOpen, new OpenParams { Target = target });
        Assert.Equal("badRequest", ErrorCode(r));
        Assert.Empty(Launcher.Opened);
    }

    [Fact]
    public async Task Open_refuses_overlong_urls()
    {
        var overlay = _h.AddSurface(SurfaceKind.Overlay);
        var r = await _h.CallRawAsync(overlay, Msg.SystemOpen, new OpenParams { Target = "https://example.com/" + new string('a', 2100) });
        Assert.Equal("badRequest", ErrorCode(r));
        Assert.Empty(Launcher.Opened);
    }

    [Fact]
    public async Task Quit_asks_the_lifecycle_to_exit_once()
    {
        var overlay = _h.AddSurface(SurfaceKind.Overlay);
        Assert.True((await _h.CallRawAsync(overlay, Msg.SystemQuit))["ok"]!.GetValue<bool>());
        Assert.Equal(1, _h.Get<FakeAppLifecycle>().Exits);
    }

    [Fact]
    public async Task Power_actions_reach_the_port()
    {
        var settingsWindow = _h.AddSurface(SurfaceKind.Settings);
        await _h.CallAsync<object>(settingsWindow, Msg.SystemPower, new PowerParams { Action = PowerAction.Sleep });
        Assert.Equal(new[] { PowerAction.Sleep }, _h.Get<FakeSystemPower>().Actions);
    }

    [Fact]
    public async Task About_reports_the_host_build_information()
    {
        var overlay = _h.AddSurface(SurfaceKind.Overlay);
        var about = await _h.CallAsync<AboutInfo>(overlay, Msg.SystemAbout);
        Assert.Equal("0.0.0-test", about!.Version);
        Assert.Equal("test", about.Build);
        Assert.Equal("Test OS", about.Os);
        Assert.Equal("0.0", about.WebView2);
    }

    [Fact]
    public async Task Monitors_are_listed_for_the_settings_window_only()
    {
        var settingsWindow = _h.AddSurface(SurfaceKind.Settings);
        var list = await _h.CallAsync<MonitorList>(settingsWindow, Msg.SystemMonitors);
        Assert.Equal(new[] { "m1" }, list!.Monitors.Select(m => m.Id));

        var overlay = _h.AddSurface(SurfaceKind.Overlay);
        Assert.Equal("forbidden", ErrorCode(await _h.CallRawAsync(overlay, Msg.SystemMonitors)));
    }

    // ---- system.pickPath -------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Pick_path_is_only_available_to_the_settings_window()
    {
        var overlay = _h.AddSurface(SurfaceKind.Overlay);
        var r = await _h.CallRawAsync(overlay, Msg.SystemPickPath, new PickPathParams { Kind = PickKind.File });
        Assert.Equal("forbidden", ErrorCode(r));
        Assert.Empty(_h.Get<FakeFilePicker>().Calls);
    }

    [Fact]
    public async Task Picked_paths_are_granted_and_unsafe_ones_are_dropped()
    {
        var picker = _h.Get<FakeFilePicker>();
        picker.Result.AddRange(new[] { @"C:\pics\a.png", @"\\server\share\secret.png", @"C:\pics\c.png" });
        var settingsWindow = _h.AddSurface(SurfaceKind.Settings);

        var result = await _h.CallAsync<PickPathResult>(settingsWindow, Msg.SystemPickPath, new PickPathParams { Kind = PickKind.Image, Multiple = true });
        Assert.Equal(new[] { @"C:\pics\a.png", @"C:\pics\c.png" }, result!.Paths);

        var grants = _h.Get<IPathGrants>();
        Assert.True(grants.IsGranted(@"C:\pics\a.png"));
        Assert.True(grants.IsGranted(@"C:\pics\c.png"));
        Assert.False(grants.IsGranted(@"\\server\share\secret.png"));
        Assert.False(grants.IsGranted(@"C:\pics\b.png"));
        Assert.Equal(SurfaceKind.Settings, picker.Calls.Single().Owner.Kind);
    }

    [Fact]
    public async Task Single_selection_keeps_only_the_first_path_and_long_titles_are_truncated()
    {
        var picker = _h.Get<FakeFilePicker>();
        picker.Result.AddRange(new[] { @"C:\one\x.txt", @"C:\two\y.txt" });
        var settingsWindow = _h.AddSurface(SurfaceKind.Settings);

        var result = await _h.CallAsync<PickPathResult>(settingsWindow, Msg.SystemPickPath, new PickPathParams { Kind = PickKind.File, Title = new string('t', 900) });
        Assert.Equal(new[] { @"C:\one\x.txt" }, result!.Paths);
        Assert.Equal(200, picker.Calls.Single().Request.Title!.Length);
    }

    [Fact]
    public async Task A_cancelled_pick_is_an_empty_list()
    {
        var settingsWindow = _h.AddSurface(SurfaceKind.Settings);
        var result = await _h.CallAsync<PickPathResult>(settingsWindow, Msg.SystemPickPath, new PickPathParams { Kind = PickKind.Folder });
        Assert.Empty(result!.Paths);
    }

    // ---- taskbar safety --------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_taskbar_is_hidden_only_with_both_bars_running_and_an_escape_hotkey()
    {
        await PatchSettings("{\"taskbar\":{\"mode\":\"hidden\"}}");

        Controller.ReconcileTaskbar();
        Assert.Equal(TaskbarMode.Visible, Taskbar.Current);

        BarsRunning();
        Controller.ReconcileTaskbar();
        Assert.Equal(TaskbarMode.Visible, Taskbar.Current); // no escape hotkey yet

        EscapeHotkey(registered: true);
        Controller.ReconcileTaskbar();
        Assert.Equal(TaskbarMode.Hidden, Taskbar.Current);

        EscapeHotkey(registered: false);
        Controller.ReconcileTaskbar();
        Assert.Equal(TaskbarMode.Visible, Taskbar.Current); // the escape hatch disappeared: the taskbar comes back
    }

    [Fact]
    public async Task Diag_explains_why_the_taskbar_is_still_visible()
    {
        await PatchSettings("{\"taskbar\":{\"mode\":\"autoHide\"}}");
        var settingsWindow = _h.AddSurface(SurfaceKind.Settings);
        BarsRunning();
        Controller.ReconcileTaskbar();

        var report = await _h.CallAsync<DiagReport>(settingsWindow, Msg.DiagReport);
        var taskbar = report!.Checks.Single(c => c.Name == "taskbar");
        Assert.False(taskbar.Ok);
        Assert.Contains("requested autohide, applied visible", taskbar.Detail);
        Assert.Contains("neither the exit nor the toggle-taskbar", taskbar.Detail);
    }

    [Fact]
    public async Task Show_taskbar_true_is_a_temporary_override_that_a_settings_change_ends()
    {
        await PatchSettings("{\"taskbar\":{\"mode\":\"hidden\"}}");
        BarsRunning();
        EscapeHotkey(registered: true);
        Controller.ReconcileTaskbar();
        Assert.Equal(TaskbarMode.Hidden, Taskbar.Current);

        var overlay = _h.AddSurface(SurfaceKind.Overlay);
        Assert.True((await _h.CallRawAsync(overlay, Msg.SystemShowTaskbar, new ShowTaskbarParams { Show = true }))["ok"]!.GetValue<bool>());
        Assert.Equal(TaskbarMode.Visible, Taskbar.Current);

        Controller.ReconcileTaskbar();
        Assert.Equal(TaskbarMode.Visible, Taskbar.Current);

        await PatchSettings("{\"dock\":{\"iconSize\":70}}");
        Controller.ReconcileTaskbar();
        Assert.Equal(TaskbarMode.Hidden, Taskbar.Current);
    }

    [Fact]
    public async Task Hiding_the_taskbar_by_hand_still_needs_the_escape_hatch()
    {
        var overlay = _h.AddSurface(SurfaceKind.Overlay);
        var refused = await _h.CallRawAsync(overlay, Msg.SystemShowTaskbar, new ShowTaskbarParams { Show = false });
        Assert.Equal("unsupported", ErrorCode(refused));
        Assert.Equal(TaskbarMode.Visible, Taskbar.Current);

        BarsRunning();
        EscapeHotkey(registered: true);
        Assert.True((await _h.CallRawAsync(overlay, Msg.SystemShowTaskbar, new ShowTaskbarParams { Show = false }))["ok"]!.GetValue<bool>());
        Assert.Equal(TaskbarMode.Hidden, Taskbar.Current);
    }

    [Fact]
    public async Task A_hotkey_change_is_reconciled_on_the_thread_pool()
    {
        await PatchSettings("{\"taskbar\":{\"mode\":\"hidden\"}}");
        BarsRunning();
        EscapeHotkey(registered: true);
        await WaitUntilAsync(() => Taskbar.Current == TaskbarMode.Hidden);

        EscapeHotkey(registered: false);
        await WaitUntilAsync(() => Taskbar.Current == TaskbarMode.Visible);
    }

    [Fact]
    public async Task Autostart_follows_the_setting_and_is_not_touched_when_it_already_matches()
    {
        Controller.ReconcileAutostart();
        Assert.False(Autostart.IsEnabled);

        await PatchSettings("{\"general\":{\"autostart\":true}}");
        await WaitUntilAsync(() => Autostart.IsEnabled);

        await PatchSettings("{\"general\":{\"autostart\":false}}");
        await WaitUntilAsync(() => !Autostart.IsEnabled);
    }

    // ---- diag.report -----------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Diag_is_read_only_and_lists_every_check()
    {
        var settingsWindow = _h.AddSurface(SurfaceKind.Settings);
        BarsRunning();
        EscapeHotkey(registered: true);
        Hotkeys.Report(new HotkeyRegistration("spotlight", "Alt+Space", false, "taken by another app"));
        // Settle the state first: background reconciles queued by the setup are then no-ops, so the call counts below are deterministic.
        Controller.ReconcileTaskbar();
        var taskbarCallsBefore = Taskbar.Calls.Count;
        var autostartBefore = Autostart.IsEnabled;
        var settingsBefore = _h.Get<ISettingsService>().Current;

        var report = await _h.CallAsync<DiagReport>(settingsWindow, Msg.DiagReport);

        Assert.NotNull(report);
        Assert.Equal("0.0.0-test", report!.Version);
        var names = report.Checks.Select(c => c.Name).ToArray();
        foreach (var expected in new[] { "host.version", "webview2.runtime", "config.persistence", "surface.dock", "surface.menubar", "hotkeys.escape", "taskbar", "autostart", "hotkey.exit", "hotkey.spotlight" })
            Assert.Contains(expected, names);
        Assert.False(report.Checks.Single(c => c.Name == "hotkey.spotlight").Ok);
        Assert.Equal(taskbarCallsBefore, Taskbar.Calls.Count);
        Assert.Equal(autostartBefore, Autostart.IsEnabled);
        Assert.Same(settingsBefore, _h.Get<ISettingsService>().Current);
    }

    [Fact]
    public async Task Diag_is_settings_only()
    {
        var overlay = _h.AddSurface(SurfaceKind.Overlay);
        Assert.Equal("forbidden", ErrorCode(await _h.CallRawAsync(overlay, Msg.DiagReport)));
    }

    [Fact]
    public void Start_is_idempotent_and_stop_unsubscribes()
    {
        Controller.Start();
        Controller.Stop();
        Controller.Stop();
        Assert.Equal("SystemController", Controller.Name);
    }
}
