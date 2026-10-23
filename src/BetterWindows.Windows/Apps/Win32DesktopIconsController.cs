using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Native;

/// <summary>STUB — owner: apps stream. Replace with the real implementation.</summary>
public sealed class Win32DesktopIconsController : IDesktopIconsController
{
    public void SetNativeIconsVisible(bool visible) { }
    public bool TryAttachDesktopLayer(nint hwnd, NativeRect boundsPx) => false;
    public void DetachDesktopLayer(nint hwnd) { }
    public event Action? ShellRestarted { add { } remove { } }
    public void Restore() { }
    public void Dispose() { }
}
