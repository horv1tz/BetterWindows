using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Host.Surfaces;

/// <summary>STUB — owner: host-surfaces. Implements the Core-facing ISurfaceManager and the Host-facing ISurfaceHost.</summary>
public sealed class SurfaceManager : ISurfaceManager, ISurfaceHost
{
    public void ResizeSurface(SurfaceId surface, double widthDip, double heightDip, double? barThicknessDip) { }
    public void ShowOverlay(OverlayRequest request, SurfaceId? source) { }
    public void HideOverlay() { }
    public bool IsOverlayVisible => false;
    public OverlayMode? CurrentOverlayMode => null;
    public int OverlayToken => 0;
    public bool SupportsGlassOverflow => false;
    public event Action? OverlayHidden { add { } remove { } }
    public void SetDockRevealed(SurfaceId dock, bool revealed) { }
    public void SetSurfaceShape(SurfaceId surface, SurfaceShapeParams shape) { }
    public void ShowSettingsWindow(string? page) { }
    public void SetSurfacesVisible(SurfaceKind? kind, string? monitorId, bool visible, string reason) { }
    public void Start() { }
    public void Stop() { }
}
