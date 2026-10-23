using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Native.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;

namespace BetterWindows.Native;

/// <summary>
/// Hides / auto-hides / restores the native Windows taskbar (see <see cref="ITaskbarController"/> for the contract).
/// The state machine lives in <see cref="TaskbarEngine"/> (snapshot persisted before the first mutation, exact restore). This class adds the
/// WATCHER that keeps Hidden hidden: one dedicated message-loop thread with (a) a targeted EVENT_OBJECT_SHOW WinEvent hook on Explorer's
/// process that schedules a re-hide 50 ms after a taskbar window is shown again (Win key, Start, display change), (b) a hidden top-level
/// window receiving the "TaskbarCreated" broadcast to re-apply after an Explorer restart, and WM_DISPLAYCHANGE to catch secondary taskbars
/// appearing. The watcher runs only while the mode is Hidden; Visible/AutoHide need no watching. Thread-safe.
/// </summary>
public sealed unsafe class Win32TaskbarController : ITaskbarController
{
    private const int TimerRehide = 1;
    private const int TimerReapplyEarly = 2;
    private const int TimerReapplyLate = 3;

    [ThreadStatic] private static Win32TaskbarController? t_current;

    private readonly ILogger _log;
    private readonly ITaskbarSurface _surface;
    private readonly ITaskbarSnapshotStore _store;
    private readonly TaskbarEngine _engine;
    private readonly object _apiGate = new();   // serializes Apply/Restore/Dispose and watcher start/stop (never taken by the loop thread)
    private MessageLoopThread? _watcher;
    private volatile TaskbarMode _current = TaskbarMode.Visible;
    private volatile TaskbarMode _desired = TaskbarMode.Visible;
    private long _stamp = TaskbarRestorer.Stamp; // TaskbarRestorer.Stamp at our last Apply/Restore (see there)
    private bool _disposed;

    // Watcher-thread state.
    private uint _taskbarCreatedMessage;
    private nint _showHook;
    private readonly Queue<long> _recentRehides = new();

    /// <summary>Creates the controller; nothing is touched until <see cref="Apply"/>.</summary>
    public Win32TaskbarController(ILogger<Win32TaskbarController>? logger = null)
        : this((ILogger?)logger ?? NullLogger.Instance, new Win32TaskbarSurface(), FileTaskbarSnapshotStore.Default)
    {
    }

    internal Win32TaskbarController(ILogger log, ITaskbarSurface surface, ITaskbarSnapshotStore store)
    {
        _log = log;
        _surface = surface;
        _store = store;
        _engine = new TaskbarEngine(surface, store, log);
    }

    /// <inheritdoc />
    public TaskbarMode Current => _current;

    /// <inheritdoc />
    public void Apply(TaskbarMode mode)
    {
        lock (_apiGate)
        {
            if (_disposed)
            {
                return;
            }

            _desired = mode;
            bool applied;
            lock (TaskbarRestorer.Gate)
            {
                SyncWithExternalRestore();
                applied = _engine.Apply(mode);
            }

            _current = mode;
            if (mode == TaskbarMode.Hidden)
            {
                // Even when Explorer is not up yet (applied == false) the watcher is started: TaskbarCreated will complete the job.
                StartWatcher();
            }
            else
            {
                StopWatcher();
            }

            if (!applied && mode != TaskbarMode.Visible)
            {
                _log.LogInformation("Taskbar mode {Mode} requested but the taskbar is not available yet; will apply when Explorer provides it", mode);
            }
        }
    }

    /// <inheritdoc />
    public void Restore()
    {
        // Crash paths call this too: if another thread is stuck inside the controller (hung Explorer), do not hang behind it.
        if (!Monitor.TryEnter(_apiGate, TimeSpan.FromSeconds(2)))
        {
            TaskbarRestorer.RestoreNow(_surface, _store, _log);
            return;
        }

        try
        {
            _desired = TaskbarMode.Visible;
            RestoreUnderGate();
            _current = TaskbarMode.Visible;
            StopWatcher();
        }
        finally
        {
            Monitor.Exit(_apiGate);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_apiGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _desired = TaskbarMode.Visible;
            RestoreUnderGate();
            _current = TaskbarMode.Visible;
            StopWatcher();
        }
    }

