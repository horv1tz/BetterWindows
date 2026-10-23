using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Native.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;

namespace BetterWindows.Native;

/// <summary>
/// Win32 monitor enumeration: EnumDisplayMonitors + GetMonitorInfo + GetDpiForMonitor(MDT_EFFECTIVE_DPI) + EnumDisplayDevices (device
/// interface path for the stable id). All values are physical pixels in virtual-screen space (the calls run under a PerMonitorV2 scope,
/// so a DPI-unaware host still gets physical values). Change notification comes from a hidden TOP-LEVEL window on a dedicated thread
/// (message-only windows do not receive broadcasts) listening to WM_DISPLAYCHANGE, WM_DPICHANGED and WM_SETTINGCHANGE(SPI_SETWORKAREA),
/// debounced by 200 ms and published only if the snapshot really differs. The thread starts lazily with the first subscriber.
/// </summary>
public sealed unsafe class Win32MonitorProvider : IMonitorProvider, IDisposable
{
    private const int DebounceMs = 200;
    private const int CacheTtlPolledMs = 500;
    private const int CacheTtlListeningMs = 5000;

    private readonly ILogger _log;
    private readonly object _gate = new();
    private MessageLoopThread? _loop;
    private Timer? _debounce;
    private Action? _monitorsChanged;
    private IReadOnlyList<MonitorInfo>? _cache;
    private long _cacheAtMs;
    private IReadOnlyList<MonitorInfo>? _published;
    private bool _disposed;

