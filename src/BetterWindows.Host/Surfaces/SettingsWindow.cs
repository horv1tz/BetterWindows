using BetterWindows.Core.Ipc;
using System.Windows.Media;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Host.Interop;
using Microsoft.Extensions.Logging;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace BetterWindows.Host.Surfaces;

/// <summary>
/// The settings window: an ordinary framed, resizable, single-instance window (not a popover). It is the only surface that is activated on purpose, it appears in
/// Alt+Tab and the taskbar like any app, and its placement is remembered (<see cref="SettingsWindowState"/>). Closing it by the user destroys the window; the next
/// open creates a new one (the WebView is cheap to re-create and the page reads its state from the host anyway).
/// </summary>
internal sealed class SettingsWindow : SurfaceWindowBase
{
    public SettingsWindow(SurfaceId id, string route, MonitorInfo monitor, TimeProvider time, ILogger log, string title)
        : base(new SurfaceWindowSpec(id, route, Layered: false, NoActivate: false, ToolWindow: false, Topmost: false, CompositionControl: false,
                                     GlassBackdrop: false, ManagesRegion: false), time, log, monitor)
    {
        // The base constructor configures a borderless glass surface; this window is an ordinary app window, so the styles are reset here.
        WindowStyle = System.Windows.WindowStyle.SingleBorderWindow;
        ResizeMode = System.Windows.ResizeMode.CanResize;
        ShowInTaskbar = true;
        Topmost = false;
        Focusable = true;
        MinWidth = SettingsWindowPlacement.MinWidthDip;
        MinHeight = SettingsWindowPlacement.MinHeightDip;
        Title = title;
        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF5, 0xF5, 0xF7));
    }

    protected override bool AllowUserClose => true;

    /// <summary>Opens the window at the resolved rectangle (physical px). Maximised restores through WPF after the first show.</summary>
    public void Open(SettingsWindowLayout layout)
    {
        if (IsShutdown) return;
        ContentHidden = false; // a framed window: the region must stay clear
        PlaceWithDpiFix(_ => layout.BoundsPx);
        if (!IsVisible) Show();
        if (layout.Maximized) WindowState = System.Windows.WindowState.Maximized;
        Activate();
    }

    /// <summary>Brings an open window to the front (settings.navigate / second launch).</summary>
    public void Bring()
    {
        if (IsShutdown) return;
        try
        {
            if (WindowState == System.Windows.WindowState.Minimized) WindowState = System.Windows.WindowState.Normal;
            if (!IsVisible) Show();
            Activate();
        }
        catch (Exception ex) { Log.LogWarning(ex, "{Surface}: could not bring the settings window forward", SurfaceId); }
    }

    /// <summary>The restored rectangle and state, for the next open. Null when the native window is gone.</summary>
    public unsafe SettingsWindowState? CaptureState()
    {
        if (Hwnd == 0) return null;
        try
        {
            var wp = new WINDOWPLACEMENT { length = (uint)sizeof(WINDOWPLACEMENT) };
            // GetWindowPlacement reports the RESTORED rectangle even while maximised, so a maximised window is remembered at its normal size too.
            if (!HostPInvoke.GetWindowPlacement(Hwnd.ToHwnd(), ref wp)) return null;
            var r = wp.rcNormalPosition;
            var dpi = (int)Math.Round(Scale * DpiMath.BaseDpi);
            return new SettingsWindowState
            {
                Left = r.left, Top = r.top, Width = r.right - r.left, Height = r.bottom - r.top, Dpi = dpi,
                Maximized = WindowState == System.Windows.WindowState.Maximized,
            };
        }
        catch (Exception ex)
        {
            Log.LogDebug(ex, "{Surface}: reading the window placement failed", SurfaceId);
            return null;
        }
    }

    /// <summary>Applies a localised title (settings.general.language changed).</summary>
    public void SetTitle(string title)
    {
        if (!IsShutdown) Title = title;
    }
}
