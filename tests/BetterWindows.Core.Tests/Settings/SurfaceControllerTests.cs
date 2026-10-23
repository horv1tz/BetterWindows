using System.Text.Json.Nodes;
using BetterWindows.Core.Controllers;
using ProtocolSettings = BetterWindows.Core.Protocol.Settings;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Core.Tests.Support;
using BetterWindows.Core.Tests.Support.Fakes;
using Xunit;
using static BetterWindows.Core.Tests.Config.ControllerRig;

namespace BetterWindows.Core.Tests.Config;

public class SurfaceControllerTests
{
    private static JsonNode Json(string text) => JsonNode.Parse(text)!;

    [Fact]
    public async Task Handshake_returns_the_init_block_for_the_assigned_surface()
    {
        using var h = Start<SurfaceController>();
        var dock = h.AddSurface(SurfaceKind.Dock, "m1", ready: false);
        var init = await h.ReadyAsync(dock);

        Assert.NotNull(init);
        Assert.Equal(SurfaceKind.Dock, init!.Surface);
        Assert.Equal("m1", init.Monitor.Id);
        Assert.Equal(ProtocolInfo.Version, init.ProtocolVersion);
        Assert.Equal("en", init.Locale);
        Assert.Equal(1, init.Settings.Version);
        Assert.Equal(ThemeMode.Light, init.Theme.Mode);
    }

    [Fact]
    public async Task Overlay_and_settings_surfaces_get_the_primary_monitor()
    {
        using var h = Start<SurfaceController>();
        var overlay = h.AddSurface(SurfaceKind.Overlay, null, ready: false);
        var init = await h.ReadyAsync(overlay);
        Assert.Equal("m1", init!.Monitor.Id);
        Assert.True(init.Monitor.Primary);
    }

    [Fact]
    public async Task A_handshake_that_names_another_surface_or_monitor_is_refused()
    {
        using var h = Start<SurfaceController>();
        var dock = h.AddSurface(SurfaceKind.Dock, "m1", ready: false);

        var wrongKind = await h.CallRawAsync(dock, Msg.SurfaceReady, new SurfaceReadyParams { Surface = SurfaceKind.Menubar, MonitorId = "m1" });
        Assert.Equal("badRequest", ErrorCode(wrongKind));

        var wrongMonitor = await h.CallRawAsync(dock, Msg.SurfaceReady, new SurfaceReadyParams { Surface = SurfaceKind.Dock, MonitorId = "m9" });
        Assert.Equal("badRequest", ErrorCode(wrongMonitor));
    }

    [Fact]
    public async Task A_surface_on_an_unknown_monitor_is_refused()
    {
        using var h = Start<SurfaceController>();
        var ghost = h.AddSurface(SurfaceKind.Dock, "m9", ready: false);
        var r = await h.CallRawAsync(ghost, Msg.SurfaceReady, new SurfaceReadyParams { Surface = SurfaceKind.Dock });
        Assert.Equal("badRequest", ErrorCode(r));
    }

    [Fact]
    public async Task The_handshake_reports_the_configured_language_and_features()
    {
        using var h = Start<SurfaceController>();
        await h.Get<ISettingsService>().PatchAsync(Json("{\"general\":{\"language\":\"ru\",\"features\":{\"liveThumbnails\":true}}}"));
        var overlay = h.AddSurface(SurfaceKind.Overlay, null, ready: false);
        var init = await h.ReadyAsync(overlay);
        Assert.Equal("ru", init!.Locale);
        Assert.True(init.Features.LiveThumbnails);
    }

    [Fact]
    public async Task Settings_get_returns_the_current_settings()
    {
        using var h = Start<SurfaceController>();
        var overlay = h.AddSurface(SurfaceKind.Overlay);
        var s = await h.CallAsync<ProtocolSettings>(overlay, Msg.SettingsGet);
        Assert.NotNull(s);
        Assert.Equal(1, s!.Version);
    }

    [Fact]
    public async Task Settings_patch_merges_validates_and_returns_the_stored_value()
    {
        using var h = Start<SurfaceController>();
        var settingsWindow = h.AddSurface(SurfaceKind.Settings);
        var stored = await h.CallAsync<ProtocolSettings>(settingsWindow, Msg.SettingsPatch, new SettingsPatchParams { Patch = Json("{\"dock\":{\"iconSize\":72}}") });
        Assert.Equal(72, stored!.Dock.IconSize);
        Assert.Equal(72, h.Get<ISettingsService>().Current.Dock.IconSize);
    }

    [Fact]
    public async Task Settings_patch_refuses_unknown_keys_naming_them_and_never_patches_the_version()
    {
        using var h = Start<SurfaceController>();
        var settingsWindow = h.AddSurface(SurfaceKind.Settings);

        var unknown = await h.CallRawAsync(settingsWindow, Msg.SettingsPatch, new SettingsPatchParams { Patch = Json("{\"dock\":{\"noSuchThing\":1}}") });
        Assert.Equal("badRequest", ErrorCode(unknown));

        var version = await h.CallRawAsync(settingsWindow, Msg.SettingsPatch, new SettingsPatchParams { Patch = Json("{\"version\":9}") });
        Assert.Equal("badRequest", ErrorCode(version));
        Assert.Equal(1, h.Get<ISettingsService>().Current.Version);
    }

