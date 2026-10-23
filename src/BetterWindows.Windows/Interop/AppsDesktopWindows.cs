using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace BetterWindows.Native.Interop;

/// <summary>Where Explorer keeps the desktop, as found by <see cref="AppsDesktopWindows.TryFind"/>. Zero = not present.</summary>
internal readonly record struct DesktopLayout(nint Progman, nint DefView, nint DefViewParent, nint ListView, nint FollowingWorkerW)
{
    /// <summary>Windows 11 24H2 style: SHELLDLL_DefView is a direct child of Progman (no WorkerW hosts it).</summary>
    public bool DefViewUnderProgman => DefView != 0 && DefViewParent == Progman;
}

/// <summary>
/// The ONLY place that knows Explorer's desktop window structure (Progman / WorkerW / SHELLDLL_DefView / SysListView32). Two layouts exist and both are handled:
/// <list type="bullet">
/// <item>Classic (Windows 8 - 11 23H2, after Progman received message 0x052C): a top-level <c>WorkerW</c> hosts <c>SHELLDLL_DefView</c> (which hosts <c>SysListView32</c>); another
/// <c>WorkerW</c> follows it in z-order and draws the wallpaper.</item>
/// <item>Windows 11 24H2 (and classic before 0x052C): <c>SHELLDLL_DefView</c> is a direct child of <c>Progman</c>.</item>
/// </list>
/// The search is repeated on every call: handles change whenever Explorer restarts or recreates the desktop.
/// </summary>
internal static unsafe class AppsDesktopWindows
{
    private const int MaxWorkerWindows = 64; // loop guard: a normal system has a handful

    public static nint N(HWND h) => (nint)h.Value;
    public static HWND H(nint h) => new(h);

    public static bool TryFind(out DesktopLayout layout)
    {
        layout = default;
        HWND progman = PInvoke.FindWindow("Progman", null!);

        HWND defView = HWND.Null, parent = HWND.Null, following = HWND.Null;
        if (!progman.IsNull)
        {
            defView = PInvoke.FindWindowEx(progman, HWND.Null, "SHELLDLL_DefView", null!);
            if (!defView.IsNull) parent = progman;
        }
        if (defView.IsNull)
        {
            HWND worker = PInvoke.FindWindowEx(HWND.Null, HWND.Null, "WorkerW", null!);
            for (int i = 0; i < MaxWorkerWindows && !worker.IsNull; i++)
            {
                var child = PInvoke.FindWindowEx(worker, HWND.Null, "SHELLDLL_DefView", null!);
                if (!child.IsNull)
                {
                    defView = child;
                    parent = worker;
                    // The sibling WorkerW after the host in z-order is the wallpaper layer (what the classic WorkerW technique parents to).
                    following = PInvoke.FindWindowEx(HWND.Null, worker, "WorkerW", null!);
                    break;
                }
                worker = PInvoke.FindWindowEx(HWND.Null, worker, "WorkerW", null!);
            }
        }
        if (defView.IsNull) return false;

        // The icon list is titled "FolderView"; fall back to any SysListView32 in case a Windows build changes the title.
        HWND list = PInvoke.FindWindowEx(defView, HWND.Null, "SysListView32", "FolderView");
        if (list.IsNull) list = PInvoke.FindWindowEx(defView, HWND.Null, "SysListView32", null!);

        layout = new DesktopLayout(N(progman), N(defView), N(parent), N(list), N(following));
        return true;
    }

    /// <summary>WS_VISIBLE of the window itself (not of its ancestors): the right question for "did someone hide this exact window".</summary>
    public static bool HasVisibleStyle(nint hwnd)
        => (PInvoke.GetWindowLongPtr(H(hwnd), WINDOW_LONG_PTR_INDEX.GWL_STYLE) & (nint)(uint)WINDOW_STYLE.WS_VISIBLE) != 0;

    /// <summary>
    /// ShowWindow on a window owned by another process. ShowWindow sends messages to the owning thread and would block for as long as that thread is unresponsive, so a hung
    /// Explorer (IsHungAppWindow) is skipped; the caller retries later. Returns true if the call was made.
    /// </summary>
    public static bool TryShow(nint hwnd, bool show)
    {
        var h = H(hwnd);
        if (!PInvoke.IsWindow(h) || PInvoke.IsHungAppWindow(h)) return false;
        PInvoke.ShowWindow(h, show ? SHOW_WINDOW_CMD.SW_SHOW : SHOW_WINDOW_CMD.SW_HIDE);
        return true;
    }
}
