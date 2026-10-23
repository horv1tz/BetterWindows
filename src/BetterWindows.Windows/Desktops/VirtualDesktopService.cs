using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Native;

/// <summary>STUB — owner: desktops stream. Replace with the real implementation.</summary>
public sealed class VirtualDesktopService : IVirtualDesktopService
{
    public bool IsSupported => false;
    public IReadOnlyList<DesktopInfo> GetDesktops() => Array.Empty<DesktopInfo>();
    public string? GetWindowDesktopId(long handle) => null;
    public bool IsWindowOnCurrentDesktop(long handle) => true;
    public bool Switch(string desktopId) => false;
    public bool MoveWindow(long handle, string desktopId) => false;
    public DesktopInfo? Create() => null;
    public bool Remove(string desktopId) => false;
    public event Action? Changed { add { } remove { } }
}
