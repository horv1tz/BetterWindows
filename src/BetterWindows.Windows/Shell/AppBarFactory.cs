using BetterWindows.Core.Platform;

namespace BetterWindows.Native;

/// <summary>STUB — owner: windows stream.</summary>
public sealed class AppBarFactory : IAppBarFactory
{
    public IAppBar Create(nint hwnd) => new AppBar(hwnd);
}

/// <summary>STUB — owner: windows stream.</summary>
public sealed class AppBar : IAppBar
{
    public AppBar(nint hwnd) { }
    public bool IsRegistered => false;
    public NativeRect Dock(AppBarEdge edge, NativeRect monitorBoundsPx, int thicknessPx) => monitorBoundsPx;
    public void Undock() { }
    public bool ProcessWindowMessage(uint msg, nint wParam, nint lParam) => false;
    public event Action<AppBarNotification>? Notification { add { } remove { } }
    public void Dispose() { }
}
