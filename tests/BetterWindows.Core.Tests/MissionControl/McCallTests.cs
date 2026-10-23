using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Core.Tests.Desktop;
using BetterWindows.Core.Tests.Support;

namespace BetterWindows.Core.Tests.MissionControl;

public class McCallTests
{
    private static McRig WithWindow(long handle = 10, bool minimized = false, bool shell = false, bool elevated = false, string? onDesktop = null, Action<McRig>? more = null)
    {
        var rig = McRig.Start(more);
        rig.Add(FakeWindowTracker.Win(handle, minimized: minimized, cloakedByShell: shell, elevated: elevated));
        if (onDesktop is not null) rig.Desktops.WindowDesktop[handle] = onDesktop;
        return rig;
    }

    /// <summary>Runs a call that waits on the (manual) clock to completion by advancing time.</summary>
    private static async Task<System.Text.Json.Nodes.JsonObject> CallWithClock(McRig rig, string type, object payload)
    {
        var task = rig.Call(type, payload);
        for (var i = 0; i < 400 && !task.IsCompleted; i++) { rig.H.Time.Advance(TimeSpan.FromMilliseconds(30)); await Task.Delay(1); }
        return await task;
    }

    // ---- mc.activateWindow

    [Fact]
    public async Task Activate_a_window_on_the_current_desktop_activates_it_and_closes_the_overlay()
    {
        using var rig = WithWindow(onDesktop: "d1");
        await rig.Ok<object?>(Msg.McActivateWindow, new WindowHandleParams { Handle = "10" });
        rig.Windows.Calls.Should().Equal(("activate", 10L));
        rig.Desktops.Calls.Should().BeEmpty();
        rig.Surfaces.HideCount.Should().Be(1);
    }

    [Fact]
    public async Task Activate_restores_a_minimised_window_first()
    {
        using var rig = WithWindow(minimized: true);
        await rig.Ok<object?>(Msg.McActivateWindow, new WindowHandleParams { Handle = "10" });
        rig.Windows.Calls.Should().Equal(("restore", 10L), ("activate", 10L));
    }

    [Fact]
    public async Task Activate_does_not_restore_a_window_that_is_not_minimised()
    {
        using var rig = WithWindow();
        await rig.Ok<object?>(Msg.McActivateWindow, new WindowHandleParams { Handle = "10" });
        rig.Windows.Calls.Select(c => c.Op).Should().NotContain("restore"); // restore would un-maximise a maximised window
    }

    [Fact]
    public async Task Activate_a_window_on_another_desktop_switches_to_its_desktop_first()
    {
        using var rig = WithWindow(shell: true, onDesktop: "d2");
        await rig.Ok<object?>(Msg.McActivateWindow, new WindowHandleParams { Handle = "10" });
        rig.Desktops.Calls.Should().Equal("switch:d2");
        rig.Windows.Calls.Should().Equal(("activate", 10L));
        rig.Surfaces.HideCount.Should().Be(1);
    }

    [Fact]
    public async Task Activate_on_another_desktop_restores_a_minimised_window_after_the_switch()
    {
        using var rig = WithWindow(shell: true, minimized: true, onDesktop: "d3");
        await rig.Ok<object?>(Msg.McActivateWindow, new WindowHandleParams { Handle = "10" });
        rig.Desktops.Calls.Should().Equal("switch:d3");
        rig.Windows.Calls.Should().Equal(("restore", 10L), ("activate", 10L));
    }

    [Fact]
    public async Task Activate_waits_for_the_shell_to_uncloak_the_window_after_the_switch()
    {
        // the switch "succeeds" but the window stays on the other desktop: the call must not hang and still tries to activate
        using var rig = WithWindow(shell: true, onDesktop: "d2", more: r => r.Desktops.SwitchUpdatesCurrent = false);
        var result = await CallWithClock(rig, Msg.McActivateWindow, new WindowHandleParams { Handle = "10" });
        Kit.Code(result).Should().Be("ok");
        rig.Windows.Calls.Should().Contain(("activate", 10L));
    }

    [Fact]
    public async Task Activate_falls_back_to_keyboard_stepping_when_the_service_cannot_switch()
    {
        using var rig = WithWindow(shell: true, onDesktop: "d2", more: r => r.Desktops.SwitchSucceeds = false);
        var result = await CallWithClock(rig, Msg.McActivateWindow, new WindowHandleParams { Handle = "10" });
        Kit.Code(result).Should().Be("ok");
        rig.Input.Calls.Should().Equal("desktopRight");
    }

