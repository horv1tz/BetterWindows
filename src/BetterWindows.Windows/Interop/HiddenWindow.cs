using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace BetterWindows.Native.Interop;

/// <summary>
/// Handles one window message. Return true when handled (then <paramref name="result"/> is the LRESULT); false falls through to
/// DefWindowProc. Must not throw (exceptions are caught and swallowed by the window procedure thunk) and must be fast.
/// </summary>
internal delegate bool WindowMessageHandler(uint msg, nuint wParam, nint lParam, out nint result);

/// <summary>
/// An invisible window owned by the thread that created it: either a TOP-LEVEL hidden window (receives HWND_BROADCAST messages such as
/// WM_DISPLAYCHANGE, WM_SETTINGCHANGE and the registered "TaskbarCreated"; can be a shell-hook window) or a MESSAGE-ONLY window
/// (HWND_MESSAGE parent; does NOT receive broadcasts, enough for hotkeys and posted work).
/// All instances share ONE window class whose procedure is a static <c>[UnmanagedCallersOnly]</c> thunk (no delegate lifetime to manage);
/// the managed instance is found through a GCHandle stored in GWLP_USERDATA during WM_NCCREATE (passed through lpCreateParams).
/// Thread-affine: create, pump messages and <see cref="Dispose"/> on the same thread (DestroyWindow fails across threads).
/// </summary>
internal sealed unsafe class HiddenWindow : IDisposable
{
    private const string ClassName = "BetterWindows.HiddenWindow";
    private static readonly object s_classLock = new();
    private static bool s_classRegistered;

    private readonly WindowMessageHandler _handler;
    private GCHandle _self;
    private HWND _hwnd;
    private bool _disposed;

    private HiddenWindow(WindowMessageHandler handler)
    {
        _handler = handler;
    }

    /// <summary>The native window handle (0 after disposal).</summary>
    public nint Handle => _hwnd.ToNint();

    /// <summary>Id of the thread that created (and must destroy) the window.</summary>
    public uint ThreadId { get; private set; }

    /// <summary>
    /// Creates the window on the calling thread. Returns null (and the Win32 error) on failure.
    /// </summary>
    public static HiddenWindow? TryCreate(WindowMessageHandler handler, bool messageOnly, out int error)
    {
        error = 0;
        if (!EnsureClass(out error))
        {
            return null;
        }

        var w = new HiddenWindow(handler);
        w._self = GCHandle.Alloc(w, GCHandleType.Normal);
        // Top-level: WS_POPUP, never shown; TOOLWINDOW keeps it off Alt-Tab/taskbar, NOACTIVATE keeps it from ever taking focus.
        // Message-only: parent HWND_MESSAGE (-3).
        var exStyle = messageOnly ? default : WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE;
        var parent = messageOnly ? new HWND((IntPtr)(-3)) : default;
        HWND hwnd;
        fixed (char* cls = ClassName)
        {
            var module = PInvoke.GetModuleHandle((PCWSTR)null);
            hwnd = PInvoke.CreateWindowEx(
                exStyle,
                cls,
                cls,
                messageOnly ? default : WINDOW_STYLE.WS_POPUP,
                0, 0, 0, 0,
                parent,
                default,
                module,
                (void*)GCHandle.ToIntPtr(w._self));
        }

        if (hwnd.IsNull)
        {
            error = Marshal.GetLastPInvokeError();
            w._self.Free();
            return null;
        }

        w._hwnd = hwnd;
        w.ThreadId = PInvoke.GetCurrentThreadId();
        return w;
    }

    private static bool EnsureClass(out int error)
    {
        error = 0;
        lock (s_classLock)
        {
            if (s_classRegistered)
            {
                return true;
            }

            fixed (char* cls = ClassName)
            {
                var module = PInvoke.GetModuleHandle((PCWSTR)null);
                var wc = new WNDCLASSEXW
                {
                    cbSize = (uint)sizeof(WNDCLASSEXW),
                    lpfnWndProc = &WndProc,
                    hInstance = module,
                    lpszClassName = cls,
                };
                ushort atom = PInvoke.RegisterClassEx(&wc);
                if (atom == 0)
                {
                    int err = Marshal.GetLastPInvokeError();
                    // Already registered (e.g. by another copy of this assembly in the process): that is fine, the class is usable.
                    if (err != Win32Consts.ERROR_CLASS_ALREADY_EXISTS)
                    {
                        error = err;
                        return false;
                    }
                }
            }

            s_classRegistered = true;
            return true;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static LRESULT WndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        // Contract: nothing may escape an UnmanagedCallersOnly method, so everything is guarded.
        try
        {
            if (msg == Win32Consts.WM_NCCREATE)
            {
                // lParam -> CREATESTRUCTW; its first field is lpCreateParams (the GCHandle we passed to CreateWindowEx).
                nint createParams = *(nint*)lParam.Value;
                PInvoke.SetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWLP_USERDATA, createParams);
            }

            nint ptr = PInvoke.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWLP_USERDATA);
            if (ptr != 0)
            {
                if (GCHandle.FromIntPtr(ptr).Target is HiddenWindow self)
                {
                    if (self._handler(msg, wParam.Value, lParam.Value, out nint result))
                    {
                        return new LRESULT(result);
                    }
                }

                if (msg == Win32Consts.WM_NCDESTROY)
                {
                    // Last message of the window: detach so late messages cannot reach a freed handle.
                    PInvoke.SetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWLP_USERDATA, 0);
                }
            }
        }
        catch
        {
            // Swallowed on purpose: see above.
        }

        return PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    /// <summary>Destroys the window (on its own thread) and releases the instance handle.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var hwnd = _hwnd;
        _hwnd = default;
        if (!hwnd.IsNull)
        {
            // Fails harmlessly with ERROR_ACCESS_DENIED when called from the wrong thread; the window is then destroyed with its thread.
            PInvoke.DestroyWindow(hwnd);
        }

        if (_self.IsAllocated)
        {
            _self.Free();
        }
    }
}
