using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Native.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace BetterWindows.Native;

/// <summary>
/// Global hotkeys through RegisterHotKey (no keyboard hook). RegisterHotKey binds a key to a window of the CALLING thread and WM_HOTKEY is
/// posted to that thread's queue, so a dedicated thread owns a message-only window and a message loop; Register/Unregister from any
/// thread are marshalled onto it and wait (bounded) for the result. MOD_NOREPEAT suppresses auto-repeat while the key is held.
/// <see cref="Pressed"/> is raised on a thread-pool thread so a slow subscriber never blocks the loop. The thread starts with the first
/// registration. Thread-safe.
/// </summary>
public sealed class Win32HotkeyRegistrar : IHotkeyRegistrar
{
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(5);

    private readonly ILogger _log;
    private readonly object _gate = new();
    private readonly HotkeyTable _table = new(); // loop-thread only
    private MessageLoopThread? _loop;
    private bool _disposed;

    /// <summary>Creates the registrar; no thread runs until the first <see cref="TryRegister"/>.</summary>
    public Win32HotkeyRegistrar(ILogger<Win32HotkeyRegistrar>? logger = null)
    {
        _log = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <inheritdoc />
    public event Action<string>? Pressed;

    /// <inheritdoc />
    public bool TryRegister(string id, HotkeyGesture gesture)
    {
        if (!HotkeyTable.IsValid(id, gesture, out string reason))
        {
            _log.LogWarning("Hotkey registration '{Id}' rejected: {Reason}", id, reason);
            return false;
        }

        var loop = EnsureLoop();
        if (loop == null)
        {
            return false;
        }

        return loop.TryInvoke(() => RegisterOnLoop(id, gesture), CallTimeout, out bool ok) && ok;
    }

    /// <inheritdoc />
    public void Unregister(string id)
    {
        var loop = CurrentLoop();
        if (loop == null || string.IsNullOrEmpty(id))
        {
            return;
        }

        loop.TryInvoke(() => UnregisterOnLoop(id), CallTimeout, out _);
    }

    /// <inheritdoc />
    public void UnregisterAll()
    {
        var loop = CurrentLoop();
        loop?.TryInvoke(() =>
        {
            UnregisterAllOnLoop();
            return true;
        }, CallTimeout, out _);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        MessageLoopThread? loop;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            loop = _loop;
            _loop = null;
        }

        loop?.Dispose(); // onStop unregisters every hotkey on the loop thread, then the thread is joined
    }

    // ============================================================================================
    //  Loop-thread side
    // ============================================================================================

    private bool RegisterOnLoop(string id, HotkeyGesture gesture)
    {
        var hwnd = (_loop?.WindowHandle ?? 0).ToHwnd();

        // Re-registering an id replaces it: drop the old binding first, remembering it so a failure restores the previous state.
        bool hadOld = _table.TryGet(id, out int oldId, out var oldGesture);
        if (hadOld)
        {
            PInvoke.UnregisterHotKey(hwnd, oldId);
            _table.Release(id, out _);
        }

        if (!_table.TryReserve(id, gesture, out int numeric, out string? conflict))
        {
            _log.LogInformation("Hotkey '{Id}' not registered: gesture already used by '{Other}'", id, conflict);
            RestoreOld();
            return false;
        }

        var mods = (HOT_KEY_MODIFIERS)(gesture.Modifiers | Win32Consts.MOD_NOREPEAT);
        if (!PInvoke.RegisterHotKey(hwnd, numeric, mods, gesture.VirtualKey))
        {
            int error = System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
            _table.Release(id, out _);
            if (error == Win32Consts.ERROR_HOTKEY_ALREADY_REGISTERED)
            {
                _log.LogInformation("Hotkey '{Id}' ({Mods:x}+{Vk:x}) is already registered by another application", id, gesture.Modifiers, gesture.VirtualKey);
            }
            else
            {
                _log.LogWarning("RegisterHotKey for '{Id}' failed (error {Error})", id, error);
            }

            RestoreOld();
            return false;
        }

        return true;

        void RestoreOld()
        {
            if (hadOld && _table.TryReserve(id, oldGesture, out int restoredId, out _))
            {
                var m = (HOT_KEY_MODIFIERS)(oldGesture.Modifiers | Win32Consts.MOD_NOREPEAT);
                if (!PInvoke.RegisterHotKey(hwnd, restoredId, m, oldGesture.VirtualKey))
                {
                    _table.Release(id, out _);
                    _log.LogWarning("Could not restore the previous hotkey '{Id}'", id);
                }
            }
        }
    }

    private bool UnregisterOnLoop(string id)
    {
        if (!_table.Release(id, out int numeric))
        {
            return false;
        }

        var hwnd = (_loop?.WindowHandle ?? 0).ToHwnd();
        if (!PInvoke.UnregisterHotKey(hwnd, numeric))
        {
            _log.LogDebug("UnregisterHotKey({Id}) failed (error {Error})", id, System.Runtime.InteropServices.Marshal.GetLastPInvokeError());
        }

        return true;
    }

    private void UnregisterAllOnLoop()
    {
        var hwnd = (_loop?.WindowHandle ?? 0).ToHwnd();
        foreach (var (_, numeric, _) in _table.All())
        {
            PInvoke.UnregisterHotKey(hwnd, numeric);
        }

        _table.Clear();
    }

    private bool OnMessage(uint msg, nuint wParam, nint lParam, out nint result)
    {
        result = 0;
        if (msg != Win32Consts.WM_HOTKEY)
        {
            return false;
        }

        if (_table.TryGetName((int)wParam, out string name))
        {
            RaisePressed(name);
        }

        return true;
    }

    private void RaisePressed(string name)
    {
        var handlers = Pressed;
        if (handlers == null)
        {
            return;
        }

        // Thread pool, never the loop thread: a handler that takes long (or touches the UI) must not delay the next hotkey.
        ThreadPool.UnsafeQueueUserWorkItem(
            static state =>
            {
                var (self, h, n) = state;
                foreach (var d in h.GetInvocationList())
                {
                    try
                    {
                        ((Action<string>)d)(n);
                    }
                    catch (Exception ex)
                    {
                        self._log.LogWarning(ex, "Hotkey subscriber threw for '{Id}'", n);
                    }
                }
            },
            (this, handlers, name),
            preferLocal: false);
    }

    // ============================================================================================
    //  Thread management
    // ============================================================================================

    private MessageLoopThread? CurrentLoop()
    {
        lock (_gate)
        {
            return _loop;
        }
    }

    private MessageLoopThread? EnsureLoop()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return null;
            }

            if (_loop != null)
            {
                return _loop;
            }

            var loop = new MessageLoopThread(
                "BetterWindows.Hotkeys",
                _log,
                messageOnly: true, // WM_HOTKEY is posted to the window; no broadcasts needed
                onStop: _ => UnregisterAllOnLoop(),
                handler: OnMessage);
            if (!loop.Start(TimeSpan.FromSeconds(5)))
            {
                _log.LogError("Hotkey thread failed to start; global hotkeys are unavailable");
                loop.Dispose();
                return null;
            }

            _loop = loop;
            return loop;
        }
    }
}
