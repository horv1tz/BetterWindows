using System.Runtime.InteropServices;
using BetterWindows.Core.Platform;
using BetterWindows.Native.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace BetterWindows.Native;

/// <summary>
/// Hides Explorer's desktop icon list (only the SysListView32 window, never the persisted HideIcons preference) and parents the desktop layer
/// window into the wallpaper WorkerW. Hiding is recorded in a marker file BEFORE it happens (<see cref="DesktopIconsRestorer"/> undoes it from
/// a fresh process). A top-level hidden watcher thread listens for "TaskbarCreated" (Explorer restarted): the icons are hidden again and
/// <see cref="ShellRestarted"/> is raised so the Host re-attaches its layer. Thread-safe; Win32 calls are bounded (no hung-Explorer hangs).
/// </summary>
public sealed unsafe class Win32DesktopIconsController : IDesktopIconsController
{
    private const uint TimerRetryHide = 1;
    private const uint TimerShellRestart = 2;
    private const uint RetryIntervalMs = 1000;
    private const uint ShellRestartSettleMs = 800;
    private const int MaxHideRetries = 30;

    /// <summary>WS_CHILD (winuser.h); not in <see cref="Win32Consts"/>.</summary>
    private const uint WS_CHILD = 0x40000000;

    private readonly ILogger _log;
    private readonly object _gate = DesktopIconsRestorer.Gate; // shared with the crash-path restorer
    private MessageLoopThread? _watcher;
    private uint _taskbarCreated;
    private bool _desiredHidden;
    private int _retries;
    private nint _attached;      // layer window currently parented into the WorkerW (0 = none)
    private uint _attachedStyle; // its style before we changed it to WS_CHILD
    private Action? _shellRestarted;
    private bool _disposed;

