using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;

namespace BetterWindows.Native.Interop;

/// <summary>Result of a token-elevation query.</summary>
internal enum ElevationState
{
    /// <summary>The token reports a limited (non-elevated) process.</summary>
    NotElevated,
    /// <summary>The token reports an elevated process.</summary>
    Elevated,
    /// <summary>The token could not be queried (access denied): treated as "not controllable from here".</summary>
    Denied,
}

/// <summary>Integrity/elevation checks used to predict UIPI blocking (activate/close/SendInput against elevated windows).</summary>
internal static unsafe class ProcessSecurity
{
    private static int s_selfElevated = -1;

    /// <summary>True when THIS process runs elevated (then UIPI never blocks us on user-level targets).</summary>
    public static bool IsSelfElevated
    {
        get
        {
            int cached = Volatile.Read(ref s_selfElevated);
            if (cached < 0)
            {
                cached = Query(PInvoke.GetCurrentProcess()) == ElevationState.Elevated ? 1 : 0;
                Volatile.Write(ref s_selfElevated, cached);
            }

            return cached == 1;
        }
    }

    /// <summary>TokenElevation of a process handle that has at least PROCESS_QUERY_LIMITED_INFORMATION.</summary>
    public static ElevationState Query(HANDLE process)
    {
        HANDLE token = default;
        if (!PInvoke.OpenProcessToken(process, TOKEN_ACCESS_MASK.TOKEN_QUERY, &token))
        {
            // Opening the token of a higher-integrity process from a medium one fails with ACCESS_DENIED: that is exactly "elevated".
            return Marshal.GetLastPInvokeError() == Win32Consts.ERROR_ACCESS_DENIED ? ElevationState.Denied : ElevationState.NotElevated;
        }

        try
        {
            uint isElevated = 0;
            uint returned = 0;
            if (!PInvoke.GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenElevation, &isElevated, sizeof(uint), &returned))
            {
                return ElevationState.Denied;
            }

            return isElevated != 0 ? ElevationState.Elevated : ElevationState.NotElevated;
        }
        finally
        {
            PInvoke.CloseHandle(token);
        }
    }

    /// <summary>
    /// Best-effort UIPI prediction for a target process: true when we are NOT elevated and the target is elevated or cannot be inspected.
    /// </summary>
    public static bool IsLikelyBlockedByUipi(int pid)
    {
        if (IsSelfElevated || pid <= 0)
        {
            return false;
        }

        HANDLE h = PInvoke.OpenProcess(Windows.Win32.System.Threading.PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (h.IsNull)
        {
            return Marshal.GetLastPInvokeError() == Win32Consts.ERROR_ACCESS_DENIED;
        }

        try
        {
            return Query(h) != ElevationState.NotElevated;
        }
        finally
        {
            PInvoke.CloseHandle(h);
        }
    }
}