    /// <summary>Restores through the engine; the shared gate is awaited for a bounded time only (a hung Explorer must not hang a crash path).</summary>
    private void RestoreUnderGate()
    {
        bool locked = Monitor.TryEnter(TaskbarRestorer.Gate, TimeSpan.FromSeconds(3));
        try
        {
            SyncWithExternalRestore();
            _engine.Apply(TaskbarMode.Visible);
        }
        finally
        {
            if (locked)
            {
                Monitor.Exit(TaskbarRestorer.Gate);
            }
        }
    }

    /// <summary>
    /// If <see cref="TaskbarRestorer.RestoreNow()"/> ran since our last operation, the marker is gone and the taskbar was put back: drop the stale
    /// in-memory snapshot so the next mutation takes a fresh one. Call under <see cref="TaskbarRestorer.Gate"/>.
    /// </summary>
    private void SyncWithExternalRestore()
    {
        long stamp = TaskbarRestorer.Stamp;
        if (stamp != _stamp)
        {
            _stamp = stamp;
            _engine.ForgetSnapshot();
        }
    }

    // ============================================================================================
    //  Watcher
    // ============================================================================================

    private void StartWatcher()
    {
        if (_watcher != null)
        {
            return;
        }

        var loop = new MessageLoopThread(
            "BetterWindows.TaskbarWatcher",
            _log,
            messageOnly: false, // "TaskbarCreated" and WM_DISPLAYCHANGE are broadcasts: top-level window required
            onStart: OnWatcherStart,
            onStop: OnWatcherStop,
            handler: OnWatcherMessage);
        if (!loop.Start(TimeSpan.FromSeconds(5)))
        {
            _log.LogWarning("Taskbar watcher could not start; the taskbar will not be re-hidden automatically");
            loop.Dispose();
            return;
        }

        _watcher = loop;
    }

    private void StopWatcher()
    {
        var w = _watcher;
        _watcher = null;
        w?.Dispose();
    }

    private bool OnWatcherStart(MessageLoopThread loop)
    {
        t_current = this;
        _taskbarCreatedMessage = PInvoke.RegisterWindowMessage("TaskbarCreated");
        InstallShowHook();
        return true;
    }

    private void OnWatcherStop(MessageLoopThread loop)
    {
        RemoveShowHook();
        nint hwnd = loop.WindowHandle;
        if (hwnd != 0)
        {
            foreach (int id in new[] { TimerRehide, TimerReapplyEarly, TimerReapplyLate })
            {
                PInvoke.KillTimer(hwnd.ToHwnd(), (nuint)id);
            }
        }

        t_current = null;
    }

    /// <summary>
    /// EVENT_OBJECT_SHOW hook restricted (idProcess) to Explorer's process: it fires for every window Explorer shows, so filtering in the
    /// kernel instead of in our callback keeps the idle cost at zero. Re-installed whenever Explorer (and thus its PID) changes.
    /// </summary>
    private void InstallShowHook()
    {
        RemoveShowHook();
        var primary = WindowNative.FindAllByClass(Win32TaskbarSurface.PrimaryClass);
        if (primary.Count == 0)
        {
            return;
        }

        int pid = WindowNative.GetProcessId(primary[0].ToHwnd(), out _);
        if (pid == 0)
        {
            return;
        }

        var hook = PInvoke.SetWinEventHook(
            Win32Consts.EVENT_OBJECT_SHOW,
            Win32Consts.EVENT_OBJECT_SHOW,
            HMODULE.Null,
            &ShowEventProc,
            (uint)pid,
            0,
            Win32Consts.WINEVENT_OUTOFCONTEXT);
        if (hook.IsNull)
        {
            _log.LogWarning("SetWinEventHook(EVENT_OBJECT_SHOW) on Explorer failed (error {Error})", Marshal.GetLastSystemError());
            return;
        }

        _showHook = (nint)hook.Value;
    }

