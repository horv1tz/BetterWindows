using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Platform;

// ======================================================================================
//  PORTS: the contract between Core (pure logic) and the Windows layer (Win32/WinRT).
//  Implementations live in BetterWindows.Windows. Every implementation must:
//    * be thread-safe; events may fire on ANY thread (Core marshals itself);
//    * never throw from event handlers' paths; return false/null/empty on failure and log;
//    * degrade gracefully on unsupported OS builds (IsSupported=false, empty results);
//    * have a constructor that never throws and does no I/O: acquire OS resources lazily in Start()/first use and expose IsSupported=false / empty Current on failure;
//    * raise events through a per-subscriber try/catch (BetterWindows.Native.SafeEvent.Raise): a Core exception must never unwind into a hook/WndProc/WinRT thread;
//    * the Core subscribers of these events return in < 1 ms and never block: they only Invalidate() a state topic or Task.Run (docs/IPC.md section 13).
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

    /// <summary>
    /// Bring to foreground (restoring if minimised). Uses every legal trick to defeat the foreground lock. Activate/Minimize/Restore/Maximize/Close return FALSE
    /// for windows of our own process and for shell classes (Progman, WorkerW, Shell_TrayWnd, Shell_SecondaryTrayWnd): the UI must never be able to close them.
    /// </summary>
    bool Activate(long handle);
    bool Minimize(long handle);
    bool Restore(long handle);
    bool Maximize(long handle);
    /// <summary>Post WM_CLOSE (graceful close).</summary>
    bool Close(long handle);
    /// <summary>
    /// True iff the FOREGROUND window is a fullscreen app on <paramref name="monitor"/>: not own process, not cloaked, not one of the shell classes
    /// (Progman, WorkerW, Shell_TrayWnd, Shell_SecondaryTrayWnd, XamlExplorerHostIslandWindow, MultitaskingViewFrame, Windows.UI.Core.CoreWindow for Start/Search),
    /// its DWMWA_EXTENDED_FRAME_BOUNDS cover monitor.BoundsPx (+-1 px) AND it is not an ordinary maximised captioned window (borderless/popup windows and
    /// F11-style fullscreen count). SHQueryUserNotificationState (D3D fullscreen / busy / presentation mode) may serve as an additional positive hint.
    /// </summary>
    bool IsFullscreenWindowActive(MonitorInfo monitor);
    /// <summary>Raised when the fullscreen state of any monitor may have changed (foreground change, foreground window moved/resized). Consumers re-query <see cref="IsFullscreenWindowActive"/>; nobody polls.</summary>
    event Action? FullscreenChanged;
}

/// <summary>
/// Monitor enumeration. MonitorInfo.id = MonitorIdentity.IdFromDevicePath(devicePath) (stable across reboots/ports), boundsPx/workAreaPx in virtual-screen
/// physical pixels, scale = effective DPI / 96. MonitorsChanged also fires on work-area changes (SPI_SETWORKAREA) and DPI changes.
/// </summary>
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
    /// <summary>Cursor position in virtual-screen physical pixels. False when unavailable (secure desktop, lock screen, UAC prompt): callers must skip that tick.</summary>
    bool TryGetCursorPosition(out NativePoint position);
    /// <summary>Any mouse button currently pressed (hot corners are suppressed while dragging).</summary>
    bool IsAnyMouseButtonDown();
}

/// <summary>
/// Hide/restore the native Windows taskbar (Shell_TrayWnd + every Shell_SecondaryTrayWnd).
/// Hidden = ABM_SETSTATE(ABS_AUTOHIDE) on the taskbar (the shell then stops reserving its band, and ABM_QUERYPOS of our appbars ignores it)
///          AND SW_HIDE on all taskbar windows (an auto-hide taskbar would otherwise pop up at the screen edge and fight the dock). No SPI_SETWORKAREA writes.
/// AutoHide = ABS_AUTOHIDE only. Visible = the captured ORIGINAL state (autohide / always-on-top flags + per-taskbar visibility).
/// Before the FIRST mutation the original state is persisted atomically to %LOCALAPPDATA%\BetterWindows\state\taskbar.json; Restore() works in a FRESH process
/// without a prior Apply (reads the snapshot, applies it, deletes it) and is idempotent, so a crash/kill is recoverable by the next start or by --restore-taskbar.
/// Re-hide on Explorer restart ("TaskbarCreated") and when the shell re-shows the taskbar, unless the user asked for a temporary Visible.
/// </summary>
public interface ITaskbarController : IDisposable
{
    TaskbarMode Current { get; }
    /// <summary>Apply a mode. Idempotent.</summary>
    void Apply(TaskbarMode mode);
    /// <summary>Always restore the native taskbar fully. Safe to call repeatedly, from any thread and from crash paths.</summary>
    void Restore();
}

/// <summary>
/// One AppBar registration for one window (thread-affine to the window's UI thread). thicknessPx is PHYSICAL pixels (DIP x monitor scale); Dock takes the
/// whole-monitor rect, not the work area. The Host must pass EVERY message of the appbar window to ProcessWindowMessage (the implementation issues
/// ABM_ACTIVATE / ABM_WINDOWPOSCHANGED itself and watches WM_DISPLAYCHANGE / WM_DPICHANGED). Notification.PosChanged means: call Dock again with the same edge,
/// monitor and thickness and move the window to the returned rect. Notifications are raised synchronously on the UI thread inside ProcessWindowMessage.
/// </summary>
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
    /// <summary>
    /// PNG, scaled to fit maxWidth (aspect kept), cropped to DWMWA_EXTENDED_FRAME_BOUNDS. Null for cloaked windows (other virtual desktops: DWM does not compose them),
    /// minimised windows and protected content. Enforces a ~500 ms per-window timeout on a worker (PrintWindow can block on hung windows) and a global concurrency
    /// limit of 3. Callers (the Core thumbnail cache) keep the last good image per window.
    /// </summary>
    Task<byte[]?> CaptureAsync(long handle, int maxWidth, CancellationToken ct = default);
}

/// <summary>
/// Notification-area hosting. With Explorer running, apps keep sending Shell_NotifyIcon to Explorer's own Shell_TrayWnd, so a second host cannot receive them:
/// IsSupported is false unless a build-specific mechanism (e.g. the undocumented ITrayNotify) was bound successfully. Start() never throws.
/// </summary>
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