    [Fact]
    public async Task Activate_failure_is_reported_and_keeps_the_overlay_open()
    {
        using var rig = WithWindow(more: r => r.Toggle.ActivateSucceeds = false);
        var result = await rig.Call(Msg.McActivateWindow, new WindowHandleParams { Handle = "10" });
        Kit.Code(result).Should().Be("failed");
        rig.Surfaces.HideCount.Should().Be(0);
    }

    [Fact]
    public async Task Activating_an_elevated_window_explains_why_it_failed()
    {
        using var rig = WithWindow(elevated: true, more: r => r.Toggle.ActivateSucceeds = false);
        var result = await rig.Call(Msg.McActivateWindow, new WindowHandleParams { Handle = "10" });
        Kit.Code(result).Should().Be("failed");
        ((string?)result["error"]!["message"]).Should().Contain("administrator");
    }

    [Fact]
    public async Task Activate_of_an_unknown_window_is_not_found()
    {
        using var rig = WithWindow();
        Kit.Code(await rig.Call(Msg.McActivateWindow, new WindowHandleParams { Handle = "9999" })).Should().Be("notFound");
        rig.Windows.Calls.Should().BeEmpty();
        rig.Surfaces.HideCount.Should().Be(0);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("12.5")]
    public async Task Activate_with_an_invalid_handle_is_a_bad_request(string handle)
    {
        using var rig = WithWindow();
        Kit.Code(await rig.Call(Msg.McActivateWindow, new WindowHandleParams { Handle = handle })).Should().Be("badRequest");
    }

