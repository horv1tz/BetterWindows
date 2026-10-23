using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Native;

/// <summary>STUB — owner: status stream. Replace with the real implementation.</summary>
public sealed class BluetoothSource : IBluetoothSource
{
    public BluetoothState Current { get; } = new();
    public event Action? Changed { add { } remove { } }
}
