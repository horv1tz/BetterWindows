using BetterWindows.Core.Platform;
using BetterWindows.Host.Bridge;

namespace BetterWindows.Host.Lifecycle;

/// <summary>
/// <see cref="IAppLifecycle"/> for the Host: <see cref="Exit"/> and <see cref="Restart"/> only REQUEST the ordered shutdown from <see cref="ShutdownCoordinator"/> and
/// return immediately (a message handler may call them: its own reply is still delivered); the version strings come from assembly metadata and the WebView2 environment.
/// </summary>
public sealed class AppLifecycle : IAppLifecycle
{
    private readonly ShutdownCoordinator _shutdown;
    private readonly WebViewBridge _bridge;

    public AppLifecycle(ShutdownCoordinator shutdown, WebViewBridge bridge)
    {
        _shutdown = shutdown;
        _bridge = bridge;
    }

    public void Exit() => _shutdown.RequestExit(restart: false);
    public void Restart() => _shutdown.RequestExit(restart: true);
    public string Version => BuildMetadata.Version;
    public string BuildInfo => BuildMetadata.BuildInfo;
    public string OsDescription => BuildMetadata.OsDescription();
    public string WebView2Version => _bridge.BrowserVersion;
}
