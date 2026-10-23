using BetterWindows.Core.Platform;

namespace BetterWindows.Host.Lifecycle;

/// <summary>STUB — owner: lifecycle/bridge stream. Implements IAppLifecycle.</summary>
public sealed class AppLifecycle : IAppLifecycle
{
    public void Exit(bool restoreTaskbar) { }
    public string Version => "0.1.0";
    public string BuildInfo => "dev";
    public string OsDescription => Environment.OSVersion.VersionString;
    public string WebView2Version => "unknown";
}