    [Fact]
    public async Task A_window_of_ours_can_never_be_activated()
    {
        using var rig = McRig.Start();
        // our own overlay window is not tracked (Find never returns it), even though the window system knows it
        rig.Windows.Windows.Add(FakeWindowTracker.Win(77, pid: 1).Raw with { IsOwnProcess = true });
        Kit.Code(await rig.Call(Msg.McActivateWindow, new WindowHandleParams { Handle = "77" })).Should().Be("notFound");
        rig.Windows.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_recycled_handle_of_another_process_is_not_acted_upon()
    {
        using var rig = WithWindow(handle: 10);
        rig.Windows.Update(10, w => w with { ProcessId = 4242 }); // same HWND value, different process now
        Kit.Code(await rig.Call(Msg.McActivateWindow, new WindowHandleParams { Handle = "10" })).Should().Be("notFound");
        Kit.Code(await rig.Call(Msg.McCloseWindow, new WindowHandleParams { Handle = "10" })).Should().Be("notFound");
        rig.Windows.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_window_that_just_disappeared_is_not_found()
    {
        using var rig = WithWindow(handle: 10);
        rig.Windows.Remove(10);
        Kit.Code(await rig.Call(Msg.McActivateWindow, new WindowHandleParams { Handle = "10" })).Should().Be("notFound");
    }

    // ---- mc.closeWindow

    [Fact]
    public async Task Close_sends_the_close_request_and_keeps_mission_control_open()
    {
        using var rig = WithWindow();
        await rig.Ok<object?>(Msg.McCloseWindow, new WindowHandleParams { Handle = "10" });
        rig.Windows.Calls.Should().Equal(("close", 10L));
        rig.Surfaces.HideCount.Should().Be(0);
    }

    [Fact]
    public async Task Close_failure_is_reported()
    {
        using var rig = WithWindow(more: r => r.Toggle.CloseSucceeds = false);
        Kit.Code(await rig.Call(Msg.McCloseWindow, new WindowHandleParams { Handle = "10" })).Should().Be("failed");
    }

    [Fact]
    public async Task Close_of_an_unknown_window_is_not_found()
    {
        using var rig = WithWindow();
        Kit.Code(await rig.Call(Msg.McCloseWindow, new WindowHandleParams { Handle = "5" })).Should().Be("notFound");
    }

    // ---- mc.switchDesktop

    [Fact]
    public async Task Switch_desktop_uses_the_service_and_closes_the_overlay()
    {
        using var rig = McRig.Start();
        await rig.Ok<object?>(Msg.McSwitchDesktop, new DesktopIdParams { DesktopId = "d2" });
        rig.Desktops.Calls.Should().Equal("switch:d2");
        rig.Input.Calls.Should().BeEmpty();
        rig.Surfaces.HideCount.Should().Be(1);
    }

    [Fact]
    public async Task Switching_to_the_current_desktop_just_closes_the_overlay()
    {
        using var rig = McRig.Start();
        await rig.Ok<object?>(Msg.McSwitchDesktop, new DesktopIdParams { DesktopId = "d1" });
        rig.Desktops.Calls.Should().BeEmpty();
        rig.Surfaces.HideCount.Should().Be(1);
    }

    [Fact]
    public async Task Switch_desktop_steps_with_the_keyboard_when_the_service_refuses()
    {
        using var rig = McRig.Start(r => r.Desktops.SwitchSucceeds = false);
        var result = await CallWithClock(rig, Msg.McSwitchDesktop, new DesktopIdParams { DesktopId = "d3" });
        Kit.Code(result).Should().Be("ok");
        rig.Input.Calls.Should().Equal("desktopRight", "desktopRight");
        rig.Surfaces.HideCount.Should().Be(1);
    }

    [Fact]
    public async Task Keyboard_stepping_goes_left_for_a_lower_desktop()
    {
        using var rig = McRig.Start(r =>
        {
            r.Desktops.SwitchSucceeds = false;
            r.Desktops.Desktops = r.Desktops.Desktops.Select(d => d with { Current = d.Id == "d3" }).ToList();
        });
        var result = await CallWithClock(rig, Msg.McSwitchDesktop, new DesktopIdParams { DesktopId = "d1" });
        Kit.Code(result).Should().Be("ok");
        rig.Input.Calls.Should().Equal("desktopLeft", "desktopLeft");
    }

    [Fact]
    public async Task Switch_desktop_to_an_unknown_desktop_is_not_found()
    {
        using var rig = McRig.Start();
        Kit.Code(await rig.Call(Msg.McSwitchDesktop, new DesktopIdParams { DesktopId = "nope" })).Should().Be("notFound");
        rig.Surfaces.HideCount.Should().Be(0);
    }

    [Fact]
    public async Task Switch_desktop_without_any_desktop_information_is_unsupported()
    {
        using var rig = McRig.Start(r => { r.Desktops.IsSupported = false; r.Desktops.Desktops = new(); });
        Kit.Code(await rig.Call(Msg.McSwitchDesktop, new DesktopIdParams { DesktopId = "d2" })).Should().Be("unsupported");
        rig.Input.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Switch_desktop_on_an_unsupported_service_still_steps_when_the_list_is_known()
    {
        using var rig = McRig.Start(r => r.Desktops.IsSupported = false);
        var result = await CallWithClock(rig, Msg.McSwitchDesktop, new DesktopIdParams { DesktopId = "d2" });
        Kit.Code(result).Should().Be("ok");
        rig.Desktops.Calls.Should().BeEmpty();
        rig.Input.Calls.Should().Equal("desktopRight");
    }

    [Theory]
    [InlineData("")]
    [InlineData("x\u0001y")]
    public async Task Switch_desktop_with_an_invalid_id_is_a_bad_request(string id)
    {
        using var rig = McRig.Start();
        Kit.Code(await rig.Call(Msg.McSwitchDesktop, new DesktopIdParams { DesktopId = id })).Should().Be("badRequest");
    }

    // ---- mc.moveWindow

    [Fact]
    public async Task Move_window_asks_the_service_and_keeps_mission_control_open()
    {
        using var rig = WithWindow(onDesktop: "d1");
        await rig.Ok<object?>(Msg.McMoveWindow, new McMoveParams { Handle = "10", DesktopId = "d2" });
        rig.Desktops.Calls.Should().Equal("move:10:d2");
        rig.Surfaces.HideCount.Should().Be(0);
    }

    [Fact]
    public async Task Move_window_refusal_is_reported_as_unsupported_with_a_clear_message()
    {
        using var rig = WithWindow(more: r => r.Desktops.MoveSucceeds = false);
        var result = await rig.Call(Msg.McMoveWindow, new McMoveParams { Handle = "10", DesktopId = "d2" });
        Kit.Code(result).Should().Be("unsupported");
        ((string?)result["error"]!["message"]).Should().Contain("another desktop");
    }

    [Fact]
    public async Task Move_window_is_unsupported_without_virtual_desktops()
    {
        using var rig = WithWindow(more: r => r.Desktops.IsSupported = false);
        Kit.Code(await rig.Call(Msg.McMoveWindow, new McMoveParams { Handle = "10", DesktopId = "d2" })).Should().Be("unsupported");
        rig.Desktops.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Move_window_validates_the_window_and_the_desktop()
    {
        using var rig = WithWindow();
        Kit.Code(await rig.Call(Msg.McMoveWindow, new McMoveParams { Handle = "999", DesktopId = "d2" })).Should().Be("notFound");
        Kit.Code(await rig.Call(Msg.McMoveWindow, new McMoveParams { Handle = "10", DesktopId = "dX" })).Should().Be("notFound");
        Kit.Code(await rig.Call(Msg.McMoveWindow, new McMoveParams { Handle = "x", DesktopId = "d2" })).Should().Be("badRequest");
        rig.Desktops.Calls.Should().BeEmpty();
    }

    // ---- create / remove

    [Fact]
    public async Task Create_desktop_returns_the_new_desktop()
    {
        using var rig = McRig.Start();
        var d = await rig.Ok<DesktopInfo>(Msg.McCreateDesktop);
        d.Should().Match<DesktopInfo>(x => x.Id == "d4" && x.Index == 3);
        rig.Desktops.Calls.Should().Equal("create");
    }

    [Fact]
    public async Task Create_desktop_failures_are_reported()
    {
        using var unsupported = McRig.Start(r => r.Desktops.IsSupported = false);
        Kit.Code(await unsupported.Call(Msg.McCreateDesktop)).Should().Be("unsupported");
        using var refusing = McRig.Start(r => r.Desktops.CreateSucceeds = false);
        Kit.Code(await refusing.Call(Msg.McCreateDesktop)).Should().Be("failed");
    }

    [Fact]
    public async Task Remove_desktop_removes_it()
    {
        using var rig = McRig.Start();
        await rig.Ok<object?>(Msg.McRemoveDesktop, new DesktopIdParams { DesktopId = "d3" });
        rig.Desktops.Calls.Should().Equal("remove:d3");
        rig.Desktops.Desktops.Select(d => d.Id).Should().Equal("d1", "d2");
    }

    [Fact]
    public async Task Remove_desktop_errors()
    {
        using var rig = McRig.Start();
        Kit.Code(await rig.Call(Msg.McRemoveDesktop, new DesktopIdParams { DesktopId = "nope" })).Should().Be("notFound");

        using var refusing = McRig.Start(r => r.Desktops.RemoveSucceeds = false);
        Kit.Code(await refusing.Call(Msg.McRemoveDesktop, new DesktopIdParams { DesktopId = "d2" })).Should().Be("failed");

        using var last = McRig.Start(r => r.Desktops.Desktops = new() { new DesktopInfo { Id = "d1", Index = 0, Current = true } });
        Kit.Code(await last.Call(Msg.McRemoveDesktop, new DesktopIdParams { DesktopId = "d1" })).Should().Be("failed");
        last.Desktops.Calls.Should().BeEmpty();

        using var unsupported = McRig.Start(r => r.Desktops.IsSupported = false);
        Kit.Code(await unsupported.Call(Msg.McRemoveDesktop, new DesktopIdParams { DesktopId = "d2" })).Should().Be("unsupported");
    }

    [Fact]
    public async Task All_mission_control_calls_are_closed_to_other_surfaces()
    {
        using var rig = WithWindow();
        var desktop = rig.H.AddSurface(SurfaceKind.Desktop, "m1");
        foreach (var type in new[] { Msg.McActivateWindow, Msg.McCloseWindow })
            Kit.Code(await rig.Call(type, new WindowHandleParams { Handle = "10" }, desktop)).Should().Be("forbidden");
        Kit.Code(await rig.Call(Msg.McCreateDesktop, null, desktop)).Should().Be("forbidden");
        Kit.Code(await rig.Call(Msg.McRemoveDesktop, new DesktopIdParams { DesktopId = "d2" }, desktop)).Should().Be("forbidden");
    }
}
