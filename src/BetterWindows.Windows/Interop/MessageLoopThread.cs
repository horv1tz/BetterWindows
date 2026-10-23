using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace BetterWindows.Native.Interop;

/// <summary>
/// A dedicated STA thread running a classic GetMessage/DispatchMessage loop around one <see cref="HiddenWindow"/>. It is the building block
/// for everything that must live on a message-pumping thread: SetWinEventHook (OUTOFCONTEXT callbacks are delivered through the
/// installing thread's message loop), RegisterHotKey, RegisterShellHookWindow, broadcast listeners.
/// Work from other threads is marshalled with <see cref="Post"/> / <see cref="TryInvoke{T}"/> (queue + a private WM_APP message),
/// which keeps every Win32 object on its owner thread without locks. Stop is cooperative (private quit message) and bounded.
/// </summary>
internal sealed unsafe class MessageLoopThread : IDisposable
{
    private const uint WmRun = Win32Consts.WM_APP + 1;
    private const uint WmStop = Win32Consts.WM_APP + 2;

    private readonly string _name;
    private readonly ILogger _log;
    private readonly bool _messageOnly;
    private readonly Func<MessageLoopThread, bool>? _onStart;
    private readonly Action<MessageLoopThread>? _onStop;
    private readonly WindowMessageHandler? _handler;
    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly ManualResetEventSlim _ready = new(false);
    private Thread? _thread;
    private HiddenWindow? _window;
    private volatile int _state; // 0 = new, 1 = starting/running, 2 = stopped
    private bool _startResult;
    private int _threadId;
    private bool _stopHandled;

    /// <param name="name">Thread name (diagnostics).</param>
    /// <param name="log">Logger for failures.</param>
    /// <param name="messageOnly">True for a message-only window, false for a top-level hidden window (needed for broadcasts / shell hook).</param>
    /// <param name="onStart">Runs on the new thread after the window exists and before messages are pumped; return false to abort the start.</param>
    /// <param name="onStop">Runs on the loop thread when stopping (before the window is destroyed): unhook, unregister.</param>
    /// <param name="handler">Window procedure hook for the hidden window; may be null.</param>
    public MessageLoopThread(string name, ILogger log, bool messageOnly, Func<MessageLoopThread, bool>? onStart = null,
        Action<MessageLoopThread>? onStop = null, WindowMessageHandler? handler = null)
    {
        _name = name;
        _log = log;
        _messageOnly = messageOnly;
        _onStart = onStart;
        _onStop = onStop;
        _handler = handler;
    }

    /// <summary>Handle of the hidden window (valid on/after start; 0 once stopped).</summary>
    public nint WindowHandle => _window?.Handle ?? 0;

    /// <summary>True while the loop is running.</summary>
    public bool IsRunning => _state == 1 && _startResult;

    /// <summary>True when the calling thread is the loop thread.</summary>
    public bool IsOnLoopThread => Environment.CurrentManagedThreadId == _threadId;

    /// <summary>Starts the thread and waits (bounded) until the window and the <c>onStart</c> hook are ready. False on failure.</summary>
    public bool Start(TimeSpan timeout)
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
        {
            return IsRunning;
        }

        var t = new Thread(Run) { IsBackground = true, Name = _name };
        t.SetApartmentState(ApartmentState.STA);
        _thread = t;
        t.Start();
        if (!_ready.Wait(timeout))
        {
            _log.LogWarning("{Thread} did not become ready within {Timeout} ms", _name, (int)timeout.TotalMilliseconds);
            return false;
        }

