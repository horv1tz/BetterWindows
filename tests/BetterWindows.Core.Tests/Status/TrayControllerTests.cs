using BetterWindows.Core.Controllers;
using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Core.Tests.Support;
using BetterWindows.Core.Tests.Support.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace BetterWindows.Core.Tests.Status;

public class TrayControllerTests
{
    private static readonly TimeSpan Coalesce = TimeSpan.FromMilliseconds(100);

    private static CoreHarness NewHarness(bool flag = true, bool hostSupported = true)
    {
        var h = Kit.Harness<TrayController>(sc =>
        {
            sc.AddSingleton<TestTrayHost>();
            sc.AddSingleton<ITrayHost>(sp => sp.GetRequiredService<TestTrayHost>());
        });
        h.Get<TestTrayHost>().IsSupported = hostSupported;
        h.Settings().Set(s => s with { General = s.General with { Features = s.General.Features with { TrayHosting = flag } } });
        return h;
    }

    private static TrayIcon Icon(string id, string tip = "tip", string url = "", bool hidden = false) => new() { Id = id, Tooltip = tip, IconUrl = url, Hidden = hidden };

    // ---- state ----

    [Fact]
    public void Tray_is_unsupported_by_default_because_the_feature_flag_is_off()
    {
        using var h = Kit.Harness<TrayController>();
        h.Get<FakeTrayHost>().IsSupported = true;
        h.StartAll<TrayController>();
        var s = h.Ready(h.AddSurface(SurfaceKind.Menubar, "m1", ready: false));
        var state = s.LastEvent<TrayState>(Msg.TrayState)!;
        state.Supported.Should().BeFalse();
        state.Icons.Should().BeEmpty();
    }

    [Fact]
    public void The_flag_alone_is_not_enough_the_platform_must_support_hosting()
    {
        using var h = NewHarness(flag: true, hostSupported: false);
        h.Get<TestTrayHost>().IconList.Add(Icon("a"));
        h.StartAll<TrayController>();
        var s = h.Ready(h.AddSurface(SurfaceKind.Menubar, "m1", ready: false));
        s.LastEvent<TrayState>(Msg.TrayState)!.Supported.Should().BeFalse();
        s.LastEvent<TrayState>(Msg.TrayState)!.Icons.Should().BeEmpty("icons of an unsupported host are never exposed");
    }

    [Fact]
    public void The_platform_alone_is_not_enough_the_flag_must_be_on()
    {
        using var h = NewHarness(flag: false, hostSupported: true);
        h.Get<TestTrayHost>().IconList.Add(Icon("a"));
        h.StartAll<TrayController>();
        var s = h.Ready(h.AddSurface(SurfaceKind.Menubar, "m1", ready: false));
        s.LastEvent<TrayState>(Msg.TrayState)!.Supported.Should().BeFalse();
    }

    [Fact]
    public void Supported_tray_publishes_the_icons_with_dyn_urls()
    {
        using var h = NewHarness();
        h.Get<TestTrayHost>().IconList.AddRange(new[] { Icon("app1", "First"), Icon("app2", "Second", hidden: true) });
        h.StartAll<TrayController>();
        var s = h.Ready(h.AddSurface(SurfaceKind.Menubar, "m1", ready: false));
        var state = s.LastEvent<TrayState>(Msg.TrayState)!;
        state.Supported.Should().BeTrue();
        state.Icons.Select(i => i.Id).Should().Equal("app1", "app2");
        state.Icons[0].Tooltip.Should().Be("First");
        state.Icons[1].Hidden.Should().BeTrue();
        state.Icons.Should().OnlyContain(i => i.IconUrl.StartsWith("/dyn/tray/"));
    }

    [Fact]
    public void Only_the_menubar_consumes_tray_state()
    {
        using var h = NewHarness();
        h.StartAll<TrayController>();
        foreach (var kind in new[] { SurfaceKind.Dock, SurfaceKind.Overlay, SurfaceKind.Desktop, SurfaceKind.Settings })
            h.Ready(h.AddSurface(kind, ready: false)).Events(Msg.TrayState).Should().BeEmpty(kind.ToString());
        h.Ready(h.AddSurface(SurfaceKind.Menubar, "m1", ready: false)).Events(Msg.TrayState).Should().ContainSingle();
    }

