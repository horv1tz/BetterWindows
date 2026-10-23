using BetterWindows.Core.Platform;
using BetterWindows.Native.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace BetterWindows.Native;

/// <summary>
/// The shell's AppBar protocol (SHAppBarMessage) as an interface, so the docking algorithm is unit-tested against a fake shell on any OS.
/// </summary>
internal interface IAppBarApi
{
    /// <summary>RegisterWindowMessage; 0 on failure.</summary>
    uint RegisterCallbackMessage(string name);

    /// <summary>ABM_NEW. False if the shell refused (including "this window is already registered").</summary>
    bool New(nint hwnd, uint callbackMessage);

    /// <summary>ABM_REMOVE.</summary>
    void Remove(nint hwnd);

    /// <summary>ABM_QUERYPOS: the shell's approved rectangle for the proposal (other appbars subtracted).</summary>
    NativeRect QueryPos(nint hwnd, AppBarEdge edge, NativeRect proposed);

    /// <summary>ABM_SETPOS: commits the rectangle; returns what the shell actually reserved.</summary>
    NativeRect SetPos(nint hwnd, AppBarEdge edge, NativeRect proposed);

    /// <summary>ABM_WINDOWPOSCHANGED.</summary>
    void WindowPosChanged(nint hwnd);

    /// <summary>ABM_ACTIVATE.</summary>
    void Activate(nint hwnd);
}

/// <summary>SHAppBarMessage implementation of <see cref="IAppBarApi"/>.</summary>
internal sealed unsafe class Win32AppBarApi : IAppBarApi
{
    /// <summary>Shared stateless instance.</summary>
    public static Win32AppBarApi Instance { get; } = new();

    /// <inheritdoc />
    public uint RegisterCallbackMessage(string name) => PInvoke.RegisterWindowMessage(name);

    /// <inheritdoc />
    public bool New(nint hwnd, uint callbackMessage)
    {
        var abd = Create(hwnd);
        abd.uCallbackMessage = callbackMessage;
        return PInvoke.SHAppBarMessage(Win32Consts.ABM_NEW, ref abd) != 0;
    }

    /// <inheritdoc />
    public void Remove(nint hwnd)
    {
        var abd = Create(hwnd);
        PInvoke.SHAppBarMessage(Win32Consts.ABM_REMOVE, ref abd);
    }

    /// <inheritdoc />
    public NativeRect QueryPos(nint hwnd, AppBarEdge edge, NativeRect proposed) => Exchange(Win32Consts.ABM_QUERYPOS, hwnd, edge, proposed);

    /// <inheritdoc />
    public NativeRect SetPos(nint hwnd, AppBarEdge edge, NativeRect proposed) => Exchange(Win32Consts.ABM_SETPOS, hwnd, edge, proposed);

    /// <inheritdoc />
    public void WindowPosChanged(nint hwnd)
    {
        var abd = Create(hwnd);
        PInvoke.SHAppBarMessage(Win32Consts.ABM_WINDOWPOSCHANGED, ref abd);
    }

    /// <inheritdoc />
    public void Activate(nint hwnd)
    {
        var abd = Create(hwnd);
        PInvoke.SHAppBarMessage(Win32Consts.ABM_ACTIVATE, ref abd);
    }

    private static APPBARDATA Create(nint hwnd) => new() { cbSize = (uint)sizeof(APPBARDATA), hWnd = hwnd.ToHwnd() };

    private static NativeRect Exchange(uint message, nint hwnd, AppBarEdge edge, NativeRect rect)
    {
        var abd = Create(hwnd);
        abd.uEdge = AppBarMath.ToAbe(edge);
        abd.rc = new RECT { left = rect.Left, top = rect.Top, right = rect.Right, bottom = rect.Bottom };
        PInvoke.SHAppBarMessage(message, ref abd);
        return new NativeRect(abd.rc.left, abd.rc.top, abd.rc.right, abd.rc.bottom);
    }
}
