using BetterWindows.Core.Platform;
using BetterWindows.Native.Interop;
using Microsoft.Extensions.Logging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace BetterWindows.Native;

public sealed unsafe partial class Win32WindowSystem
{
    private const string FrameHostClass = "ApplicationFrameWindow";
    private const string CoreWindowClass = "Windows.UI.Core.CoreWindow";
    private const int TitleTimeoutMs = 100;
    private const int TitleBackoffMs = 3000;
    private const int WindowAumidTtlMs = 10_000;

    /// <inheritdoc />
    public IReadOnlyList<RawWindow> EnumerateTopLevel()
    {
        try
        {
            using var dpi = DpiAwarenessScope.PerMonitorV2();
            var handles = WindowNative.EnumerateTopLevel();
            var result = new List<RawWindow>(handles.Count);
            foreach (nint h in handles)
            {
                var w = DescribeCore(h.ToHwnd());
                if (w != null)
                {
                    result.Add(w);
                }
            }

            PruneCaches(handles);
            return result;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "EnumerateTopLevel failed");
            return Array.Empty<RawWindow>();
        }
    }

    /// <inheritdoc />
    public RawWindow? TryDescribe(long handle)
    {
        if (handle == 0)
        {
            return null;
        }

        try
        {
            using var dpi = DpiAwarenessScope.PerMonitorV2();
            return DescribeCore(handle.ToHwnd());
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "TryDescribe({Handle}) failed", handle);
            return null;
        }
    }

    /// <inheritdoc />
    public long GetForegroundWindow() => PInvoke.GetForegroundWindow().ToLong();

    /// <summary>
    /// Builds the full <see cref="RawWindow"/> for a window, or null if it is gone / not a top-level window.
    /// Expensive per-window queries (title round trip, AUMID property store) are only made for windows that are visible or cloaked:
    /// invisible helper windows (the large majority) are filtered out by Core anyway, and are re-described when they become visible.
    /// </summary>
    private RawWindow? DescribeCore(HWND hwnd)
    {
        if (hwnd.IsNull || !PInvoke.IsWindow(hwnd))
        {
            return null;
        }

        int framePid = WindowNative.GetProcessId(hwnd, out uint threadId);
        if (threadId == 0)
        {
            return null;
        }

        MaybePruneProcesses();
        long handle = hwnd.ToLong();
        string className = WindowNative.GetClassName(hwnd);
        uint style = WindowNative.GetStyle(hwnd);
        uint exStyle = WindowNative.GetExStyle(hwnd);
        bool visible = PInvoke.IsWindowVisible(hwnd);
        uint cloaked = WindowNative.GetCloakedFlags(hwnd);
        bool minimized = (style & Win32Consts.WS_MINIMIZE) != 0;
        bool maximized = !minimized && (style & Win32Consts.WS_MAXIMIZE) != 0;
        bool interesting = visible || cloaked != 0;

        // ---- identity: process, exe, AUMID (UWP frames report the HOSTED app, never ApplicationFrameHost) ----
        int pid = framePid;
        ProcessInfo process = _processes.Get(framePid);
        string? explicitAumid = interesting ? GetWindowAumid(handle, hwnd) : null;
        if (string.Equals(className, FrameHostClass, StringComparison.Ordinal))
        {
            int hosted = FindHostedAppProcess(hwnd, framePid);
            if (hosted != 0)
            {
                pid = hosted;
                process = _processes.Get(hosted);
            }
            else
            {
                // Minimised / suspended: no CoreWindow to ask. Report the frame's own AUMID and NO executable (it would be ApplicationFrameHost.exe).
                process = new ProcessInfo(framePid, null, null, false);
            }
        }

        string title = interesting ? GetTitle(hwnd, handle, framePid) : (_titles.TryGetValue(handle, out var cachedTitle) ? cachedTitle : "");

        NativeRect bounds;
        if (minimized && WindowNative.TryGetNormalRect(hwnd, out var normal))
        {
            // A minimised window's live frame is parked off-screen (-32000); its restored rectangle is the useful geometry.
            bounds = normal;
        }
        else if (!WindowNative.TryGetFrameBounds(hwnd, out bounds))
        {
            return null; // vanished while we were looking
        }

        return new RawWindow
        {
            Handle = handle,
            ProcessId = pid,
            Title = title,
            ClassName = className,
            ExePath = process.ExePath,
            Aumid = explicitAumid ?? process.Aumid,
            IsVisible = visible,
            IsCloaked = CloakInfo.IsCloaked(cloaked),
            CloakedFlags = cloaked,
            IsCloakedByShell = CloakInfo.IsCloakedByShell(cloaked),
            IsMinimized = minimized,
            IsMaximized = maximized,
            IsToolWindow = (exStyle & Win32Consts.WS_EX_TOOLWINDOW) != 0,
            IsAppWindow = (exStyle & Win32Consts.WS_EX_APPWINDOW) != 0,
            IsNoActivate = (exStyle & Win32Consts.WS_EX_NOACTIVATE) != 0,
            OwnerHandle = PInvoke.GetWindow(hwnd, GET_WINDOW_CMD.GW_OWNER).ToLong(),
            IsElevated = process.IsElevated,
            IsOwnProcess = framePid == Environment.ProcessId,
            IsFlashing = _flashing.ContainsKey(handle),
            Bounds = bounds,
        };
    }

    /// <summary>Title with a hard 100 ms bound; after a timeout the process is not asked again for 3 s and the last good title is used.</summary>
    private string GetTitle(HWND hwnd, long handle, int pid)
    {
        long now = Environment.TickCount64;
        if (_titleBackoffUntilMs.TryGetValue(pid, out long until) && now < until)
        {
            return _titles.TryGetValue(handle, out var stale) ? stale : "";
        }

        string? title = WindowNative.GetTitle(hwnd, TitleTimeoutMs);
        if (title == null)
        {
            _titleBackoffUntilMs[pid] = now + TitleBackoffMs;
            return _titles.TryGetValue(handle, out var stale) ? stale : "";
        }

        _titleBackoffUntilMs.TryRemove(pid, out _);
        _titles[handle] = title;
        return title;
    }

    /// <summary>Explicit window AUMID (property store), cached for a few seconds because COM round trips add up over hundreds of windows.</summary>
    private string? GetWindowAumid(long handle, HWND hwnd)
    {
        long now = Environment.TickCount64;
        if (_windowAumids.TryGetValue(handle, out var cached) && now - cached.AtMs < WindowAumidTtlMs)
        {
            return cached.Aumid;
        }

        string? aumid = WindowPropertyStore.TryGetAppUserModelId(hwnd.ToNint());
        _windowAumids[handle] = (aumid, now);
        return aumid;
    }

    /// <summary>
    /// The process of the app hosted by an ApplicationFrameWindow: the child Windows.UI.Core.CoreWindow living in a DIFFERENT process
    /// than the frame (ApplicationFrameHost.exe). Prefers a child with WS_VISIBLE. 0 when there is none.
    /// </summary>
    private static int FindHostedAppProcess(HWND frame, int framePid)
    {
        int fallback = 0;
        foreach (nint child in WindowNative.EnumerateChildren(frame))
        {
            var c = child.ToHwnd();
            if (!string.Equals(WindowNative.GetClassName(c), CoreWindowClass, StringComparison.Ordinal))
            {
                continue;
            }

            int cpid = WindowNative.GetProcessId(c, out _);
            if (cpid == 0 || cpid == framePid)
            {
                continue;
            }

            if ((WindowNative.GetStyle(c) & Win32Consts.WS_VISIBLE) != 0)
            {
                return cpid;
            }

            fallback = cpid;
        }

        return fallback;
    }

    /// <summary>
    /// Releases the process handles of exited processes at most every 30 s, so a long-running shell that mostly uses TryDescribe (no full
    /// enumerations) does not keep thousands of dead process objects alive.
    /// </summary>
    private void MaybePruneProcesses()
    {
        long now = Environment.TickCount64;
        long next = Interlocked.Read(ref _nextProcessPruneMs);
        if (now >= next && Interlocked.CompareExchange(ref _nextProcessPruneMs, now + 30_000, next) == next)
        {
            _processes.Prune();
        }
    }

    /// <summary>Drops cache entries of windows that no longer exist and closes handles of exited processes.</summary>
    private void PruneCaches(List<nint> liveHandles)
    {
        var live = new HashSet<long>(liveHandles.Count);
        foreach (nint h in liveHandles)
        {
            live.Add(h);
        }

        foreach (long k in _titles.Keys)
        {
            if (!live.Contains(k))
            {
                _titles.TryRemove(k, out _);
            }
        }

        foreach (long k in _windowAumids.Keys)
        {
            if (!live.Contains(k))
            {
                _windowAumids.TryRemove(k, out _);
            }
        }

        foreach (long k in _flashing.Keys)
        {
            if (!live.Contains(k))
            {
                _flashing.TryRemove(k, out _);
            }
        }

        long now = Environment.TickCount64;
        foreach (var (pid, until) in _titleBackoffUntilMs)
        {
            if (now >= until)
            {
                _titleBackoffUntilMs.TryRemove(pid, out _);
            }
        }

        _processes.Prune();
    }
}