    [Fact]
    public void Host_changes_are_coalesced_and_published()
    {
        using var h = NewHarness();
        h.StartAll<TrayController>();
        var s = h.AddSurface(SurfaceKind.Menubar, "m1");
        s.Clear();
        var host = h.Get<TestTrayHost>();
        host.IconList.Add(Icon("a"));
        host.RaiseChanged();
        host.IconList.Add(Icon("b"));
        host.RaiseChanged();
        h.Time.Advance(Coalesce);
        s.Events(Msg.TrayState).Should().ContainSingle();
        s.LastEvent<TrayState>(Msg.TrayState)!.Icons.Should().HaveCount(2);
    }

    [Fact]
    public void Toggling_the_feature_flag_republishes()
    {
        using var h = NewHarness(flag: false);
        h.Get<TestTrayHost>().IconList.Add(Icon("a"));
        h.StartAll<TrayController>();
        var s = h.AddSurface(SurfaceKind.Menubar, "m1");
        s.LastEvent<TrayState>(Msg.TrayState)!.Supported.Should().BeFalse();
        h.Settings().Set(x => x with { General = x.General with { Features = x.General.Features with { TrayHosting = true } } });
        h.Time.Advance(Coalesce);
        s.LastEvent<TrayState>(Msg.TrayState)!.Supported.Should().BeTrue();
        s.LastEvent<TrayState>(Msg.TrayState)!.Icons.Should().ContainSingle();
    }

    [Fact]
    public void An_icon_url_revision_changes_only_when_the_icon_changes()
    {
        using var h = NewHarness();
        var host = h.Get<TestTrayHost>();
        host.IconList.Add(Icon("a", "one", url: "v1"));
        host.IconList.Add(Icon("b", "two", url: "v1"));
        h.StartAll<TrayController>();
        var s = h.AddSurface(SurfaceKind.Menubar, "m1");
        string Url(string id) => s.LastEvent<TrayState>(Msg.TrayState)!.Icons.Single(i => i.Id == id).IconUrl;
        var a1 = Url("a");
        var b1 = Url("b");

        host.IconList[1] = Icon("b", "two!", url: "v1");                   // only b changed (tooltip)
        host.RaiseChanged();
        h.Time.Advance(Coalesce);
        Url("a").Should().Be(a1);
        Url("b").Should().NotBe(b1);

        var b2 = Url("b");
        host.IconList[1] = Icon("b", "two!", url: "v2");                   // the host marked a new image
        host.RaiseChanged();
        h.Time.Advance(Coalesce);
        Url("b").Should().NotBe(b2);
        Url("a").Should().Be(a1);
    }

    [Fact]
    public void Hostile_icon_data_is_bounded()
    {
        using var h = NewHarness();
        var host = h.Get<TestTrayHost>();
        for (var i = 0; i < 300; i++) host.IconList.Add(Icon("i" + i, new string('t', 2000)));
        h.StartAll<TrayController>();
        var s = h.Ready(h.AddSurface(SurfaceKind.Menubar, "m1", ready: false));
        var state = s.LastEvent<TrayState>(Msg.TrayState)!;
        state.Icons.Should().HaveCount(StatusSanitizerLimits.MaxTrayIcons);
        state.Icons.Should().OnlyContain(i => i.Tooltip.Length <= 256);
    }

    // ---- tray.click ----

    [Fact]
    public async Task Click_is_forwarded_with_the_real_cursor_position_not_a_ui_supplied_one()
    {
        using var h = NewHarness();
        h.Get<TestTrayHost>().IconList.Add(Icon("app1"));
        h.Get<FakeCursorProvider>().Position = new NativePoint(1234, 56);
        h.StartAll<TrayController>();
        var s = h.AddSurface(SurfaceKind.Menubar, "m1");
        var r = await h.CallRawAsync(s, Msg.TrayClick, new TrayClickParams { Id = "app1", Button = TrayButton.Right, Anchor = new Point { X = 9999, Y = -5 } });
        Kit.IsOk(r).Should().BeTrue();
        h.Get<TestTrayHost>().Clicks.Should().Equal(("app1", TrayButton.Right, new NativePoint(1234, 56)));
    }

