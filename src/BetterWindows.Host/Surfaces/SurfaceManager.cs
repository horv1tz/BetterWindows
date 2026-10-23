using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Host.Surfaces;

/// <summary>STUB — owner: surfaces stream. Implements the Core-facing ISurfaceManager and the Host-facing ISurfaceHost.</summary>
public sealed class SurfaceManager : ISurfaceManager, ISurfaceHost
{
    public void ResizeSurface(SurfaceId surface, double widthDip, double heightDip) { }
    public void ShowOverlay(OverlayRequest request, SurfaceId source) { }
    public void HideOverlay() { }
    public bool IsOverlayVisible => false;
    public OverlayMode? CurrentOverlayMode => null;
    public void SetDockRevealed(bool revealed) { }
    public void ShowSettingsWindow(string? page) { }
    public void SetSurfacesVisible(bool visible, string reason) { }
    public void Start() { }
    public void Stop() { }
}
