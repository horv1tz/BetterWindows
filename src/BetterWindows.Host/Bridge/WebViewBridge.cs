using BetterWindows.Core.Ipc;
using Microsoft.Web.WebView2.Wpf;

namespace BetterWindows.Host.Bridge;

/// <summary>STUB — owner: lifecycle/bridge stream.</summary>
public sealed class WebViewBridge : IWebViewBridge
{
    public Task AttachAsync(IWebView2 webView, SurfaceId id, string route) => Task.CompletedTask;
    public void Detach(SurfaceId id) { }
    public void OpenDevTools(SurfaceId id) { }
    public event Action<SurfaceFailure>? Failed { add { } remove { } }
    public bool IsHealthy(SurfaceId id) => true;
    public Task ReloadAsync(SurfaceId id) => Task.CompletedTask;
}
