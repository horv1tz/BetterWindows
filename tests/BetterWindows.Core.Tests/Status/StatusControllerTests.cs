using BetterWindows.Core.Controllers;
using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Tests.Support;
using BetterWindows.Core.Tests.Support.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace BetterWindows.Core.Tests.Status;

public class StatusControllerTests
{
    private static readonly TimeSpan Coalesce = TimeSpan.FromMilliseconds(100);

    private static CoreHarness NewHarness() => Kit.Harness<StatusController>();

    private static readonly string[] AllStatusEvents = { Msg.StatusBattery, Msg.StatusNetwork, Msg.StatusAudio, Msg.StatusBrightness, Msg.StatusBluetooth };

    // ---- initial push per surface kind ----

    [Theory]
    [InlineData(SurfaceKind.Menubar, "status.battery,status.network,status.audio,status.brightness,status.bluetooth")]
    [InlineData(SurfaceKind.Overlay, "status.battery,status.network,status.audio,status.brightness,status.bluetooth")]
    [InlineData(SurfaceKind.Desktop, "status.battery,status.bluetooth")]
    [InlineData(SurfaceKind.Dock, "")]
    [InlineData(SurfaceKind.Settings, "")]
    public void A_surface_that_just_became_ready_gets_exactly_the_status_events_its_kind_consumes(SurfaceKind kind, string expected)
    {
        using var h = NewHarness();
        h.StartAll<StatusController>();
        var s = h.AddSurface(kind, ready: false);
        h.Ready(s);
        var got = s.All.Where(e => (string?)e["kind"] == "event").Select(e => (string)e["type"]!).OrderBy(t => t).ToArray();
        got.Should().Equal(expected.Split(',', StringSplitOptions.RemoveEmptyEntries).OrderBy(t => t));
    }

