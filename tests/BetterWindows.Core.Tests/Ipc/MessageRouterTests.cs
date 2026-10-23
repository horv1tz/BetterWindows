using System.Text.Json.Nodes;
using BetterWindows.Core.Ipc;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Tests.Support;

namespace BetterWindows.Core.Tests.Ipc;

public class MessageRouterTests
{
    [Fact]
    public async Task Call_with_params_and_result_roundtrips()
    {
        using var h = new CoreHarness();
        var ui = h.AddSurface(SurfaceKind.Dock);
        h.Router.Handle<DockItemParams, DockItem>("test.echo", (_, p) => Task.FromResult(new DockItem { Id = p.ItemId, Name = "n" }));

        var r = await h.CallRawAsync(ui, "test.echo", new DockItemParams { ItemId = "aumid:X" });

        r["ok"]!.GetValue<bool>().Should().BeTrue();
        r["payload"]!["id"]!.GetValue<string>().Should().Be("aumid:X");
        r["payload"]!["kind"]!.GetValue<string>().Should().Be("app"); // camelCase enum on the wire
    }

    [Fact]
    public async Task Unknown_call_returns_notFound()
    {
        using var h = new CoreHarness();
        var ui = h.AddSurface(SurfaceKind.Dock);
        var r = await h.CallRawAsync(ui, "nope.nothing");
        r["ok"]!.GetValue<bool>().Should().BeFalse();
        r["error"]!["code"]!.GetValue<string>().Should().Be("notFound");
    }

    [Fact]
    public async Task ProtocolException_maps_to_error_code()
    {
        using var h = new CoreHarness();
        var ui = h.AddSurface(SurfaceKind.Dock);
        h.Router.HandleNoParams("test.fail", _ => throw ProtocolException.Unsupported("not here"));
        var r = await h.CallRawAsync(ui, "test.fail");
        r["error"]!["code"]!.GetValue<string>().Should().Be("unsupported");
        r["error"]!["message"]!.GetValue<string>().Should().Be("not here");
    }

    [Fact]
    public async Task Unexpected_exception_maps_to_internal()
    {
        using var h = new CoreHarness();
        var ui = h.AddSurface(SurfaceKind.Dock);
        h.Router.HandleNoParams("test.boom", _ => throw new InvalidOperationException("kaboom"));
        var r = await h.CallRawAsync(ui, "test.boom");
        r["error"]!["code"]!.GetValue<string>().Should().Be("internal");
    }

    [Fact]
    public async Task Missing_payload_for_record_params_uses_defaults()
    {
        using var h = new CoreHarness();
        var ui = h.AddSurface(SurfaceKind.Desktop);
        h.Router.Handle<WeatherGetParams, bool>("test.weather", (_, p) => Task.FromResult(p.Force));
        var r = await h.CallRawAsync(ui, "test.weather");
        r["ok"]!.GetValue<bool>().Should().BeTrue();
        r["payload"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task Invalid_payload_is_badRequest()
    {
        using var h = new CoreHarness();
        var ui = h.AddSurface(SurfaceKind.Dock);
        h.Router.Handle<DockPinParams>("test.pin", (_, _) => Task.CompletedTask);
        var r = await h.CallRawAsync(ui, "test.pin", new JsonObject { ["appId"] = 5, ["index"] = "x" });
        r["error"]!["code"]!.GetValue<string>().Should().Be("badRequest");
    }

    [Fact]
    public async Task Ui_event_reaches_handler_with_sender()
    {
        using var h = new CoreHarness();
        var dock = h.AddSurface(SurfaceKind.Dock);
        SurfaceId? who = null; SurfaceResizeParams? got = null;
        h.Router.OnEvent<SurfaceResizeParams>(Msg.SurfaceResize, (ctx, p) => { who = ctx.Surface; got = p; });

        await h.SendEventAsync(dock, Msg.SurfaceResize, new SurfaceResizeParams { Width = 640, Height = 120 });

        who.Should().Be(dock.Id);
        got!.Width.Should().Be(640);
    }

    [Fact]
    public async Task Duplicate_call_handler_registration_throws()
    {
        using var h = new CoreHarness();
        h.Router.HandleNoParams("dup", _ => Task.CompletedTask);
        var act = () => h.Router.HandleNoParams("dup", _ => Task.CompletedTask);
        act.Should().Throw<InvalidOperationException>();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Successful_surface_ready_raises_SurfaceReady()
    {
        using var h = new CoreHarness();
        var ui = h.AddSurface(SurfaceKind.Menubar, "1");
        SurfaceId? ready = null;
        h.Registry.SurfaceReady += id => ready = id;
        h.Router.Handle<SurfaceReadyParams, SurfaceInit>(Msg.SurfaceReady, (_, _) => Task.FromResult(new SurfaceInit()));

        await h.CallRawAsync(ui, Msg.SurfaceReady, new SurfaceReadyParams { Surface = SurfaceKind.Menubar, MonitorId = "1" });

        ready.Should().Be(ui.Id);
    }
}