    /// <summary>Creates the controller; nothing is touched or started until the first call.</summary>
    public Win32DesktopIconsController(ILogger<Win32DesktopIconsController>? logger = null)
    {
        _log = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <inheritdoc />
    public event Action? ShellRestarted
    {
        add
        {
            lock (_gate)
            {
                _shellRestarted += value;
            }
        }
        remove
        {
            lock (_gate)
            {
                _shellRestarted -= value;
            }
        }
    }

    /// <inheritdoc />
    public void SetNativeIconsVisible(bool visible)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _desiredHidden = !visible;
            _retries = 0;
            if (visible)
            {
                ShowNativeIconsUnderGate();
                return;
            }

            EnsureWatcher(); // so that a shell restart re-hides the icons
            HideUnderGate();
        }
    }

    /// <inheritdoc />
    public bool TryAttachDesktopLayer(nint hwnd, NativeRect boundsPx)
    {
        if (hwnd == 0 || boundsPx.Width <= 0 || boundsPx.Height <= 0)
        {
            return false;
        }

        lock (_gate)
        {
            if (_disposed || !PInvoke.IsWindow(hwnd.ToHwnd()))
            {
                return false;
            }

            nint workerW = DesktopShellNative.SpawnAndFindWorkerW();
            if (workerW == 0)
            {
                _log.LogWarning("Desktop layer: no wallpaper WorkerW found (unrecognised shell layout); the Host will use its fallback");
                return false;
            }

            if (_attached != 0 && _attached != hwnd)
            {
                DetachUnderGate(); // one desktop layer at a time: the previous one goes back to being a top-level window
            }

            var h = hwnd.ToHwnd();
            uint originalStyle = WindowNative.GetStyle(h);

            // SetParent (documented) moves the window under WorkerW; it returns the previous parent, or NULL on failure.
            HWND previous = PInvoke.SetParent(h, workerW.ToHwnd());
            if (previous.IsNull)
            {
                _log.LogWarning("SetParent into the WorkerW failed (error {Error})", Marshal.GetLastPInvokeError());
                return false;
            }

            // A child window must not be WS_POPUP; WS_CHILD makes it clip to the WorkerW like any child.
            nint childStyle = unchecked((nint)(int)((originalStyle & ~Win32Consts.WS_POPUP) | WS_CHILD));
            PInvoke.SetWindowLongPtr(h, WINDOW_LONG_PTR_INDEX.GWL_STYLE, childStyle);

            // Our bounds are screen pixels (PerMonitorV2, physical); a child is positioned in its parent's client coordinates.
            System.Drawing.Point origin = new(boundsPx.Left, boundsPx.Top);
            PInvoke.ScreenToClient(workerW.ToHwnd(), ref origin);
            // HWND_TOP among the siblings: above the (hidden) icon view so that the layer receives input; no activation.
            bool placed = PInvoke.SetWindowPos(
                h,
                default,
                origin.X,
                origin.Y,
                boundsPx.Width,
                boundsPx.Height,
                SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW | SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED);
            if (!placed)
            {
                _log.LogWarning("SetWindowPos of the desktop layer failed (error {Error}); detaching again", Marshal.GetLastPInvokeError());
                RestoreTopLevel(h, originalStyle);
                return false;
            }

            _attached = hwnd;
            _attachedStyle = originalStyle;
            EnsureWatcher();
            return true;
        }
    }

    /// <inheritdoc />
    public void DetachDesktopLayer(nint hwnd)
    {
        lock (_gate)
        {
            if (_attached == 0 || _attached != hwnd)
            {
                return;
            }

            DetachUnderGate();
        }
    }

    /// <inheritdoc />
    public void Restore()
    {
        // Crash paths call this too: do not hang behind a stuck controller; the crash-path restorer is idempotent.
        if (!Monitor.TryEnter(_gate, TimeSpan.FromSeconds(2)))
        {
            DesktopIconsRestorer.RestoreNow();
            return;
        }

        try
        {
            _desiredHidden = false;
            _retries = 0;
            ShowNativeIconsUnderGate();
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        MessageLoopThread? watcher;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _desiredHidden = false;
            DetachUnderGate();
            ShowNativeIconsUnderGate();
            watcher = _watcher;
            _watcher = null;
        }

        // Outside the gate: the watcher thread may be waiting for it, and Stop() joins that thread.
        watcher?.Dispose();
    }

    // ============================================================================================
    //  Under the gate
    // ============================================================================================

    /// <summary>Hides the icon view if Explorer has it; records the hide first. Schedules a bounded retry when Explorer is not up yet.</summary>
    private bool HideUnderGate()
    {
        nint listView = DesktopShellNative.FindListView();
        if (listView == 0)
        {
            ScheduleRetryUnderGate();
            return false;
        }

        // Persist BEFORE the change: a crash between the two leaves a marker for a list that is still visible (harmless).
        if (!ShellStateStore.TryWriteText(DesktopIconsMarker.FileName, new DesktopIconsMarker(Environment.ProcessId, DateTime.UtcNow).ToJson()))
        {
            _log.LogError("Could not record the desktop icon state; leaving the desktop icons visible");
            return false;
        }

        // ShowWindowAsync posts the request to Explorer's thread: a hung Explorer cannot block us.
        PInvoke.ShowWindowAsync(listView.ToHwnd(), SHOW_WINDOW_CMD.SW_HIDE);
        _retries = 0;
        return true;
    }

    private void ShowNativeIconsUnderGate()
    {
        nint listView = DesktopShellNative.FindListView();
        if (listView != 0)
        {
            // SW_SHOWNA: show without activating the desktop.
            PInvoke.ShowWindowAsync(listView.ToHwnd(), SHOW_WINDOW_CMD.SW_SHOWNA);
        }

        // If Explorer is not running there is nothing to show; a new Explorer shows its icons by default.
        ShellStateStore.Delete(DesktopIconsMarker.FileName);
    }

    private void DetachUnderGate()
    {
        nint layer = _attached;
        _attached = 0;
        if (layer == 0)
        {
            return;
        }

        var h = layer.ToHwnd();
        if (PInvoke.IsWindow(h))
        {
            RestoreTopLevel(h, _attachedStyle);
        }
    }

    /// <summary>Makes the window a top-level window again: parent = desktop, original style back.</summary>
    private static void RestoreTopLevel(HWND h, uint originalStyle)
    {
        PInvoke.SetParent(h, PInvoke.GetDesktopWindow());
        PInvoke.SetWindowLongPtr(h, WINDOW_LONG_PTR_INDEX.GWL_STYLE, unchecked((nint)(int)originalStyle));
        PInvoke.SetWindowPos(
            h,
            default,
            0,
            0,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED);
    }

    private void ScheduleRetryUnderGate()
    {
        if (!_desiredHidden || _retries >= MaxHideRetries || _watcher is not { IsRunning: true })
        {
            return;
        }

        _retries++;
        PostTimer(TimerRetryHide, RetryIntervalMs);
    }

    // ============================================================================================
    //  Watcher (top-level hidden window: receives the "TaskbarCreated" broadcast)
    // ============================================================================================

    private void EnsureWatcher()
    {
        if (_watcher != null || _disposed)
        {
            return;
        }

        var loop = new MessageLoopThread(
            "BetterWindows.DesktopIcons",
            _log,
            messageOnly: false, // broadcasts need a top-level window
            onStart: OnWatcherStart,
            onStop: OnWatcherStop,
            handler: OnWatcherMessage);
        if (!loop.Start(TimeSpan.FromSeconds(5)))
        {
            _log.LogWarning("Desktop icon watcher could not start; the icons will not be re-hidden after an Explorer restart");
            loop.Dispose();
            return;
        }

        _watcher = loop;
    }

    private bool OnWatcherStart(MessageLoopThread loop)
    {
        _taskbarCreated = PInvoke.RegisterWindowMessage("TaskbarCreated");
        return true;
    }

    private void OnWatcherStop(MessageLoopThread loop)
    {
        nint hwnd = loop.WindowHandle;
        if (hwnd != 0)
        {
            PInvoke.KillTimer(hwnd.ToHwnd(), (nuint)TimerRetryHide);
            PInvoke.KillTimer(hwnd.ToHwnd(), (nuint)TimerShellRestart);
        }
    }

    private bool OnWatcherMessage(uint msg, nuint wParam, nint lParam, out nint result)
    {
        result = 0;
        uint created = _taskbarCreated;
        if (created != 0 && msg == created)
        {
            // Explorer (re)started. Its desktop is rebuilt a little later: wait for it to settle, then re-hide and tell the Host.
            PostTimer(TimerShellRestart, ShellRestartSettleMs);
            return true;
        }

        if (msg == Win32Consts.WM_TIMER)
        {
            uint id = unchecked((uint)wParam);
            if (id is TimerRetryHide or TimerShellRestart)
            {
                var w = _watcher;
                if (w is { WindowHandle: not 0 })
                {
                    PInvoke.KillTimer(w.WindowHandle.ToHwnd(), (nuint)id);
                }

                if (id == TimerRetryHide)
                {
                    ReapplyHide();
                }
                else
                {
                    OnShellRestarted();
                }

                return true;
            }
        }

        return false;
    }

    /// <summary>Queues a timer on the watcher's own thread (SetTimer must run there; Post is non-blocking, so this is safe under the gate).</summary>
    private void PostTimer(uint id, uint delayMs)
    {
        var w = _watcher;
        w?.Post(() =>
        {
            nint hwnd = w.WindowHandle;
            if (hwnd != 0)
            {
                PInvoke.SetTimer(hwnd.ToHwnd(), (nuint)id, delayMs, null);
            }
        });
    }

    private void ReapplyHide()
    {
        lock (_gate)
        {
            if (_disposed || !_desiredHidden)
            {
                return;
            }

            HideUnderGate();
        }
    }

    private void OnShellRestarted()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_attached != 0 && !PInvoke.IsWindow(_attached.ToHwnd()))
            {
                _attached = 0; // destroyed together with the previous WorkerW
            }

            if (_desiredHidden)
            {
                _retries = 0;
                HideUnderGate();
            }
        }

        RaiseShellRestarted();
    }

    private void RaiseShellRestarted()
    {
        Action? handlers;
        lock (_gate)
        {
            handlers = _shellRestarted;
        }

        if (handlers == null)
        {
            return;
        }

        // One subscriber's failure must not stop the others, and must never escape into the window procedure.
        foreach (Delegate d in handlers.GetInvocationList())
        {
            try
            {
                ((Action)d)();
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "A ShellRestarted subscriber failed");
            }
        }
    }
}
