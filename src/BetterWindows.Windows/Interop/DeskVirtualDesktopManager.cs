using System.Runtime.InteropServices;

namespace BetterWindows.Native.Interop;

/// <summary>
/// Public, documented IVirtualDesktopManager (shobjidl_core.h). Its own GUID and CLSID are published by Microsoft; the three methods are the only
/// documented way to query and move windows between virtual desktops. MoveWindowToDesktop only works for windows of the calling process.
/// </summary>
[ComImport]
[Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDeskVirtualDesktopManager
{
    [PreserveSig] int IsWindowOnCurrentVirtualDesktop(nint topLevelWindow, out int onCurrentDesktop);
    [PreserveSig] int GetWindowDesktopId(nint topLevelWindow, out Guid desktopId);
    [PreserveSig] int MoveWindowToDesktop(nint topLevelWindow, [In] ref Guid desktopId);
}

/// <summary>
/// Owns the lazily created IVirtualDesktopManager COM object. Every call is serialised by the lock and reports failure through
/// <c>false</c>; an HRESULT failure or a COM activation failure never escapes as an exception. The object is created on the first
/// call from whichever thread makes it (COM proxies marshal calls to its apartment as needed).
/// </summary>
internal sealed class DeskVirtualDesktopManager : IDisposable
{
    // CLSID_VirtualDesktopManager, documented with IVirtualDesktopManager.
    private static readonly Guid Clsid = new("AA509086-5CA9-4C25-8F95-589D3C07B48A");
    private readonly object _lock = new();
    private IDeskVirtualDesktopManager? _manager;
    private bool _unavailable;

    /// <summary>IsWindowOnCurrentVirtualDesktop. Returns false when the answer is unknown.</summary>
    public bool TryIsOnCurrentDesktop(nint hwnd, out bool onCurrent)
    {
        onCurrent = false;
        lock (_lock)
        {
            var m = Acquire();
            if (m is null) return false;
            try
            {
                int hr = m.IsWindowOnCurrentVirtualDesktop(hwnd, out int value);
                if (hr < 0) return false;
                onCurrent = value != 0;
                return true;
            }
            catch (COMException) { return false; }
        }
    }

    /// <summary>GetWindowDesktopId. Returns false on failure; a GUID of all zeros is reported as success with Guid.Empty (caller maps it to null).</summary>
    public bool TryGetDesktopId(nint hwnd, out Guid desktopId)
    {
        desktopId = Guid.Empty;
        lock (_lock)
        {
            var m = Acquire();
            if (m is null) return false;
            try
            {
                int hr = m.GetWindowDesktopId(hwnd, out desktopId);
                return hr >= 0;
            }
            catch (COMException) { return false; }
        }
    }

    /// <summary>MoveWindowToDesktop. Documented to fail for windows of other processes (E_ACCESSDENIED); returns false then.</summary>
    public bool TryMoveWindow(nint hwnd, Guid desktopId)
    {
        lock (_lock)
        {
            var m = Acquire();
            if (m is null) return false;
            try
            {
                int hr = m.MoveWindowToDesktop(hwnd, ref desktopId);
                return hr >= 0;
            }
            catch (COMException) { return false; }
        }
    }

    private IDeskVirtualDesktopManager? Acquire()
    {
        if (_manager is not null || _unavailable) return _manager;
        try
        {
            // GetTypeFromCLSID returns null (no throw) when the class is not registered; that means the API is absent on this system.
            Type? type = Type.GetTypeFromCLSID(Clsid, throwOnError: false);
            if (type is null) { _unavailable = true; return null; }
            _manager = Activator.CreateInstance(type) as IDeskVirtualDesktopManager;
            if (_manager is null) _unavailable = true;
        }
        catch (Exception ex) when (ex is COMException or TypeLoadException or MissingMethodException or InvalidCastException or UnauthorizedAccessException)
        {
            _unavailable = true;
        }
        return _manager;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_manager is not null)
            {
                Marshal.FinalReleaseComObject(_manager);
                _manager = null;
            }
        }
    }
}
