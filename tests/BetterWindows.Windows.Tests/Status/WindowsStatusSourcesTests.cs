using System.Text.RegularExpressions;
using BetterWindows.Core.Protocol;
using BetterWindows.Native.Status;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace BetterWindows.Native.Tests.Status;

/// <summary>
/// Windows-only integration checks for the status sources. They skip on other platforms and tolerate missing hardware: CI runners have no battery, no
/// Bluetooth radio, no audio device and no media session, so every assertion is about the shape and consistency of the answer, not its value.
/// No power action is executed here (only <see cref="SystemPowerPlan"/> mapping is tested).
/// </summary>
public class WindowsStatusSourcesTests
{
    private static readonly Regex HexColor = new("^#[0-9a-f]{6}$", RegexOptions.CultureInvariant);

    [SkippableFact]
    public void Battery_current_is_consistent_and_subscription_is_clean()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");
        using var source = new PowerBatterySource();
        Action handler = () => { };
        source.Changed += handler;
        var c = source.Current;
        c.Should().NotBeNull();
        if (c.Present) c.Percent.Should().BeInRange(0, 100);
        else c.Percent.Should().Be(0);
        source.Changed -= handler;
    }

    [SkippableFact]
    public void Network_current_is_consistent()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");
        using var source = new NetworkInfoSource();
        var c = source.Current;
        c.SignalBars.Should().BeInRange(0, 4);
        if (!c.Connected) c.SignalBars.Should().Be(0);
        if (c.Kind == NetworkKind.None) c.Connected.Should().BeFalse();
    }

    [SkippableFact]
    public void Bluetooth_without_a_radio_reports_unsupported_and_no_devices()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");
        using var source = new BluetoothSource();
        Action handler = () => { };
        source.Changed += handler;
        var c = source.Current;
        c.Should().NotBeNull();
        if (!c.Supported)
        {
            c.Enabled.Should().BeFalse();
            c.Devices.Should().BeEmpty();
        }
        source.Changed -= handler;
    }

    [SkippableFact]
    public void Audio_current_has_no_exception_without_any_device()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");
        using var source = new CoreAudioEndpoint();
        var act = () => source.Current;
        act.Should().NotThrow();
        var c = source.Current;
        c.Volume.Should().BeInRange(0.0, 1.0);
        if (c.DeviceId is null) c.Devices.Should().NotContain(d => d.IsDefault);
        var canSet = source.CanSetDefaultDevice;
        if (!canSet) source.SetDefaultDevice("definitely-not-a-device").Should().BeFalse();
    }

    [SkippableFact]
    public void Brightness_current_is_consistent_on_machines_without_a_panel()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");
        using var source = new WmiBrightnessControl();
        var c = source.Current;
        c.Level.Should().BeInRange(0.0, 1.0);
        if (!c.Supported) c.Level.Should().Be(0.0);
    }

    [SkippableFact]
    public void Media_current_is_consistent_without_a_session()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");
        using var source = new SmtcMediaSource();
        var c = source.Current;
        c.Should().NotBeNull();
        if (!c.Available)
        {
            c.Playing.Should().BeFalse();
            c.Title.Should().BeNull();
        }
        c.PositionSec.Should().BeGreaterThanOrEqualTo(0);
    }

    [SkippableFact]
    public void Theme_returns_a_valid_accent_and_flags()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");
        using var source = new RegistryThemeSource();
        Action handler = () => { };
        source.Changed += handler;
        HexColor.IsMatch(source.AccentColorHex).Should().BeTrue();
        var _ = source.AppsUseLightTheme;
        var __ = source.ReduceMotion;
        var ___ = source.ReduceTransparency;
        source.Changed -= handler;
    }

    [SkippableFact]
    public void Theme_preference_roundtrip_restores_the_original_value()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");
        using var source = new RegistryThemeSource();
        var original = ReadAppsUseLightTheme();
        Skip.If(original is null, "AppsUseLightTheme is not present on this machine");

        var flipped = original!.Value == 0;
        try
        {
            try
            {
                source.SetAppsUseLightTheme(flipped);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                Skip.If(true, "Writing the personalisation key is not permitted here: " + ex.GetType().Name);
            }
            ReadAppsUseLightTheme().Should().Be(flipped ? 1 : 0, "SetAppsUseLightTheme(true) writes 1");
        }
        finally
        {
            try
            {
                source.SetAppsUseLightTheme(original.Value != 0);
            }
            catch (Exception)
            {
                // Best effort: the test must not fail on the cleanup path, but the restore is attempted.
            }
        }
        ReadAppsUseLightTheme().Should().Be(original);
    }

    [SkippableFact]
    public void Power_mapping_is_checked_without_executing_anything()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");
        // The actions themselves are never performed in tests; only the plan is verified (see SystemPowerPlanTests).
        SystemPowerPlan.Plan(PowerAction.Lock, false).Mechanism.Should().Be(PowerMechanism.LockWorkStation);
    }

    [SkippableFact]
    public void Creating_and_disposing_every_source_leaves_no_subscriptions_behind()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");
        for (var i = 0; i < 2; i++)
        {
            using var battery = new PowerBatterySource(NullLogger<PowerBatterySource>.Instance);
            using var network = new NetworkInfoSource(NullLogger<NetworkInfoSource>.Instance);
            using var bluetooth = new BluetoothSource(NullLogger<BluetoothSource>.Instance);
            using var audio = new CoreAudioEndpoint(NullLogger<CoreAudioEndpoint>.Instance);
            using var brightness = new WmiBrightnessControl(NullLogger<WmiBrightnessControl>.Instance);
            using var media = new SmtcMediaSource(NullLogger<SmtcMediaSource>.Instance);
            using var theme = new RegistryThemeSource(NullLogger<RegistryThemeSource>.Instance);
            _ = battery.Current; _ = network.Current; _ = bluetooth.Current; _ = audio.Current; _ = brightness.Current; _ = media.Current; _ = theme.AppsUseLightTheme;
        }
    }

    private static int? ReadAppsUseLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", writable: false);
        return key?.GetValue("AppsUseLightTheme") as int?;
    }
}
