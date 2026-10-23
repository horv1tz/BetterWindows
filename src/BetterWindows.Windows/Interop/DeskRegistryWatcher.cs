using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Registry;

namespace BetterWindows.Native.Interop;

/// <summary>
/// Watches a registry key (and its subkeys) with RegNotifyChangeKeyValue on a dedicated background thread. The notification is one-shot,
/// so the watcher re-arms it BEFORE raising the callback: a change that lands while the callback reads the registry produces another notification.
/// A 100 ms debounce collapses the burst of writes Explorer makes when a desktop is created or renamed.
/// </summary>
internal sealed class DeskRegistryWatcher : IDisposable
{
    private const int DebounceMs = 100;
    private readonly string _subKey;
    private readonly Action _onChanged;
    private readonly Action<string>? _onError;
    private readonly AutoResetEvent _changed = new(false);
    private readonly ManualResetEvent _stop = new(false);
    private Thread? _thread;
    private HKEY _key;
    private bool _disposed;

    /// <summary>Creates the watcher; no I/O happens until <see cref="Start"/>.</summary>
    public DeskRegistryWatcher(string subKey, Action onChanged, Action<string>? onError = null)
    {
        _subKey = subKey;
        _onChanged = onChanged;
        _onError = onError;
    }

    /// <summary>Opens the key (KEY_NOTIFY | KEY_READ) and starts the thread. Returns false when the key is missing or cannot be watched.</summary>
    public bool Start()
    {
        if (_disposed || _thread is not null) return _thread is not null;
        HKEY root = new((nint)unchecked((int)0x80000001)); // HKEY_CURRENT_USER
        WIN32_ERROR result;
        unsafe
        {
            HKEY opened;
            fixed (char* sub = _subKey)
            {
                // KEY_NOTIFY is required by RegNotifyChangeKeyValue; KEY_READ lets the caller read values after the notification.
                result = PInvoke.RegOpenKeyEx(root, new PCWSTR(sub), 0, (REG_SAM_FLAGS)(0x0010 | 0x20019), &opened);
            }
            if (result == WIN32_ERROR.ERROR_SUCCESS) _key = opened;
        }
        if (result != WIN32_ERROR.ERROR_SUCCESS)
        {
            _onError?.Invoke($"RegOpenKeyEx failed: {result}");
            return false;
        }
        _thread = new Thread(Loop) { IsBackground = true, Name = "BetterWindows.DesktopRegistryWatcher" };
        _thread.Start();
        return true;
    }

    /// <summary>Signals the thread, waits briefly for it to exit, then closes the key. Safe to call more than once.</summary>
    public void Stop()
    {
        if (_thread is null) return;
        _stop.Set();
        if (!_thread.Join(TimeSpan.FromSeconds(2)))
        {
            // The thread is still inside WaitAny/RegNotifyChangeKeyValue: keep the key and the events alive (they are used by that thread) until process exit.
            _onError?.Invoke("registry watcher did not stop within 2 s");
            return;
        }
        _thread = null;
        CloseKey();
    }

    private void Loop()
    {
        try
        {
            while (true)
            {
                if (!Arm()) break;
                int index = WaitHandle.WaitAny(new WaitHandle[] { _changed, _stop });
                if (index != 0) break;                 // stop requested
                if (_stop.WaitOne(DebounceMs)) break; // coalesce the burst of writes
                if (!Arm()) break;                     // re-arm before the read so later writes are seen
                SafeRaise();
            }
        }
        catch (Exception ex)
        {
            _onError?.Invoke("registry watcher stopped: " + ex.GetType().Name);
        }
    }

    private bool Arm()
    {
        // The event handle is owned by _changed for the object's lifetime; DangerousGetHandle is only used to pass it to the OS.
        var handle = new HANDLE(_changed.SafeWaitHandle.DangerousGetHandle());
        var result = PInvoke.RegNotifyChangeKeyValue(_key, true,
            REG_NOTIFY_FILTER.REG_NOTIFY_CHANGE_NAME | REG_NOTIFY_FILTER.REG_NOTIFY_CHANGE_LAST_SET, handle, true);
        if (result == WIN32_ERROR.ERROR_SUCCESS) return true;
        _onError?.Invoke($"RegNotifyChangeKeyValue failed: {result}");
        return false;
    }

    private void SafeRaise()
    {
        try { _onChanged(); }
        catch (Exception ex) { _onError?.Invoke("change callback threw: " + ex.GetType().Name); }
    }

    private unsafe void CloseKey()
    {
        if (_key.Value != default)
        {
            PInvoke.RegCloseKey(_key);
            _key = default;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        if (_thread is null)
        {
            _changed.Dispose();
            _stop.Dispose();
        }
        _disposed = true;
    }
}
