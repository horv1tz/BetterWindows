using System.ComponentModel;
using System.Windows.Interop;
using System.Windows.Media;
using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Host.Bridge;
using BetterWindows.Host.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Wpf;

namespace BetterWindows.Host.Surfaces;

/// <summary>What a surface window is made of (decided once, at construction: WPF cannot change AllowsTransparency after the HWND exists).</summary>
/// <param name="Id">Surface identity assigned to the WebView.</param>
/// <param name="Route">Route for IWebViewBridge.AttachAsync.</param>
/// <param name="Layered">WPF AllowsTransparency (WS_EX_LAYERED, per-pixel alpha): overlay only. See the technology decision in <see cref="SurfaceWindowBase"/>.</param>
/// <param name="NoActivate">WS_EX_NOACTIVATE + WM_MOUSEACTIVATE=MA_NOACTIVATE: dock, menu bar, desktop.</param>
/// <param name="ToolWindow">WS_EX_TOOLWINDOW: no taskbar button, not in Alt+Tab.</param>
/// <param name="Topmost">Topmost z-order band.</param>
/// <param name="CompositionControl">Prefer WebView2CompositionControl (renders into the WPF visual tree with alpha) over the classic HWND-hosted WebView2; see <see cref="WebViewFactory"/>.</param>
/// <param name="GlassBackdrop">Non-layered window with DwmExtendFrameIntoClientArea + a DWM system backdrop (dock, menu bar).</param>
/// <param name="ManagesRegion">The window region is driven by surface.shape (dock, menu bar).</param>
internal sealed record SurfaceWindowSpec(SurfaceId Id, string Route, bool Layered, bool NoActivate, bool ToolWindow, bool Topmost, bool CompositionControl, bool GlassBackdrop, bool ManagesRegion)
{
    public SurfaceKind Kind => Id.Kind;
}

/// <summary>
/// Base of the five surface windows: a WPF <see cref="System.Windows.Window"/> that hosts ONE WebView2 and owns every native behaviour around it.
///
/// ===== WINDOW TECHNOLOGY DECISION (the "transparent glass" question) =====
/// Requirements: the dock and menu bar must show a real BLURRED backdrop behind rounded glass panels and be click-through everywhere else; the overlay must be fully
/// transparent (no backdrop) but still catch clicks; none of them may flash white.
///  (a) WPF AllowsTransparency=True (WS_EX_LAYERED, UpdateLayeredWindow per-pixel alpha) + WebView2CompositionControl: renders transparently and hit-tests by alpha, but a
///      layered window is NOT known to coexist with DWM system backdrops (DWMWA_SYSTEMBACKDROP_TYPE) and DwmEnableBlurBehindWindow no longer blurs since Windows 8.
///  (b) NON-layered window + DwmExtendFrameIntoClientArea(-1) + DWMWA_SYSTEMBACKDROP_TYPE (acrylic) + transparent WebView2 background: the documented way to get a real
///      backdrop on Windows 11 22H2+. A non-layered window cannot be see-through, so the shape is cut out with SetWindowRgn (union of the glass rects and hitRects):
///      everything outside is not part of the window at all (invisible AND click-through).
/// DECISION: dock and menu bar use (b) (GlassBackdrop + ManagesRegion, WebView2CompositionControl so the page's alpha reaches the backdrop); the overlay uses (a)
/// (it needs true see-through over a full monitor and never wants a backdrop; its WPF background is alpha 1/255 so that the layered window hit-tests EVERYWHERE and the
/// transparent backdrop area catches the "click outside to dismiss"); the desktop layer is an opaque, region-less NOACTIVATE tool window with the classic WebView2;
/// settings is an ordinary framed window with Mica (DWMSBT_MAINWINDOW) and a graceful opaque fallback. Both control types implement IWebView2, so the bridge accepts either.
/// Needs a real Windows 11 desktop to confirm (listed in the report): (1) the acrylic backdrop is clipped by the window region, (2) WebView2CompositionControl alpha reaches
/// the backdrop in a non-layered window, (3) the DWM shadow/border around a region-shaped popup. Fallback switch if (1)-(3) fail on some build:
/// BETTERWINDOWS_SURFACE_TECH=layered builds the dock/menu bar as layered windows without a backdrop (the UI's translucent CSS fill remains).
///
/// ===== common behaviour =====
///  - The HWND is created with EnsureHandle() BEFORE the window is shown; styles, DWM attributes and the region are applied first, so there is no flash of an unstyled window.
///    Until the page reported its shape (dock/menu bar) or surface.ready (desktop) the window region is EMPTY: the window exists and the WebView loads, but nothing is
///    visible or clickable ("no white flash on creation").
///  - Placement always uses physical pixels via SetWindowPos (PerMonitorV2); the DPI in effect is read back after the move because moving a window onto a monitor with
///    another DPI changes it (WM_DPICHANGED), and the geometry is recomputed once with the real scale.
///  - Exceptions never escape: window operations catch, log and degrade.
/// </summary>
internal abstract class SurfaceWindowBase : System.Windows.Window
{
    protected readonly ILogger Log;
    private readonly TimeProvider _time;
    private readonly WebViewHost _web;
    private WndProcHook? _hook;
    private nint _hwnd;
    private bool _shutdown;
    private bool _detached;
    private bool _allowClose;
    private bool _contentHidden = true;
    private SurfaceShapeParams? _shape;
    private string? _appliedRegionSignature;
    private ITimer? _shapeTimer;
    private bool _glassEnabled;
    private bool _glassDark;
    private bool _glassApplied;
    private double _lastKnownScale = 1.0;
    private bool _topmostAsserted;

