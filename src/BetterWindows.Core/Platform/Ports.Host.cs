using BetterWindows.Core.Ipc;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Platform;

// Ports implemented by the WPF Host (they need windows). Core controllers call them; tests fake them.
// THREADING: all members may be called from ANY thread. Implementations marshal to the WPF Dispatcher, never block the caller, and getters return a volatile snapshot.

public interface ISurfaceManager
{
    /// <summary>Resize/reposition a surface window (dock): size in DIPs. Host clamps and keeps the dock edge-aligned/centred. barThickness (DIP) is what the AppBar reserves.</summary>
    void ResizeSurface(SurfaceId surface, double widthDip, double heightDip, double? barThicknessDip);
    /// <summary>
    /// Show the overlay window on a monitor (full-screen, activatable, topmost), translate popover anchors from the source surface to overlay coordinates, then send overlay.show.
    /// source == null for hotkeys / hot corners / system actions. Target monitor = request.MonitorId ?? the source's monitor ?? the monitor under the cursor (resolved by the Host).
    /// </summary>
    void ShowOverlay(OverlayRequest request, SurfaceId? source);
    /// <summary>Hide the overlay window (and send overlay.hide if still showing).</summary>
    void HideOverlay();
    bool IsOverlayVisible { get; }
    OverlayMode? CurrentOverlayMode { get; }
    /// <summary>True if the host restricts the blur backdrop to SurfaceShapeParams.rects while hitRects stay un-blurred (becomes SurfaceInit.glassOverflow). A whole-window backdrop (DWMWA_SYSTEMBACKDROP_TYPE) cannot, so the default honest answer is false.</summary>
    bool SupportsGlassOverflow { get; }
    /// <summary>Raised whenever the HOST hides the overlay on its own (deactivation, monitor change, other app activated), so controllers can cancel searches / clear state.</summary>
    event Action? OverlayHidden;
    /// <summary>
    /// Core override of auto-hide reveal for ONE dock (e.g. keep it revealed while a dockMenu popover is open or during an attention bounce). The Host itself owns pointer
    /// edge detection and sends dock.reveal; this only forces the state until released with revealed=false.
    /// </summary>
    void SetDockRevealed(SurfaceId dock, bool revealed);
    /// <summary>
    /// A surface reported its glass panels (surface.shape). Host contract: see the surface.shape doc in docs/IPC.md (window region = union(rects, hitRects); backdrop via
    /// DWMWA_SYSTEMBACKDROP_TYPE; never DwmEnableBlurBehindWindow).
    /// </summary>
    void SetSurfaceShape(SurfaceId surface, SurfaceShapeParams shape);
    /// <summary>Open or focus the settings window (a normal window, not click-through) and navigate to <paramref name="page"/>.</summary>
    void ShowSettingsWindow(string? page);
    /// <summary>
    /// Reason-keyed visibility for Core-initiated hides: a surface is shown only while NO reason hides it. kind/monitorId == null means all. The Host sends surface.visibility
    /// to the affected surfaces and uses SetWindowPos(HWND_BOTTOM)/hiding instead of destroying them. Automatic fullscreen suppression is OWNED BY THE HOST (per monitor, from
    /// IAppBar.Notification + IWindowSystem.FullscreenChanged); Core uses this only for its own reasons (e.g. "overlay").
    /// </summary>
    void SetSurfacesVisible(SurfaceKind? kind, string? monitorId, bool visible, string reason);
}

public interface IAppLifecycle
{
    /// <summary>Shut the shell down: restore the taskbar/desktop icons, dispose surfaces, exit the process. Posts the shutdown to the Dispatcher and returns immediately; the in-flight call result is still delivered.</summary>
    void Exit(bool restoreTaskbar);
    string Version { get; }
    string BuildInfo { get; }
    string OsDescription { get; }
    string WebView2Version { get; }
}

/// <summary>Owner of the clipboard (needs an STA UI thread, so implemented by the Host).</summary>
public interface IClipboard
{
    void SetText(string text);
}

/// <summary>Native open dialogs (the UI cannot obtain filesystem paths itself). Implemented by the Host; modal to the owner surface's window.</summary>
public interface IFilePicker
{
    /// <summary>Returns the chosen absolute paths; empty = cancelled. Only one dialog at a time (a second call while open returns empty).</summary>
    Task<IReadOnlyList<string>> PickAsync(PickPathParams request, SurfaceId owner, CancellationToken ct = default);
}
