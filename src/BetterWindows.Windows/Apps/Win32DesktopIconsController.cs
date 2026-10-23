using System.Runtime.CompilerServices;
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
/// Explorer's desktop, as far as BetterWindows needs it: hide/show the native icon list and host our own desktop windows behind everything else.
/// <list type="bullet">
/// <item><b>Icons</b>: <c>ShowWindow(SW_HIDE/SW_SHOW)</c> on the <c>SysListView32</c> only. The persisted "Show desktop icons" setting (HideIcons) is never written. Before hiding, a snapshot
/// and a marker are persisted (<see cref="DesktopIconsState"/>) so <see cref="DesktopIconsRestorer.RestoreNow"/> can undo it from a fresh process after a crash.</item>
/// <item><b>Layer</b>: the WorkerW technique. Progman is sent message 0x052C (wParam 0xD, lParam 1) which makes Explorer split the desktop into a WorkerW that hosts the icon view and a
/// WorkerW that paints the wallpaper. Our window is turned into a child of the window that hosts <c>SHELLDLL_DefView</c> (a WorkerW, or Progman itself on Windows 11 24H2 where the view is a
/// direct child of Progman). It sits above the wallpaper and, being a child of the desktop, below every normal window. Z-order among the siblings follows the icon state: while native icons are
/// HIDDEN the window is placed above the icon view so it receives the mouse; while they are visible it is placed just below it so the icons stay usable.</item>
/// <item><b>Shell restarts</b>: a hidden top-level window on its own message-loop thread watches <c>TaskbarCreated</c> and polls (2 s) whether the recorded parents still exist; on a restart it re-hides
/// the icons (if we should be hiding them) and raises <see cref="ShellRestarted"/> so the Host re-attaches its windows.</item>
/// </list>
/// The layout knowledge lives in <see cref="AppsDesktopWindows"/> (the only copy). Cannot be exercised on non-Windows hosts; see the integration tests.
/// </summary>
public sealed unsafe class Win32DesktopIconsController : IDesktopIconsController
{
    private const string SentinelClassName = "BetterWindows.DesktopIconsSentinel";
    private const uint ProgmanSpawnWorkerWMessage = 0x052C;
    private const uint TickIntervalMs = 2000;
    private const nuint TickTimerId = 1;
    private const nuint RehideTimerId = 2;
    private const uint RehideIntervalMs = 500;
    private const int MaxRehideAttempts = 40; // 20 s: Explorer needs a while to rebuild the desktop after a restart

    private static readonly object InstancesGate = new();
    private static readonly Dictionary<nint, Win32DesktopIconsController> Instances = new();
    private static uint s_taskbarCreatedMessage;

    // Window style flags as nint without a constant-overflow warning: WS_POPUP is 0x80000000, which does not fit a 32-bit nint (the process is x64, where it does).
    private static nint Flag(WINDOW_STYLE s) => unchecked((nint)(long)(uint)s);
    private static nint Flag(WINDOW_EX_STYLE s) => unchecked((nint)(long)(uint)s);

    private sealed record AttachedLayer(nint Window, nint Parent, nint OriginalStyle, nint OriginalExStyle);

    private readonly ILogger _log;
    private readonly DesktopIconsState _state;
    private readonly object _gate = new();
    private readonly Dictionary<nint, AttachedLayer> _layers = new();
    private readonly ManualResetEventSlim _sentinelReady = new(false);
    private Thread? _sentinelThread;
    private nint _sentinelWindow;
    private bool _hiding;
    private bool _disposed;
    private int _rehideAttempts;

    /// <summary>Raised when Explorer restarted or a recorded desktop parent vanished; the Host must re-attach its desktop windows.</summary>
    public event Action? ShellRestarted;

    /// <summary>Creates the controller using the standard state folder.</summary>
    public Win32DesktopIconsController(ILogger<Win32DesktopIconsController>? logger = null)
        : this(logger, DesktopIconsState.CreateDefault())
    {
    }