    [Fact]
    public void The_initial_events_carry_the_current_port_values()
    {
        using var h = NewHarness();
        h.StartAll<StatusController>();
        var s = h.Ready(h.AddSurface(SurfaceKind.Menubar, "m1", ready: false));
        s.LastEvent<BatteryState>(Msg.StatusBattery).Should().BeEquivalentTo(new BatteryState { Present = true, Percent = 80, AcOnline = true });
        s.LastEvent<NetworkState>(Msg.StatusNetwork)!.Name.Should().Be("Home");
        s.LastEvent<AudioState>(Msg.StatusAudio)!.Volume.Should().Be(0.5);
        s.LastEvent<BrightnessState>(Msg.StatusBrightness)!.Level.Should().Be(0.6);
        s.LastEvent<BluetoothState>(Msg.StatusBluetooth)!.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Every_menubar_monitor_gets_the_events()
    {
        using var h = NewHarness();
        h.StartAll<StatusController>();
        var a = h.AddSurface(SurfaceKind.Menubar, "m1");
        var b = h.AddSurface(SurfaceKind.Menubar, "m2");
        h.Get<FakeBatterySource>().Current = new BatteryState { Present = true, Percent = 20 };
        h.Get<FakeBatterySource>().RaiseChanged();
        h.Time.Advance(Coalesce);
        a.LastEvent<BatteryState>(Msg.StatusBattery)!.Percent.Should().Be(20);
        b.LastEvent<BatteryState>(Msg.StatusBattery)!.Percent.Should().Be(20);
    }

    // ---- changes, coalescing, dedupe ----

    [Fact]
    public void A_port_change_is_published_after_the_coalescing_window()
    {
        using var h = NewHarness();
        h.StartAll<StatusController>();
        var s = h.AddSurface(SurfaceKind.Menubar, "m1");
        s.Clear();
        h.Get<FakeBatterySource>().Current = new BatteryState { Present = true, Percent = 55, Charging = true };
        h.Get<FakeBatterySource>().RaiseChanged();
        h.Time.Advance(TimeSpan.FromMilliseconds(99));
        s.Events(Msg.StatusBattery).Should().BeEmpty("coalescing is 100 ms");
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        s.Events(Msg.StatusBattery).Should().ContainSingle();
        s.LastEvent<BatteryState>(Msg.StatusBattery)!.Charging.Should().BeTrue();
    }

    [Fact]
    public void A_burst_of_changes_inside_the_window_publishes_only_the_newest_state()
    {
        using var h = NewHarness();
        h.StartAll<StatusController>();
        var s = h.AddSurface(SurfaceKind.Menubar, "m1");
        s.Clear();
        var net = h.Get<FakeNetworkSource>();
        for (var bars = 0; bars <= 4; bars++)
        {
            net.Current = new NetworkState { Kind = NetworkKind.Wifi, Connected = true, Name = "Home", SignalBars = bars };
            net.RaiseChanged();
            h.Time.Advance(TimeSpan.FromMilliseconds(10));
        }
        h.Time.Advance(Coalesce);
        s.Events(Msg.StatusNetwork).Should().ContainSingle();
        s.LastEvent<NetworkState>(Msg.StatusNetwork)!.SignalBars.Should().Be(4);
    }

    [Fact]
    public void Identical_consecutive_states_are_not_posted_again()
    {
        using var h = NewHarness();
        h.StartAll<StatusController>();
        var s = h.AddSurface(SurfaceKind.Menubar, "m1");
        s.Clear();
        h.Get<FakeBatterySource>().RaiseChanged();
        h.Get<FakeNetworkSource>().RaiseChanged();
        h.Get<FakeAudioEndpoint>().RaiseChanged();
        h.Time.Advance(Coalesce);
        s.All.Where(e => (string?)e["kind"] == "event").Should().BeEmpty();
    }

    [Fact]
    public void Each_topic_is_independent()
    {
        using var h = NewHarness();
        h.StartAll<StatusController>();
        var s = h.AddSurface(SurfaceKind.Overlay);
        s.Clear();
        h.Get<FakeBrightnessControl>().Current = new BrightnessState { Supported = true, Level = 0.1 };
        h.Get<FakeBrightnessControl>().RaiseChanged();
        h.Time.Advance(Coalesce);
        s.All.Select(e => (string?)e["type"]).Where(t => t is not null).Should().Equal(Msg.StatusBrightness);
    }

    [Fact]
    public void All_five_topics_update_on_their_own_port_event()
    {
        using var h = NewHarness();
        h.StartAll<StatusController>();
        var s = h.AddSurface(SurfaceKind.Overlay);
        s.Clear();
        h.Get<FakeBatterySource>().Current = new BatteryState { Present = true, Percent = 11 };
        h.Get<FakeNetworkSource>().Current = new NetworkState { Kind = NetworkKind.Ethernet, Connected = true };
        h.Get<FakeAudioEndpoint>().Current = new AudioState { Volume = 0.9, Muted = true };
        h.Get<FakeBrightnessControl>().Current = new BrightnessState { Supported = false };
        h.Get<FakeBluetoothSource>().Current = new BluetoothState { Supported = true, Enabled = false };
        h.Get<FakeBatterySource>().RaiseChanged();
        h.Get<FakeNetworkSource>().RaiseChanged();
        h.Get<FakeAudioEndpoint>().RaiseChanged();
        h.Get<FakeBrightnessControl>().RaiseChanged();
        h.Get<FakeBluetoothSource>().RaiseChanged();
        h.Time.Advance(Coalesce);
        foreach (var type in AllStatusEvents) s.Events(type).Should().ContainSingle(type);
        s.LastEvent<AudioState>(Msg.StatusAudio)!.Muted.Should().BeTrue();
        s.LastEvent<NetworkState>(Msg.StatusNetwork)!.Kind.Should().Be(NetworkKind.Ethernet);
    }

    [Fact]
    public void Dock_and_settings_surfaces_never_receive_status_events()
    {
        using var h = NewHarness();
        h.StartAll<StatusController>();
        var dock = h.AddSurface(SurfaceKind.Dock);
        var settings = h.AddSurface(SurfaceKind.Settings);
        h.Get<FakeBatterySource>().Current = new BatteryState { Present = true, Percent = 1 };
        h.Get<FakeBatterySource>().RaiseChanged();
        h.Time.Advance(Coalesce);
        dock.All.Should().BeEmpty();
        settings.All.Should().BeEmpty();
    }

    [Fact]
    public void Port_values_are_sanitised_before_they_reach_the_wire()
    {
        using var h = NewHarness();
        h.StartAll<StatusController>();
        h.Get<FakeBatterySource>().Current = new BatteryState { Present = true, Percent = 250, MinutesRemaining = -5 };
        h.Get<FakeBrightnessControl>().Current = new BrightnessState { Supported = true, Level = 7 };
        h.Get<FakeAudioEndpoint>().Current = new AudioState { Volume = double.NaN, DeviceName = new string('x', 5000) };
        h.Get<FakeNetworkSource>().Current = new NetworkState { Kind = NetworkKind.Wifi, Connected = true, SignalBars = 99, Name = "ev\u0007il" };
        var s = h.Ready(h.AddSurface(SurfaceKind.Overlay, ready: false));
        s.LastEvent<BatteryState>(Msg.StatusBattery).Should().Match<BatteryState>(b => b.Percent == 100 && b.MinutesRemaining == null);
        s.LastEvent<BrightnessState>(Msg.StatusBrightness)!.Level.Should().Be(1);
        s.LastEvent<AudioState>(Msg.StatusAudio).Should().Match<AudioState>(a => a.Volume == 0 && a.DeviceName!.Length == 256);
        s.LastEvent<NetworkState>(Msg.StatusNetwork).Should().Match<NetworkState>(n => n.SignalBars == 4 && n.Name == "ev il");
    }

    // ---- audio.setVolume / setMuted ----

    [Theory]
    [InlineData(0.25, 0.25)]
    [InlineData(0.0, 0.0)]
    [InlineData(1.0, 1.0)]
    [InlineData(1.7, 1.0)]
    [InlineData(-0.4, 0.0)]
    [InlineData(1e9, 1.0)]
    public async Task SetVolume_clamps_to_zero_one(double requested, double applied)
    {
        using var h = NewHarness();
        h.StartAll<StatusController>();
        var s = h.AddSurface(SurfaceKind.Overlay);
        var r = await h.CallRawAsync(s, Msg.AudioSetVolume, new VolumeParams { Volume = requested });
        Kit.IsOk(r).Should().BeTrue();
        h.Get<FakeAudioEndpoint>().Calls.Should().Equal($"volume:{applied.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
    }

    [Fact]
    public async Task SetVolume_without_a_volume_is_a_badRequest_and_touches_nothing()
    {
        using var h = NewHarness();
        h.StartAll<StatusController>();
        var s = h.AddSurface(SurfaceKind.Overlay);
        var r = await h.CallRawAsync(s, Msg.AudioSetVolume, new { });
        Kit.ErrorCode(r).Should().Be("badRequest");
        r = await h.CallRawAsync(s, Msg.AudioSetVolume, new { volume = "loud" });
        Kit.ErrorCode(r).Should().Be("badRequest");
        h.Get<FakeAudioEndpoint>().Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task SetVolume_is_visible_to_the_ui_within_one_coalescing_window()
    {
        using var h = NewHarness();
        h.StartAll<StatusController>();
        var s = h.AddSurface(SurfaceKind.Overlay);
        s.Clear();
        await h.CallAsync<object>(s, Msg.AudioSetVolume, new VolumeParams { Volume = 0.8 });
        h.Time.Advance(Coalesce);
        s.LastEvent<AudioState>(Msg.StatusAudio)!.Volume.Should().Be(0.8);
    }

    [Fact]
    public async Task SetMuted_forwards_the_flag()
    {
        using var h = NewHarness();
        h.StartAll<StatusController>();
        var s = h.AddSurface(SurfaceKind.Settings);
        Kit.IsOk(await h.CallRawAsync(s, Msg.AudioSetMuted, new MuteParams { Muted = true })).Should().BeTrue();
        Kit.IsOk(await h.CallRawAsync(s, Msg.AudioSetMuted, new MuteParams { Muted = false })).Should().BeTrue();
        h.Get<FakeAudioEndpoint>().Calls.Should().Equal("muted:True", "muted:False");
    }

    [Fact]
    public async Task A_platform_failure_becomes_failed_without_leaking_the_exception_text()
    {
        using var h = Kit.Harness<StatusController>(sc => sc.AddSingleton<IAudioEndpoint, ExplodingAudio>());
        h.StartAll<StatusController>();
        var s = h.AddSurface(SurfaceKind.Overlay);
        var r = await h.CallRawAsync(s, Msg.AudioSetVolume, new VolumeParams { Volume = 0.5 });
        Kit.ErrorCode(r).Should().Be("failed");
        ((string?)Kit.ErrorOf(r)!["message"]).Should().NotContain("SECRET");
    }

    private sealed class ExplodingAudio : IAudioEndpoint
    {
        public AudioState Current => new();
        public event Action? Changed { add { } remove { } }
        public void SetVolume(double volume01) => throw new InvalidOperationException("SECRET C:\\Users\\me\\device");
        public void SetMuted(bool muted) => throw new InvalidOperationException("SECRET");
        public bool CanSetDefaultDevice => true;
        public bool SetDefaultDevice(string deviceId) => throw new InvalidOperationException("SECRET");
    }

    // ---- authorization (enforced by the router from the protocol) ----

    [Theory]
    [InlineData(SurfaceKind.Overlay, true)]
    [InlineData(SurfaceKind.Settings, true)]
    [InlineData(SurfaceKind.Menubar, false)]
    [InlineData(SurfaceKind.Dock, false)]
    [InlineData(SurfaceKind.Desktop, false)]
    public async Task Audio_calls_are_only_allowed_from_overlay_and_settings(SurfaceKind kind, bool allowed)
    {
        using var h = NewHarness();
        h.StartAll<StatusController>();
        var s = h.AddSurface(kind);
        var r = await h.CallRawAsync(s, Msg.AudioSetMuted, new MuteParams { Muted = true });
        if (allowed) Kit.IsOk(r).Should().BeTrue();
        else Kit.ErrorCode(r).Should().Be("forbidden");
        h.Get<FakeAudioEndpoint>().Calls.Count.Should().Be(allowed ? 1 : 0);
    }

    [Theory]
    [InlineData(Msg.BrightnessSet)]
    [InlineData(Msg.ThemeSetPreference)]
    [InlineData(Msg.AudioSetDevice)]
    [InlineData(Msg.AudioSetVolume)]
    public async Task Brightness_theme_and_device_calls_are_forbidden_from_the_menubar_and_dock(string type)
    {
        using var h = NewHarness();
        h.StartAll<StatusController>();
        foreach (var kind in new[] { SurfaceKind.Menubar, SurfaceKind.Dock, SurfaceKind.Desktop })
        {
            var s = h.AddSurface(kind);
            Kit.ErrorCode(await h.CallRawAsync(s, type, new { level = 0.5, preference = "dark", deviceId = "dev1", volume = 0.5 })).Should().Be("forbidden");
        }
    }

    // ---- audio.setDevice ----

    [Fact]
    public async Task SetDevice_switches_to_a_listed_device()
    {
        using var h = NewHarness();
        h.StartAll<StatusController>();
        var s = h.AddSurface(SurfaceKind.Overlay);
        Kit.IsOk(await h.CallRawAsync(s, Msg.AudioSetDevice, new AudioDeviceParams { DeviceId = "dev1" })).Should().BeTrue();
        h.Get<FakeAudioEndpoint>().Calls.Should().Equal("device:dev1");
    }

    [Fact]
    public async Task SetDevice_with_an_unknown_id_is_notFound_and_the_port_is_not_called()
    {
        using var h = NewHarness();
        h.StartAll<StatusController>();
        var s = h.AddSurface(SurfaceKind.Overlay);
        Kit.ErrorCode(await h.CallRawAsync(s, Msg.AudioSetDevice, new AudioDeviceParams { DeviceId = "{evil}" })).Should().Be("notFound");
        Kit.ErrorCode(await h.CallRawAsync(s, Msg.AudioSetDevice, new AudioDeviceParams { DeviceId = "DEV1" })).Should().Be("notFound", "ids are compared exactly");
        h.Get<FakeAudioEndpoint>().Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task SetDevice_is_unsupported_when_the_platform_cannot_switch_devices()
    {
        using var h = NewHarness();
        h.Get<FakeAudioEndpoint>().CanSetDefaultDevice = false;
        h.StartAll<StatusController>();
        var s = h.AddSurface(SurfaceKind.Overlay);
        Kit.ErrorCode(await h.CallRawAsync(s, Msg.AudioSetDevice, new AudioDeviceParams { DeviceId = "dev1" })).Should().Be("unsupported");
        h.Get<FakeAudioEndpoint>().Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task SetDevice_is_unsupported_when_the_switch_returns_false()
    {
        using var h = Kit.Harness<StatusController>(sc => sc.AddSingleton<IAudioEndpoint>(_ => new RefusingAudio()));
        h.StartAll<StatusController>();
        var s = h.AddSurface(SurfaceKind.Overlay);
        Kit.ErrorCode(await h.CallRawAsync(s, Msg.AudioSetDevice, new AudioDeviceParams { DeviceId = "dev1" })).Should().Be("unsupported");
    }

    private sealed class RefusingAudio : IAudioEndpoint
    {
        public AudioState Current => new() { Devices = new[] { new AudioDevice { Id = "dev1", Name = "A", IsDefault = true } } };
        public event Action? Changed { add { } remove { } }
        public void SetVolume(double volume01) { }
        public void SetMuted(bool muted) { }
        public bool CanSetDefaultDevice => true;
        public bool SetDefaultDevice(string deviceId) => false;
    }

    [Theory]
    [InlineData("")]
    [InlineData("a\u0000b")]
    [InlineData("line\nbreak")]
    public async Task SetDevice_rejects_malformed_ids_as_badRequest(string id)
    {
        using var h = NewHarness();
        h.StartAll<StatusController>();
        var s = h.AddSurface(SurfaceKind.Overlay);
        Kit.ErrorCode(await h.CallRawAsync(s, Msg.AudioSetDevice, new AudioDeviceParams { DeviceId = id })).Should().Be("badRequest");
        Kit.ErrorCode(await h.CallRawAsync(s, Msg.AudioSetDevice, new AudioDeviceParams { DeviceId = new string('d', 600) })).Should().Be("badRequest");
        Kit.ErrorCode(await h.CallRawAsync(s, Msg.AudioSetDevice, new { })).Should().Be("badRequest");
        h.Get<FakeAudioEndpoint>().Calls.Should().BeEmpty();
    }

    // ---- brightness ----

    [Theory]
    [InlineData(0.3, 0.3)]
    [InlineData(5.0, 1.0)]
    [InlineData(-1.0, 0.0)]
    public async Task Brightness_is_clamped(double requested, double expected)
    {
        using var h = NewHarness();
        h.StartAll<StatusController>();
        var s = h.AddSurface(SurfaceKind.Overlay);
        Kit.IsOk(await h.CallRawAsync(s, Msg.BrightnessSet, new BrightnessParams { Level = requested })).Should().BeTrue();
        h.Get<FakeBrightnessControl>().Current.Level.Should().Be(expected);
    }

    [Fact]
    public async Task Brightness_is_unsupported_on_displays_without_control()
    {
        using var h = NewHarness();
        h.Get<FakeBrightnessControl>().Current = new BrightnessState { Supported = false };
        h.StartAll<StatusController>();
        var s = h.AddSurface(SurfaceKind.Overlay);
        Kit.ErrorCode(await h.CallRawAsync(s, Msg.BrightnessSet, new BrightnessParams { Level = 0.5 })).Should().Be("unsupported");
        h.Get<FakeBrightnessControl>().Current.Level.Should().Be(0);
    }

    [Fact]
    public async Task Brightness_without_a_level_is_a_badRequest()
    {
        using var h = NewHarness();
        h.StartAll<StatusController>();
        var s = h.AddSurface(SurfaceKind.Overlay);
        Kit.ErrorCode(await h.CallRawAsync(s, Msg.BrightnessSet, new { })).Should().Be("badRequest");
    }

    // ---- theme.setPreference ----

    [Theory]
    [InlineData(ThemePreference.Dark, "dark")]
    [InlineData(ThemePreference.Light, "light")]
    [InlineData(ThemePreference.Auto, "auto")]
    public async Task ThemePreference_is_exactly_a_settings_patch_of_appearance_mode(ThemePreference pref, string wire)
    {
        using var h = NewHarness();
        h.StartAll<StatusController>();
        var s = h.AddSurface(SurfaceKind.Settings);
        Kit.IsOk(await h.CallRawAsync(s, Msg.ThemeSetPreference, new ThemePreferenceParams { Preference = pref })).Should().BeTrue();
        h.Settings().Patches.Should().ContainSingle();
        h.Settings().Patches[0].ToJsonString().Should().Be($"{{\"appearance\":{{\"mode\":\"{wire}\"}}}}");
        h.Settings().Current.Appearance.Mode.Should().Be(pref);
    }

    [Fact]
    public async Task ThemePreference_never_writes_the_windows_theme()
    {
        using var h = NewHarness();
        h.StartAll<StatusController>();
        var theme = h.Get<FakeThemeSource>();
        var s = h.AddSurface(SurfaceKind.Overlay);
        await h.CallRawAsync(s, Msg.ThemeSetPreference, new ThemePreferenceParams { Preference = ThemePreference.Dark });
        theme.AppsUseLightTheme.Should().BeTrue("IThemeSource.SetAppsUseLightTheme must not be called");
    }

    [Fact]
    public async Task ThemePreference_with_an_unknown_value_is_a_badRequest()
    {
        using var h = NewHarness();
        h.StartAll<StatusController>();
        var s = h.AddSurface(SurfaceKind.Overlay);
        Kit.ErrorCode(await h.CallRawAsync(s, Msg.ThemeSetPreference, new { preference = "sepia" })).Should().Be("badRequest");
        Kit.ErrorCode(await h.CallRawAsync(s, Msg.ThemeSetPreference, new { preference = 7 })).Should().Be("badRequest");
        Kit.ErrorCode(await h.CallRawAsync(s, Msg.ThemeSetPreference, new { })).Should().Be("badRequest");
        h.Settings().Patches.Should().BeEmpty();
    }

    // ---- lifecycle ----

    [Fact]
    public void Handlers_are_registered_for_every_call_of_the_domain()
    {
        using var h = NewHarness();
        h.StartAll<StatusController>();
        h.Router.RegisteredTypes.Should().Contain(new[] { Msg.AudioSetVolume, Msg.AudioSetMuted, Msg.AudioSetDevice, Msg.BrightnessSet, Msg.ThemeSetPreference });
    }

    [Fact]
    public void Stop_detaches_from_the_ports()
    {
        using var h = NewHarness();
        var c = h.StartAll<StatusController>();
        var s = h.AddSurface(SurfaceKind.Menubar, "m1");
        c.Stop();
        s.Clear();
        h.Get<FakeBatterySource>().Current = new BatteryState { Present = true, Percent = 3 };
        h.Get<FakeBatterySource>().RaiseChanged();
        h.Time.Advance(Coalesce);
        s.Events(Msg.StatusBattery).Should().BeEmpty();
    }

    [Fact]
    public void Starting_twice_does_not_declare_the_topics_twice()
    {
        using var h = NewHarness();
        var c = h.StartAll<StatusController>();
        Action again = c.Start;
        again.Should().NotThrow();
    }

    [Fact]
    public void Another_controller_cannot_declare_the_same_state_event()
    {
        using var h = NewHarness();
        h.StartAll<StatusController>();
        Action act = () => h.State.Declare<BatteryState>(Msg.StatusBattery, _ => new BatteryState());
        act.Should().Throw<InvalidOperationException>();
    }

    // ---- sanitizer ----

    [Fact]
    public void Sanitizer_limits_lists_and_text()
    {
        var many = Enumerable.Range(0, 200).Select(i => new AudioDevice { Id = "d" + i, Name = new string('n', 1000) }).ToArray();
        var audio = StatusSanitizerAccess.Audio(new AudioState { Volume = 2, Devices = many });
        audio.Devices.Should().HaveCount(64);
        audio.Devices.Should().OnlyContain(d => d.Name.Length == 256);
        audio.Volume.Should().Be(1);
    }
}
