using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;

namespace BetterWindows.Native.Interop;

/// <summary>Immutable facts about one process, resolved once and cached by <see cref="ProcessInfoCache"/>.</summary>
/// <param name="Pid">Process id.</param>
/// <param name="ExePath">Full image path (QueryFullProcessImageName) or null when inaccessible.</param>
/// <param name="Aumid">AppUserModelID of a packaged process (GetApplicationUserModelId) or null.</param>
/// <param name="IsElevated">Elevated or not inspectable from this non-elevated process (see RawWindow.IsElevated); always false when we run elevated.</param>
internal sealed record ProcessInfo(int Pid, string? ExePath, string? Aumid, bool IsElevated);

/// <summary>
/// Per-process cache of image path, packaged AUMID and elevation. PID reuse cannot poison it: an entry keeps a process HANDLE open, and
/// Windows never recycles a PID while any handle to the process object exists. Entries of exited processes (ExitTime != 0) are closed by
/// <see cref="Prune"/>, which the window system calls on every full enumeration. Failed opens are cached for a few seconds only.
/// </summary>
internal sealed unsafe class ProcessInfoCache : IDisposable
{
    private sealed class Entry
    {
        public ProcessInfo Info = null!;
        public SafeProcessHandle? Handle;
        public long ExpiresAtMs; // only for negative entries (Handle == null)
    }

    private const int NegativeTtlMs = 3000;
    private readonly ConcurrentDictionary<int, Entry> _entries = new();
    private readonly object _pruneGate = new();
    private bool _disposed;

    /// <summary>Facts about <paramref name="pid"/>, or a minimal record (everything unknown) when the process cannot be opened.</summary>
    public ProcessInfo Get(int pid)
    {
        if (pid <= 0)
        {
            return new ProcessInfo(pid, null, null, false);
        }

        if (_entries.TryGetValue(pid, out var e) && (e.Handle != null || Environment.TickCount64 < e.ExpiresAtMs))
        {
            return e.Info;
        }

        var created = Create(pid);
        if (_disposed)
        {
            created.Handle?.Dispose();
            return created.Info;
        }

        // Replace a stale negative entry; if another thread won the race, drop our handle and use theirs.
        _entries.AddOrUpdate(
            pid,
            created,
            (_, old) =>
            {
                if (old.Handle != null)
                {
                    created.Handle?.Dispose();
                    return old;
                }

                return created;
            });
        return _entries.TryGetValue(pid, out var final) ? final.Info : created.Info;
    }

    private static Entry Create(int pid)
    {
        HANDLE h = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (h.IsNull)
        {
            int err = Marshal.GetLastPInvokeError();
            // ACCESS_DENIED here means a protected / higher-integrity process: report it as elevated (not controllable) unless we are elevated too.
            bool elevated = err == Win32Consts.ERROR_ACCESS_DENIED && !ProcessSecurity.IsSelfElevated;
            return new Entry
            {
                Info = new ProcessInfo(pid, null, null, elevated),
                ExpiresAtMs = Environment.TickCount64 + NegativeTtlMs,
            };
        }

        var safe = new SafeProcessHandle(h, ownsHandle: true);
        string? exe = QueryImagePath(h);
        string? aumid = QueryAumid(h);
        bool isElevated = !ProcessSecurity.IsSelfElevated && ProcessSecurity.Query(h) != ElevationState.NotElevated;
        return new Entry { Info = new ProcessInfo(pid, exe, aumid, isElevated), Handle = safe };
    }

    /// <summary>QueryFullProcessImageName(PROCESS_NAME_WIN32) with a long-path retry.</summary>
    internal static string? QueryImagePath(HANDLE process)
    {
        char* small = stackalloc char[1024];
        uint size = 1024;
        if (PInvoke.QueryFullProcessImageName(process, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, new PWSTR(small), &size))
        {
            return new string(small, 0, (int)size);
        }

        if (Marshal.GetLastPInvokeError() != Win32Consts.ERROR_INSUFFICIENT_BUFFER)
        {
            return null;
        }

        var big = new char[32768];
        size = (uint)big.Length;
        fixed (char* p = big)
        {
            return PInvoke.QueryFullProcessImageName(process, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, new PWSTR(p), &size)
                ? new string(p, 0, (int)size)
                : null;
        }
    }

    /// <summary>GetApplicationUserModelId: the AUMID of a packaged (MSIX/UWP) process; null for unpackaged ones.</summary>
    internal static string? QueryAumid(HANDLE process)
    {
        // APPLICATION_USER_MODEL_ID_MAX_LENGTH is 130; use a roomier buffer and honour the reported length.
        char* buf = stackalloc char[256];
        uint len = 256;
        var rc = PInvoke.GetApplicationUserModelId(process, &len, new PWSTR(buf));
        if ((uint)rc == 0 && len > 1)
        {
            return new string(buf, 0, (int)len - 1); // len includes the terminating NUL
        }

        // APPMODEL_ERROR_NO_APPLICATION / NO_PACKAGE = unpackaged; anything else is also "no AUMID".
        return null;
    }

    /// <summary>Closes the handles of exited processes so their PIDs/kernel objects can be released. Cheap; call on full enumerations.</summary>
    public void Prune()
    {
        if (!Monitor.TryEnter(_pruneGate))
        {
            return;
        }

        try
        {
            long now = Environment.TickCount64;
            foreach (var (pid, e) in _entries)
            {
                bool drop;
                if (e.Handle == null)
                {
                    drop = now >= e.ExpiresAtMs;
                }
                else
                {
                    drop = HasExited(e.Handle);
                }

                if (drop && _entries.TryRemove(new KeyValuePair<int, Entry>(pid, e)))
                {
                    e.Handle?.Dispose();
                }
            }
        }
        finally
        {
            Monitor.Exit(_pruneGate);
        }
    }

    private static bool HasExited(SafeProcessHandle handle)
    {
        bool added = false;
        try
        {
            handle.DangerousAddRef(ref added);
            FILETIME create, exit, kernel, user;
            if (!PInvoke.GetProcessTimes(new HANDLE(handle.DangerousGetHandle()), &create, &exit, &kernel, &user))
            {
                return true; // handle unusable: forget it
            }

            return exit.dwLowDateTime != 0 || exit.dwHighDateTime != 0;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
        finally
        {
            if (added)
            {
                handle.DangerousRelease();
            }
        }
    }

    /// <summary>Number of cached processes (diagnostics / tests).</summary>
    public int Count => _entries.Count;

    /// <summary>Closes every handle.</summary>
    public void Dispose()
    {
        _disposed = true;
        foreach (var (pid, e) in _entries)
        {
            if (_entries.TryRemove(pid, out var removed))
            {
                removed.Handle?.Dispose();
            }
        }
    }
}