    [Fact]
    public async Task Settings_patch_without_a_patch_object_is_a_bad_request()
    {
        using var h = Start<SurfaceController>();
        var settingsWindow = h.AddSurface(SurfaceKind.Settings);
        Assert.Equal("badRequest", ErrorCode(await h.CallRawAsync(settingsWindow, Msg.SettingsPatch, new SettingsPatchParams())));
        Assert.Equal("badRequest", ErrorCode(await h.CallRawAsync(settingsWindow, Msg.SettingsPatch, new SettingsPatchParams { Patch = Json("[1,2]") })));
    }

    [Fact]
    public async Task Settings_patch_is_refused_as_forbidden_from_the_dock()
    {
        using var h = Start<SurfaceController>();
        var dock = h.AddSurface(SurfaceKind.Dock);
        var r = await h.CallRawAsync(dock, Msg.SettingsPatch, new SettingsPatchParams { Patch = Json("{\"dock\":{\"iconSize\":72}}") });
        Assert.Equal("forbidden", ErrorCode(r));
    }

    [Fact]
    public async Task A_settings_change_is_broadcast_as_settings_changed_after_the_coalescing_window()
    {
        using var h = Start<SurfaceController>();
        var dock = h.AddSurface(SurfaceKind.Dock);
        var settingsWindow = h.AddSurface(SurfaceKind.Settings);
        await h.CallAsync<ProtocolSettings>(settingsWindow, Msg.SettingsPatch, new SettingsPatchParams { Patch = Json("{\"dock\":{\"iconSize\":72}}") });
        h.Time.Advance(TimeSpan.FromMilliseconds(200));

        var last = dock.LastEvent<ProtocolSettings>(Msg.SettingsChanged);
        Assert.NotNull(last);
        Assert.Equal(72, last!.Dock.IconSize);
        Assert.NotEmpty(settingsWindow.Events(Msg.SettingsChanged));
    }

    [Fact]
    public async Task A_language_change_emits_locale_changed_and_a_theme_change_emits_theme_changed()
    {
        using var h = Start<SurfaceController>();
        var overlay = h.AddSurface(SurfaceKind.Overlay);
        var settingsWindow = h.AddSurface(SurfaceKind.Settings);
        await h.CallAsync<ProtocolSettings>(settingsWindow, Msg.SettingsPatch, new SettingsPatchParams { Patch = Json("{\"general\":{\"language\":\"ru\"}}") });
        h.Time.Advance(TimeSpan.FromMilliseconds(200));
        Assert.Equal("ru", overlay.LastEvent<LocaleState>(Msg.LocaleChanged)!.Locale);

        var before = overlay.Events(Msg.ThemeChanged).Count;
        h.Get<FakeThemeSource>().SetAppsUseLightTheme(false);
        h.Time.Advance(TimeSpan.FromMilliseconds(200));
        Assert.True(overlay.Events(Msg.ThemeChanged).Count > before);
        Assert.Equal(ThemeMode.Dark, overlay.LastEvent<ThemeState>(Msg.ThemeChanged)!.Mode);
    }

    [Fact]
    public async Task A_ready_surface_receives_the_current_state_right_after_the_handshake()
    {
        using var h = Start<SurfaceController>();
        var dock = h.AddSurface(SurfaceKind.Dock, "m1", ready: false);
        await h.ReadyAsync(dock);
        Assert.NotEmpty(dock.Events(Msg.SettingsChanged));
        Assert.NotEmpty(dock.Events(Msg.ThemeChanged));
        Assert.NotEmpty(dock.Events(Msg.LocaleChanged));
    }

    [Fact]
    public async Task Open_window_forwards_known_pages_and_refuses_others()
    {
        using var h = Start<SurfaceController>();
        var overlay = h.AddSurface(SurfaceKind.Overlay);
        var surfaces = h.Get<FakeSurfaceManager>();

        var first = await h.CallRawAsync(overlay, Msg.SettingsOpenWindow, new SettingsOpenParams { Page = "appearance" });
        Assert.True(first["ok"]!.GetValue<bool>());
        var second = await h.CallRawAsync(overlay, Msg.SettingsOpenWindow, new SettingsOpenParams());
        Assert.True(second["ok"]!.GetValue<bool>());
        Assert.Equal(new string?[] { "appearance", null }, surfaces.SettingsOpened);

        var bad = await h.CallRawAsync(overlay, Msg.SettingsOpenWindow, new SettingsOpenParams { Page = "../../evil" });
        Assert.Equal("badRequest", ErrorCode(bad));
        Assert.Equal(2, surfaces.SettingsOpened.Count);
    }

