using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Native;

/// <summary>STUB — owner: windows stream. Replace with the real implementation.</summary>
public sealed class Win32MonitorProvider : IMonitorProvider
{
    public IReadOnlyList<MonitorInfo> GetMonitors() => Array.Empty<MonitorInfo>();
    public MonitorInfo? FromPoint(int xPx, int yPx) => null;
    public event Action? MonitorsChanged { add { } remove { } }
}
