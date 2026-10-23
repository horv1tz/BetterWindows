using BetterWindows.Core.Platform;

namespace BetterWindows.Native;

/// <summary>
/// Pure rectangle math of the documented AppBar positioning pattern ("Using Application Desktop Toolbars"): propose a band of the wanted
/// thickness along the edge, let the shell shrink it around other appbars (ABM_QUERYPOS), re-pin the thickness at the docked edge,
/// then commit (ABM_SETPOS). Cross-platform and unit-tested; coordinates are physical pixels.
/// </summary>
internal static class AppBarMath
{
    /// <summary>Thickness clamped to [1, extent of the monitor across the edge].</summary>
    public static int ClampThickness(AppBarEdge edge, NativeRect monitor, int thickness)
    {
        int extent = edge is AppBarEdge.Left or AppBarEdge.Right ? monitor.Width : monitor.Height;
        return Math.Clamp(thickness, 1, Math.Max(1, extent));
    }

    /// <summary>The initial proposal: the full length of the monitor along the edge, <paramref name="thickness"/> deep.</summary>
    public static NativeRect Propose(NativeRect monitor, AppBarEdge edge, int thickness) => edge switch
    {
        AppBarEdge.Left => new NativeRect(monitor.Left, monitor.Top, monitor.Left + thickness, monitor.Bottom),
        AppBarEdge.Right => new NativeRect(monitor.Right - thickness, monitor.Top, monitor.Right, monitor.Bottom),
        AppBarEdge.Top => new NativeRect(monitor.Left, monitor.Top, monitor.Right, monitor.Top + thickness),
        AppBarEdge.Bottom => new NativeRect(monitor.Left, monitor.Bottom - thickness, monitor.Right, monitor.Bottom),
        _ => throw new ArgumentOutOfRangeException(nameof(edge)),
    };

    /// <summary>
    /// After ABM_QUERYPOS the shell may have moved the docked edge inwards (another appbar already owns the outer band). Keep that new edge
    /// position and restore the requested thickness measured from it - exactly the "adjust the rectangle" step of the documented sample.
    /// </summary>
    public static NativeRect AdjustToThickness(NativeRect queried, AppBarEdge edge, int thickness) => edge switch
    {
        AppBarEdge.Left => queried with { Right = queried.Left + thickness },
        AppBarEdge.Right => queried with { Left = queried.Right - thickness },
        AppBarEdge.Top => queried with { Bottom = queried.Top + thickness },
        AppBarEdge.Bottom => queried with { Top = queried.Bottom - thickness },
        _ => throw new ArgumentOutOfRangeException(nameof(edge)),
    };

    /// <summary>ABE_* value for an edge.</summary>
    public static uint ToAbe(AppBarEdge edge) => edge switch
    {
        AppBarEdge.Left => 0u,
        AppBarEdge.Top => 1u,
        AppBarEdge.Right => 2u,
        AppBarEdge.Bottom => 3u,
        _ => throw new ArgumentOutOfRangeException(nameof(edge)),
    };

    /// <summary>
    /// Maps an AppBar callback (uCallbackMessage, wParam = ABN_*, lParam) to a notification. Null for ABN_STATECHANGE and unknown codes
    /// (the message is still consumed). For ABN_FULLSCREENAPP lParam != 0 means a full-screen app opened, 0 that it closed.
    /// </summary>
    public static AppBarNotification? MapNotification(int abn, nint lParam) => abn switch
    {
        1 /* ABN_POSCHANGED */ => AppBarNotification.PosChanged,
        2 /* ABN_FULLSCREENAPP */ => lParam != 0 ? AppBarNotification.FullScreenAppEntered : AppBarNotification.FullScreenAppLeft,
        3 /* ABN_WINDOWARRANGE */ => AppBarNotification.WindowArrange,
        _ => null,
    };
}
