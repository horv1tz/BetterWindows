using BetterWindows.Core.Services;

namespace BetterWindows.Core.Controllers;

/// <summary>STUB — owner: settings. Replace with the real implementation (see docs/IPC.md for the messages this controller owns).</summary>
public sealed class SystemController : IShellController
{
    public string Name => "SystemController";
    public void Start() { }
    public void Stop() { }
}