    internal Win32DesktopIconsController(ILogger? logger, DesktopIconsState state)
    {
        _log = logger ?? NullLogger.Instance;
        _state = state;
    }

    // ------------------------------------------------------------------ native icons

    /// <inheritdoc />
    public void SetNativeIconsVisible(bool visible)
    {
        try
        {
            lock (_gate)
            {
                if (_disposed) return;
                _hiding = !visible;
                EnsureSentinelLocked();
                if (visible) DesktopIconsRestorer.Restore(_state);
                else HideIconsLocked();
                ReorderLayersLocked();
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "SetNativeIconsVisible({Visible}) failed", visible);
        }
    }

    /// <summary>Hides the icon list if it is visible. Idempotent; also used to re-hide after Explorer re-showed it or restarted. Caller holds the lock.</summary>
    private void HideIconsLocked()
    {
        if (!AppsDesktopWindows.TryFind(out var layout) || layout.ListView == 0)
        {
            _log.LogDebug("Desktop icon list not found yet; will retry");
            return;
        }
        bool visibleNow = AppsDesktopWindows.HasVisibleStyle(layout.ListView);
        if (!_state.MarkerExists)
        {
            // The user (not us) already hid the icons through Explorer's own toggle: nothing to do, and above all nothing to "restore" later.
            if (!visibleNow) return;
            var snapshot = new DesktopIconsSnapshot(DesktopIconsState.CurrentVersion, ListViewWasVisible: true, layout.ListView, Environment.ProcessId, DateTime.UtcNow);
            // Marker + snapshot are written BEFORE the first mutation; if that fails we do not hide (a state we could not undo after a crash is worse than visible icons).
            if (!_state.TryBegin(snapshot))
            {
                _log.LogError("Cannot persist the desktop-icons state under {Dir}; leaving the icons visible", _state.Directory);
                return;
            }
        }
        if (visibleNow) AppsDesktopWindows.TryShow(layout.ListView, show: false);
    }

