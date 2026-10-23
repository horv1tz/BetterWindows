using BetterWindows.Core.Controllers;
using BetterWindows.Core.Input;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Core.Tests.Status;
using BetterWindows.Core.Tests.Support;
using BetterWindows.Core.Tests.Support.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace BetterWindows.Core.Tests.Input;

public class HotkeyControllerTests
{
    private static CoreHarness NewHarness(Action<IServiceCollection>? more = null) => Kit.Harness<HotkeyController>(sc =>
    {
        sc.AddSingleton<RecordingHotkeyRegistrar>();
        sc.AddSingleton<IHotkeyRegistrar>(sp => sp.GetRequiredService<RecordingHotkeyRegistrar>());
        more?.Invoke(sc);
    });

    private static HotkeyGesture G(string text) => HotkeyParser.Parse(text);

    // ---- registration ----

    [Fact]
    public void Registers_the_five_default_hotkeys_with_the_right_gestures()
    {
        using var h = NewHarness();
        h.StartAll<HotkeyController>();
        var reg = h.Get<RecordingHotkeyRegistrar>().Registered;
        reg.Should().HaveCount(5);
        reg["spotlight"].Should().Be(G("Alt+Space"));
        reg["launchpad"].Should().Be(G("Ctrl+Alt+L"));
        reg["missionControl"].Should().Be(G("Ctrl+Alt+M"));
        reg["exit"].Should().Be(G("Ctrl+Alt+Q"));
        reg["toggleTaskbar"].Should().Be(G("Ctrl+Alt+Shift+T"));
    }

    [Fact]
    public void The_first_pass_runs_inside_Start_so_the_escape_hotkey_is_known_immediately()
    {
        using var h = NewHarness();
        h.StartAll<HotkeyController>();
        h.Get<IHotkeyStatus>().EscapeHotkeyRegistered.Should().BeTrue();
    }

    [Fact]
    public void Every_registration_is_reported_with_the_canonical_gesture()
    {
        using var h = NewHarness();
        h.StartAll<HotkeyController>();
        var items = h.Get<IHotkeyStatus>().Items.ToDictionary(i => i.Id);
        items.Keys.Should().BeEquivalentTo("spotlight", "launchpad", "missionControl", "exit", "toggleTaskbar");
        items.Values.Should().OnlyContain(i => i.Registered && i.Error == null);
        items["exit"].Gesture.Should().Be("Ctrl+Alt+Q");
        items["spotlight"].Gesture.Should().Be("Alt+Space");
    }

    [Fact]
    public void A_gesture_taken_by_another_app_is_reported_and_remembered_not_thrown()
    {
        using var h = NewHarness(sc => sc.AddSingleton<RecordingHotkeyRegistrar>(_ =>
        {
            var r = new RecordingHotkeyRegistrar();
            r.Taken.Add(G("Alt+Space"));
            return r;
        }));
        h.StartAll<HotkeyController>();
        var reg = h.Get<RecordingHotkeyRegistrar>();
        reg.Registered.Should().NotContainKey("spotlight");
        reg.Registered.Should().HaveCount(4);
        var item = h.Get<IHotkeyStatus>().Items.Single(i => i.Id == "spotlight");
        item.Registered.Should().BeFalse();
        item.Gesture.Should().Be("Alt+Space");
        item.Error.Should().Contain("in use");
    }

    [Fact]
    public void Escape_hotkey_status_is_false_when_both_escape_hotkeys_are_taken()
    {
        using var h = NewHarness(sc => sc.AddSingleton<RecordingHotkeyRegistrar>(_ =>
        {
            var r = new RecordingHotkeyRegistrar();
            r.Taken.Add(G("Ctrl+Alt+Q"));
            r.Taken.Add(G("Ctrl+Alt+Shift+T"));
            return r;
        }));
        h.StartAll<HotkeyController>();
        h.Get<IHotkeyStatus>().EscapeHotkeyRegistered.Should().BeFalse();
    }

