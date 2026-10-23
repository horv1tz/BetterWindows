using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Tests.Support.Fakes;

public class FakeBatterySource : IBatterySource
{
    public BatteryState Current { get; set; } = new() { Present = true, Percent = 80, AcOnline = true };
    public event Action? Changed;
    public void RaiseChanged() => Changed?.Invoke();
}

public class FakeNetworkSource : INetworkSource
{
    public NetworkState Current { get; set; } = new() { Kind = NetworkKind.Wifi, Connected = true, Name = "Home", SignalBars = 3 };
    public event Action? Changed;
    public void RaiseChanged() => Changed?.Invoke();
}

public class FakeBluetoothSource : IBluetoothSource
{
    public BluetoothState Current { get; set; } = new() { Supported = true, Enabled = true };
    public event Action? Changed;
    public void RaiseChanged() => Changed?.Invoke();
}

public class FakeAudioEndpoint : IAudioEndpoint
{
    public AudioState Current { get; set; } = new() { Volume = 0.5, DeviceId = "dev1", DeviceName = "Speakers", Devices = new[] { new AudioDevice { Id = "dev1", Name = "Speakers", IsDefault = true } } };
    public List<string> Calls { get; } = new();
    public event Action? Changed;
    public void RaiseChanged() => Changed?.Invoke();
    public void SetVolume(double volume01) { Calls.Add($"volume:{volume01}"); Current = Current with { Volume = volume01 }; }
    public void SetMuted(bool muted) { Calls.Add($"muted:{muted}"); Current = Current with { Muted = muted }; }
    public bool CanSetDefaultDevice { get; set; } = true;
    public bool SetDefaultDevice(string deviceId) { Calls.Add("device:" + deviceId); return CanSetDefaultDevice; }
}

public class FakeBrightnessControl : IBrightnessControl
{
    public BrightnessState Current { get; set; } = new() { Supported = true, Level = 0.6 };
    public event Action? Changed;
    public void RaiseChanged() => Changed?.Invoke();
    public void SetLevel(double level01) { Current = Current with { Level = level01 }; }
}

public class FakeMediaSource : IMediaSource
{
    public MediaState Current { get; set; } = new();
    public List<(MediaAction Action, double? Pos)> Controls { get; } = new();
    public (byte[] Data, string ContentType)? Art { get; set; }
    public event Action? Changed;
    public void RaiseChanged() => Changed?.Invoke();
    public Task ControlAsync(MediaAction action, double? positionSec = null) { Controls.Add((action, positionSec)); return Task.CompletedTask; }
    public Task<(byte[] Data, string ContentType)?> GetArtAsync(CancellationToken ct = default) => Task.FromResult(Art);
}

public class FakeThemeSource : IThemeSource
{
    public bool AppsUseLightTheme { get; set; } = true;
    public string AccentColorHex { get; set; } = "#0078d4";
    public bool ReduceMotion { get; set; }
    public bool ReduceTransparency { get; set; }
    public event Action? Changed;
    public void RaiseChanged() => Changed?.Invoke();
    public void SetAppsUseLightTheme(bool light) { AppsUseLightTheme = light; RaiseChanged(); }
}

public class FakeSystemPower : ISystemPower
{
    public List<PowerAction> Actions { get; } = new();
    public void Perform(PowerAction action) => Actions.Add(action);
}

// ---- Host-implemented ports ----

public class FakeSurfaceManager : ISurfaceManager
{
    public List<(SurfaceId Surface, double W, double H, double? Bar)> Resizes { get; } = new();
    public List<(OverlayRequest Request, SurfaceId? Source)> Shown { get; } = new();
    public List<(SurfaceId Surface, SurfaceShapeParams Shape)> Shapes { get; } = new();
    public List<string?> SettingsOpened { get; } = new();
    public List<(SurfaceId Dock, bool Revealed)> DockReveals { get; } = new();
    public List<(SurfaceKind? Kind, string? MonitorId, bool Visible, string Reason)> Visibility { get; } = new();
    public int HideCount { get; private set; }
    public bool IsOverlayVisible { get; private set; }
    public OverlayMode? CurrentOverlayMode { get; private set; }
    public bool SupportsGlassOverflow { get; set; }
    public event Action? OverlayHidden;
    /// <summary>Simulate the HOST hiding the overlay by itself (deactivation).</summary>
    public void SimulateHostHide() { IsOverlayVisible = false; CurrentOverlayMode = null; OverlayHidden?.Invoke(); }

    public void ResizeSurface(SurfaceId surface, double widthDip, double heightDip, double? barThicknessDip) => Resizes.Add((surface, widthDip, heightDip, barThicknessDip));
    public void ShowOverlay(OverlayRequest request, SurfaceId? source) { Shown.Add((request, source)); IsOverlayVisible = true; CurrentOverlayMode = request.Mode; }
    public void HideOverlay() { HideCount++; IsOverlayVisible = false; CurrentOverlayMode = null; }
    public void SetDockRevealed(SurfaceId dock, bool revealed) => DockReveals.Add((dock, revealed));
    public void SetSurfaceShape(SurfaceId surface, SurfaceShapeParams shape) => Shapes.Add((surface, shape));
    public void ShowSettingsWindow(string? page) => SettingsOpened.Add(page);
    public void SetSurfacesVisible(SurfaceKind? kind, string? monitorId, bool visible, string reason) => Visibility.Add((kind, monitorId, visible, reason));
}

public class FakeAppLifecycle : IAppLifecycle
{
    public List<bool> Exits { get; } = new();
    public void Exit(bool restoreTaskbar) => Exits.Add(restoreTaskbar);
    public string Version => "0.0.0-test";
    public string BuildInfo => "test";
    public string OsDescription => "Test OS";
    public string WebView2Version => "0.0";
}

public class FakeClipboard : IClipboard
{
    public List<string> Texts { get; } = new();
    public void SetText(string text) => Texts.Add(text);
}

public class FakeFilePicker : IFilePicker
{
    public List<string> Result { get; } = new();
    public List<(PickPathParams Request, SurfaceId Owner)> Calls { get; } = new();
    public Task<IReadOnlyList<string>> PickAsync(PickPathParams request, SurfaceId owner, CancellationToken ct = default)
    {
        Calls.Add((request, owner));
        return Task.FromResult<IReadOnlyList<string>>(Result.ToArray());
    }
}
