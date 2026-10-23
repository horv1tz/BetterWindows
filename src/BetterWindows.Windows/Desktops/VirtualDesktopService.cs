using System.Diagnostics;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Native.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace BetterWindows.Native;

/// <summary>
/// Virtual desktops for Explorer's shell. The desktop list comes from the documented-by-usage registry values under
/// HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops (no internal API). Switching, creating and removing desktops send the
/// documented shortcuts Win+Ctrl+Left/Right, Win+Ctrl+D and Win+Ctrl+F4 and then poll the registry until the change is visible. Window queries and moves
/// use the public IVirtualDesktopManager COM API. Nothing throws: failures return false/null/empty and are logged.
/// Only the Win+Ctrl+Left/Right path needs no feature flag; the undocumented internal path (MoveWindow of foreign windows) runs only when
/// features.internalVirtualDesktops is on, behind <see cref="DesktopBuildTable"/> and <see cref="DesktopInternalGate"/>.
/// </summary>
public sealed class VirtualDesktopService : IVirtualDesktopService, IDisposable
{
    /// <summary>Registry root of the desktop list. Probed on Windows 10 and 11 (22H2 and 24H2) as the only known location; see <see cref="ProbeRoots"/>.</summary>
    internal const string RegistryRoot = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";

    /// <summary>Candidate roots, in order. Only the documented-by-usage root is known; add a verified alternative here (never guess).</summary>
    internal static readonly string[] ProbeRoots = { RegistryRoot };

    /// <summary>How long to wait for Explorer to finish one switching animation before the next key press (animations are about 0.3 s).</summary>
    internal const int SwitchStepTimeoutMs = 1500;

    /// <summary>Create and remove are slower than a switch because Explorer rewrites the window-to-desktop mapping.</summary>
    internal const int CreateRemoveTimeoutMs = 2500;

    private const int PollIntervalMs = 25;
    private readonly ILogger? _log;
    private readonly ISettingsService? _settings;
    private readonly IDesktopInternalMover? _internalMover;
    private readonly DesktopInternalGate _internalGate;
    private readonly DeskVirtualDesktopManager _vdm = new();
    private readonly object _switchLock = new();
    private readonly object _eventLock = new();
    private Action? _changed;
    private DeskRegistryWatcher? _watcher;
    private bool _disposed;
    private int _internalUnavailableLogged;

    /// <summary>
    /// Creates the service. Does no I/O (the registry and COM are touched on first use). <paramref name="internalMover"/> is null in production:
    /// no interface table has been verified on hardware yet, so the internal path reports that it is unavailable instead of calling anything.
    /// </summary>
    public VirtualDesktopService(ISettingsService? settings = null, ILogger<VirtualDesktopService>? logger = null, IDesktopInternalMover? internalMover = null)
    {
        _settings = settings;
        _log = logger;
        _internalMover = internalMover;
        _internalGate = new DesktopInternalGate(msg => _log?.LogWarning("{Message}", msg));
    }

    /// <inheritdoc />
    /// <remarks>True when the desktop list can be read from the registry (at least one desktop).</remarks>
    public bool IsSupported => TryReadState(out var ids, out _) && ids.Count > 0;

    /// <inheritdoc />
    public IReadOnlyList<DesktopInfo> GetDesktops()
    {
        try
        {
            using var root = OpenRoot();
            if (root is null) return Array.Empty<DesktopInfo>();
            var ids = DesktopRegistry.ParseGuidBlob(root.GetValue("VirtualDesktopIDs") as byte[]);
            var current = DesktopRegistry.ParseCurrent(root.GetValue("CurrentVirtualDesktop") as byte[]);
            return DesktopRegistry.BuildDesktops(ids, id => ReadName(root, id), current);
        }
        catch (Exception ex)
        {
            _log?.LogDebug("Reading the virtual desktop list failed: {Type}", ex.GetType().Name);
            return Array.Empty<DesktopInfo>();
        }
    }

    /// <inheritdoc />
    public string? GetWindowDesktopId(long handle)
    {
        nint hwnd = ToHandle(handle);
        if (!DeskNative.IsLiveWindow(hwnd)) return null;
        return _vdm.TryGetDesktopId(hwnd, out var id) && id != Guid.Empty ? DesktopRegistry.FormatId(id) : null;
    }

    /// <inheritdoc />
    /// <remarks>Returns false when the answer cannot be determined (unknown window, COM unavailable).</remarks>
    public bool IsWindowOnCurrentDesktop(long handle)
    {
        nint hwnd = ToHandle(handle);
        if (!DeskNative.IsLiveWindow(hwnd)) return false;
        return _vdm.TryIsOnCurrentDesktop(hwnd, out bool on) && on;
    }

