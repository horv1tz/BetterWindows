using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Native;

/// <summary>STUB — owner: apps stream. Replace with the real implementation.</summary>
public sealed class ShellAppLauncher : IAppLauncher
{
    public LaunchResult Launch(LaunchTarget target, string? arguments = null) => LaunchResult.Failed;
    public bool Open(string target) => false;
    public bool RevealInExplorer(string path) => false;
}