    [Fact]
    public async Task Open_window_is_not_available_to_the_menu_bar()
    {
        using var h = Start<SurfaceController>();
        var bar = h.AddSurface(SurfaceKind.Menubar, "m1");
        Assert.Equal("forbidden", ErrorCode(await h.CallRawAsync(bar, Msg.SettingsOpenWindow, new SettingsOpenParams { Page = "about" })));
    }

    [Fact]
    public async Task Dock_resize_is_clamped_to_the_monitor_and_passes_the_bar_thickness()
    {
        using var h = Start<SurfaceController>();
        var dock = h.AddSurface(SurfaceKind.Dock, "m1");
        var surfaces = h.Get<FakeSurfaceManager>();

        await h.SendEventAsync(dock, Msg.SurfaceResize, new SurfaceResizeParams { Width = 9000, Height = 120, BarThickness = 64 });
        Assert.Single(surfaces.Resizes);
        Assert.Equal(1920, surfaces.Resizes[0].W);
        Assert.Equal(120, surfaces.Resizes[0].H);
        Assert.Equal(64, surfaces.Resizes[0].Bar);

        await h.SendEventAsync(dock, Msg.SurfaceResize, new SurfaceResizeParams { Width = 300, Height = 90 });
        Assert.Equal(2, surfaces.Resizes.Count);
        Assert.Null(surfaces.Resizes[1].Bar);
    }

    [Fact]
    public async Task Only_the_dock_may_resize_and_a_degenerate_size_is_clamped_to_one_dip()
    {
        using var h = Start<SurfaceController>();
        var dock = h.AddSurface(SurfaceKind.Dock, "m1");
        var bar = h.AddSurface(SurfaceKind.Menubar, "m1");
        var surfaces = h.Get<FakeSurfaceManager>();

        // The wire writes NaN as 0 (ProtocolJson), so a degenerate size arrives as 0 and is clamped to the 1 DIP minimum.
        await h.SendEventAsync(dock, Msg.SurfaceResize, new SurfaceResizeParams { Width = double.NaN, Height = 100 });
        Assert.Single(surfaces.Resizes);
        Assert.Equal(1, surfaces.Resizes[0].W);

        await h.SendEventAsync(bar, Msg.SurfaceResize, new SurfaceResizeParams { Width = 100, Height = 100 });
        Assert.Single(surfaces.Resizes);
    }

    [Fact]
    public async Task Shapes_are_clamped_to_the_last_dock_size_and_only_dock_and_menubar_may_report_them()
    {
        using var h = Start<SurfaceController>();
        var dock = h.AddSurface(SurfaceKind.Dock, "m1");
        var overlay = h.AddSurface(SurfaceKind.Overlay);
        var surfaces = h.Get<FakeSurfaceManager>();

        await h.SendEventAsync(dock, Msg.SurfaceResize, new SurfaceResizeParams { Width = 400, Height = 80 });
        var shape = new SurfaceShapeParams
        {
            Rects = new[] { new ShapeRect { X = 380, Y = 10, Width = 100, Height = 60, Radius = 99 } },
            HitRects = new[] { new ShapeRect { X = -5, Y = 0, Width = 20, Height = 20, Radius = 0 } },
            Blur = false,
        };
        await h.SendEventAsync(dock, Msg.SurfaceShape, shape);
        await h.SendEventAsync(overlay, Msg.SurfaceShape, shape);

        Assert.Single(surfaces.Shapes);
        var (surface, applied) = surfaces.Shapes[0];
        Assert.Equal(SurfaceKind.Dock, surface.Kind);
        Assert.Equal(380, applied.Rects[0].X);
        Assert.Equal(20, applied.Rects[0].Width);
        Assert.Equal(10, applied.Rects[0].Radius);
        Assert.Equal(0, applied.HitRects[0].X);
        Assert.False(applied.Blur);
    }

    [Fact]
    public async Task The_menu_bar_shape_is_clamped_to_the_monitor_and_its_28_dip_height()
    {
        using var h = Start<SurfaceController>();
        var bar = h.AddSurface(SurfaceKind.Menubar, "m1");
        await h.SendEventAsync(bar, Msg.SurfaceShape, new SurfaceShapeParams
        {
            Rects = new[] { new ShapeRect { X = 0, Y = 20, Width = 5000, Height = 50, Radius = 0 } },
        });
        var applied = h.Get<FakeSurfaceManager>().Shapes.Single().Shape;
        Assert.Equal(1920, applied.Rects[0].Width);
        Assert.Equal(8, applied.Rects[0].Height);
    }

    [Fact]
    public async Task Log_lines_are_accepted_and_never_posted_back_to_the_page()
    {
        using var h = Start<SurfaceController>();
        var dock = h.AddSurface(SurfaceKind.Dock);
        await h.SendEventAsync(dock, Msg.SurfaceLog, new LogParams { Level = LogLevel.Warn, Message = "line one\nline two\u0007", Data = Json("{\"k\":1}") });
        Assert.Empty(dock.Violations);
        Assert.All(dock.All, e => Assert.Equal("event", (string?)e["kind"]));
    }
}
