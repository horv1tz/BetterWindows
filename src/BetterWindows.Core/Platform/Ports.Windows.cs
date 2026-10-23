using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Platform;

// ======================================================================================
//  PORTS: the contract between Core (pure logic) and the Windows layer (Win32/WinRT).
//  Implementations live in BetterWindows.Windows. Every implementation must:
//    * be thread-safe; events may fire on ANY thread (Core marshals itself);
//    * never throw from event handlers' paths; return false/null/empty on failure and log;
//    * degrade gracefully on unsupported OS builds (IsSupported=false, empty results).
// ======================================================================================

/// <summary>Window enumeration, change notifications and basic window commands.</summary>
public interface IWindowSystem : IDisposable
{
    /// <summary>All top-level windows (visible or not), unfiltered. Filtering is Core's job (WindowFilter).</summary>
    IReadOnlyList<RawWindow> EnumerateTopLevel();
    /// <summary>Describe one window; null if it no longer exists.</summary>
    RawWindow? TryDescribe(long handle);
    /// <summary>Current foreground window handle (0 if none).</summary>
    long GetForegroundWindow();
    /// <summary>
    /// Raised for window changes (SetWinEventHook + shell hook). Fast handlers only; raised from a dedicated
    /// hook thread. Not raised before <see cref="Start"/>.
    /// </summary>
    event Action<WindowEvent>? WindowChanged;
    void Start();
    void Stop();

    /// <summary>Bring to foreground (restoring if minimised). Uses every legal trick to defeat the foreground lock.</summary>
    bool Activate(long handle);
    bool Minimize(long handle);
    bool Restore(long handle);
    bool Maximize(long handle);
    /// <summary>Post WM_CLOSE (graceful close).</summary>
    bool Close(long handle);
    /// <summary>True if a window of another process fully covers <paramref name="monitor"/> (fullscreen game/video).</summary>
    bool IsFullscreenWindowActive(MonitorInfo monitor);
}

public interface IMonitorProvider
{
    IReadOnlyList<MonitorInfo> GetMonitors();
    /// <summary>Monitor containing the physical-pixel point (nearest if outside).</summary>
    MonitorInfo? FromPoint(int xPx, int yPx);
    /// <summary>Raised when monitors are added/removed/moved or DPI changes.</summary>
    event Action? MonitorsChanged;
}

public interface ICursorProvider
{
    /// <summary>Cursor position in virtual-screen physical pixels.</summary>
    NativePoint GetCursorPosition();
    /// <summary>Any mouse button currently pressed (hot corners are suppressed while dragging).</summary>
    bool IsAnyMouseButtonDown();
}

/// <summary>Hide/restore the native Windows taskbar (Shell_TrayWnd + secondary taskbars) and manage the work area.</summary>
public interface ITaskbarController : IDisposable
{
    TaskbarMode Current { get; }
    /// <summary>Apply a mode. Hidden: SW_HIDE all taskbars + keep hidden if Explorer re-shows/restarts. Idempotent.</summary>
    void Apply(TaskbarMode mode);
    /// <summary>Always restore the native taskbar fully (visible, auto-hide as the user had it). Safe to call repeatedly and from crash paths.</summary>
    void Restore();
}

public interface IAppBar : IDisposable
{
    bool IsRegistered { get; }
    /// <summary>
    /// Reserve <paramref name="thicknessPx"/> along an edge of the monitor (ABM_NEW/QUERYPOS/SETPOS) and return the final
    /// rectangle (physical px) the window must occupy. The caller moves its window there.
    /// </summary>
    NativeRect Dock(AppBarEdge edge, NativeRect monitorBoundsPx, int thicknessPx);
    /// <summary>ABM_REMOVE; releases the reserved work area.</summary>
    void Undock();
    /// <summary>Feed WndProc messages of the appbar window here; returns true if consumed (callback message).</summary>
    bool ProcessWindowMessage(uint msg, nint wParam, nint lParam);
    event Action<AppBarNotification>? Notification;
}

public interface IAppBarFactory
{
    IAppBar Create(nint hwnd);
}

public interface IHotkeyRegistrar : IDisposable
{
    /// <summary>RegisterHotKey on a private message-only window. Returns false if the combination is taken.</summary>
    bool TryRegister(string id, HotkeyGesture gesture);
    void Unregister(string id);
    void UnregisterAll();
    /// <summary>Hotkey id pressed. Raised on a worker thread.</summary>
    event Action<string>? Pressed;
}

/// <summary>Synthesises Win+D / Win+Ctrl+Arrow etc. (SendInput).</summary>
public interface IInputSimulator
{
    void ShowDesktop();
    void SwitchDesktopLeft();
    void SwitchDesktopRight();
    void SendTaskView();
}

public interface IVirtualDesktopService
{
    /// <summary>False when the OS build is unsupported or features.internalVirtualDesktops is off: only the current desktop exists.</summary>
    bool IsSupported { get; }
    IReadOnlyList<DesktopInfo> GetDesktops();
    /// <summary>Desktop id the window lives on (IVirtualDesktopManager.GetWindowDesktopId); null if unknown/pinned.</summary>
    string? GetWindowDesktopId(long handle);
    bool IsWindowOnCurrentDesktop(long handle);
    bool Switch(string desktopId);
    bool MoveWindow(long handle, string desktopId);
    DesktopInfo? Create();
    bool Remove(string desktopId);
    event Action? Changed;
}

/// <summary>Window snapshots for Mission Control.</summary>
public interface IWindowCapture
{
    /// <summary>PNG, scaled to fit maxWidth (aspect kept). Null if not capturable (minimised UWP, protected content).</summary>
    Task<byte[]?> CaptureAsync(long handle, int maxWidth, CancellationToken ct = default);
}

public interface ITrayHost : IDisposable
{
    bool IsSupported { get; }
    void Start();
    void Stop();
    IReadOnlyList<TrayIcon> Icons { get; }
    event Action? Changed;
    Task<byte[]?> GetIconPngAsync(string id, CancellationToken ct = default);
    void Click(string id, TrayButton button, NativePoint screenPx);
}
