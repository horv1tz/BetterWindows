using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Native;

/// <summary>STUB — owner: windows stream. Replace with the real implementation.</summary>
public sealed class Win32CursorProvider : ICursorProvider
{
    public bool TryGetCursorPosition(out NativePoint position) { position = default; return false; }
    public bool IsAnyMouseButtonDown() => false;
}
