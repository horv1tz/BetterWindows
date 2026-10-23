using BetterWindows.Core.Ipc;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Platform;

// Ports implemented by the WPF Host (they need windows). Core controllers call them; tests fake them.

public interface ISurfaceManager
{
    /// <summary>Resize/reposition a surface window (dock): size in DIPs. Host clamps and keeps the dock edge-aligned/centred.</summary>
    void ResizeSurface(SurfaceId surface, double widthDip, double heightDip);
    /// <summary>
    /// Show the overlay window on the monitor (full-screen, activatable, topmost), translate popover anchors from the
    /// source surface to overlay coordinates, then send overlay.show. Hides the dock's autohide while open.
    /// </summary>
    void ShowOverlay(OverlayRequest request, SurfaceId source);
    /// <summary>Hide the overlay window (and send overlay.hide if still showing).</summary>
    void HideOverlay();
    bool IsOverlayVisible { get; }
    OverlayMode? CurrentOverlayMode { get; }
    /// <summary>Tell the dock surface to slide in/out (auto-hide) and size its window accordingly.</summary>
    void SetDockRevealed(bool revealed);
    /// <summary>Open or focus the settings window (a normal window, not click-through) and navigate to <paramref name="page"/>.</summary>
    void ShowSettingsWindow(string? page);
    /// <summary>
    /// A surface reported its glass panels (surface.shape). The host applies DWM blur-behind to exactly these rounded rects
    /// (DIPs, window-client relative) and uses them as the click-through window region (dock/menubar/desktop widgets).
    /// </summary>
    void SetSurfaceShape(SurfaceId surface, SurfaceShapeParams shape);
    /// <summary>Show/hide a whole surface kind (e.g. hide menubar+dock while a fullscreen app is active).</summary>
    void SetSurfacesVisible(bool visible, string reason);
}

public interface IAppLifecycle
{
    /// <summary>Shut the shell down: restore the taskbar/desktop icons, dispose surfaces, exit the process.</summary>
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