    [Fact]
    public void One_escape_hotkey_is_enough()
    {
        using var h = NewHarness(sc => sc.AddSingleton<RecordingHotkeyRegistrar>(_ =>
        {
            var r = new RecordingHotkeyRegistrar();
            r.Taken.Add(G("Ctrl+Alt+Q"));
            return r;
        }));
        h.StartAll<HotkeyController>();
        h.Get<IHotkeyStatus>().EscapeHotkeyRegistered.Should().BeTrue();
    }

    [Fact]
    public void An_unparsable_hotkey_is_reported_without_touching_the_registrar()
    {
        using var h = NewHarness();
        h.Settings().Set(s => s with { Hotkeys = s.Hotkeys with { Launchpad = "Ctrl+Nonsense" } });
        h.StartAll<HotkeyController>();
        var reg = h.Get<RecordingHotkeyRegistrar>();
        reg.Registered.Should().NotContainKey("launchpad");
        reg.Snapshot().Should().NotContain(l => l.StartsWith("register:launchpad"));
        var item = h.Get<IHotkeyStatus>().Items.Single(i => i.Id == "launchpad");
        item.Registered.Should().BeFalse();
        item.Gesture.Should().Be("Ctrl+Nonsense");
        item.Error.Should().Contain("valid");
    }

    [Fact]
    public void A_hostile_hotkey_string_is_truncated_and_stripped_in_the_report()
    {
        using var h = NewHarness();
        h.Settings().Set(s => s with { Hotkeys = s.Hotkeys with { Launchpad = "Ctrl+\u0007" + new string('x', 500) } });
        h.StartAll<HotkeyController>();
        var item = h.Get<IHotkeyStatus>().Items.Single(i => i.Id == "launchpad");
        item.Gesture.Length.Should().BeLessThanOrEqualTo(HotkeyParser.MaxLength);
        item.Gesture.Should().NotContain("\u0007");
    }

    [Fact]
    public void An_empty_hotkey_means_unassigned_and_is_not_registered_or_reported()
    {
        using var h = NewHarness();
        h.Settings().Set(s => s with { Spotlight = s.Spotlight with { Hotkey = "" } });
        h.StartAll<HotkeyController>();
        h.Get<RecordingHotkeyRegistrar>().Registered.Should().NotContainKey("spotlight");
        h.Get<IHotkeyStatus>().Items.Should().NotContain(i => i.Id == "spotlight");
    }

    [Fact]
    public void Duplicate_gestures_are_refused_and_the_escape_hotkeys_win()
    {
        using var h = NewHarness();
        h.Settings().Set(s => s with { Hotkeys = s.Hotkeys with { Launchpad = "ctrl + alt + q" } });   // same as exit, different spelling
        h.StartAll<HotkeyController>();
        var reg = h.Get<RecordingHotkeyRegistrar>();
        reg.Registered["exit"].Should().Be(G("Ctrl+Alt+Q"));
        reg.Registered.Should().NotContainKey("launchpad");
        reg.Snapshot().Should().NotContain(l => l.StartsWith("register:launchpad"));
        var item = h.Get<IHotkeyStatus>().Items.Single(i => i.Id == "launchpad");
        item.Registered.Should().BeFalse();
        item.Error.Should().Contain("exit");
    }

    [Fact]
    public void A_registrar_that_throws_is_survived_and_reported()
    {
        using var h = NewHarness();
        h.Get<RecordingHotkeyRegistrar>().ThrowOnRegister = true;
        h.StartAll<HotkeyController>();
        h.Get<IHotkeyStatus>().Items.Should().HaveCount(5).And.OnlyContain(i => !i.Registered && i.Error != null);
        h.Get<IHotkeyStatus>().EscapeHotkeyRegistered.Should().BeFalse();
    }

    // ---- changes ----

    [Fact]
    public async Task Changing_a_hotkey_unregisters_the_old_gesture_before_registering_the_new_one()
    {
        using var h = NewHarness();
        h.StartAll<HotkeyController>();
        var reg = h.Get<RecordingHotkeyRegistrar>();
        var before = reg.Snapshot().Length;

        h.Settings().Set(s => s with { Hotkeys = s.Hotkeys with { Launchpad = "Ctrl+Alt+K" } });
        await Kit.Eventually(() => reg.Registered.TryGetValue("launchpad", out var g) && g == G("Ctrl+Alt+K"));

        var after = reg.Snapshot().Skip(before).ToArray();
        after.Should().Equal("unregister:launchpad", $"register:launchpad:{HotkeyGesture.Control | HotkeyGesture.Alt}:{(uint)'K'}");
        h.Get<IHotkeyStatus>().Items.Single(i => i.Id == "launchpad").Gesture.Should().Be("Ctrl+Alt+K");
    }

