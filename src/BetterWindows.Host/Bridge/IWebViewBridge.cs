using BetterWindows.Core.Ipc;
using Microsoft.Web.WebView2.Wpf;

namespace BetterWindows.Host.Bridge;

/// <summary>
/// Owner: lifecycle/bridge stream. Used by the surface windows (surfaces stream).
/// Attaches a WebView2 control to the shared environment and the IPC fabric:
///  - EnsureCoreWebView2Async(sharedEnvironment) with locked-down settings (no context menu, no zoom, no accelerators, devtools in DEBUG);
///  - SetVirtualHostNameToFolderMapping("app.local", wwwroot) — or, if BETTERWINDOWS_UI_URL is set, navigate to that dev server;
///  - Navigate to https://app.local/index.html#/&lt;route&gt;;
///  - WebMessageReceived -> IMessageRouter.DispatchAsync(id, json); outgoing -> an ISurfaceChannel registered in SurfaceRegistry;
///  - WebResourceRequested for "/dyn/*" -> IDynamicResourceProvider;
///  - transparent DefaultBackgroundColor (alpha 0) and WEBVIEW2_DEFAULT_BACKGROUND_COLOR to avoid a white flash.
/// </summary>
public interface IWebViewBridge
{
    /// <param name="route">"dock" | "menubar" | "overlay" | "desktop".</param>
    Task AttachAsync(IWebView2 webView, SurfaceId id, string route);
    void Detach(SurfaceId id);
    /// <summary>Open DevTools for the surface (debug builds / --devtools).</summary>
    void OpenDevTools(SurfaceId id);
}