    /// <inheritdoc />
    public void Restore()
    {
        try
        {
            lock (_gate)
            {
                _hiding = false;
                DesktopIconsRestorer.Restore(_state);
                ReorderLayersLocked();
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Restoring the desktop icons failed");
        }
    }

    // ------------------------------------------------------------------ desktop layer

    /// <inheritdoc />
    public bool TryAttachDesktopLayer(nint hwnd, NativeRect boundsPx)
    {
        try
        {
            if (hwnd == 0 || boundsPx.Width <= 0 || boundsPx.Height <= 0) return false;
            var window = AppsDesktopWindows.H(hwnd);
            if (!PInvoke.IsWindow(window)) return false;

            bool hiding;
            AttachedLayer? existing;
            lock (_gate)
            {
                if (_disposed) return false;
                EnsureSentinelLocked();
                hiding = _hiding;
                _layers.TryGetValue(hwnd, out existing);
            }

            // Windows 8 - 11 23H2: ask Progman to create the WorkerW pair. Unknown to (and ignored by) builds where DefView already lives under Progman. Failure/timeout is not fatal.
            HWND progman = PInvoke.FindWindow("Progman", null!);
            if (progman.IsNull) return false;
            nuint ignored = 0;
            PInvoke.SendMessageTimeout(progman, ProgmanSpawnWorkerWMessage, new WPARAM(0xD), new LPARAM(1), SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_NORMAL, 1000, &ignored);

            if (!AppsDesktopWindows.TryFind(out var layout) || layout.DefView == 0 || layout.DefViewParent == 0) return false;

            // Already attached to a still-valid parent: just move/resize.
            if (existing != null && CandidateParents(layout).Contains(existing.Parent) && PInvoke.IsWindow(AppsDesktopWindows.H(existing.Parent))
                && AppsDesktopWindows.N(PInvoke.GetParent(window)) == existing.Parent)
            {
                return PlaceLayer(window, layout, boundsPx, hiding, moveAndSize: true);
            }

            // Preferred parent: the window hosting the icon view (WorkerW, or Progman on 24H2). Fallback: the WorkerW that follows it (the classic wallpaper WorkerW).
            foreach (var parent in CandidateParents(layout))
            {
                var layer = AttachTo(window, parent, existing);
                if (layer == null) continue;
                if (!PlaceLayer(window, layout, boundsPx, hiding, moveAndSize: true))
                {
                    DetachCore(window, layer);
                    continue;
                }
                lock (_gate) _layers[hwnd] = layer;
                _log.LogInformation("Attached desktop layer {Window:X} to {Parent:X} (DefView under Progman: {Direct})", hwnd, parent, layout.DefViewUnderProgman);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "TryAttachDesktopLayer failed");
            return false;
        }
    }

    private static IEnumerable<nint> CandidateParents(AppsDesktopLayout layout)
    {
        yield return layout.DefViewParent;
        if (layout.FollowingWorkerW != 0 && layout.FollowingWorkerW != layout.DefViewParent) yield return layout.FollowingWorkerW;
    }

    /// <summary>Turns the window into a child of <paramref name="parent"/>. Returns the bookkeeping record, or null if the shell refused (nothing is left modified then).</summary>
    private AttachedLayer? AttachTo(HWND window, nint parent, AttachedLayer? existing)
    {
        nint style = PInvoke.GetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        nint exStyle = PInvoke.GetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        // Keep the very first (top-level) styles across re-attachments so Detach can restore them.
        nint originalStyle = existing?.OriginalStyle ?? style;
        nint originalEx = existing?.OriginalExStyle ?? exStyle;

        // SetParent does not touch WS_CHILD/WS_POPUP "for compatibility": per its documentation a window that becomes a child must lose WS_POPUP and gain WS_CHILD BEFORE the call.
        nint childStyle = (style & ~Flag(WINDOW_STYLE.WS_POPUP)) | Flag(WINDOW_STYLE.WS_CHILD | WINDOW_STYLE.WS_CLIPSIBLINGS);
        nint childEx = exStyle & ~Flag(WINDOW_EX_STYLE.WS_EX_APPWINDOW | WINDOW_EX_STYLE.WS_EX_TOPMOST);
        PInvoke.SetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWL_STYLE, childStyle);
        PInvoke.SetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, childEx);