    [Fact]
    public async Task Swapping_two_gestures_works_because_all_removals_happen_first()
    {
        using var h = NewHarness();
        h.StartAll<HotkeyController>();
        var reg = h.Get<RecordingHotkeyRegistrar>();
        var before = reg.Snapshot().Length;

        h.Settings().Set(s => s with { Hotkeys = s.Hotkeys with { Launchpad = "Ctrl+Alt+M", MissionControl = "Ctrl+Alt+L" } });
        await Kit.Eventually(() => reg.Registered["launchpad"] == G("Ctrl+Alt+M") && reg.Registered["missionControl"] == G("Ctrl+Alt+L"));

        var log = reg.Snapshot().Skip(before).ToArray();
        log.Take(2).Should().OnlyContain(l => l.StartsWith("unregister:"));
        log.Skip(2).Should().OnlyContain(l => l.StartsWith("register:"));
        h.Get<IHotkeyStatus>().Items.Should().OnlyContain(i => i.Registered);
    }

    [Fact]
    public async Task Unrelated_settings_changes_do_not_touch_the_registrar()
    {
        using var h = NewHarness();
        h.StartAll<HotkeyController>();
        var reg = h.Get<RecordingHotkeyRegistrar>();
        var before = reg.Snapshot().Length;
        var statusChanges = 0;
        h.Get<IHotkeyStatus>().Changed += () => statusChanges++;

        h.Settings().Set(s => s with { Dock = s.Dock with { IconSize = 90 } });
        await Kit.Settle();

        reg.Snapshot().Length.Should().Be(before);
        statusChanges.Should().Be(0);
    }

    [Fact]
    public async Task Clearing_a_hotkey_unregisters_it_and_clears_its_status()
    {
        using var h = NewHarness();
        h.StartAll<HotkeyController>();
        var reg = h.Get<RecordingHotkeyRegistrar>();

        h.Settings().Set(s => s with { Hotkeys = s.Hotkeys with { MissionControl = "" } });
        await Kit.Eventually(() => !reg.Registered.ContainsKey("missionControl"));

        h.Get<IHotkeyStatus>().Items.Should().NotContain(i => i.Id == "missionControl");
        reg.Registered.Should().HaveCount(4);
    }

    [Fact]
    public async Task Breaking_a_hotkey_unregisters_the_previous_registration()
    {
        using var h = NewHarness();
        h.StartAll<HotkeyController>();
        var reg = h.Get<RecordingHotkeyRegistrar>();

        h.Settings().Set(s => s with { Hotkeys = s.Hotkeys with { MissionControl = "garbage" } });
        await Kit.Eventually(() => !reg.Registered.ContainsKey("missionControl"));

        var item = h.Get<IHotkeyStatus>().Items.Single(i => i.Id == "missionControl");
        item.Registered.Should().BeFalse();
    }

    [Fact]
    public async Task A_hotkey_that_was_taken_is_retried_on_the_next_pass_and_the_status_updates()
    {
        using var h = NewHarness();
        var reg = h.Get<RecordingHotkeyRegistrar>();
        reg.Taken.Add(G("Alt+Space"));
        h.StartAll<HotkeyController>();
        h.Get<IHotkeyStatus>().Items.Single(i => i.Id == "spotlight").Registered.Should().BeFalse();

        reg.Taken.Clear();                                                  // the other app let go
        h.Settings().Set(s => s with { Dock = s.Dock with { IconSize = 80 } });
        await Kit.Eventually(() => reg.Registered.ContainsKey("spotlight"));

        h.Get<IHotkeyStatus>().Items.Single(i => i.Id == "spotlight").Registered.Should().BeTrue();
    }

