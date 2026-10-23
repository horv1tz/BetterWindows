using BetterWindows.Core.Platform;
using BetterWindows.Native.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Native;

/// <summary>Creates <see cref="AppBar"/> registrations for the Host's surface windows.</summary>
public sealed class AppBarFactory : IAppBarFactory
{
    private readonly ILoggerFactory? _loggers;

    /// <summary>Creates the factory; the logger factory is optional.</summary>
    public AppBarFactory(ILoggerFactory? loggers = null)
    {
        _loggers = loggers;
    }

    /// <inheritdoc />
    public IAppBar Create(nint hwnd) => new AppBar(hwnd, Win32AppBarApi.Instance, (ILogger?)_loggers?.CreateLogger<AppBar>() ?? NullLogger.Instance);
}

/// <summary>
/// One AppBar registration (SHAppBarMessage) for one window: reserves a band along a monitor edge so maximised windows and the work area stop
/// short of it. THREAD-AFFINE: create it, call <see cref="Dock"/>/<see cref="Undock"/>/<see cref="ProcessWindowMessage"/> and dispose it on the
/// UI thread that owns the window (the shell delivers the callback through that thread's message loop). The Host must pass EVERY message of the
/// window to <see cref="ProcessWindowMessage"/>; notifications are raised synchronously inside that call. See <see cref="IAppBar"/>.
/// Deliberately NEVER uses ABM_SETAUTOHIDEBAR (the shell allows one auto-hide appbar per edge system-wide); dock auto-hide is a plain topmost window.
/// </summary>
public sealed class AppBar : IAppBar
{
    private const int MaxPasses = 3;

    private readonly nint _hwnd;
    private readonly IAppBarApi _api;
    private readonly ILogger _log;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private uint _callbackMessage;
    private bool _registered;
    private bool _disposed;
    private bool _warnedThread;

    /// <summary>Creates an unregistered AppBar for the window; call <see cref="Dock"/> to register and reserve space.</summary>
    public AppBar(nint hwnd)
        : this(hwnd, Win32AppBarApi.Instance, NullLogger.Instance)
    {
    }

    internal AppBar(nint hwnd, IAppBarApi api, ILogger log)
    {
        _hwnd = hwnd;
        _api = api;
        _log = log;
    }

    /// <inheritdoc />
    public bool IsRegistered => _registered;

    /// <inheritdoc />
    public event Action<AppBarNotification>? Notification;

    /// <inheritdoc />
    public NativeRect Dock(AppBarEdge edge, NativeRect monitorBoundsPx, int thicknessPx)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CheckThread();
        int thickness = AppBarMath.ClampThickness(edge, monitorBoundsPx, thicknessPx);
        var proposed = AppBarMath.Propose(monitorBoundsPx, edge, thickness);

        if (!_registered && !Register())
        {
            // Degraded: no space is reserved, but the caller still gets a sensible rectangle to position its window at.
            return proposed;
        }

        // The documented loop: QUERYPOS (the shell subtracts other appbars) -> re-pin the thickness -> SETPOS. If the shell changes the
        // rectangle again on SETPOS, run once more from its answer; three passes always converge in practice.
        var result = proposed;
        for (int pass = 0; pass < MaxPasses; pass++)
        {
            var queried = _api.QueryPos(_hwnd, edge, proposed);
            var adjusted = AppBarMath.AdjustToThickness(queried, edge, thickness);
            result = _api.SetPos(_hwnd, edge, adjusted);
            if (result == adjusted)
            {
                break;
            }

            proposed = result;
        }

        return result;
    }

    /// <inheritdoc />
    public void Undock()
    {
        CheckThread();
        if (!_registered)
        {
            return;
        }

        _registered = false;
        _api.Remove(_hwnd); // releases the reserved band; the shell recomputes the work area
    }

    /// <inheritdoc />
    public bool ProcessWindowMessage(uint msg, nint wParam, nint lParam)
    {
        if (!_registered)
        {
            return false;
        }

        try
        {
            if (_callbackMessage != 0 && msg == _callbackMessage)
            {
                var n = AppBarMath.MapNotification((int)wParam, lParam);
                if (n.HasValue)
                {
                    Raise(n.Value);
                }

                return true; // ours, even for notifications we do not map (ABN_STATECHANGE)
            }

            switch (msg)
            {
                case Win32Consts.WM_ACTIVATE:
                    _api.Activate(_hwnd); // documented: an appbar tells the shell when it is activated
                    break;
                case Win32Consts.WM_WINDOWPOSCHANGED:
                    _api.WindowPosChanged(_hwnd); // documented: after every move/size of the appbar window
                    break;
                case Win32Consts.WM_DISPLAYCHANGE:
                case Win32Consts.WM_DPICHANGED:
                    // The monitor layout or scale changed under us: the Host re-Docks with fresh geometry.
                    Raise(AppBarNotification.PosChanged);
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "AppBar message {Msg:x} failed", msg);
        }

        return false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (_registered)
            {
                _registered = false;
                _api.Remove(_hwnd);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "ABM_REMOVE failed");
        }
    }

    private bool Register()
    {
        // One callback message per window handle: RegisterWindowMessage atoms are never freed before logoff, so a per-HWND name keeps the
        // number of atoms bounded no matter how often AppBars are re-created.
        _callbackMessage = _api.RegisterCallbackMessage($"BetterWindows.AppBar.{_hwnd:X}");
        if (_callbackMessage == 0)
        {
            _log.LogWarning("RegisterWindowMessage failed; the AppBar cannot receive shell notifications");
            return false;
        }

        if (!_api.New(_hwnd, _callbackMessage))
        {
            // ABM_NEW fails when this window is already registered (a previous registration was leaked): clear it and try once more.
            _api.Remove(_hwnd);
            if (!_api.New(_hwnd, _callbackMessage))
            {
                _log.LogWarning("ABM_NEW failed for window {Hwnd:X}; no space will be reserved", _hwnd);
                return false;
            }
        }

        _registered = true;
        return true;
    }

    private void Raise(AppBarNotification notification)
    {
        var handlers = Notification;
        if (handlers == null)
        {
            return;
        }

        foreach (var d in handlers.GetInvocationList())
        {
            try
            {
                ((Action<AppBarNotification>)d)(notification);
            }
            catch (Exception ex)
            {
                // The shell is waiting for our WndProc: a subscriber bug must not escape into it.
                _log.LogWarning(ex, "AppBar.Notification subscriber threw for {Notification}", notification);
            }
        }
    }

    private void CheckThread()
    {
        if (!_warnedThread && Environment.CurrentManagedThreadId != _ownerThread)
        {
            _warnedThread = true;
            _log.LogWarning("AppBar for window {Hwnd:X} used from a different thread than the one that created it", _hwnd);
        }
    }
}
