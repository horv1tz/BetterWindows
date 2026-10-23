using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Host.Interop;
using Microsoft.Extensions.Logging;

namespace BetterWindows.Host.Surfaces;

/// <summary>
/// The menu bar of one monitor: a 28 DIP full-width glass strip on the top edge. It is an AppBar (reserves its band) unless auto-hide is on. Auto-hide slides the window up
/// by its own height; the part that leaves the monitor is clipped by the window region (<see cref="SlideAnimation.LocalClip"/>), so it never shows on the monitor above.
/// </summary>
internal sealed class MenuBarWindow : SurfaceWindowBase
{
    private readonly IAppBarFactory _appBarFactory;
    private IAppBar? _appBar;
    private (NativeRect Bounds, int Thickness)? _docked;
    private int _offsetPx;
    private bool _revealed = true;
    private bool _revealing = true;
    private readonly TimeProvider _clock;
    private long _slideStart;
    private ITimer? _slideTimer;
    private bool _sliding;

    public MenuBarWindow(SurfaceId id, string route, MonitorInfo monitor, TimeProvider time, ILogger log, IAppBarFactory appBars)
        : base(new SurfaceWindowSpec(id, route, Layered: SurfaceTechnology.UseLayeredForBars, NoActivate: true, ToolWindow: true, Topmost: true, CompositionControl: true,
                                     GlassBackdrop: !SurfaceTechnology.UseLayeredForBars, ManagesRegion: true), time, log, monitor)
    {
        _appBarFactory = appBars;
        _clock = time;
    }

    /// <summary>settings.menuBar.autoHide: the bar slides away and reserves no space.</summary>
    public bool AutoHide { get; set; }

    /// <summary>True while the bar is shown (not sliding away). Starts shown.</summary>
    public bool IsRevealed => _revealed;

    /// <summary>Raised on the UI thread for every AppBar notification (PosChanged -> re-dock, FullScreenApp -> fullscreen re-evaluation).</summary>
    public event Action<MenuBarWindow, AppBarNotification>? AppBarNotified;

    /// <summary>Places the bar at the top of its monitor (reserving the band when not auto-hidden) and returns the rectangle it occupies (virtual-screen px).</summary>
    public NativeRect Reposition()
    {
        if (IsShutdown || Hwnd == 0) return default;
        var bounds = MonitorGeometry.BoundsPx(Monitor);
        var reserve = !AutoHide;
        return PlaceWithDpiFix(scale =>
        {
            var h = MenuBarPlacement.HeightPx(scale);
            int top = bounds.Top;
            if (reserve)
            {
                var rect = DockAppBar(bounds, h);
                if (rect is { } r) top = r.Top;
            }
            else
            {
                UndockAppBar();
            }
            // The slide offset moves the bar up out of the monitor; the region clip removes the part above the monitor.
            var offset = _offsetPx;
            var t = top - offset;
            return new NativeRect(bounds.Left, t, bounds.Right, t + h);
        });
    }

    /// <summary>Slides the bar in or out. With <paramref name="animate"/> false it jumps (reduceMotion).</summary>
    public void SetRevealed(bool revealed, bool animate)
    {
        if (IsShutdown) return;
        _revealed = revealed;
        if (!animate)
        {
            StopSlide();
            _offsetPx = revealed ? 0 : MenuBarPlacement.HeightPx(Scale);
            Reposition();
            return;
        }
        _revealing = revealed;
        _slideStart = _clock.GetTimestamp();
        _sliding = true;
        _slideTimer ??= _clock.CreateTimer(_ => OnSlideTimer(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _slideTimer.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(16));
    }

    /// <summary>Timer thread: marshals one animation frame to the window's dispatcher (never touches WPF here).</summary>
    private void OnSlideTimer()
    {
        try { Dispatcher.BeginInvoke(new Action(SlideStep)); }
        catch (Exception) { /* dispatcher shut down */ }
    }

    private void SlideStep()
    {
        if (!_sliding || IsShutdown) return;
        var elapsed = _clock.GetElapsedTime(_slideStart);
        var p = SlideAnimation.Progress(elapsed, SlideAnimation.Duration);
        _offsetPx = SlideAnimation.OffsetPx(p, MenuBarPlacement.HeightPx(Scale), _revealing);
        Reposition();
        if (p >= 1.0) StopSlide();
    }

    private void StopSlide()
    {
        _sliding = false;
        try { _slideTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan); } catch (Exception) { }
    }

    /// <summary>The part above the monitor is clipped away while the bar is partially slid out.</summary>
    protected override NativeRect? RegionClipLocal()
    {
        if (_offsetPx <= 0 || !WindowStyling.TryGetWindowRect(Hwnd, out var rect)) return null;
        return SlideAnimation.LocalClip(rect, MonitorGeometry.BoundsPx(Monitor));
    }

    private NativeRect? DockAppBar(NativeRect bounds, int thickness)
    {
        try
        {
            var bar = EnsureAppBar();
            var key = (bounds, thickness);
            if (_docked != key || !bar.IsRegistered)
            {
                var rect = bar.Dock(AppBarEdge.Top, bounds, thickness);
                _docked = key;
                return rect;
            }
            return bar.IsRegistered ? new NativeRect(bounds.Left, bounds.Top, bounds.Right, bounds.Top + thickness) : null;
        }
        catch (Exception ex)
        {
            Log.LogWarning(ex, "{Surface}: AppBar registration failed; the menu bar does not reserve space", SurfaceId);
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
        if (n == AppBarNotification.PosChanged) _docked = null;
        try { AppBarNotified?.Invoke(this, n); }
        catch (Exception ex) { Log.LogError(ex, "{Surface}: AppBar notification handler failed", SurfaceId); }
    }

    protected override WndProcResult OnMessage(int msg, nint wParam, nint lParam)
    {
        var bar = _appBar;
        if (bar is not null && bar.ProcessWindowMessage((uint)msg, wParam, lParam)) return WndProcResult.Return(0);
        return WndProcResult.NotHandled;
    }

    protected override void OnShuttingDown()
    {
        StopSlide();
        try { _slideTimer?.Dispose(); } catch (Exception) { }
        _slideTimer = null;
        UndockAppBar();
        try { _appBar?.Dispose(); } catch (Exception ex) { Log.LogDebug(ex, "{Surface}: AppBar dispose failed", SurfaceId); }
        _appBar = null;
    }
}
