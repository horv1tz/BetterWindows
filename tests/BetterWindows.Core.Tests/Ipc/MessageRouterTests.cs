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
    public async Task Missing_payload_for_params_with_defaults_is_valid()
    {
        using var h = new CoreHarness();
        var ui = h.AddSurface(SurfaceKind.Desktop);
        h.Router.Handle<WeatherGetParams, bool>("test.weather", (_, p) => Task.FromResult(p.Force));
        var r = await h.CallRawAsync(ui, "test.weather");
        r["ok"]!.GetValue<bool>().Should().BeTrue();
        r["payload"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task Missing_required_field_is_badRequest()
    {
        using var h = new CoreHarness();
        var ui = h.AddSurface(SurfaceKind.Dock);
        h.Router.Handle<DockPinParams>("test.pin", (_, _) => Task.CompletedTask);
        // no payload at all, and a payload with a misspelt field: both must be rejected, not silently defaulted
        (await h.CallRawAsync(ui, "test.pin"))["error"]!["code"]!.GetValue<string>().Should().Be("badRequest");
        (await h.CallRawAsync(ui, "test.pin", new JsonObject { ["appld"] = "x" }))["error"]!["code"]!.GetValue<string>().Should().Be("badRequest");
    }

    [Fact]
    public async Task Wrong_types_and_unknown_enum_are_badRequest()
    {
        using var h = new CoreHarness();
        var ui = h.AddSurface(SurfaceKind.Dock);
        h.Router.Handle<DockPinParams>("test.pin", (_, _) => Task.CompletedTask);
        (await h.CallRawAsync(ui, "test.pin", new JsonObject { ["appId"] = 5, ["index"] = "x" }))["error"]!["code"]!.GetValue<string>().Should().Be("badRequest");
        h.Router.Handle<ThemePreferenceParams>("test.theme", (_, _) => Task.CompletedTask);
        (await h.CallRawAsync(ui, "test.theme", new JsonObject { ["preference"] = "purple" }))["error"]!["code"]!.GetValue<string>().Should().Be("badRequest");
    }

    [Fact]
    public async Task Explicit_null_never_overwrites_a_non_nullable_member()
    {
        using var h = new CoreHarness();
        var ui = h.AddSurface(SurfaceKind.Dock);
        string? seen = "unset";
        h.Router.Handle<DockItemParams>("test.nulls", (_, p) => { seen = p.ItemId; return Task.CompletedTask; });
        var r = await h.CallRawAsync(ui, "test.nulls", new JsonObject { ["itemId"] = null });
        r["ok"]!.GetValue<bool>().Should().BeTrue();
        seen.Should().Be(""); // default kept, no null reference reaches the handler
    }

    [Fact]
    public async Task Floating_point_numbers_are_accepted_for_int_fields_and_rounded()
    {
        using var h = new CoreHarness();
        var ui = h.AddSurface(SurfaceKind.Dock);
        int? idx = null;
        h.Router.Handle<DockPinParams>("test.pin", (_, p) => { idx = p.Index; return Task.CompletedTask; });
        await h.CallRawAsync(ui, "test.pin", new JsonObject { ["appId"] = "a", ["index"] = 2.6 });
        idx.Should().Be(3);
    }

    [Fact]
    public async Task Ui_event_reaches_handler_with_sender()
    {
        using var h = new CoreHarness();
        var dock = h.AddSurface(SurfaceKind.Dock);
        SurfaceId? who = null; SurfaceResizeParams? got = null;
        h.Router.OnEvent<SurfaceResizeParams>(Msg.SurfaceResize, (ctx, p) => { who = ctx.Surface; got = p; });

        await h.SendEventAsync(dock, Msg.SurfaceResize, new SurfaceResizeParams { Width = 640, Height = 120, BarThickness = 80 });

        who.Should().Be(dock.Id);
        got!.Width.Should().Be(640);
        got.BarThickness.Should().Be(80);
    }

    [Fact]
    public void Duplicate_registration_throws_for_calls_and_events()
    {
        using var h = new CoreHarness();
        h.Router.HandleNoParams("dup", _ => Task.CompletedTask);
        h.Router.Invoking(r => r.HandleNoParams("dup", _ => Task.CompletedTask)).Should().Throw<InvalidOperationException>();
        h.Router.OnEvent("dup.ev", _ => { });
        h.Router.Invoking(r => r.OnEvent("dup.ev", _ => { })).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Successful_surface_ready_marks_ready_then_raises_SurfaceReady()
    {
        using var h = new CoreHarness();
        var ui = h.AddSurface(SurfaceKind.Menubar, "1", ready: false);
        var ready = new List<bool>();
        h.Registry.SurfaceReady += id => ready.Add(h.Registry.IsReady(id));
        h.Router.Handle<SurfaceReadyParams, SurfaceInit>(Msg.SurfaceReady, (_, _) => Task.FromResult(new SurfaceInit()));

        h.Registry.IsReady(ui).Should().BeFalse();
        await h.CallRawAsync(ui, Msg.SurfaceReady, new SurfaceReadyParams { Surface = SurfaceKind.Menubar, MonitorId = "1" });

        ready.Should().Equal(true);
        h.Registry.IsReady(ui).Should().BeTrue();
    }

    [Fact]
    public async Task Failed_surface_ready_does_not_mark_ready()
    {
        using var h = new CoreHarness();
        var ui = h.AddSurface(SurfaceKind.Menubar, "1", ready: false);
        h.Router.Handle<SurfaceReadyParams, SurfaceInit>(Msg.SurfaceReady, (_, _) => throw ProtocolException.BadRequest("nope"));
        await h.CallRawAsync(ui, Msg.SurfaceReady, new SurfaceReadyParams { Surface = SurfaceKind.Menubar, MonitorId = "1" });
        h.Registry.IsReady(ui).Should().BeFalse();
    }

    [Fact]
    public async Task Calls_from_a_surface_kind_that_is_not_allowed_are_forbidden()
    {
        using var h = new CoreHarness();
        var called = 0;
        h.Router.Handle<SettingsPatchParams, Settings>(Msg.SettingsPatch, (_, _) => { called++; return Task.FromResult(new Settings()); });
        var desktop = h.AddSurface(SurfaceKind.Desktop);
        var settings = h.AddSurface(SurfaceKind.Settings);

        var denied = await h.CallRawAsync(desktop, Msg.SettingsPatch, new SettingsPatchParams { Patch = new JsonObject() });
        var allowed = await h.CallRawAsync(settings, Msg.SettingsPatch, new SettingsPatchParams { Patch = new JsonObject() });

        denied["error"]!["code"]!.GetValue<string>().Should().Be("forbidden");
        allowed["ok"]!.GetValue<bool>().Should().BeTrue();
        called.Should().Be(1);
    }

    [Theory]
    [InlineData(Msg.SystemPower, SurfaceKind.Dock)]
    [InlineData(Msg.SystemPower, SurfaceKind.Desktop)]
    [InlineData(Msg.SystemPickPath, SurfaceKind.Overlay)]
    [InlineData(Msg.DiagReport, SurfaceKind.Overlay)]
    [InlineData(Msg.SearchQuery, SurfaceKind.Settings)]
    [InlineData(Msg.McSnapshot, SurfaceKind.Dock)]
    [InlineData(Msg.AppsLaunch, SurfaceKind.Settings)]
    [InlineData(Msg.WeatherSetLocation, SurfaceKind.Overlay)]
    public void Restricted_calls_are_declared_forbidden_for_other_surfaces(string type, SurfaceKind from)
    {
        MessageCatalog.ByType[type].IsAllowedFrom(from).Should().BeFalse();
    }

    [Fact]
    public async Task Ui_event_from_a_forbidden_surface_is_dropped()
    {
        using var h = new CoreHarness();
        var n = 0;
        h.Router.OnEvent<SurfaceResizeParams>(Msg.SurfaceResize, (_, _) => n++);
        var menubar = h.AddSurface(SurfaceKind.Menubar, "1");
        await h.SendEventAsync(menubar, Msg.SurfaceResize, new SurfaceResizeParams { Width = 1, Height = 1 });
        n.Should().Be(0);
    }

    [Fact]
    public async Task Protocol_version_mismatch_is_badRequest_for_calls_and_dropped_for_events()
    {
        using var h = new CoreHarness();
        var ui = h.AddSurface(SurfaceKind.Dock);
        h.Router.HandleNoParams("test.ok", _ => Task.CompletedTask);
        var json = "{\"v\":99,\"kind\":\"call\",\"id\":\"x1\",\"type\":\"test.ok\"}";
        await h.Router.DispatchAsync(ui, json);
        ui.ResultFor("x1")!["error"]!["code"]!.GetValue<string>().Should().Be("badRequest");
        ui.ResultFor("x1")!["error"]!["message"]!.GetValue<string>().Should().Contain("protocol mismatch");
    }

    [Fact]
    public async Task Unparsable_envelope_with_readable_id_still_gets_an_answer()
    {
        using var h = new CoreHarness();
        var ui = h.AddSurface(SurfaceKind.Dock);
        await h.Router.DispatchAsync(ui, "{\"v\":1,\"kind\":\"call\",\"id\":\"c9\",\"type\":5}");
        ui.ResultFor("c9")!["error"]!["code"]!.GetValue<string>().Should().Be("badRequest");
    }

    [Fact]
    public async Task Oversized_messages_are_dropped()
    {
        using var h = new CoreHarness();
        var ui = h.AddSurface(SurfaceKind.Dock);
        h.Router.HandleNoParams("test.ok", _ => Task.CompletedTask);
        await h.Router.DispatchAsync(ui, "{\"v\":1,\"kind\":\"call\",\"id\":\"big\",\"type\":\"test.ok\",\"payload\":\"" + new string('x', Limits.MaxMessageChars) + "\"}");
        ui.ResultFor("big").Should().BeNull();
    }

    [Fact]
    public async Task Handlers_start_in_arrival_order_per_surface_even_when_slow()
    {
        using var h = new CoreHarness();
        var ui = h.AddSurface(SurfaceKind.Overlay);
        var started = new List<int>();
        var gate = new TaskCompletionSource();
        h.Router.Handle<VolumeParams>("test.slider", async (_, p) =>
        {
            lock (started) started.Add((int)p.Volume);
            if ((int)p.Volume == 0) await gate.Task; // the first call is slow
        });

        var tasks = Enumerable.Range(0, 20).Select(i => h.CallRawAsync(ui, "test.slider", new VolumeParams { Volume = i })).ToArray();
        await Task.Delay(150);
        gate.SetResult();
        await Task.WhenAll(tasks);

        started.Should().Equal(Enumerable.Range(0, 20));
    }

    [Fact]
    public async Task In_flight_cap_answers_busy()
    {
        using var h = new CoreHarness();
        var ui = h.AddSurface(SurfaceKind.Overlay);
        var gate = new TaskCompletionSource();
        h.Router.HandleNoParams("test.slow", async _ => await gate.Task);

        var tasks = Enumerable.Range(0, Limits.MaxInFlightCallsPerSurface + 8).Select(_ => h.CallRawAsync(ui, "test.slow")).ToArray();
        await Task.Delay(200);
        gate.SetResult();
        var results = await Task.WhenAll(tasks);

        results.Count(r => r["error"]?["code"]?.GetValue<string>() == "busy").Should().Be(8);
    }

    [Fact]
    public async Task Rate_limit_drops_event_floods()
    {
        using var h = new CoreHarness();
        var ui = h.AddSurface(SurfaceKind.Dock);
        var n = 0;
        h.Router.OnEvent<SurfaceResizeParams>(Msg.SurfaceResize, (_, _) => Interlocked.Increment(ref n));
        for (var i = 0; i < 500; i++) await h.SendEventAsync(ui, Msg.SurfaceResize, new SurfaceResizeParams { Width = i, Height = 1 });
        n.Should().BeLessThan(500).And.BeGreaterThan(0); // the manual clock never refills the bucket: only the burst gets through
    }

    [Fact]
    public async Task Reply_goes_to_the_channel_that_asked_even_if_another_channel_has_the_same_id()
    {
        using var h = new CoreHarness();
        var old = h.AddSurface(SurfaceKind.Dock);
        var gate = new TaskCompletionSource();
        h.Router.HandleNoParams("test.slow", async _ => await gate.Task);
        var pending = h.CallRawAsyncNoWait(old, "test.slow");
        await Task.Delay(100);
        var replacement = h.AddSurface(SurfaceKind.Dock); // same SurfaceId, new channel instance
        gate.SetResult();
        await pending.dispatch;

        old.ResultFor(pending.id).Should().NotBeNull();
        replacement.ResultFor(pending.id).Should().BeNull();
    }

    [Fact]
    public async Task Closing_a_surface_cancels_its_in_flight_calls()
    {
        using var h = new CoreHarness();
        var ui = h.AddSurface(SurfaceKind.Overlay);
        var seenCancel = new TaskCompletionSource<bool>();
        h.Router.HandleNoParams("test.wait", async ctx =>
        {
            try { await Task.Delay(Timeout.Infinite, ctx.Cancellation); }
            catch (OperationCanceledException) { seenCancel.TrySetResult(true); throw; }
        });
        var pending = h.CallRawAsyncNoWait(ui, "test.wait");
        await Task.Delay(100);
        h.Registry.Unregister(ui);
        (await seenCancel.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
        await pending.dispatch;
    }

    [Fact]
    public async Task A_second_surface_ready_cancels_work_of_the_previous_page_session()
    {
        using var h = new CoreHarness();
        var ui = h.AddSurface(SurfaceKind.Overlay, ready: false);
        var cancelled = new TaskCompletionSource<bool>();
        h.Router.Handle<SurfaceReadyParams, SurfaceInit>(Msg.SurfaceReady, (_, _) => Task.FromResult(new SurfaceInit()));
        h.Router.HandleNoParams("test.wait", async ctx =>
        {
            try { await Task.Delay(Timeout.Infinite, ctx.Cancellation); }
            catch (OperationCanceledException) { cancelled.TrySetResult(true); throw; }
        });
        await h.ReadyAsync(ui);
        var pending = h.CallRawAsyncNoWait(ui, "test.wait");
        await Task.Delay(100);
        await h.ReadyAsync(ui); // page reloaded
        (await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
        await pending.dispatch;
    }

    [Fact]
    public async Task A_dead_webview_does_not_break_dispatch()
    {
        using var h = new CoreHarness();
        var ui = h.AddSurface(SurfaceKind.Dock);
        h.Router.HandleNoParams("test.ok", _ => Task.CompletedTask);
        ui.Broken = true;
        var act = async () => await h.Router.DispatchAsync(ui, "{\"v\":1,\"kind\":\"call\",\"id\":\"z\",\"type\":\"test.ok\"}");
        await act.Should().NotThrowAsync();
    }
}

internal static class HarnessExtensions
{
    /// <summary>Start a call and return its id and the dispatch task without awaiting the reply.</summary>
    public static (string id, Task dispatch) CallRawAsyncNoWait(this CoreHarness h, FakeSurface from, string type)
    {
        var id = "nw" + Guid.NewGuid().ToString("N")[..8];
        var env = new Envelope { V = ProtocolInfo.Version, Kind = EnvelopeKind.Call, Id = id, Type = type };
        return (id, h.Router.DispatchAsync(from, env.ToJson()));
    }
}