    [Theory]
    [InlineData(TrayButton.Left)]
    [InlineData(TrayButton.Right)]
    [InlineData(TrayButton.Double)]
    public async Task All_buttons_are_forwarded(TrayButton button)
    {
        using var h = NewHarness();
        h.Get<TestTrayHost>().IconList.Add(Icon("a"));
        h.StartAll<TrayController>();
        var s = h.AddSurface(SurfaceKind.Overlay);
        Kit.IsOk(await h.CallRawAsync(s, Msg.TrayClick, new TrayClickParams { Id = "a", Button = button })).Should().BeTrue();
        h.Get<TestTrayHost>().Clicks.Single().Button.Should().Be(button);
    }

    [Fact]
    public async Task Click_on_an_unknown_icon_is_notFound()
    {
        using var h = NewHarness();
        h.Get<TestTrayHost>().IconList.Add(Icon("a"));
        h.StartAll<TrayController>();
        var s = h.AddSurface(SurfaceKind.Menubar, "m1");
        Kit.ErrorCode(await h.CallRawAsync(s, Msg.TrayClick, new TrayClickParams { Id = "nope", Button = TrayButton.Left })).Should().Be("notFound");
        Kit.ErrorCode(await h.CallRawAsync(s, Msg.TrayClick, new TrayClickParams { Id = "A", Button = TrayButton.Left })).Should().Be("notFound");
        h.Get<TestTrayHost>().Clicks.Should().BeEmpty();
    }

