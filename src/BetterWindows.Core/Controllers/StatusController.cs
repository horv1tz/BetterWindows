using System.Text.Json.Nodes;
using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Core.Status;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Core.Controllers;

/// <summary>
/// System status for the menu bar / Control Center: publishes status.battery/network/audio/brightness/bluetooth as state events (declared on <see cref="IStatePublisher"/>:
/// initial push after surface.ready, deduplication and 100 ms coalescing are the publisher's job; every port Changed handler only calls Invalidate) and handles the
/// calls that change the system: audio.setVolume/setMuted/setDevice, brightness.set and theme.setPreference. The UI is untrusted: every value is validated, clamped and
/// reported with a precise protocol error (docs/IPC.md sections 9 and 14).
/// </summary>
public sealed class StatusController : IShellController
{
    private static readonly TimeSpan Coalesce = TimeSpan.FromMilliseconds(100);

    private readonly IMessageRouter _router;
    private readonly IStatePublisher _state;
    private readonly IBatterySource _battery;
    private readonly INetworkSource _network;
    private readonly IBluetoothSource _bluetooth;
    private readonly IAudioEndpoint _audio;
    private readonly IBrightnessControl _brightness;
    private readonly ISettingsService _settings;
    private readonly ILogger _log;
    private readonly List<Action> _detach = new();
    private IStateTopic? _audioTopic, _brightnessTopic;
    private bool _started;

    public StatusController(IMessageRouter router, IStatePublisher state, IBatterySource battery, INetworkSource network, IBluetoothSource bluetooth,
        IAudioEndpoint audio, IBrightnessControl brightness, ISettingsService settings, ILogger<StatusController>? log = null)
    {
        _router = router;
        _state = state;
        _battery = battery;
        _network = network;
        _bluetooth = bluetooth;
        _audio = audio;
        _brightness = brightness;
        _settings = settings;
        _log = (ILogger?)log ?? NullLogger.Instance;
    }

    public string Name => "StatusController";

    public void Start()
    {
        if (_started) return;
        _started = true;

        // Snapshot functions are cheap pure reads of port.Current; the port events (OS threads) only invalidate the topic.
        Bind(Msg.StatusBattery, () => StatusSanitizer.Battery(_battery.Current), h => _battery.Changed += h, h => _battery.Changed -= h);
        Bind(Msg.StatusNetwork, () => StatusSanitizer.Network(_network.Current), h => _network.Changed += h, h => _network.Changed -= h);
        Bind(Msg.StatusBluetooth, () => StatusSanitizer.Bluetooth(_bluetooth.Current), h => _bluetooth.Changed += h, h => _bluetooth.Changed -= h);
        _audioTopic = Bind(Msg.StatusAudio, () => StatusSanitizer.Audio(_audio.Current), h => _audio.Changed += h, h => _audio.Changed -= h);
        _brightnessTopic = Bind(Msg.StatusBrightness, () => StatusSanitizer.Brightness(_brightness.Current), h => _brightness.Changed += h, h => _brightness.Changed -= h);

        _router.Handle<VolumeParams>(Msg.AudioSetVolume, SetVolume);
        _router.Handle<MuteParams>(Msg.AudioSetMuted, SetMuted);
        _router.Handle<AudioDeviceParams>(Msg.AudioSetDevice, SetDevice);
        _router.Handle<BrightnessParams>(Msg.BrightnessSet, SetBrightness);
        _router.Handle<ThemePreferenceParams>(Msg.ThemeSetPreference, SetThemePreference);
    }

    public void Stop()
    {
        foreach (var d in _detach) d();
        _detach.Clear();
    }

    private IStateTopic Bind<T>(string type, Func<T> read, Action<Action> attach, Action<Action> detach) where T : class
    {
        var topic = _state.Declare<T>(type, _ => read(), Coalesce);
        Action handler = topic.Invalidate;
        attach(handler);
        _detach.Add(() => detach(handler));
        return topic;
    }

    // ---- calls ----

    private Task SetVolume(CallContext ctx, VolumeParams p)
    {
        if (!double.IsFinite(p.Volume)) throw ProtocolException.BadRequest("volume must be a finite number");
        var volume = StatusSanitizer.Clamp01(p.Volume);
        Port("audio.setVolume", () => _audio.SetVolume(volume));
        _audioTopic?.Invalidate(); // the user sees the new value without waiting for the OS notification
        return Task.CompletedTask;
    }

    private Task SetMuted(CallContext ctx, MuteParams p)
    {
        Port("audio.setMuted", () => _audio.SetMuted(p.Muted));
        _audioTopic?.Invalidate();
        return Task.CompletedTask;
    }

    private Task SetDevice(CallContext ctx, AudioDeviceParams p)
    {
        if (!IsSafeId(p.DeviceId)) throw ProtocolException.BadRequest("invalid deviceId");
        if (!_audio.CanSetDefaultDevice) throw ProtocolException.Unsupported("switching the default audio device is not available on this system");
        var devices = _audio.Current.Devices ?? Array.Empty<AudioDevice>();
        if (!devices.Any(d => string.Equals(d.Id, p.DeviceId, StringComparison.Ordinal))) throw ProtocolException.NotFound("unknown audio device");

        var switched = false;
        Port("audio.setDevice", () => switched = _audio.SetDefaultDevice(p.DeviceId));
        if (!switched) throw ProtocolException.Unsupported("the audio device could not be switched");
        _audioTopic?.Invalidate();
        return Task.CompletedTask;
    }

    private Task SetBrightness(CallContext ctx, BrightnessParams p)
    {
        if (!double.IsFinite(p.Level)) throw ProtocolException.BadRequest("level must be a finite number");
        if (!_brightness.Current.Supported) throw ProtocolException.Unsupported("brightness control is not available on this display");
        var level = StatusSanitizer.Clamp01(p.Level);
        Port("brightness.set", () => _brightness.SetLevel(level));
        _brightnessTopic?.Invalidate();
        return Task.CompletedTask;
    }

    /// <summary>Exactly <c>settings.patch { appearance: { mode } }</c>: the Windows personalisation is never written (the effective theme is derived by ThemeService).</summary>
    private async Task SetThemePreference(CallContext ctx, ThemePreferenceParams p)
    {
        if (!Enum.IsDefined(p.Preference)) throw ProtocolException.BadRequest("unknown theme preference");
        var patch = new JsonObject { ["appearance"] = new JsonObject { ["mode"] = ProtocolJson.ToNode(p.Preference) } };
        await _settings.PatchAsync(patch, ctx.Cancellation).ConfigureAwait(false);
    }

    // ---- helpers ----

    /// <summary>A platform call failed: log the details, tell the UI only that it failed (messages from COM/WMI may contain paths and device names).</summary>
    private void Port(string what, Action call)
    {
        try { call(); }
        catch (ProtocolException) { throw; }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "{Operation} failed", what);
            throw ProtocolException.Failed(what + " failed");
        }
    }

    private static bool IsSafeId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > StatusSanitizer.MaxIdChars) return false;
        foreach (var c in id) if (c < 0x20 || c == 0x7f) return false;
        return true;
    }
}
