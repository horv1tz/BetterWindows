using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BetterWindows.Core.Platform;
using BetterWindows.Native.Interop;
using Microsoft.Extensions.Logging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;

namespace BetterWindows.Native;

public sealed unsafe partial class Win32WindowSystem
{
    // ---- state owned by the hook thread (touched only there, except where noted) ----
    [ThreadStatic] private static Win32WindowSystem? t_current;

    private readonly List<nint> _hooks = new();
    private nint _locationHook;
    private HWND _desktop;
    private uint _shellHookMsg;
    private bool _shellHooked;
    private bool _fullscreenTimerArmed;
    private bool _throttleTimerArmed;
    private long _lastForegroundEmitted;
    private volatile bool _hasLoop;
    private MessageLoopThread? _hookLoop; // the loop as seen from its own thread (set before any hook can fire; _loop is only assigned after Start returns)

    // ---- FullscreenChanged subscription accounting (any thread) ----
    private readonly object _fullscreenGate = new();
    private Action? _fullscreenChanged;
    private int _fullscreenSubscribers;
    private long _foreground; // last known foreground handle (Interlocked)

    /// <inheritdoc />
    public event Action? FullscreenChanged
    {
        add
        {
            bool first;
            lock (_fullscreenGate)
            {
                _fullscreenChanged += value;
                first = ++_fullscreenSubscribers == 1;
            }

            // The location hook is high volume: install it only once somebody listens.
            if (first)
            {
                _loop?.Post(InstallLocationHook);
            }
        }

        remove
        {
            bool last = false;
            lock (_fullscreenGate)
            {
                var before = _fullscreenChanged;
                _fullscreenChanged -= value;
                if (!ReferenceEquals(before, _fullscreenChanged) && _fullscreenSubscribers > 0)
                {
                    last = --_fullscreenSubscribers == 0;
                }
            }

            if (last)
            {
                _loop?.Post(RemoveLocationHook);
            }
        }
    }

    // ============================================================================================
    //  Hook thread lifecycle
    // ============================================================================================

    private bool OnLoopStart(MessageLoopThread loop)
    {
        t_current = this;
        _hookLoop = loop;
        _hasLoop = true;
        _desktop = PInvoke.GetDesktopWindow();
        _foreground = PInvoke.GetForegroundWindow().ToLong();
        _lastForegroundEmitted = _foreground;

        // Shell hook: HSHELL_FLASH / activation come only through a registered shell-hook window. A top-level hidden window is required.
        _shellHookMsg = PInvoke.RegisterWindowMessage("SHELLHOOK");
        _shellHooked = _shellHookMsg != 0 && PInvoke.RegisterShellHookWindow(loop.WindowHandle.ToHwnd());
        if (!_shellHooked)
        {
            _log.LogWarning("RegisterShellHookWindow failed (error {Error}); taskbar flash notifications are unavailable", Marshal.GetLastSystemError());
        }

        foreach (var range in WindowEventMapper.HookRanges)
        {
            nint h = InstallHook(range);
            if (h != 0)
            {
                _hooks.Add(h);
            }
        }

        if (_hooks.Count == 0)
        {
            _log.LogError("No WinEvent hook could be installed; window change events are unavailable");
        }

        // Seed AFTER the hooks are live so a window created in between is not lost (it is simply seen twice).
        foreach (nint h in WindowNative.EnumerateTopLevel())
        {
            _topLevel[h] = 0;
        }

        bool needLocation;
        lock (_fullscreenGate)
        {
            needLocation = _fullscreenSubscribers > 0;
        }

        if (needLocation)
        {
            InstallLocationHook();
        }

        return true;
    }

    private void OnLoopStop(MessageLoopThread loop)
    {
        RemoveLocationHook();
        foreach (nint h in _hooks)
        {
            PInvoke.UnhookWinEvent(new HWINEVENTHOOK((IntPtr)h));
        }

        _hooks.Clear();
        if (_shellHooked)
        {
            PInvoke.DeregisterShellHookWindow(loop.WindowHandle.ToHwnd());
            _shellHooked = false;
        }

        nint hwnd = loop.WindowHandle;
        if (hwnd != 0)
        {
            PInvoke.KillTimer(hwnd.ToHwnd(), TimerThrottleFlush);
            PInvoke.KillTimer(hwnd.ToHwnd(), TimerFullscreen);
        }

        _throttleTimerArmed = false;
        _fullscreenTimerArmed = false;
        _hasLoop = false;
        _hookLoop = null;
        t_current = null;
    }