    [Fact]
    public async Task Click_is_unsupported_when_tray_hosting_is_off()
    {
        using var h = NewHarness(flag: false);
        h.Get<TestTrayHost>().IconList.Add(Icon("a"));
        h.StartAll<TrayController>();
        var s = h.AddSurface(SurfaceKind.Menubar, "m1");
        Kit.ErrorCode(await h.CallRawAsync(s, Msg.TrayClick, new TrayClickParams { Id = "a", Button = TrayButton.Left })).Should().Be("unsupported");
        h.Get<TestTrayHost>().Clicks.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad\u0000id")]
    [InlineData("new\nline")]
    public async Task Click_with_a_malformed_id_is_a_badRequest(string id)
    {
        using var h = NewHarness();
        h.StartAll<TrayController>();
        var s = h.AddSurface(SurfaceKind.Menubar, "m1");
        Kit.ErrorCode(await h.CallRawAsync(s, Msg.TrayClick, new TrayClickParams { Id = id, Button = TrayButton.Left })).Should().Be("badRequest");
        Kit.ErrorCode(await h.CallRawAsync(s, Msg.TrayClick, new TrayClickParams { Id = new string('x', 300), Button = TrayButton.Left })).Should().Be("badRequest");
        Kit.ErrorCode(await h.CallRawAsync(s, Msg.TrayClick, new { id = "a", button = "middle" })).Should().Be("badRequest");
        Kit.ErrorCode(await h.CallRawAsync(s, Msg.TrayClick, new { button = "left" })).Should().Be("badRequest");
    }

    [Fact]
    public async Task Click_fails_cleanly_when_the_pointer_is_unavailable()
    {
        using var h = NewHarness();
        h.Get<TestTrayHost>().IconList.Add(Icon("a"));
        h.Get<FakeCursorProvider>().Unavailable = true;
        h.StartAll<TrayController>();
        var s = h.AddSurface(SurfaceKind.Menubar, "m1");
        Kit.ErrorCode(await h.CallRawAsync(s, Msg.TrayClick, new TrayClickParams { Id = "a", Button = TrayButton.Left })).Should().Be("failed");
        h.Get<TestTrayHost>().Clicks.Should().BeEmpty();
    }

    [Fact]
    public async Task A_host_that_throws_on_click_becomes_failed()
    {
        using var h = NewHarness();
        h.Get<TestTrayHost>().IconList.Add(Icon("a"));
        h.Get<TestTrayHost>().ThrowOnClick = true;
        h.StartAll<TrayController>();
        var s = h.AddSurface(SurfaceKind.Menubar, "m1");
        Kit.ErrorCode(await h.CallRawAsync(s, Msg.TrayClick, new TrayClickParams { Id = "a", Button = TrayButton.Left })).Should().Be("failed");
    }

    [Theory]
    [InlineData(SurfaceKind.Menubar, true)]
    [InlineData(SurfaceKind.Overlay, true)]
    [InlineData(SurfaceKind.Dock, false)]
    [InlineData(SurfaceKind.Desktop, false)]
    [InlineData(SurfaceKind.Settings, false)]
    public async Task Click_is_only_allowed_from_menubar_and_overlay(SurfaceKind kind, bool allowed)
    {
        using var h = NewHarness();
        h.Get<TestTrayHost>().IconList.Add(Icon("a"));
        h.StartAll<TrayController>();
        var s = h.AddSurface(kind);
        var r = await h.CallRawAsync(s, Msg.TrayClick, new TrayClickParams { Id = "a", Button = TrayButton.Left });
        if (allowed) Kit.IsOk(r).Should().BeTrue(); else Kit.ErrorCode(r).Should().Be("forbidden");
    }

    // ---- host lifecycle ----

    [Fact]
    public async Task The_host_is_started_when_hosting_is_enabled_and_supported()
    {
        using var h = NewHarness();
        h.StartAll<TrayController>();
        await Kit.Eventually(() => h.Get<TestTrayHost>().Starts == 1);
        h.Get<TestTrayHost>().Stops.Should().Be(0);
    }

    [Fact]
    public async Task The_host_is_never_started_when_the_flag_is_off_or_the_platform_cannot()
    {
        using var off = NewHarness(flag: false);
        off.StartAll<TrayController>();
        using var unsupported = NewHarness(flag: true, hostSupported: false);
        unsupported.StartAll<TrayController>();
        await Kit.Settle();
        off.Get<TestTrayHost>().Starts.Should().Be(0);
        unsupported.Get<TestTrayHost>().Starts.Should().Be(0);
    }

    [Fact]
    public async Task Flipping_the_flag_starts_and_stops_the_host()
    {
        using var h = NewHarness(flag: false);
        h.StartAll<TrayController>();
        h.Settings().Set(x => x with { General = x.General with { Features = x.General.Features with { TrayHosting = true } } });
        await Kit.Eventually(() => h.Get<TestTrayHost>().Starts == 1);
        h.Settings().Set(x => x with { General = x.General with { Features = x.General.Features with { TrayHosting = false } } });
        await Kit.Eventually(() => h.Get<TestTrayHost>().Stops == 1);
        h.Settings().Set(x => x with { General = x.General with { Features = x.General.Features with { TrayHosting = true } } });
        await Kit.Eventually(() => h.Get<TestTrayHost>().Starts == 2);
    }

    [Fact]
    public async Task Unrelated_settings_changes_do_not_restart_the_host()
    {
        using var h = NewHarness();
        h.StartAll<TrayController>();
        await Kit.Eventually(() => h.Get<TestTrayHost>().Starts == 1);
        for (var i = 0; i < 5; i++) h.Settings().Set(x => x with { Dock = x.Dock with { IconSize = 40 + i } });
        await Kit.Settle();
        h.Get<TestTrayHost>().Starts.Should().Be(1);
        h.Get<TestTrayHost>().Stops.Should().Be(0);
    }

    [Fact]
    public async Task Stop_stops_the_running_host_and_detaches()
    {
        using var h = NewHarness();
        var c = h.StartAll<TrayController>();
        await Kit.Eventually(() => h.Get<TestTrayHost>().Starts == 1);
        await Kit.Settle();
        h.Time.Advance(Coalesce);                                           // flush the invalidation the start-up pass requested
        c.Stop();
        h.Get<TestTrayHost>().Stops.Should().Be(1);
        var s = h.AddSurface(SurfaceKind.Menubar, "m1");
        s.Clear();
        h.Get<TestTrayHost>().IconList.Add(Icon("late"));
        h.Get<TestTrayHost>().RaiseChanged();
        h.Time.Advance(Coalesce);
        s.Events(Msg.TrayState).Should().BeEmpty();
    }

    [Fact]
    public void The_tray_calls_have_handlers()
    {
        using var h = NewHarness();
        h.StartAll<TrayController>();
        h.Router.RegisteredTypes.Should().Contain(Msg.TrayClick);
    }

    private static class StatusSanitizerLimits
    {
        public const int MaxTrayIcons = BetterWindows.Core.Status.StatusSanitizer.MaxTrayIcons;
    }
}