        // SetParent returns the previous parent, which is legitimately NULL for a top-level window, so success is verified through GetParent.
        PInvoke.SetParent(window, AppsDesktopWindows.H(parent));
        if (AppsDesktopWindows.N(PInvoke.GetParent(window)) != parent)
        {
            _log.LogDebug("SetParent to {Parent:X} was refused", parent);
            PInvoke.SetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWL_STYLE, style);
            PInvoke.SetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, exStyle);
            return null;
        }
        return new AttachedLayer(AppsDesktopWindows.N(window), parent, originalStyle, originalEx);
    }

    /// <summary>Moves/sizes a child of the desktop host to <paramref name="boundsPx"/> (virtual-screen px -> parent client px) and orders it against the icon view.</summary>
    private bool PlaceLayer(HWND window, AppsDesktopLayout layout, NativeRect boundsPx, bool hiding, bool moveAndSize)
    {
        HWND parent = PInvoke.GetParent(window);
        if (parent.IsNull) return false;

        // Child coordinates are relative to the parent's client area, which does not start at the virtual-screen origin: convert the top-left corner.
        System.Drawing.Point origin = new(boundsPx.Left, boundsPx.Top);
        PInvoke.MapWindowPoints(HWND.Null, parent, &origin, 1);

        var flags = SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW;
        if (!moveAndSize) flags |= SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE;
        HWND insertAfter = InsertAfterFor(AppsDesktopWindows.N(parent), layout, hiding);
        bool ok = PInvoke.SetWindowPos(window, insertAfter, origin.X, origin.Y, boundsPx.Width, boundsPx.Height, flags);
        if (!ok) _log.LogDebug("SetWindowPos for the desktop layer failed");
        return ok;
    }

    /// <summary>
    /// The sibling to insert after. Icons hidden: on top of the icon view (HWND_TOP = null), so the layer gets the mouse. Icons visible: directly below the icon view (icons stay
    /// clickable, we are still above the wallpaper). The icon view is only a sibling when the layer lives in its own parent; under the fallback WorkerW we can only use TOP.
    /// </summary>
    private static HWND InsertAfterFor(nint layerParent, AppsDesktopLayout layout, bool hiding)
        => hiding || layerParent != layout.DefViewParent ? HWND.Null : AppsDesktopWindows.H(layout.DefView);

    /// <inheritdoc />
    public void DetachDesktopLayer(nint hwnd)
    {
        try
        {
            AttachedLayer? layer;
            lock (_gate)
            {
                _layers.Remove(hwnd, out layer);
            }
            var window = AppsDesktopWindows.H(hwnd);
            if (hwnd == 0 || !PInvoke.IsWindow(window)) return;
            DetachCore(window, layer);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "DetachDesktopLayer failed");
        }
    }

    private static void DetachCore(HWND window, AttachedLayer? layer)
    {
        // GetWindowRect is always in screen coordinates: remember where the window is so it stays put when it becomes top-level again.
        PInvoke.GetWindowRect(window, out RECT screen);

        PInvoke.SetParent(window, HWND.Null);
        // Reverse of attaching: AFTER SetParent(NULL) clear WS_CHILD and set WS_POPUP (SetParent documentation).
        nint current = PInvoke.GetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        nint style = (current & ~Flag(WINDOW_STYLE.WS_CHILD)) | Flag(WINDOW_STYLE.WS_POPUP);
        if (layer != null) style = (layer.OriginalStyle & ~Flag(WINDOW_STYLE.WS_VISIBLE)) | (style & Flag(WINDOW_STYLE.WS_VISIBLE));
        PInvoke.SetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWL_STYLE, style);
        if (layer != null) PInvoke.SetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, layer.OriginalExStyle);

        // HWND_BOTTOM (1): do not pop up above normal windows; the Host decides what happens next (re-attach or its own bottom-most fallback).
        PInvoke.SetWindowPos(window, AppsDesktopWindows.H(1), screen.left, screen.top, screen.right - screen.left, screen.bottom - screen.top,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED);
    }

    /// <summary>Re-applies the z-order of every layer to the current icon state. ASYNC so we never block on (or deadlock with) the Host's UI thread while holding the lock.</summary>
    private void ReorderLayersLocked()
    {
        if (_layers.Count == 0) return;
        if (!AppsDesktopWindows.TryFind(out var layout) || layout.DefView == 0) return;
        foreach (var layer in _layers.Values)
        {
            var window = AppsDesktopWindows.H(layer.Window);
            if (!PInvoke.IsWindow(window)) continue;
            HWND insertAfter = InsertAfterFor(layer.Parent, layout, _hiding);
            PInvoke.SetWindowPos(window, insertAfter, 0, 0, 0, 0,
                SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_ASYNCWINDOWPOS);
        }
    }

    // ------------------------------------------------------------------ sentinel (shell restart detection + enforcement)

    private void EnsureSentinelLocked()
    {
        if (_sentinelThread != null || _disposed) return;
        _sentinelThread = new Thread(SentinelMain) { IsBackground = true, Name = "BW.DesktopIcons.Sentinel" };
        _sentinelThread.Start();
        // Wait (briefly) for the window so that a TaskbarCreated broadcast right after this call cannot be missed.
        _sentinelReady.Wait(TimeSpan.FromSeconds(2));
    }

    private void SentinelMain()
    {
        nint hwnd = 0;
        bool classRegistered = false;
        try
        {
            s_taskbarCreatedMessage = PInvoke.RegisterWindowMessage("TaskbarCreated");

            fixed (char* className = SentinelClassName)
            {
                var wc = new WNDCLASSEXW
                {
                    cbSize = (uint)sizeof(WNDCLASSEXW),
                    lpfnWndProc = &SentinelWndProc,
                    hInstance = (HINSTANCE)PInvoke.GetModuleHandle(default(PCWSTR)).Value,
                    lpszClassName = new PCWSTR(className),
                };
                // 0 also happens when the class already exists (a second controller instance); CreateWindowEx below decides whether we can go on.
                classRegistered = PInvoke.RegisterClassEx(&wc) != 0;

                // A top-level (not message-only) window: only those receive the TaskbarCreated broadcast. Never visible, never activated.
                HWND created = PInvoke.CreateWindowEx(
                    WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE, new PCWSTR(className), default,
                    WINDOW_STYLE.WS_POPUP, 0, 0, 0, 0, HWND.Null, default, wc.hInstance, null);
                hwnd = AppsDesktopWindows.N(created);
            }
            if (hwnd == 0)
            {
                _log.LogWarning("Cannot create the desktop sentinel window; Explorer restarts will not be detected");
                return;
            }

            lock (InstancesGate) Instances[hwnd] = this;
            _sentinelWindow = hwnd;
            PInvoke.SetTimer(AppsDesktopWindows.H(hwnd), TickTimerId, TickIntervalMs, null);
            _sentinelReady.Set();

            MSG msg;
            // GetMessage: > 0 message, 0 WM_QUIT, -1 error.
            while (PInvoke.GetMessage(&msg, HWND.Null, 0, 0).Value > 0)
            {
                PInvoke.TranslateMessage(&msg);
                PInvoke.DispatchMessage(&msg);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Desktop sentinel thread failed");
        }
        finally
        {
            lock (InstancesGate)
            {
                if (hwnd != 0) Instances.Remove(hwnd);
            }
            if (hwnd != 0 && PInvoke.IsWindow(AppsDesktopWindows.H(hwnd))) PInvoke.DestroyWindow(AppsDesktopWindows.H(hwnd));
            if (classRegistered)
            {
                fixed (char* className = SentinelClassName)
                    PInvoke.UnregisterClass(new PCWSTR(className), (HINSTANCE)PInvoke.GetModuleHandle(default(PCWSTR)).Value);
            }
            _sentinelWindow = 0;
            _sentinelReady.Set();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT SentinelWndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        // A window procedure must never let an exception escape (that would take the process down).
        try
        {
            Win32DesktopIconsController? owner;
            lock (InstancesGate) Instances.TryGetValue(AppsDesktopWindows.N(hwnd), out owner);
            if (owner != null)
            {
                if (s_taskbarCreatedMessage != 0 && msg == s_taskbarCreatedMessage)
                {
                    owner.OnTaskbarCreated();
                    return default;
                }
                switch (msg)
                {
                    case PInvoke.WM_TIMER:
                        owner.OnTimer((nuint)wParam.Value);
                        return default;
                    case PInvoke.WM_CLOSE:
                        PInvoke.DestroyWindow(hwnd);
                        return default;
                    case PInvoke.WM_DESTROY:
                        PInvoke.PostQuitMessage(0);
                        return default;
                }
            }
        }
        catch (Exception)
        {
            // fall through to the default procedure
        }
        return PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void OnTimer(nuint id)
    {
        try
        {
            bool lost = false;
            lock (_gate)
            {
                if (_disposed) return;
                if (id == RehideTimerId)
                {
                    HideIconsLocked();
                    bool done = !_hiding || (AppsDesktopWindows.TryFind(out var l) && l.ListView != 0 && !AppsDesktopWindows.HasVisibleStyle(l.ListView));
                    if (done || ++_rehideAttempts >= MaxRehideAttempts) PInvoke.KillTimer(AppsDesktopWindows.H(_sentinelWindow), RehideTimerId);
                    return;
                }

                // Regular tick: (1) keep the icons the way we were told, (2) notice that a recorded parent is gone, (3) repair a z-order Explorer changed.
                if (_hiding) HideIconsLocked();
                else if (_state.MarkerExists) DesktopIconsRestorer.Restore(_state); // a restore that could not finish earlier (Explorer was hung)
                lost = CheckLayersLocked();
            }
            if (lost) RaiseShellRestarted();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Desktop sentinel tick failed");
        }
    }

    /// <summary>True if any attached layer lost its parent (those records are dropped). Repairs the z-order of the healthy ones. Caller holds the lock.</summary>
    private bool CheckLayersLocked()
    {
        if (_layers.Count == 0) return false;
        bool lost = false;
        AppsDesktopLayout layout = default;
        bool haveLayout = AppsDesktopWindows.TryFind(out layout);
        foreach (var layer in _layers.Values.ToList())
        {
            var window = AppsDesktopWindows.H(layer.Window);
            bool windowAlive = PInvoke.IsWindow(window);
            bool parentAlive = PInvoke.IsWindow(AppsDesktopWindows.H(layer.Parent));
            if (!windowAlive)
            {
                _layers.Remove(layer.Window); // the Host destroyed it: not a shell problem
                continue;
            }
            if (!parentAlive || !haveLayout || AppsDesktopWindows.N(PInvoke.GetParent(window)) != layer.Parent)
            {
                _layers.Remove(layer.Window);
                lost = true;
                continue;
            }
            if (layer.Parent == layout.DefViewParent && !IsOrderedCorrectly(window, layout.DefView))
            {
                PInvoke.SetWindowPos(window, InsertAfterFor(layer.Parent, layout, _hiding), 0, 0, 0, 0,
                    SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_ASYNCWINDOWPOS);
            }
        }
        return lost;
    }

    private bool IsOrderedCorrectly(HWND window, nint defView)
    {
        if (_hiding) return PInvoke.GetWindow(window, GET_WINDOW_CMD.GW_HWNDPREV).IsNull; // topmost among its siblings
        // Not hiding: the icon view must be somewhere above us.
        HWND above = PInvoke.GetWindow(window, GET_WINDOW_CMD.GW_HWNDPREV);
        for (int i = 0; i < 32 && !above.IsNull; i++)
        {
            if (AppsDesktopWindows.N(above) == defView) return true;
            above = PInvoke.GetWindow(above, GET_WINDOW_CMD.GW_HWNDPREV);
        }
        return false;
    }

    private void OnTaskbarCreated()
    {
        try
        {
            lock (_gate)
            {
                if (_disposed) return;
                _layers.Clear(); // their parents died with the old Explorer
                _rehideAttempts = 0;
                // The new desktop is created over the next seconds and shows its icons: re-hide as soon as it exists, polling quickly.
                if (_hiding) PInvoke.SetTimer(AppsDesktopWindows.H(_sentinelWindow), RehideTimerId, RehideIntervalMs, null);
            }
            RaiseShellRestarted();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Handling TaskbarCreated failed");
        }
    }

    private void RaiseShellRestarted()
    {
        // Off the sentinel thread: handlers call back into this controller (re-attach) and must not run inside our window procedure.
        ThreadPool.QueueUserWorkItem(static state =>
        {
            var self = (Win32DesktopIconsController)state!;
            try { self.ShellRestarted?.Invoke(); }
            catch (Exception ex) { self._log.LogError(ex, "A ShellRestarted handler threw"); }
        }, this);
    }

    /// <summary>Restores the icons and stops the sentinel thread.</summary>
    public void Dispose()
    {
        nint sentinel;
        Thread? thread;
        lock (_gate)
        {
            if (_disposed) return;
            _hiding = false;
            try { DesktopIconsRestorer.Restore(_state); }
            catch (Exception ex) { _log.LogError(ex, "Restoring the desktop icons on dispose failed"); }
            _disposed = true;
            _layers.Clear();
            sentinel = _sentinelWindow;
            thread = _sentinelThread;
        }
        if (sentinel != 0) PInvoke.PostMessage(AppsDesktopWindows.H(sentinel), PInvoke.WM_CLOSE, default, default);
        // Bounded join: a wedged message loop must not hang shutdown (the thread is a background thread anyway).
        thread?.Join(TimeSpan.FromSeconds(2));
    }
}