    /// <summary>SetWinEventHook for one narrow range, out-of-context (callbacks come through this thread's message loop), skipping our own process.</summary>
    private nint InstallHook(WinEventRange range)
    {
        var hook = PInvoke.SetWinEventHook(
            range.Min,
            range.Max,
            HMODULE.Null, // out-of-context hook: no module
            &WinEventProc,
            0,
            0,
            Win32Consts.WINEVENT_OUTOFCONTEXT | Win32Consts.WINEVENT_SKIPOWNPROCESS);
        if (hook.IsNull)
        {
            _log.LogWarning("SetWinEventHook({Name}) failed (error {Error})", range.Name, Marshal.GetLastSystemError());
            return 0;
        }

        return (nint)hook.Value;
    }

    private void InstallLocationHook()
    {
        if (_locationHook != 0 || !_hasLoop)
        {
            return;
        }

        _locationHook = InstallHook(WindowEventMapper.LocationRange);
    }

    private void RemoveLocationHook()
    {
        if (_locationHook == 0)
        {
            return;
        }

        PInvoke.UnhookWinEvent(new HWINEVENTHOOK((IntPtr)_locationHook));
        _locationHook = 0;
    }

    // ============================================================================================
    //  Callbacks (hook thread). Nothing here may throw or block.
    // ============================================================================================

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })]
    private static void WinEventProc(HWINEVENTHOOK hook, uint eventType, HWND hwnd, int idObject, int idChild, uint idEventThread, uint eventTime)
    {
        try
        {
            t_current?.OnWinEvent(eventType, hwnd.ToLong(), idObject, idChild);
        }
        catch
        {
            // Never let an exception cross the native boundary.
        }
    }

    private void OnWinEvent(uint winEvent, long handle, int idObject, int idChild)
    {
        // Only the window object itself: skip carets, cursors, menus, accessible children.
        if (idObject != Win32Consts.OBJID_WINDOW || idChild != Win32Consts.CHILDID_SELF)
        {
            return;
        }

        if (!WindowEventMapper.TryMap(winEvent, out var kind))
        {
            return;
        }

        if (kind == WindowEventKind.Foreground)
        {
            OnForeground(handle);
            return;
        }

        if (handle == 0)
        {
            return;
        }

        if (kind == WindowEventKind.Destroyed)
        {
            // The window is gone, so top-level-ness cannot be asked any more: we know the top-level windows we have seen.
            if (_topLevel.TryRemove(handle, out _))
            {
                _flashing.TryRemove(handle, out _);
                _titles.TryRemove(handle, out _);
                _windowAumids.TryRemove(handle, out _);
                _throttle.Remove((WindowEventKind.TitleChanged, handle));
                _throttle.Remove((WindowEventKind.Moved, handle));
                _throttle.Remove((WindowEventKind.Redraw, handle));
                Emit(WindowEventKind.Destroyed, handle);
                if (Interlocked.Read(ref _foreground) == handle)
                {
                    ScheduleFullscreenCheck();
                }
            }

            return;
        }

        // Children, menus and message-only windows are not interesting.
        if (!WindowNative.IsTopLevel(handle.ToHwnd(), _desktop))
        {
            return;
        }

        _topLevel[handle] = 0;
        if (WindowEventMapper.IsThrottled(kind))
        {
            EmitThrottled(kind, handle);
        }
        else
        {
            Emit(kind, handle);
        }

        if (WindowEventMapper.AffectsFullscreen(kind) && Interlocked.Read(ref _foreground) == handle)
        {
            ScheduleFullscreenCheck();
        }
    }

    private void OnForeground(long handle)
    {
        Interlocked.Exchange(ref _foreground, handle);
        _flashing.TryRemove(handle, out _); // activation ends a flash
        if (handle != _lastForegroundEmitted)
        {
            _lastForegroundEmitted = handle;
            Emit(WindowEventKind.Foreground, handle);
        }

        ScheduleFullscreenCheck();
    }

    private bool OnLoopMessage(uint msg, nuint wParam, nint lParam, out nint result)
    {
        result = 0;
        if (_shellHookMsg != 0 && msg == _shellHookMsg)
        {
            HandleShellHook((int)wParam, lParam);
            return true;
        }

        if (msg == Win32Consts.WM_TIMER)
        {
            switch ((int)wParam)
            {
                case TimerThrottleFlush:
                    _throttleTimerArmed = false;
                    FlushThrottle();
                    return true;
                case TimerFullscreen:
                    _fullscreenTimerArmed = false;
                    var loop = _hookLoop;
                    if (loop != null)
                    {
                        PInvoke.KillTimer(loop.WindowHandle.ToHwnd(), TimerFullscreen);
                    }

                    RaiseFullscreenChanged();
                    return true;
            }
        }

        return false;
    }

    private void HandleShellHook(int code, nint lParam)
    {
        long handle = lParam;
        switch (WindowEventMapper.ClassifyShellHook(code))
        {
            case ShellHookAction.Flash:
                if (handle != 0 && !IsOwnWindow(handle))
                {
                    _flashing[handle] = 0;
                    Emit(WindowEventKind.Flashed, handle);
                }

                break;
            case ShellHookAction.Activated:
                if (handle != 0 && !IsOwnWindow(handle))
                {
                    // Same semantics as EVENT_SYSTEM_FOREGROUND; the dedupe in OnForeground drops the duplicate of whichever arrives second.
                    OnForeground(handle);
                }

                break;
            case ShellHookAction.Redraw:
                if (handle != 0 && !IsOwnWindow(handle) && WindowNative.IsTopLevel(handle.ToHwnd(), _desktop))
                {
                    EmitThrottled(WindowEventKind.Redraw, handle);
                }

                break;
        }
    }

    private static bool IsOwnWindow(long handle) => WindowNative.GetProcessId(handle.ToHwnd(), out _) == Environment.ProcessId;

    // ============================================================================================
    //  Timers (hook thread)
    // ============================================================================================

    private void ArmTimer(int id, uint ms)
    {
        var loop = _hookLoop;
        if (loop == null || !loop.IsOnLoopThread)
        {
            return;
        }

        if (id == TimerThrottleFlush)
        {
            if (_throttleTimerArmed)
            {
                return;
            }

            _throttleTimerArmed = true;
        }

        PInvoke.SetTimer(loop.WindowHandle.ToHwnd(), (nuint)id, ms, null);
    }

    private void FlushThrottle()
    {
        var loop = _hookLoop;
        if (loop != null)
        {
            PInvoke.KillTimer(loop.WindowHandle.ToHwnd(), TimerThrottleFlush);
        }

        _throttle.Flush(key => Emit(key.Kind, key.Handle));
        if (_throttle.HasPending)
        {
            ArmTimer(TimerThrottleFlush, 50);
        }
    }

    /// <summary>Coalesces every trigger inside a 50 ms window into ONE FullscreenChanged (first trigger arms the timer; later ones ride along).</summary>
    private void ScheduleFullscreenCheck()
    {
        bool anyone;
        lock (_fullscreenGate)
        {
            anyone = _fullscreenSubscribers > 0;
        }

        var loop = _hookLoop;
        if (!anyone || _fullscreenTimerArmed || loop == null || !loop.IsOnLoopThread)
        {
            return;
        }

        _fullscreenTimerArmed = true;
        PInvoke.SetTimer(loop.WindowHandle.ToHwnd(), TimerFullscreen, 50, null);
    }

    private void RaiseFullscreenChanged()
    {
        Action? handlers;
        lock (_fullscreenGate)
        {
            handlers = _fullscreenChanged;
        }

        if (handlers == null)
        {
            return;
        }

        // Never run subscriber code on the hook thread.
        ThreadPool.UnsafeQueueUserWorkItem(
            static state =>
            {
                var (self, h) = state;
                foreach (var d in h.GetInvocationList())
                {
                    try
                    {
                        ((Action)d)();
                    }
                    catch (Exception ex)
                    {
                        self._log.LogWarning(ex, "FullscreenChanged subscriber threw");
                    }
                }
            },
            (this, handlers),
            preferLocal: false);
    }
}
