using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Native;

/// <summary>STUB — owner: win-apps. Replace with the real implementation.</summary>
public sealed class ShellDesktopFolderSource : IDesktopFolderSource
{
    public IReadOnlyList<DesktopEntry> Enumerate() => Array.Empty<DesktopEntry>();
    public event Action? Changed { add { } remove { } }
}