    /// <summary>Creates the provider; no thread is started until somebody subscribes to <see cref="MonitorsChanged"/>.</summary>
    public Win32MonitorProvider(ILogger<Win32MonitorProvider>? logger = null)
    {
        _log = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <inheritdoc />
    public event Action? MonitorsChanged
    {
        add
        {
            lock (_gate)
            {
                _monitorsChanged += value;
            }

            EnsureListening();
        }

        remove
        {
            lock (_gate)
            {
                _monitorsChanged -= value;
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        long now = Environment.TickCount64;
        bool listening = _loop?.IsRunning == true;
        lock (_gate)
        {
            if (_cache != null && now - _cacheAtMs < (listening ? CacheTtlListeningMs : CacheTtlPolledMs))
            {
                return _cache;
            }
        }

        var fresh = Enumerate();
        lock (_gate)
        {
            // An empty result (display driver reset, disconnected session) must not erase a good snapshot.
            if (fresh.Count == 0 && _cache is { Count: > 0 })
            {
                return _cache;
            }

            _cache = fresh;
            _cacheAtMs = now;
            return fresh;
        }
    }

    /// <inheritdoc />
    public MonitorInfo? FromPoint(int xPx, int yPx) => MonitorMath.FromPoint(GetMonitors(), xPx, yPx);

    // ============================================================================================
    //  Enumeration
    // ============================================================================================

    private IReadOnlyList<MonitorInfo> Enumerate()
    {
        try
        {
            using var dpi = DpiAwarenessScope.PerMonitorV2();
            var handles = new List<nint>(4);
            var gc = GCHandle.Alloc(handles);
            try
            {
                PInvoke.EnumDisplayMonitors(default, (RECT*)null, &MonitorEnumProc, (nint)GCHandle.ToIntPtr(gc));
            }
            finally
            {
                gc.Free();
            }

            var list = new List<MonitorInfo>(handles.Count);
            var friendly = new List<string>(handles.Count);
            var raw = new List<(nint H, string Device, string? Path, string? Name)>(handles.Count);
            foreach (nint h in handles)
            {
                if (TryReadMonitor(h, out var device, out var path, out var name, out var mi))
                {
                    raw.Add((h, device, path, name));
                    friendly.Add(MonitorMath.DisplayName(name, raw.Count - 1));
                    list.Add(Build(h, raw.Count - 1, device, path, mi));
                }
            }

            var names = MonitorMath.MakeUnique(friendly);
            for (int i = 0; i < list.Count; i++)
            {
                list[i] = list[i] with { Name = names[i] };
            }

            return list;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Monitor enumeration failed");
            return Array.Empty<MonitorInfo>();
        }
    }

    private bool TryReadMonitor(nint hmonitor, out string device, out string? devicePath, out string? friendlyName, out MONITORINFO info)
    {
        var mi = default(MONITORINFOEXW);
        mi.monitorInfo.cbSize = (uint)sizeof(MONITORINFOEXW);
        if (!PInvoke.GetMonitorInfo(new HMONITOR((IntPtr)hmonitor), (MONITORINFO*)&mi))
        {
            _log.LogDebug("GetMonitorInfo failed for {Monitor} (error {Error})", hmonitor, Marshal.GetLastPInvokeError());
            device = "";
            devicePath = null;
            friendlyName = null;
            info = default;
            return false;
        }

        info = mi.monitorInfo;
        device = mi.szDevice.ToString();
        devicePath = null;
        friendlyName = null;

        // EnumDisplayDevices(adapter output, 0, EDD_GET_DEVICE_INTERFACE_NAME) -> the attached monitor: DeviceID is its device interface path
        // (the stable identity), DeviceString its friendly name.
        var dd = default(DISPLAY_DEVICEW);
        dd.cb = (uint)sizeof(DISPLAY_DEVICEW);
        fixed (char* dev = device)
        {
            if (PInvoke.EnumDisplayDevices(dev, 0, &dd, 1 /* EDD_GET_DEVICE_INTERFACE_NAME */))
            {
                string id = dd.DeviceID.ToString();
                devicePath = id.Length > 0 ? id : null;
                string name = dd.DeviceString.ToString();
                friendlyName = name.Length > 0 ? name : null;
            }
        }

        return true;
    }

    private MonitorInfo Build(nint hmonitor, int index, string device, string? devicePath, MONITORINFO mi)
    {
        double scale = 1.0;
        uint dpiX, dpiY;
        var hr = PInvoke.GetDpiForMonitor(new HMONITOR((IntPtr)hmonitor), MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, &dpiX, &dpiY);
        if (hr.Succeeded && dpiX > 0)
        {
            scale = dpiX / 96.0;
        }
        else
        {
            _log.LogDebug("GetDpiForMonitor failed ({Hr}); assuming 100 %", hr);
        }

        var bounds = new NativeRect(mi.rcMonitor.left, mi.rcMonitor.top, mi.rcMonitor.right, mi.rcMonitor.bottom);
        var work = new NativeRect(mi.rcWork.left, mi.rcWork.top, mi.rcWork.right, mi.rcWork.bottom);
        return new MonitorInfo
        {
            Id = MonitorMath.IdFor(devicePath, device),
            Index = index,
            Primary = (mi.dwFlags & 1 /* MONITORINFOF_PRIMARY */) != 0,
            Name = MonitorMath.DisplayName(null, index),
            BoundsPx = RectMath.ToProtocol(bounds),
            WorkAreaPx = RectMath.ToProtocol(work),
            Scale = scale,
            DevicePath = devicePath,
        };
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static BOOL MonitorEnumProc(HMONITOR hmonitor, HDC hdc, RECT* rect, LPARAM lParam)
    {
        try
        {
            if (GCHandle.FromIntPtr(lParam.Value).Target is List<nint> list)
            {
                list.Add((nint)hmonitor.Value);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    // ============================================================================================
    //  Change notification
    // ============================================================================================

    private void EnsureListening()
    {
        lock (_gate)
        {
            if (_loop != null || _disposed)
            {
                return;
            }

            _debounce = new Timer(_ => OnDebounced(), null, Timeout.Infinite, Timeout.Infinite);
            var loop = new MessageLoopThread(
                "BetterWindows.Monitors",
                _log,
                messageOnly: false, // broadcasts (WM_DISPLAYCHANGE, WM_SETTINGCHANGE) only reach top-level windows
                handler: OnMessage);
            if (!loop.Start(TimeSpan.FromSeconds(5)))
            {
                _log.LogWarning("Monitor change listener could not start; MonitorsChanged will not fire");
                loop.Dispose();
                _debounce.Dispose();
                _debounce = null;
                return;
            }

            _loop = loop;
        }

        // Baseline for the "did anything really change" comparison.
        lock (_gate)
        {
            _published = null;
        }

        var baseline = Enumerate();
        lock (_gate)
        {
            _cache = baseline;
            _cacheAtMs = Environment.TickCount64;
            _published = baseline;
        }
    }

    private bool OnMessage(uint msg, nuint wParam, nint lParam, out nint result)
    {
        result = 0;
        switch (msg)
        {
            case Win32Consts.WM_DISPLAYCHANGE:
            case Win32Consts.WM_DPICHANGED:
                Trigger();
                break;
            case Win32Consts.WM_SETTINGCHANGE when wParam is Win32Consts.SPI_SETWORKAREA or Win32Consts.SPI_SETLOGICALDPIOVERRIDE:
                Trigger();
                break;
        }

        return false; // let DefWindowProc finish the broadcast
    }

    private void Trigger()
    {
        lock (_gate)
        {
            _cache = null;
            _debounce?.Change(DebounceMs, Timeout.Infinite);
        }
    }

    private void OnDebounced()
    {
        try
        {
            var fresh = Enumerate();
            Action? handlers;
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                if (fresh.Count > 0)
                {
                    _cache = fresh;
                    _cacheAtMs = Environment.TickCount64;
                }

                if (_published != null && MonitorMath.SameSnapshot(_published, fresh))
                {
                    return;
                }

                _published = fresh;
                handlers = _monitorsChanged;
            }

            if (handlers == null)
            {
                return;
            }

            foreach (var d in handlers.GetInvocationList())
            {
                try
                {
                    ((Action)d)();
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "MonitorsChanged subscriber threw");
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Monitor change processing failed");
        }
    }

    /// <summary>Stops the listener thread.</summary>
    public void Dispose()
    {
        MessageLoopThread? loop;
        Timer? timer;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            loop = _loop;
            timer = _debounce;
            _loop = null;
            _debounce = null;
        }

        timer?.Dispose();
        loop?.Dispose();
    }
}