    protected SurfaceWindowBase(SurfaceWindowSpec spec, TimeProvider time, ILogger log, MonitorInfo monitor)
    {
        Spec = spec;
        Log = log;
        _time = time;
        Monitor = monitor;
        _lastKnownScale = MonitorGeometry.Scale(monitor);

        // These must be set BEFORE the HWND is created.
        WindowStyle = System.Windows.WindowStyle.None;
        ResizeMode = System.Windows.ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = spec.Topmost;
        AllowsTransparency = spec.Layered;
        WindowStartupLocation = System.Windows.WindowStartupLocation.Manual;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Focusable = !spec.NoActivate;
        Background = spec.Layered ? new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) : System.Windows.Media.Brushes.Transparent;

        _web = WebViewFactory.Create(spec.CompositionControl, log);
        Content = _web.Element;
    }

    public SurfaceWindowSpec Spec { get; }
    public SurfaceId SurfaceId => Spec.Id;
    public SurfaceKind Kind => Spec.Kind;
    public string Route => Spec.Route;

    /// <summary>The monitor this window currently belongs to (updated by the manager on every re-plan).</summary>
    public MonitorInfo Monitor { get; set; }

    public nint Hwnd => _hwnd;
    public bool IsShutdown => _shutdown;
    public IWebView2 WebView => _web.WebView;
    public System.Windows.FrameworkElement WebViewElement => _web.Element;

    /// <summary>True once the page completed surface.ready (set by the manager from SurfaceRegistry.SurfaceReady).</summary>
    public bool PageReady { get; set; }

    /// <summary>Raised on WM_DPICHANGED (after WPF processed it). The manager re-plans the geometry.</summary>
    public event Action<SurfaceWindowBase>? ScaleChanged;

    /// <summary>Raised when the window is (de)activated by the system (overlay dismissal, settings).</summary>
    public event Action<SurfaceWindowBase, bool>? ActivationChanged;

    /// <summary>Raised when the user closed the window (only windows with <see cref="AllowUserClose"/>).</summary>
    public event Action<SurfaceWindowBase>? ClosedByUser;

    /// <summary>Raised when the user tried to close a window that must not be closed by the user (Alt+F4 on the overlay): the owner decides what that means (hide).</summary>
    public event Action<SurfaceWindowBase>? CloseRequested;

    protected virtual bool AllowUserClose => false;

    /// <summary>DPI scale in effect for THIS window (GetDpiForWindow), falling back to the last known value.</summary>
    public double Scale
    {
        get
        {
            var s = DpiHelper.GetScaleForWindow(_hwnd, _lastKnownScale);
            _lastKnownScale = s;
            return s;
        }
    }

    // ------------------------------------------------------------------ creation

    /// <summary>Creates the HWND (hidden), applies the styles and hooks. Idempotent.</summary>
    public void CreateNativeWindow()
    {
        if (_hwnd != 0 || _shutdown) return;
        var helper = new WindowInteropHelper(this);
        _hwnd = helper.EnsureHandle();
        var source = HwndSource.FromHwnd(_hwnd);
        if (source is not null)
        {
            // Pixels WPF does not draw must be transparent black so that the DWM backdrop (or the layered alpha) shows through them.
            if (source.CompositionTarget is { } target) target.BackgroundColor = Colors.Transparent;
            _hook = new WndProcHook(source, OnWndProc, Log);
        }
        if (Spec.ToolWindow && !WindowStyling.ApplyToolWindow(_hwnd, Spec.NoActivate))
            Log.LogWarning("{Surface}: could not apply WS_EX_TOOLWINDOW/NOACTIVATE", SurfaceId);
        if (Spec.GlassBackdrop || Spec.Kind is SurfaceKind.Overlay or SurfaceKind.Desktop)
        {
            // Our windows are rectangles cut by regions: DWM must not round or outline them on its own.
            DwmHelper.TrySetCorners(_hwnd, Windows.Win32.Graphics.Dwm.DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_DONOTROUND, out _);
            DwmHelper.TrySetNoBorder(_hwnd, out _);
        }
        OnNativeWindowCreated();
        if (_web.Element is System.Windows.Controls.Control c) c.IsTabStop = !Spec.NoActivate;
        ApplyRegion(); // an empty region immediately: nothing of this window is visible until the page is ready
    }

    protected virtual void OnNativeWindowCreated() { }

    // ------------------------------------------------------------------ messages

    private WndProcResult OnWndProc(int msg, nint wParam, nint lParam)
    {
        if (_shutdown) return WndProcResult.NotHandled;
        switch (msg)
        {
            case Win32Const.WM_MOUSEACTIVATE when Spec.NoActivate:
                // A click on a no-activate surface must never make BetterWindows the foreground application.
                return WndProcResult.Return(Win32Const.MA_NOACTIVATE);
            case Win32Const.WM_DPICHANGED:
                // Not handled: WPF must update its own DPI/layout first; the manager re-plans afterwards (a posted callback).
                SafeRaise(() => ScaleChanged?.Invoke(this));
                break;
            case Win32Const.WM_ACTIVATE:
                SafeRaise(() => ActivationChanged?.Invoke(this, (wParam.ToInt64() & 0xFFFF) != Win32Const.WA_INACTIVE));
                break;
        }
        return OnMessage(msg, wParam, lParam);
    }

    /// <summary>Subclass hook for window messages (the AppBar windows feed every message to IAppBar.ProcessWindowMessage here).</summary>
    protected virtual WndProcResult OnMessage(int msg, nint wParam, nint lParam) => WndProcResult.NotHandled;

    private void SafeRaise(Action a)
    {
        try { a(); }
        catch (Exception ex) { Log.LogError(ex, "{Surface}: window event handler failed", SurfaceId); }
    }

    // ------------------------------------------------------------------ WebView / bridge

    /// <summary>Attaches the WebView to the IPC fabric. Failures are logged; the manager's failure policy handles recreation.</summary>
    public async Task AttachAsync(IWebViewBridge bridge)
    {
        try { await bridge.AttachAsync(WebView, SurfaceId, Route).ConfigureAwait(true); }
        catch (Exception ex) when (!_shutdown) { Log.LogError(ex, "{Surface}: attaching the WebView failed", SurfaceId); }
        catch (Exception) { /* window was shut down while the WebView was still initialising */ }
    }

    /// <summary>
    /// Tears the window down exactly once: bridge detach (exactly once per window), AppBar undock via <see cref="OnShuttingDown"/>, region clear, close, WebView dispose.
    /// </summary>
    public void Shutdown(IWebViewBridge bridge)
    {
        if (_shutdown) return;
        _shutdown = true;
        try { _shapeTimer?.Dispose(); } catch { }
        try { OnShuttingDown(); } catch (Exception ex) { Log.LogError(ex, "{Surface}: shutdown hook failed", SurfaceId); }
        if (!_detached)
        {
            _detached = true;
            try { bridge.Detach(SurfaceId); } catch (Exception ex) { Log.LogError(ex, "{Surface}: bridge detach failed", SurfaceId); }
        }
        try { _hook?.Dispose(); } catch { }
        try { if (_hwnd != 0) RegionBuilder.Clear(_hwnd); } catch { }
        _allowClose = true;
        try { Close(); } catch (Exception ex) { Log.LogDebug(ex, "{Surface}: Close failed", SurfaceId); }
        try { _web.Dispose(); } catch (Exception ex) { Log.LogDebug(ex, "{Surface}: WebView dispose failed", SurfaceId); }
    }

    /// <summary>Called first during <see cref="Shutdown"/> (undock AppBars, detach the desktop layer ...).</summary>
    protected virtual void OnShuttingDown() { }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose && !AllowUserClose)
        {
            e.Cancel = true; // Alt+F4 on a surface window must not destroy it
            SafeRaise(() => CloseRequested?.Invoke(this));
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (!_allowClose && AllowUserClose) SafeRaise(() => ClosedByUser?.Invoke(this));
    }

    // ------------------------------------------------------------------ visibility

    /// <summary>Shows the window without activating it. Safe to call repeatedly.</summary>
    public void ShowNoActivate()
    {
        if (_shutdown) return;
        try { if (!IsVisible) Show(); }
        catch (Exception ex) { Log.LogError(ex, "{Surface}: Show failed", SurfaceId); }
    }

    public void HideWindow()
    {
        if (_shutdown) return;
        try { if (IsVisible) Hide(); }
        catch (Exception ex) { Log.LogError(ex, "{Surface}: Hide failed", SurfaceId); }
    }

    // ------------------------------------------------------------------ placement

    /// <summary>
    /// Moves/sizes the window to the rectangle computed for a given scale. The rectangle depends on the DPI scale (DIP sizes), and the scale depends on the monitor the window
    /// ends up on, so: place with the monitor's nominal scale, read the REAL scale back, and place again once if it differs. Returns the final rectangle.
    /// </summary>
    protected NativeRect PlaceWithDpiFix(Func<double, NativeRect> compute)
    {
        // The topmost band is asserted ONCE (first placement) and on demand: re-asserting it on every resize would raise the dock above the overlay while a popover is open.
        nint? z = Spec.Topmost && !_topmostAsserted ? WindowStyling.HwndTopmost : null;
        var nominal = MonitorGeometry.Scale(Monitor);
        var rect = compute(nominal);
        ApplyBounds(rect, z);
        var actual = Scale;
        if (Math.Abs(actual - nominal) > 0.01)
        {
            rect = compute(actual);
            ApplyBounds(rect, z);
        }
        if (z is not null) _topmostAsserted = true;
        return rect;
    }

    /// <summary>Forces the next placement to re-assert HWND_TOPMOST (e.g. after the window was hidden and shown again).</summary>
    public void InvalidateZOrder() => _topmostAsserted = false;

    private void ApplyBounds(NativeRect rect, nint? insertAfter)
    {
        if (_hwnd == 0 || _shutdown) return;
        if (insertAfter is null && WindowStyling.TryGetWindowRect(_hwnd, out var cur) && cur == rect) return;
        if (!WindowStyling.SetBounds(_hwnd, rect, insertAfter))
            Log.LogDebug("{Surface}: SetWindowPos failed ({Error})", SurfaceId, System.Runtime.InteropServices.Marshal.GetLastPInvokeError());
        ApplyRegion(); // the region is window-relative and depends on the window size
    }

    // ------------------------------------------------------------------ region (surface.shape)

    /// <summary>The window shows nothing and ignores the pointer while true (the region is empty). Starts true; the manager flips it when the page is presentable.</summary>
    public bool ContentHidden
    {
        get => _contentHidden;
        set
        {
            if (_contentHidden == value) return;
            _contentHidden = value;
            ApplyRegion();
        }
    }

    public bool HasShape => _shape is not null;

    /// <summary>
    /// Stores the latest shape (already limited by Core; <see cref="ShapePlanner"/> enforces the limits again). The first shape is applied immediately (it makes the surface
    /// presentable); later ones are debounced by 16 ms so a burst of surface.shape messages costs one region.
    /// </summary>
    public void SetShape(SurfaceShapeParams shape)
    {
        if (!Spec.ManagesRegion || _shutdown) return;
        var first = _shape is null;
        _shape = shape;
        if (first) { ApplyRegion(); return; }
        _shapeTimer ??= _time.CreateTimer(_ => OnShapeTimer(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _shapeTimer.Change(TimeSpan.FromMilliseconds(16), Timeout.InfiniteTimeSpan);
    }

    /// <summary>Last blur request of the UI (surface.shape.blur); combined with reduceTransparency by the manager.</summary>
    public bool ShapeWantsBlur => _shape?.Blur ?? true;

    private void OnShapeTimer()
    {
        try { Dispatcher.BeginInvoke(new Action(ApplyRegion)); }
        catch (Exception) { /* dispatcher shut down */ }
    }

    /// <summary>The part of the window that is clipped away (null = nothing). The auto-hiding menu bar overrides it while it slides.</summary>
    protected virtual NativeRect? RegionClipLocal() => null;

    /// <summary>Recomputes the region from the stored shape and window size and applies it if it changed.</summary>
    public void ApplyRegion()
    {
        if (_hwnd == 0 || _shutdown) return;
        string signature;
        RegionPlan plan = RegionPlan.Empty;
        NativeRect? clip = null;
        var clear = false;
        if (Spec.ManagesRegion)
        {
            if (!_contentHidden && _shape is not null && WindowStyling.TryGetWindowRect(_hwnd, out var rect))
            {
                plan = ShapePlanner.Plan(_shape, rect.Width, rect.Height, Scale);
                clip = RegionClipLocal();
            }
            signature = plan.Signature + "|" + (clip is { } c ? $"{c.Left},{c.Top},{c.Right},{c.Bottom}" : "-");
        }
        else if (_contentHidden)
        {
            signature = "empty";
        }
        else
        {
            clear = true;
            signature = "clear";
        }
        if (signature == _appliedRegionSignature) return;
        var ok = clear ? RegionBuilder.Clear(_hwnd) : RegionBuilder.Apply(_hwnd, plan, clip);
        if (ok) _appliedRegionSignature = signature;
        else Log.LogWarning("{Surface}: SetWindowRgn failed ({Error})", SurfaceId, System.Runtime.InteropServices.Marshal.GetLastPInvokeError());
    }

    // ------------------------------------------------------------------ glass backdrop

    /// <summary>
    /// Applies the backdrop for the current theme: acrylic when <paramref name="blur"/>, nothing otherwise. Windows 11 22H2+ use DWMWA_SYSTEMBACKDROP_TYPE; older builds
    /// use the undocumented accent only behind BETTERWINDOWS_LEGACY_ACCENT=1; otherwise there is no backdrop and the UI's translucent CSS fill remains.
    /// </summary>
    public void ApplyGlass(bool blur, bool darkTheme)
    {
        if (!Spec.GlassBackdrop || _hwnd == 0 || _shutdown) return;
        if (_glassApplied && _glassEnabled == blur && _glassDark == darkTheme) return;
        _glassApplied = true;
        _glassEnabled = blur;
        _glassDark = darkTheme;
        if (DwmHelper.IsSystemBackdropSupported)
        {
            // Order matters: the tint follows the immersive dark flag, the frame extension makes the backdrop visible behind alpha-0 pixels, the type switches it on/off.
            if (!DwmHelper.TrySetDarkMode(_hwnd, darkTheme, out var hr1)) Log.LogDebug("{Surface}: dark mode attribute failed 0x{Hr:X8}", SurfaceId, hr1);
            if (!DwmHelper.TryExtendFrame(_hwnd, blur, out var hr2)) Log.LogDebug("{Surface}: DwmExtendFrameIntoClientArea failed 0x{Hr:X8}", SurfaceId, hr2);
            if (!DwmHelper.TrySetBackdrop(_hwnd, blur ? SystemBackdrop.Acrylic : SystemBackdrop.None, out var hr3)) Log.LogWarning("{Surface}: system backdrop failed 0x{Hr:X8}", SurfaceId, hr3);
        }
        else if (LegacyAccentBackdrop.ShouldUse)
        {
            if (!LegacyAccentBackdrop.TrySet(_hwnd, blur, darkTheme ? 0x66101010u : 0x66F2F2F2u)) Log.LogWarning("{Surface}: legacy accent backdrop failed", SurfaceId);
        }
    }

    // ------------------------------------------------------------------ helpers
}