    /// <inheritdoc />
    public bool Switch(string desktopId)
    {
        if (!DesktopRegistry.TryParseId(desktopId, out var target)) return false;
        lock (_switchLock) return SwitchLocked(target);
    }

    /// <inheritdoc />
    public bool MoveWindow(long handle, string desktopId)
    {
        nint hwnd = ToHandle(handle);
        if (!DeskNative.IsLiveWindow(hwnd)) return false;
        if (!DesktopRegistry.TryParseId(desktopId, out var target)) return false;
        if (!TryReadState(out var ids, out _) || !ids.Contains(target)) return false;

        // Public API first. MoveWindowToDesktop is documented to succeed only for windows of the calling process; the result is verified afterwards
        // because a COM call can return S_OK for a window that did not move.
        if (_vdm.TryMoveWindow(hwnd, target) && _vdm.TryGetDesktopId(hwnd, out var got) && got == target) return true;
        return TryInternalMove(hwnd, target);
    }

    /// <inheritdoc />
    /// <remarks>Presses Win+Ctrl+D (which also switches to the new desktop) and returns the desktop that appeared, or null.</remarks>
    public DesktopInfo? Create()
    {
        lock (_switchLock)
        {
            if (!TryReadState(out var before, out _)) return null;
            if (!DeskInput.SendChord(DeskInput.VkLWin, DeskInput.VkLControl, DeskInput.VkD))
            {
                _log?.LogWarning("Win+Ctrl+D was not injected");
                return null;
            }
            if (!WaitUntil(() => TryReadState(out var ids, out _) && ids.Count > before.Count, CreateRemoveTimeoutMs))
            {
                _log?.LogWarning("A new virtual desktop did not appear within {Ms} ms", CreateRemoveTimeoutMs);
                return null;
            }
            var known = before.ToHashSet();
            return GetDesktops().FirstOrDefault(d => !known.Contains(Guid.TryParse(d.Id, out var g) ? g : Guid.Empty));
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Switches to the desktop if needed, then presses Win+Ctrl+F4 (Explorer closes the current desktop and moves its windows to a neighbour).
    /// Refuses to remove the last remaining desktop.
    /// </remarks>
    public bool Remove(string desktopId)
    {
        if (!DesktopRegistry.TryParseId(desktopId, out var target)) return false;
        lock (_switchLock)
        {
            if (!TryReadState(out var ids, out var current)) return false;
            if (ids.Count <= 1 || !ids.Contains(target)) return false;
            if (DesktopRegistry.ResolveCurrentIndex(ids, current) != IndexOf(ids, target) && !SwitchLocked(target)) return false;
            if (!DeskInput.SendChord(DeskInput.VkLWin, DeskInput.VkLControl, DeskInput.VkF4))
            {
                _log?.LogWarning("Win+Ctrl+F4 was not injected");
                return false;
            }
            return WaitUntil(() => TryReadState(out var now, out _) && !now.Contains(target), CreateRemoveTimeoutMs);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Raised on a background thread after the registry changed (debounced 100 ms). The watcher starts with the first subscriber and stops with the last.
    /// </remarks>
    public event Action? Changed
    {
        add
        {
            lock (_eventLock)
            {
                _changed += value;
                EnsureWatcher();
            }
        }
        remove
        {
            // The watcher is stopped outside the lock: its thread may be inside a callback that unsubscribes and needs this lock.
            DeskRegistryWatcher? watcher = null;
            lock (_eventLock)
            {
                _changed -= value;
                if (_changed is null) watcher = TakeWatcher();
            }
            watcher?.Dispose();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        DeskRegistryWatcher? watcher;
        lock (_eventLock)
        {
            if (_disposed) return;
            _disposed = true;
            _changed = null;
            watcher = TakeWatcher();
        }
        watcher?.Dispose();
        _vdm.Dispose();
    }

    private bool SwitchLocked(Guid target)
    {
        if (!TryReadState(out var ids, out var current)) return false;
        int currentIndex = DesktopRegistry.ResolveCurrentIndex(ids, current);
        int targetIndex = IndexOf(ids, target);
        if (currentIndex < 0 || targetIndex < 0) return false;
        var plan = DesktopSwitchPlanner.Plan(currentIndex, targetIndex, ids.Count);
        if (!plan.Valid) return false;
        if (plan.Steps == 0) return true;

        ushort arrow = plan.Direction > 0 ? DeskInput.VkRight : DeskInput.VkLeft;
        int index = currentIndex;
        for (int step = 0; step < plan.Steps; step++)
        {
            if (!DeskInput.SendChord(DeskInput.VkLWin, DeskInput.VkLControl, arrow))
            {
                _log?.LogWarning("Win+Ctrl+Arrow was not injected (blocked input or elevated foreground window)");
                return false;
            }
            // One step per press: Explorer ignores or merges presses sent during its switch animation, so each step waits until the registry shows it.
            index += plan.Direction;
            int expected = index;
            if (!WaitUntil(() => TryReadState(out var nowIds, out var nowCurrent) && DesktopRegistry.ResolveCurrentIndex(nowIds, nowCurrent) == expected, SwitchStepTimeoutMs))
            {
                _log?.LogWarning("Desktop switch step {Step} of {Steps} was not confirmed", step + 1, plan.Steps);
                return false;
            }
        }
        return true;
    }

    private bool TryInternalMove(nint hwnd, Guid target)
    {
        if (_settings?.Current.General.Features.InternalVirtualDesktops != true) return false;
        if (_internalGate.IsDisabled) return false;
        var mover = _internalMover;
        if (mover is null)
        {
            // No verified interface table exists for any build yet (see DesktopBuildTable), so nothing undocumented is called. Logged once.
            if (Interlocked.Exchange(ref _internalUnavailableLogged, 1) == 0)
            {
                var label = DesktopBuildTable.Lookup(Environment.OSVersion.Version.Build)?.Label ?? "this Windows build";
                _log?.LogInformation("Internal virtual desktop path has no verified interface table for {Label}; MoveWindow of foreign windows is unavailable", label);
            }
            return false;
        }
        bool moved = _internalGate.TryRun(() => mover.TryMove(hwnd, target));
        return moved && _vdm.TryGetDesktopId(hwnd, out var got) && got == target;
    }

    private void EnsureWatcher()
    {
        if (_watcher is not null || _disposed) return;
        var watcher = new DeskRegistryWatcher(RegistryRoot, OnRegistryChanged, msg => _log?.LogDebug("{Message}", msg));
        if (watcher.Start()) _watcher = watcher;
        else watcher.Dispose();
    }

    /// <summary>Detaches the watcher under the event lock; the caller disposes it after releasing the lock.</summary>
    private DeskRegistryWatcher? TakeWatcher()
    {
        var watcher = _watcher;
        _watcher = null;
        return watcher;
    }

    private void OnRegistryChanged()
    {
        var handler = _changed;
        SafeEvent.Raise(handler, ex => _log?.LogDebug("Virtual desktop subscriber threw: {Type}", ex.GetType().Name));
    }

    /// <summary>Reads the id list and the current id. Returns false when the root key is missing or holds no desktops.</summary>
    private bool TryReadState(out IReadOnlyList<Guid> ids, out Guid? current)
    {
        ids = Array.Empty<Guid>();
        current = null;
        try
        {
            using var root = OpenRoot();
            if (root is null) return false;
            ids = DesktopRegistry.ParseGuidBlob(root.GetValue("VirtualDesktopIDs") as byte[]);
            current = DesktopRegistry.ParseCurrent(root.GetValue("CurrentVirtualDesktop") as byte[]);
            return ids.Count > 0;
        }
        catch (Exception ex)
        {
            _log?.LogDebug("Reading virtual desktop state failed: {Type}", ex.GetType().Name);
            return false;
        }
    }

    private static RegistryKey? OpenRoot()
    {
        foreach (var path in ProbeRoots)
        {
            var key = Registry.CurrentUser.OpenSubKey(path, writable: false);
            if (key is not null) return key;
        }
        return null;
    }

    private static string? ReadName(RegistryKey root, Guid id)
    {
        using var sub = root.OpenSubKey(@"Desktops\" + id.ToString("B"), writable: false);
        return sub?.GetValue("Name") as string;
    }

    private static bool WaitUntil(Func<bool> condition, int timeoutMs)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            if (condition()) return true;
            if (clock.ElapsedMilliseconds >= timeoutMs) return false;
            Thread.Sleep(PollIntervalMs); // worker or UI-marshalled thread only; never a Core-owned timer
        }
    }

    private static int IndexOf(IReadOnlyList<Guid> ids, Guid id)
    {
        for (int i = 0; i < ids.Count; i++)
        {
            if (ids[i] == id) return i;
        }
        return -1;
    }

    /// <summary>Converts a Core handle (64-bit carrier of a 32-bit Win32 HWND) to nint; values outside the HWND range map to 0.</summary>
    private static nint ToHandle(long handle) => handle <= 0 || handle > uint.MaxValue ? 0 : (nint)handle;
}
