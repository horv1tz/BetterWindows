using System.Windows.Interop;
using Microsoft.Extensions.Logging;

namespace BetterWindows.Host.Interop;

/// <summary>Result of a window message handler.</summary>
internal readonly record struct WndProcResult(bool Handled, nint Result)
{
    public static readonly WndProcResult NotHandled = new(false, 0);
    public static WndProcResult Return(nint value) => new(true, value);
}

/// <summary>Handler signature: (message, wParam, lParam) -&gt; handled/result. Runs on the window's UI thread.</summary>
internal delegate WndProcResult WndProcHandler(int msg, nint wParam, nint lParam);

/// <summary>
/// Installs a message hook on the HwndSource of a WPF window. WHY: WS_EX_NOACTIVATE needs WM_MOUSEACTIVATE -&gt; MA_NOACTIVATE, the AppBar needs EVERY message of its window
/// (callback message, WM_WINDOWPOSCHANGED, display changes), and DPI changes need WM_DPICHANGED - none of which WPF exposes as events. The hook never lets an exception
/// escape into the WndProc (an exception there would tear down the process): it is logged and the message is treated as not handled.
/// </summary>
internal sealed class WndProcHook : IDisposable
{
    private readonly HwndSource _source;
    private readonly WndProcHandler _handler;
    private readonly ILogger _log;
    private readonly HwndSourceHook _hook;
    private bool _disposed;

    public WndProcHook(HwndSource source, WndProcHandler handler, ILogger log)
    {
        _source = source;
        _handler = handler;
        _log = log;
        _hook = Hook;
        _source.AddHook(_hook);
    }

    private nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (_disposed) return 0;
        try
        {
            var r = _handler(msg, wParam, lParam);
            if (!r.Handled) return 0;
            handled = true;
            return r.Result;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Window message handler failed for message 0x{Message:X}", msg);
            return 0;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _source.RemoveHook(_hook); } catch { /* the source may already be disposed with its window */ }
    }
}
