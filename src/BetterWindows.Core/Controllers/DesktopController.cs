using BetterWindows.Core.Services;

namespace BetterWindows.Core.Controllers;

/// <summary>STUB — owner: desktop. Replace with the real implementation (see docs/IPC.md for the messages this controller owns).</summary>
public sealed class DesktopController : IShellController
{
    public string Name => "DesktopController";
    public void Start() { }
    public void Stop() { }
}