    private void RemoveShowHook()
    {
        if (_showHook != 0)
        {
            PInvoke.UnhookWinEvent(new HWINEVENTHOOK((IntPtr)_showHook));
            _showHook = 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static void ShowEventProc(HWINEVENTHOOK hook, uint eventType, HWND hwnd, int idObject, int idChild, uint idEventThread, uint eventTime)
    {
        try
        {
            if (idObject != Win32Consts.OBJID_WINDOW || idChild != Win32Consts.CHILDID_SELF)
            {
                return;
            }

            string cls = WindowNative.GetClassName(hwnd);
            if (cls == Win32TaskbarSurface.PrimaryClass || cls == Win32TaskbarSurface.SecondaryClass)
            {
                t_current?.ScheduleRehide();
            }
        }
        catch
        {
            // Never throw into native code.
        }
    }

    private void ScheduleRehide()
    {
        if (_desired != TaskbarMode.Hidden)
        {
            return; // Visible/AutoHide: the user (or Core) wants the taskbar - never fight that
        }

        // Fight limiter: if Explorer re-shows the taskbar more than 20 times in 2 s something is looping; back off to once per second.
        long now = Environment.TickCount64;
        while (_recentRehides.Count > 0 && now - _recentRehides.Peek() > 2000)
        {
            _recentRehides.Dequeue();
        }

        _recentRehides.Enqueue(now);
        uint delay = _recentRehides.Count > 20 ? 1000u : 50u;
        var loop = _watcher;
        if (loop != null)
        {
            // 50 ms: lets the shell finish what it is doing (e.g. showing the taskbar for the Start menu) before we hide it again.
            PInvoke.SetTimer(loop.WindowHandle.ToHwnd(), TimerRehide, delay, null);
        }
    }

    private bool OnWatcherMessage(uint msg, nuint wParam, nint lParam, out nint result)
    {
        result = 0;
        var loop = _watcher;
        nint hwnd = loop?.WindowHandle ?? 0;
        if (_taskbarCreatedMessage != 0 && msg == _taskbarCreatedMessage)
        {
            // Explorer (re)started: its new taskbar windows are visible. Re-apply once the shell has settled, and again later for the
            // secondary taskbars that Explorer creates after the primary one.
            if (hwnd != 0)
            {
                PInvoke.SetTimer(hwnd.ToHwnd(), TimerReapplyEarly, 300, null);
                PInvoke.SetTimer(hwnd.ToHwnd(), TimerReapplyLate, 1500, null);
            }

            return true;
        }

        if (msg == Win32Consts.WM_DISPLAYCHANGE && hwnd != 0)
        {
            // Monitors added/removed: secondary taskbars are created/destroyed.
            PInvoke.SetTimer(hwnd.ToHwnd(), TimerReapplyEarly, 600, null);
            return false;
        }

        if (msg == Win32Consts.WM_TIMER && hwnd != 0)
        {
            int id = (int)wParam;
            if (id is TimerRehide or TimerReapplyEarly or TimerReapplyLate)
            {
                PInvoke.KillTimer(hwnd.ToHwnd(), (nuint)id);
                if (id != TimerRehide)
                {
                    InstallShowHook(); // Explorer may have a new PID
                }

                Enforce();
                return true;
            }
        }

        return false;
    }

    /// <summary>Re-asserts Hidden on the watcher thread (the engine is only ever used under the shared gate).</summary>
    private void Enforce()
    {
        try
        {
            lock (TaskbarRestorer.Gate)
            {
                if (TaskbarRestorer.Stamp != _stamp)
                {
                    // Somebody (crash handler / rescue command) restored the taskbar behind our back: stop fighting it. The next Apply starts over.
                    _desired = TaskbarMode.Visible;
                    _current = TaskbarMode.Visible;
                    return;
                }

                if (_desired == TaskbarMode.Hidden)
                {
                    _engine.Enforce();
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Re-hiding the taskbar failed");
        }
    }
}
