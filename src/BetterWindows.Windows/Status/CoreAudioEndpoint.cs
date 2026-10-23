using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Native;

/// <summary>STUB — owner: status stream. Replace with the real implementation.</summary>
public sealed class CoreAudioEndpoint : IAudioEndpoint
{
    public AudioState Current { get; } = new();
    public event Action? Changed { add { } remove { } }
    public void SetVolume(double volume01) { }
    public void SetMuted(bool muted) { }
    public bool CanSetDefaultDevice => false;
    public bool SetDefaultDevice(string deviceId) => false;
}
