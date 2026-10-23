using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Platform;

// Status sources: each exposes the CURRENT value plus a Changed event. Values are the protocol DTOs directly.

public interface IBatterySource { BatteryState Current { get; } event Action? Changed; }
public interface INetworkSource { NetworkState Current { get; } event Action? Changed; }
public interface IBluetoothSource { BluetoothState Current { get; } event Action? Changed; }

public interface IAudioEndpoint
{
    AudioState Current { get; }
    event Action? Changed;
    void SetVolume(double volume01);
    void SetMuted(bool muted);
    void SetDefaultDevice(string deviceId);
}

public interface IBrightnessControl
{
    BrightnessState Current { get; }
    event Action? Changed;
    void SetLevel(double level01);
}

public interface IMediaSource
{
    MediaState Current { get; }
    event Action? Changed;
    Task ControlAsync(MediaAction action, double? positionSec = null);
    /// <summary>Cover art PNG/JPEG bytes of the current session, with its content type; null if none.</summary>
    Task<(byte[] Data, string ContentType)?> GetArtAsync(CancellationToken ct = default);
}

/// <summary>Windows personalisation: light/dark, accent colour, animation + transparency settings.</summary>
public interface IThemeSource
{
    bool AppsUseLightTheme { get; }
    /// <summary>CSS hex, e.g. "#0078d4".</summary>
    string AccentColorHex { get; }
    bool ReduceMotion { get; }
    bool ReduceTransparency { get; }
    /// <summary>Raised on WM_SETTINGCHANGE (ImmersiveColorSet) etc.</summary>
    event Action? Changed;
    /// <summary>Write the Windows app theme preference (HKCU Personalize\AppsUseLightTheme + SystemUsesLightTheme).</summary>
    void SetAppsUseLightTheme(bool light);
}

public interface ISystemPower
{
    void Perform(PowerAction action);
}
