using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Native;

/// <summary>STUB — owner: windows stream. Replace with the real implementation.</summary>
public sealed class Win32HotkeyRegistrar : IHotkeyRegistrar
{
    public bool TryRegister(string id, HotkeyGesture gesture) => false;
    public void Unregister(string id) { }
    public void UnregisterAll() { }
    public event Action<string>? Pressed { add { } remove { } }
    public void Dispose() { }
}