        return _startResult;
    }

    /// <summary>Queues <paramref name="action"/> for the loop thread. False if the loop is not running.</summary>
    public bool Post(Action action)
    {
        if (_state == 2)
        {
            return false;
        }

        _queue.Enqueue(action);
        nint hwnd = WindowHandle;
        if (hwnd != 0)
        {
            PInvoke.PostMessage(hwnd.ToHwnd(), WmRun, 0, 0);
        }

        return true;
    }

    /// <summary>
    /// Runs <paramref name="func"/> on the loop thread and waits for its result. Inline when already on the loop thread. False on timeout
    /// or when the loop is not running (the work may still execute later; its result is then discarded).
    /// </summary>
    public bool TryInvoke<T>(Func<T> func, TimeSpan timeout, out T? result)
    {
        result = default;
        if (IsOnLoopThread)
        {
            result = func();
            return true;
        }

        if (!IsRunning)
        {
            return false;
        }

        T? value = default;
        Exception? error = null;
        using var done = new ManualResetEventSlim(false);
        if (!Post(() =>
            {
                try
                {
                    value = func();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    try
                    {
                        done.Set();
                    }
                    catch (ObjectDisposedException)
                    {
                        // The caller timed out and left.
                    }
                }
            }))
        {
            return false;
        }

        if (!done.Wait(timeout))
        {
            return false;
        }

        if (error != null)
        {
            _log.LogWarning(error, "{Thread}: marshalled call failed", _name);
            return false;
        }

        result = value;
        return true;
    }

    private void Run()
    {
        _threadId = Environment.CurrentManagedThreadId;
        try
        {
            // PerMonitorV2 for the thread's lifetime so coordinates are physical even in a DPI-unaware host (tests).
            using var dpi = DpiAwarenessScope.PerMonitorV2();
            _window = HiddenWindow.TryCreate(Handle_, _messageOnly, out int err);
            if (_window == null)
            {
                _log.LogWarning("{Thread}: could not create the hidden window (error {Error})", _name, err);
                _startResult = false;
                _state = 2;
                _ready.Set();
                return;
            }

            bool ok;
            try
            {
                ok = _onStart?.Invoke(this) ?? true;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "{Thread}: start hook failed", _name);
                ok = false;
            }

            _startResult = ok;
            if (!ok)
            {
                _state = 2;
                RunStopHook();
                _window.Dispose();
                _window = null;
                _ready.Set();
                return;
            }

            _ready.Set();
            // Work queued before the window existed.
            DrainQueue();

            MSG msg;
            while (true)
            {
                int r = PInvoke.GetMessage(&msg, default, 0, 0).Value;
                if (r <= 0)
                {
                    break; // WM_QUIT (0) or error (-1)
                }

                PInvoke.TranslateMessage(&msg);
                PInvoke.DispatchMessage(&msg);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "{Thread}: message loop crashed", _name);
        }
        finally
        {
            _state = 2;
            try
            {
                RunStopHook();
            }
            finally
            {
                _window?.Dispose();
                _window = null;
                _ready.Set();
            }
        }
    }

    private bool Handle_(uint msg, nuint wParam, nint lParam, out nint result)
    {
        result = 0;
        try
        {
            switch (msg)
            {
                case WmRun:
                    DrainQueue();
                    return true;
                case WmStop:
                    RunStopHook();
                    // DestroyWindow -> WM_DESTROY -> PostQuitMessage ends the loop.
                    PInvoke.DestroyWindow((_window?.Handle ?? 0).ToHwnd());
                    return true;
                case Win32Consts.WM_DESTROY:
                    PInvoke.PostQuitMessage(0);
                    break;
            }

            return _handler != null && _handler(msg, wParam, lParam, out result);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "{Thread}: window message {Msg:x} handler failed", _name, msg);
            return false;
        }
    }

    private void DrainQueue()
    {
        while (_queue.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "{Thread}: queued action failed", _name);
            }
        }
    }

    private void RunStopHook()
    {
        if (_stopHandled)
        {
            return;
        }

        _stopHandled = true;
        try
        {
            _onStop?.Invoke(this);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "{Thread}: stop hook failed", _name);
        }
    }

    /// <summary>Asks the loop to end and waits (bounded) for the thread. Safe to call repeatedly and from the loop thread itself.</summary>
    public void Stop(TimeSpan? timeout = null)
    {
        var t = _thread;
        if (t == null || _state == 0)
        {
            _state = 2;
            return;
        }

        nint hwnd = WindowHandle;
        if (_state != 2 && hwnd != 0)
        {
            PInvoke.PostMessage(hwnd.ToHwnd(), WmStop, 0, 0);
        }

        if (!IsOnLoopThread && !t.Join(timeout ?? TimeSpan.FromSeconds(3)))
        {
            _log.LogWarning("{Thread} did not stop in time; abandoning it (background thread)", _name);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();
        _ready.Dispose();
    }
}
