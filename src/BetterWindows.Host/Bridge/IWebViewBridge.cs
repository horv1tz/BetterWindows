using BetterWindows.Core.Ipc;
using Microsoft.Web.WebView2.Wpf;

namespace BetterWindows.Host.Bridge;

public enum SurfaceFailureKind
{
    RuntimeMissing, EnvironmentFailed, BrowserProcessExited, RenderProcessExited, RenderProcessUnresponsive, GpuProcessExited, NavigationFailed, ReadyTimeout, ProtocolMismatch,
}

/// <summary>A surface's WebView failed. <paramref name="Recovered"/>: the bridge already reloaded/recreated it.</summary>
public sealed record SurfaceFailure(SurfaceId Surface, SurfaceFailureKind Kind, string Detail, bool Recovered);

/// <summary>
/// Owner: lifecycle/bridge stream. Used by the surface windows (surfaces stream).
/// Attaches a WebView2 control to the shared environment and the IPC fabric (normative details: docs/IPC.md sections 1.1, 8, 16):
///  - ONE shared CoreWebView2Environment (InPrivate profile, user data folder from IAppPaths.WebView2Dir), transparent DefaultBackgroundColor (alpha 0) plus the env var
///    WEBVIEW2_DEFAULT_BACKGROUND_COLOR to avoid a white flash;
///  - NO SetVirtualHostNameToFolderMapping (it disables WebResourceRequested): the bridge answers every https://app.local/* request itself (static wwwroot files + /dyn/*);
///  - the full hardening list of docs/IPC.md section 16 (origin-checked messages, navigation/new-window/permission/download blocking, no external drop);
///  - WebMessageReceived -> IMessageRouter.DispatchAsync(channel, json) WITHOUT awaiting; outgoing: an ISurfaceChannel registered in SurfaceRegistry whose Post marshals to the UI
///    thread (bounded queue, nothing before surface.ready);
///  - failures are reported through <see cref="Failed"/> and handled per the failure policy of section 16.
/// </summary>
public interface IWebViewBridge
{
    /// <param name="route">"dock" | "menubar" | "overlay" | "desktop" | "settings" (settings may carry a page: "settings/dock").</param>
    Task AttachAsync(IWebView2 webView, SurfaceId id, string route);
    /// <summary>Unregisters the channel of THIS attachment only (a newer attachment with the same id is untouched).</summary>
    void Detach(SurfaceId id);
    /// <summary>Open DevTools for the surface (debug builds / --devtools).</summary>
    void OpenDevTools(SurfaceId id);
    event Action<SurfaceFailure>? Failed;
    /// <summary>False while the surface is failed or has not completed surface.ready within its timeout.</summary>
    bool IsHealthy(SurfaceId id);
    Task ReloadAsync(SurfaceId id);
}
