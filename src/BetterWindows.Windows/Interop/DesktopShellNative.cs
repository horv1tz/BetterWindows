using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace BetterWindows.Native.Interop;

/// <summary>
/// Finds Explorer's desktop windows: Progman / WorkerW (the wallpaper hosts), SHELLDLL_DefView (the icon view) and SysListView32 (the icon
/// list itself). Every lookup is repeated on each call (Explorer can restart at any time); nothing is cached. Calls to Explorer use
/// bounded <c>SendMessageTimeout</c>/<c>ShowWindowAsync</c> so a hung Explorer cannot hang BetterWindows.
/// </summary>
internal static unsafe class DesktopShellNative
{
    /// <summary>Program Manager class (the desktop host).</summary>
    public const string ProgmanClass = "Progman";

    /// <summary>Icon view class that hosts the desktop icon list.</summary>
    public const string DefViewClass = "SHELLDLL_DefView";

    /// <summary>The desktop icon list control.</summary>
    public const string ListViewClass = "SysListView32";

    /// <summary>
    /// Undocumented Progman message (0x052C) that makes it create a WorkerW behind the desktop icons. This is the widely used wallpaper
    /// technique for Windows 10 and 11; it is not in the SDK, so it is only used behind the fallback in the controller (returns 0 when the
    /// layout is unexpected and the Host then uses a bottom-most window instead).
    /// </summary>
    private const uint WmSpawnWorkerW = 0x052C;

    /// <summary>Program Manager, or 0 when Explorer's desktop is not running.</summary>
    public static nint FindProgman() => PInvoke.FindWindow(ProgmanClass, null).ToNint();

    /// <summary>
    /// The icon view (SHELLDLL_DefView) and its host (Progman, or a WorkerW on layouts that moved it). False when Explorer has no desktop view.
    /// </summary>
    public static bool TryFindDefView(out nint defView, out nint host)
    {
        defView = 0;
        host = 0;
        nint progman = FindProgman();
        if (progman != 0)
        {
            nint dv = PInvoke.FindWindowEx(progman.ToHwnd(), default, DefViewClass, null).ToNint();
            if (dv != 0)
            {
                defView = dv;
                host = progman;
                return true;
            }
        }

        foreach (nint w in WindowNative.FindAllByClass(DesktopLayerSelection.WorkerWClass))
        {
            nint dv = PInvoke.FindWindowEx(w.ToHwnd(), default, DefViewClass, null).ToNint();
            if (dv != 0)
            {
                defView = dv;
                host = w;
                return true;
            }
        }

        return false;
    }

    /// <summary>The desktop icon list (SysListView32 inside the icon view), or 0 when Explorer's desktop is not up.</summary>
    public static nint FindListView()
    {
        if (!TryFindDefView(out nint defView, out _))
        {
            return 0;
        }

        return PInvoke.FindWindowEx(defView.ToHwnd(), default, ListViewClass, null).ToNint();
    }

    /// <summary>
    /// Makes Progman create the wallpaper WorkerW (if it is not there yet) and returns the WorkerW the desktop layer should be parented to, or 0.
    /// The message is sent with <c>SMTO_ABORTIFHUNG</c> and a 1 s timeout; the second variant (wParam 0) is tried when the first produced nothing.
    /// </summary>
    public static nint SpawnAndFindWorkerW()
    {
        nint progman = FindProgman();
        if (progman == 0)
        {
            return 0;
        }

        nint w = FindLayerWorkerW();
        if (w != 0)
        {
            return w;
        }

        SendSpawn(progman.ToHwnd(), 0xD, 1);
        w = FindLayerWorkerW();
        if (w != 0)
        {
            return w;
        }

        SendSpawn(progman.ToHwnd(), 0xD, 0);
        return FindLayerWorkerW();
    }

    /// <summary>The wallpaper WorkerW chosen by <see cref="DesktopLayerSelection.ChooseWorkerW"/> from the current Z order; 0 when none.</summary>
    public static nint FindLayerWorkerW()
    {
        var candidates = new List<DesktopCandidate>(32);
        foreach (nint h in WindowNative.EnumerateTopLevel())
        {
            var hwnd = h.ToHwnd();
            string cls = WindowNative.GetClassName(hwnd);
            bool hosts = false;
            if (cls is DesktopLayerSelection.WorkerWClass or ProgmanClass)
            {
                hosts = PInvoke.FindWindowEx(hwnd, default, DefViewClass, null).ToNint() != 0;
            }

            candidates.Add(new DesktopCandidate(h, cls, hosts));
        }

        return DesktopLayerSelection.ChooseWorkerW(candidates);
    }

    private static void SendSpawn(HWND progman, nuint wParam, nint lParam)
    {
        nuint ignored;
        // The return value carries no information (the message has no defined result); a timeout is simply a failed attempt.
        PInvoke.SendMessageTimeout(progman, WmSpawnWorkerW, wParam, lParam, SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ABORTIFHUNG, 1000, &ignored);
    }
}
