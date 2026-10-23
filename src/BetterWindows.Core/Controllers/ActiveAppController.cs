using BetterWindows.Core.Services;

namespace BetterWindows.Core.Controllers;

/// <summary>STUB — owner: windows. Replace with the real implementation (see docs/IPC.md for the messages this controller owns).</summary>
public sealed class ActiveAppController : IShellController
{
    public string Name => "ActiveAppController";
    public void Start() { }
    public void Stop() { }
}
