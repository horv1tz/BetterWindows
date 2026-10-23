using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Native;

/// <summary>STUB — owner: status stream. Replace with the real implementation.</summary>
public sealed class NetworkInfoSource : INetworkSource
{
    public NetworkState Current { get; } = new();
    public event Action? Changed { add { } remove { } }
}
