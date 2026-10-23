using Windows.Win32;
using Windows.Win32.System.Services;

namespace BetterWindows.Native.Interop;

/// <summary>Service Control Manager queries (documented advapi32 APIs, read-only access rights).</summary>
internal static unsafe class AppsServiceState
{
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;

    /// <summary>
    /// True if the named service is in SERVICE_RUNNING, false if it exists but is not running, null if the state cannot be determined (no such service, access denied).
    /// Only SC_MANAGER_CONNECT / SERVICE_QUERY_STATUS are requested, which every interactive user has.
    /// </summary>
    public static bool? IsRunning(string serviceName)
    {
        SC_HANDLE scm = PInvoke.OpenSCManager(default(Windows.Win32.Foundation.PCWSTR), default(Windows.Win32.Foundation.PCWSTR), ScManagerConnect);
        if (scm.IsNull) return null;
        try
        {
            SC_HANDLE service;
            fixed (char* name = serviceName)
                service = PInvoke.OpenService(scm, new Windows.Win32.Foundation.PCWSTR(name), ServiceQueryStatus);
            if (service.IsNull) return null;
            try
            {
                SERVICE_STATUS_PROCESS status = default;
                uint needed = 0;
                if (!PInvoke.QueryServiceStatusEx(service, SC_STATUS_TYPE.SC_STATUS_PROCESS_INFO, (byte*)&status, (uint)sizeof(SERVICE_STATUS_PROCESS), &needed))
                    return null;
                return status.dwCurrentState == SERVICE_STATUS_CURRENT_STATE.SERVICE_RUNNING;
            }
            finally
            {
                PInvoke.CloseServiceHandle(service);
            }
        }
        finally
        {
            PInvoke.CloseServiceHandle(scm);
        }
    }
}
