using Windows.Win32.Foundation;

namespace BetterWindows.Native.Interop;

/// <summary>Conversions between CsWin32 handle structs (void*-based) and the plain <c>nint</c>/<c>long</c> forms used by the ports.</summary>
internal static class HandleExtensions
{
    /// <summary>HWND to its integer value.</summary>
    public static unsafe nint ToNint(this HWND h) => (nint)h.Value;

    /// <summary>HWND to the 64-bit wire form used by <c>RawWindow.Handle</c>.</summary>
    public static unsafe long ToLong(this HWND h) => (long)(nint)h.Value;

    /// <summary>Integer value to HWND.</summary>
    public static unsafe HWND ToHwnd(this nint h) => new((void*)h);

    /// <summary>64-bit wire handle to HWND (a truncated/garbage value simply yields a window that does not exist).</summary>
    public static unsafe HWND ToHwnd(this long h) => new((void*)(nint)h);
}
