using System.Text.Json.Nodes;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Tests.Support;
using BetterWindows.Core.Widgets;

namespace BetterWindows.Core.Tests.Desktop;

public class DesktopWidgetCallTests
{
    // ---- desktop.addWidget

    [Fact]
    public async Task Add_widget_creates_it_with_the_type_defaults_and_publishes_it()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d));
        var s = rig.AddDesktop("m1");
        var w = await rig.Ok<WidgetInstance>(s, Msg.DesktopAddWidget, new AddWidgetParams { Type = WidgetType.Notes });
        w.Type.Should().Be(WidgetType.Notes);
        w.Size.Should().Be(WidgetSize.Medium);
        w.Config!.ToJsonString().Should().Be("{\"text\":\"\"}");
        w.Id.Should().NotBeNullOrEmpty();
        (w.X, w.Y).Should().Be((48.0, 80.0));
        rig.Settings.Current.Desktop.Widgets.Should().ContainSingle().Which.Id.Should().Be(w.Id);
        rig.State(s)!.Widgets.Should().ContainSingle().Which.Id.Should().Be(w.Id);
    }

    [Fact]
    public async Task Add_widget_uses_an_explicit_size_and_clamps_and_snaps_an_explicit_position()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d));
        var w = await rig.Ok<WidgetInstance>(rig.AddDesktop("m1"), Msg.DesktopAddWidget, new AddWidgetParams { Type = WidgetType.Clock, Size = WidgetSize.Large, X = 5000, Y = 101 });
        w.Size.Should().Be(WidgetSize.Large);
        w.X.Should().Be(1576);
        w.Y.Should().Be(104);
    }

    [Fact]
    public async Task Add_widget_without_a_position_never_overlaps_earlier_widgets()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d));
        var s = rig.AddDesktop("m1");
        var rects = new List<Rects>();
        for (var i = 0; i < 6; i++)
        {
            var w = await rig.Ok<WidgetInstance>(s, Msg.DesktopAddWidget, new AddWidgetParams { Type = WidgetType.Weather });
            var r = WidgetOps.RectOf(w);
            rects.Any(o => o.R.Overlaps(r)).Should().BeFalse();
            rects.Add(new Rects(r));
        }
    }

    [Fact]
    public async Task Add_widget_on_another_monitor_shows_up_only_there()
    {
        using var rig = await DesktopRig.StartAsync(r =>
        {
            r.Monitors.Monitors.Add(Kit.Monitor("m2", 1920, 0, 1280, 720));
            r.Configure(d => d);
        });
        var d1 = rig.AddDesktop("m1");
        var d2 = rig.AddDesktop("m2");
        var w = await rig.Ok<WidgetInstance>(d1, Msg.DesktopAddWidget, new AddWidgetParams { Type = WidgetType.Battery, MonitorId = "m2" });
        w.MonitorId.Should().Be("m2");
        rig.State(d1)!.Widgets.Should().BeEmpty();
        rig.State(d2)!.Widgets.Select(x => x.Id).Should().Equal(w.Id);
    }

    [Fact]
    public async Task Add_widget_with_an_unknown_monitor_or_type_is_a_bad_request()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d));
        var s = rig.AddDesktop("m1");
        Kit.Code(await rig.Call(s, Msg.DesktopAddWidget, new AddWidgetParams { Type = WidgetType.Clock, MonitorId = "m99" })).Should().Be("badRequest");
        Kit.Code(await rig.Call(s, Msg.DesktopAddWidget, new JsonObject { ["type"] = "banana" })).Should().Be("badRequest");
        Kit.Code(await rig.Call(s, Msg.DesktopAddWidget, new JsonObject())).Should().Be("badRequest");
        rig.Settings.Current.Desktop.Widgets.Should().BeEmpty();
    }

    [Fact]
    public async Task Add_widget_fails_cleanly_at_the_limit()
    {
        using var rig = await DesktopRig.StartAsync(r =>
        {
            var many = Enumerable.Range(0, 100).Select(i => new WidgetInstance { Id = "w" + i, Type = WidgetType.Clock }).ToArray();
            r.Configure(d => d with { Widgets = many });
        });
        Kit.Code(await rig.Call(rig.AddDesktop("m1"), Msg.DesktopAddWidget, new AddWidgetParams { Type = WidgetType.Clock })).Should().Be("failed");
        rig.Settings.Current.Desktop.Widgets.Should().HaveCount(100);
    }

    [Fact]
    public async Task Every_widget_id_is_unique()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d));
        var s = rig.AddDesktop("m1");
        var ids = new HashSet<string>();
        for (var i = 0; i < 10; i++) ids.Add((await rig.Ok<WidgetInstance>(s, Msg.DesktopAddWidget, new AddWidgetParams { Type = WidgetType.Clock })).Id);
        ids.Should().HaveCount(10);
    }

    // ---- desktop.moveWidget

    [Fact]
    public async Task Move_widget_clamps_snaps_and_persists()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d));
        var s = rig.AddDesktop("m1");
        var w = await rig.Ok<WidgetInstance>(s, Msg.DesktopAddWidget, new AddWidgetParams { Type = WidgetType.Clock, X = 100, Y = 100 });
        await rig.Ok<object?>(s, Msg.DesktopMoveWidget, new MoveWidgetParams { Id = w.Id, X = 203, Y = 301 });
        rig.Settings.Current.Desktop.Widgets.Single().Should().Match<WidgetInstance>(x => x.X == 200 && x.Y == 304);
        await rig.Ok<object?>(s, Msg.DesktopMoveWidget, new MoveWidgetParams { Id = w.Id, X = -9999, Y = 99999 });
        var moved = rig.Settings.Current.Desktop.Widgets.Single();
        moved.X.Should().Be(0);
        moved.Y.Should().BeLessThanOrEqualTo(1040 - 160);
    }

    [Fact]
    public async Task Move_widget_keeps_a_widget_below_the_menu_bar()
    {
        using var rig = await DesktopRig.StartAsync(r =>
        {
            r.Monitors.Monitors[0] = Kit.Monitor("m1", 0, 0, 1920, 1080, primary: true, workTop: 28);
            r.Configure(d => d);
        });
        var s = rig.AddDesktop("m1");
        var w = await rig.Ok<WidgetInstance>(s, Msg.DesktopAddWidget, new AddWidgetParams { Type = WidgetType.Clock, X = 100, Y = 100 });
        await rig.Ok<object?>(s, Msg.DesktopMoveWidget, new MoveWidgetParams { Id = w.Id, X = 100, Y = -50 });
        rig.Settings.Current.Desktop.Widgets.Single().Y.Should().BeGreaterThanOrEqualTo(28);
    }

    [Fact]
    public async Task Move_widget_errors()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d));
        var s = rig.AddDesktop("m1");
        Kit.Code(await rig.Call(s, Msg.DesktopMoveWidget, new MoveWidgetParams { Id = "nope", X = 1, Y = 1 })).Should().Be("notFound");
        Kit.Code(await rig.Call(s, Msg.DesktopMoveWidget, new MoveWidgetParams { Id = "", X = 1, Y = 1 })).Should().Be("badRequest");
        Kit.Code(await rig.Call(s, Msg.DesktopMoveWidget, new JsonObject { ["id"] = "w" })).Should().Be("badRequest");
    }

    // ---- desktop.removeWidget

    [Fact]
    public async Task Remove_widget_deletes_it_and_then_reports_not_found()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d));
        var s = rig.AddDesktop("m1");
        var w = await rig.Ok<WidgetInstance>(s, Msg.DesktopAddWidget, new AddWidgetParams { Type = WidgetType.Clock });
        await rig.Ok<object?>(s, Msg.DesktopRemoveWidget, new IdParams { Id = w.Id });
        rig.Settings.Current.Desktop.Widgets.Should().BeEmpty();
        rig.State(s)!.Widgets.Should().BeEmpty();
        Kit.Code(await rig.Call(s, Msg.DesktopRemoveWidget, new IdParams { Id = w.Id })).Should().Be("notFound");
    }

    // ---- desktop.updateWidget

    [Fact]
    public async Task Update_widget_replaces_the_whole_config()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d));
        var s = rig.AddDesktop("m1");
        var w = await rig.Ok<WidgetInstance>(s, Msg.DesktopAddWidget, new AddWidgetParams { Type = WidgetType.Clock });
        var updated = await rig.Ok<WidgetInstance>(s, Msg.DesktopUpdateWidget, new UpdateWidgetParams { Id = w.Id, Config = new JsonObject { ["style"] = "analog" } });
        updated.Config!.ToJsonString().Should().Be("{\"style\":\"analog\"}");
        rig.Settings.Current.Desktop.Widgets.Single().Config!.ToJsonString().Should().Be("{\"style\":\"analog\"}");
        rig.State(s)!.Widgets.Single().Config!.ToJsonString().Should().Be("{\"style\":\"analog\"}");
    }

    [Fact]
    public async Task Update_widget_changes_the_size_and_keeps_the_config_when_none_is_given()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d));
        var s = rig.AddDesktop("m1");
        var w = await rig.Ok<WidgetInstance>(s, Msg.DesktopAddWidget, new AddWidgetParams { Type = WidgetType.Notes, X = 100, Y = 100 });
        await rig.Ok<WidgetInstance>(s, Msg.DesktopUpdateWidget, new UpdateWidgetParams { Id = w.Id, Config = new JsonObject { ["text"] = "hello" } });
        var u = await rig.Ok<WidgetInstance>(s, Msg.DesktopUpdateWidget, new UpdateWidgetParams { Id = w.Id, Size = WidgetSize.Large });
        u.Size.Should().Be(WidgetSize.Large);
        u.Config!["text"]!.GetValue<string>().Should().Be("hello");
    }

    [Fact]
    public async Task Update_widget_accepts_an_empty_config_object()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d));
        var s = rig.AddDesktop("m1");
        var w = await rig.Ok<WidgetInstance>(s, Msg.DesktopAddWidget, new AddWidgetParams { Type = WidgetType.Clock });
        var u = await rig.Ok<WidgetInstance>(s, Msg.DesktopUpdateWidget, new UpdateWidgetParams { Id = w.Id, Config = new JsonObject() });
        u.Config!.ToJsonString().Should().Be("{}");
    }

    [Fact]
    public async Task Update_widget_rejects_a_config_that_is_not_an_object_or_is_too_large()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d));
        var s = rig.AddDesktop("m1");
        var w = await rig.Ok<WidgetInstance>(s, Msg.DesktopAddWidget, new AddWidgetParams { Type = WidgetType.Notes });
        Kit.Code(await rig.Call(s, Msg.DesktopUpdateWidget, new JsonObject { ["id"] = w.Id, ["config"] = new JsonArray(1, 2) })).Should().Be("badRequest");
        Kit.Code(await rig.Call(s, Msg.DesktopUpdateWidget, new JsonObject { ["id"] = w.Id, ["config"] = "text" })).Should().Be("badRequest");
        Kit.Code(await rig.Call(s, Msg.DesktopUpdateWidget, new JsonObject { ["id"] = w.Id, ["config"] = new JsonObject { ["text"] = new string('x', 17 * 1024) } })).Should().Be("badRequest");
        rig.Settings.Current.Desktop.Widgets.Single().Config!.ToJsonString().Should().Be("{\"text\":\"\"}");
    }

    [Fact]
    public async Task Update_widget_with_an_unknown_id_or_bad_size_fails()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d));
        var s = rig.AddDesktop("m1");
        Kit.Code(await rig.Call(s, Msg.DesktopUpdateWidget, new UpdateWidgetParams { Id = "nope", Size = WidgetSize.Small })).Should().Be("notFound");
        Kit.Code(await rig.Call(s, Msg.DesktopUpdateWidget, new JsonObject { ["id"] = "nope", ["size"] = "huge" })).Should().Be("badRequest");
    }

    [Fact]
    public async Task A_16_kb_config_is_still_accepted()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d));
        var s = rig.AddDesktop("m1");
        var w = await rig.Ok<WidgetInstance>(s, Msg.DesktopAddWidget, new AddWidgetParams { Type = WidgetType.Notes });
        var text = new string('x', 16 * 1024 - 20);
        var u = await rig.Ok<WidgetInstance>(s, Msg.DesktopUpdateWidget, new UpdateWidgetParams { Id = w.Id, Config = new JsonObject { ["text"] = text } });
        u.Config!["text"]!.GetValue<string>().Should().HaveLength(text.Length);
    }

    private sealed record Rects(BetterWindows.Core.Desktop.DipRect R);
}
