using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Host.Interop;
using Microsoft.Extensions.Logging;

namespace BetterWindows.Host.Surfaces;

/// <summary>
/// The dock: a topmost, never-activating, region-shaped window centred on the docked edge of its monitor. The UI owns the SIZE (surface.resize), the host clamps and places
/// it (physical px, <see cref="DockPlacement"/>), optionally reserves the bar thickness as an AppBar (only when reserveSpace and not auto-hide) and re-docks on ABN_POSCHANGED.
/// </summary>
internal sealed class DockWindow : SurfaceWindowBase
{
    private readonly IAppBarFactory _appBarFactory;
    private IAppBar? _appBar;
    private (AppBarEdge Edge, NativeRect Bounds, int Thickness)? _docked;
    private NativeRect _appBarRect;
    private bool _suspended;

    public DockWindow(SurfaceId id, string route, MonitorInfo monitor, TimeProvider time, ILogger log, IAppBarFactory appBars)
        : base(new SurfaceWindowSpec(id, route, Layered: SurfaceTechnology.UseLayeredForBars, NoActivate: true, ToolWindow: true, Topmost: true, CompositionControl: true,
                                     GlassBackdrop: !SurfaceTechnology.UseLayeredForBars, ManagesRegion: true), time, log, monitor)
    {
        _appBarFactory = appBars;
    }

    public DockPosition Position { get; set; } = DockPosition.Bottom;
    public bool ReserveSpace { get; set; } = true;
    public bool AutoHide { get; set; }
    public int IconSize { get; set; } = 56;
    /// <summary>The native taskbar stays visible: the dock edge is the work-area edge so the dock does not cover it.</summary>
    public bool UseWorkAreaEdge { get; set; }

    /// <summary>Window size the UI asked for (DIP). Defaults make the first layout sane before the first surface.resize.</summary>
    public double WidthDip { get; private set; } = 640;
    public double HeightDip { get; private set; } = 160;
    /// <summary>Bar thickness the UI reported (DIP); null = use icon size + 24.</summary>
    public double? BarThicknessDip { get; private set; }

    /// <summary>Last placed rectangle (virtual-screen px).</summary>
    public NativeRect RectPx { get; private set; }

    public bool IsSuspended => _suspended;

    /// <summary>Raised on the UI thread for every AppBar notification (PosChanged -> re-dock, FullScreenApp -> fullscreen re-evaluation).</summary>
    public event Action<DockWindow, AppBarNotification>? AppBarNotified;

    /// <summary>Applies a size from surface.resize. Non-finite or non-positive values are ignored; the size is clamped to the monitor in <see cref="Reposition"/>.</summary>
    public void SetRequestedSize(double widthDip, double heightDip, double? barThicknessDip)
    {
        if (double.IsFinite(widthDip) && widthDip >= 1) WidthDip = widthDip;
        if (double.IsFinite(heightDip) && heightDip >= 1) HeightDip = heightDip;
        if (barThicknessDip is { } b && double.IsFinite(b) && b >= 1) BarThicknessDip = b;
    }

    /// <summary>Computes and applies the window rectangle (and the AppBar reservation). Cheap when nothing changed.</summary>
    public NativeRect Reposition()
    {
        if (_suspended || IsShutdown || Hwnd == 0) return RectPx;
        var bounds = MonitorGeometry.BoundsPx(Monitor);
        var work = MonitorGeometry.WorkAreaPx(Monitor);
        var barDip = BarThicknessDip ?? DockPlacement.DefaultBarThicknessDip(IconSize);
        var reserve = ReserveSpace && !AutoHide;
        RectPx = PlaceWithDpiFix(scale =>
        {
            int edge;
            if (reserve)
            {
                var thickness = DockPlacement.ReservedThicknessPx(barDip, scale, Position, bounds);
                edge = DockAppBar(bounds, thickness) ?? DockPlacement.EdgeWithoutAppBar(Position, bounds, work, UseWorkAreaEdge);
            }
            else
            {
                UndockAppBar();
                edge = DockPlacement.EdgeWithoutAppBar(Position, bounds, work, UseWorkAreaEdge);
            }
            var w = DpiMath.DipToPxCeil(WidthDip, scale);
            var h = DpiMath.DipToPxCeil(HeightDip, scale);
            return DockPlacement.Compute(new DockPlacementInput(Position, bounds, work, edge, w, h));
        });
        return RectPx;
    }

    /// <summary>Fullscreen suppression / Core hide: release the reserved space and hide the window.</summary>
    public void Suspend()
    {
        if (_suspended) return;
        _suspended = true;
        UndockAppBar();
        HideWindow();
    }

    public void Resume()
    {
        if (!_suspended) return;
        _suspended = false;
        InvalidateZOrder();
        ShowNoActivate();
        Reposition();
    }

    private int? DockAppBar(NativeRect bounds, int thickness)
    {
        try
        {
            var bar = EnsureAppBar();
            var edge = DockPlacement.ToAppBarEdge(Position);
            var key = (edge, bounds, thickness);
            if (_docked != key || !bar.IsRegistered)
            {
                _appBarRect = bar.Dock(edge, bounds, thickness);
                _docked = key;
            }
            return DockPlacement.EdgeFromAppBarRect(Position, _appBarRect);
        }
        catch (Exception ex)
        {
            Log.LogWarning(ex, "{Surface}: AppBar docking failed; placing the dock without reserving space", SurfaceId);
            return null;
        }
    }

    private void UndockAppBar()
    {
        try { if (_appBar?.IsRegistered == true) _appBar.Undock(); }
        catch (Exception ex) { Log.LogWarning(ex, "{Surface}: AppBar undock failed", SurfaceId); }
        _docked = null;
    }

    private IAppBar EnsureAppBar()
    {
        if (_appBar is not null) return _appBar;
        var bar = _appBarFactory.Create(Hwnd);
        bar.Notification += OnAppBarNotification;
        _appBar = bar;
        return bar;
    }

    private void OnAppBarNotification(AppBarNotification n)
    {
        if (IsShutdown) return;
        // PosChanged: another AppBar changed the work area; re-run QUERYPOS/SETPOS with the same parameters.
        if (n == AppBarNotification.PosChanged) _docked = null;
        try { AppBarNotified?.Invoke(this, n); }
        catch (Exception ex) { Log.LogError(ex, "{Surface}: AppBar notification handler failed", SurfaceId); }
    }

    protected override WndProcResult OnMessage(int msg, nint wParam, nint lParam)
    {
        // The AppBar implementation needs EVERY message of its window (callback message, WM_WINDOWPOSCHANGED, display/DPI changes).
        var bar = _appBar;
        if (bar is not null && bar.ProcessWindowMessage((uint)msg, wParam, lParam)) return WndProcResult.Return(0);
        return WndProcResult.NotHandled;
    }

    protected override void OnShuttingDown()
    {
        UndockAppBar();
        try { _appBar?.Dispose(); } catch (Exception ex) { Log.LogDebug(ex, "{Surface}: AppBar dispose failed", SurfaceId); }
        _appBar = null;
    }
}
