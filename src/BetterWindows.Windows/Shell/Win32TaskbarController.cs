using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Native;

/// <summary>STUB — owner: windows stream. Replace with the real implementation.</summary>
public sealed class Win32TaskbarController : ITaskbarController
{
    public TaskbarMode Current { get; private set; } = TaskbarMode.Visible;
    public void Apply(TaskbarMode mode) { Current = mode; }
    public void Restore() { Current = TaskbarMode.Visible; }
    public void Dispose() { }
}