    [Fact]
    public async Task Failed_retries_do_not_spam_the_status_listeners()
    {
        using var h = NewHarness();
        var reg = h.Get<RecordingHotkeyRegistrar>();
        reg.Taken.Add(G("Alt+Space"));
        h.StartAll<HotkeyController>();
        var changes = 0;
        h.Get<IHotkeyStatus>().Changed += () => changes++;

        for (var i = 0; i < 3; i++)
        {
            var seen = reg.Snapshot().Length;
            h.Settings().Set(s => s with { Dock = s.Dock with { IconSize = 70 + i } });
            await Kit.Eventually(() => reg.Snapshot().Length > seen);       // a retry happened
        }
        changes.Should().Be(0);
    }

    [Fact]
    public async Task Rapid_changes_converge_to_the_newest_settings()
    {
        using var h = NewHarness();
        h.StartAll<HotkeyController>();
        var reg = h.Get<RecordingHotkeyRegistrar>();
        foreach (var key in new[] { "A", "B", "C", "D", "E", "F" })
            h.Settings().Set(s => s with { Hotkeys = s.Hotkeys with { Launchpad = "Ctrl+Alt+" + key } });
        await Kit.Eventually(() => reg.Registered.TryGetValue("launchpad", out var g) && g == G("Ctrl+Alt+F"));
    }

    // ---- presses ----

    [Fact]
    public async Task Pressing_spotlight_toggles_the_overlay()
    {
        using var h = NewHarness();
        h.StartAll<HotkeyController>();
        var surfaces = h.Get<FakeSurfaceManager>();

        h.Get<RecordingHotkeyRegistrar>().Press("spotlight");
        await Kit.Eventually(() => surfaces.Shown.Count == 1);
        surfaces.Shown[0].Request.Mode.Should().Be(OverlayMode.Spotlight);
        surfaces.Shown[0].Source.Should().BeNull("hotkeys have no source surface");

        h.Get<RecordingHotkeyRegistrar>().Press("spotlight");
        await Kit.Eventually(() => surfaces.HideCount == 1);
        surfaces.Shown.Should().HaveCount(1);
    }

    [Theory]
    [InlineData("launchpad", OverlayMode.Launchpad)]
    [InlineData("missionControl", OverlayMode.MissionControl)]
    public async Task Pressing_launchpad_or_mission_control_shows_that_mode(string id, OverlayMode mode)
    {
        using var h = NewHarness();
        h.StartAll<HotkeyController>();
        h.Get<RecordingHotkeyRegistrar>().Press(id);
        await Kit.Eventually(() => h.Get<FakeSurfaceManager>().Shown.Count == 1);
        h.Get<FakeSurfaceManager>().Shown[0].Request.Mode.Should().Be(mode);
    }

    [Fact]
    public async Task Pressing_exit_quits_the_app_through_the_lifecycle_service()
    {
        using var h = NewHarness();
        h.StartAll<HotkeyController>();
        h.Get<RecordingHotkeyRegistrar>().Press("exit");
        await Kit.Eventually(() => h.Get<FakeAppLifecycle>().Exits == 1);
        h.Get<FakeAppLifecycle>().Restarts.Should().Be(0);
    }

    [Fact]
    public async Task Toggle_taskbar_hides_a_visible_taskbar_temporarily_and_shows_it_again()
    {
        using var h = NewHarness();
        h.StartAll<HotkeyController>();
        var taskbar = h.Get<FakeTaskbarController>();
        taskbar.Current.Should().Be(TaskbarMode.Visible);                   // fake default

        h.Get<RecordingHotkeyRegistrar>().Press("toggleTaskbar");
        await Kit.Eventually(() => taskbar.Current == TaskbarMode.Hidden);
        h.Get<RecordingHotkeyRegistrar>().Press("toggleTaskbar");
        await Kit.Eventually(() => taskbar.Current == TaskbarMode.Visible);

        taskbar.Calls.Should().Equal("apply:Hidden", "apply:Visible");
        h.Settings().Current.Taskbar.Mode.Should().Be(TaskbarMode.Hidden, "the toggle is temporary and never writes settings");
        h.Settings().Patches.Should().BeEmpty();
    }

