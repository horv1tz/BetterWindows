using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Native;

/// <summary>STUB — owner: windows stream. Replace with the real implementation.</summary>
public sealed class Win32WindowSystem : IWindowSystem
{
    public IReadOnlyList<RawWindow> EnumerateTopLevel() => Array.Empty<RawWindow>();
    public RawWindow? TryDescribe(long handle) => null;
    public long GetForegroundWindow() => 0;
    public event Action<WindowEvent>? WindowChanged { add { } remove { } }
    public void Start() { }
    public void Stop() { }
    public bool Activate(long handle) => false;
    public bool Minimize(long handle) => false;
    public bool Restore(long handle) => false;
    public bool Maximize(long handle) => false;
    public bool Close(long handle) => false;
    public bool IsFullscreenWindowActive(MonitorInfo monitor) => false;
    public void Dispose() { }
}