    [Fact]
    public async Task Toggle_taskbar_returns_to_the_configured_autoHide_mode()
    {
        using var h = NewHarness();
        h.Settings().Set(s => s with { Taskbar = new TaskbarSettings { Mode = TaskbarMode.AutoHide } });
        h.StartAll<HotkeyController>();
        var taskbar = h.Get<FakeTaskbarController>();

        h.Get<RecordingHotkeyRegistrar>().Press("toggleTaskbar");       // Visible -> configured AutoHide
        await Kit.Eventually(() => taskbar.Current == TaskbarMode.AutoHide);
        h.Get<RecordingHotkeyRegistrar>().Press("toggleTaskbar");       // anything else -> Visible
        await Kit.Eventually(() => taskbar.Current == TaskbarMode.Visible);
    }

    [Fact]
    public async Task Toggle_taskbar_with_configured_visible_hides()
    {
        using var h = NewHarness();
        h.Settings().Set(s => s with { Taskbar = new TaskbarSettings { Mode = TaskbarMode.Visible } });
        h.StartAll<HotkeyController>();
        var taskbar = h.Get<FakeTaskbarController>();
        h.Get<RecordingHotkeyRegistrar>().Press("toggleTaskbar");
        await Kit.Eventually(() => taskbar.Current == TaskbarMode.Hidden);
    }

    [Fact]
    public async Task Unknown_press_ids_are_ignored_and_a_failing_action_does_not_break_later_presses()
    {
        using var h = NewHarness(sc => sc.AddSingleton<ISystemPower, ThrowingPower>());
        h.StartAll<HotkeyController>();
        var registrar = h.Get<RecordingHotkeyRegistrar>();
        registrar.Press("nonsense");
        registrar.Press("");
        registrar.Press("spotlight");
        await Kit.Eventually(() => h.Get<FakeSurfaceManager>().Shown.Count == 1);
        h.Get<FakeSurfaceManager>().Shown.Should().HaveCount(1);
    }

    private sealed class ThrowingPower : ISystemPower { public void Perform(PowerAction action) => throw new InvalidOperationException("boom"); }

    // ---- lifecycle ----

    [Fact]
    public void Stop_unregisters_everything_clears_the_status_and_ignores_later_presses()
    {
        using var h = NewHarness();
        var c = h.StartAll<HotkeyController>();
        c.Stop();
        h.Get<RecordingHotkeyRegistrar>().Registered.Should().BeEmpty();
        h.Get<IHotkeyStatus>().Items.Should().BeEmpty();
        h.Get<IHotkeyStatus>().EscapeHotkeyRegistered.Should().BeFalse();
        h.Get<RecordingHotkeyRegistrar>().HasPressedSubscribers.Should().BeFalse();
    }

    [Fact]
    public async Task After_Stop_settings_changes_no_longer_register_anything()
    {
        using var h = NewHarness();
        var c = h.StartAll<HotkeyController>();
        c.Stop();
        h.Settings().Set(s => s with { Hotkeys = s.Hotkeys with { Launchpad = "Ctrl+Alt+K" } });
        await Kit.Settle();
        h.Get<RecordingHotkeyRegistrar>().Registered.Should().BeEmpty();
    }

    [Fact]
    public void Start_twice_registers_once()
    {
        using var h = NewHarness();
        var c = h.StartAll<HotkeyController>();
        var calls = h.Get<RecordingHotkeyRegistrar>().Snapshot().Length;
        c.Start();
        h.Get<RecordingHotkeyRegistrar>().Snapshot().Length.Should().Be(calls);
    }

    [Fact]
    public void HotkeyBindings_maps_all_five_settings_fields()
    {
        var s = new Settings
        {
            Spotlight = new SpotlightSettings { Hotkey = "S" },
            Hotkeys = new HotkeySettings { Launchpad = "L", MissionControl = "M", Exit = "E", ToggleTaskbar = "T" },
        };
        HotkeyBindings.From(s).Should().Equal(
            new KeyValuePair<string, string>("exit", "E"), new KeyValuePair<string, string>("toggleTaskbar", "T"),
            new KeyValuePair<string, string>("spotlight", "S"), new KeyValuePair<string, string>("launchpad", "L"), new KeyValuePair<string, string>("missionControl", "M"));
        HotkeyIds.All.Should().Equal("exit", "toggleTaskbar", "spotlight", "launchpad", "missionControl");
    }
}
