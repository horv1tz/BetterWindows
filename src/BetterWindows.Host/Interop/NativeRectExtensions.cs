using BetterWindows.Core.Platform;
using Windows.Win32.Foundation;

namespace BetterWindows.Host.Interop;

/// <summary>Conversions between the Core's <see cref="NativeRect"/> and the Win32 <see cref="RECT"/>/<see cref="HWND"/>.</summary>
internal static class NativeRectExtensions
{
    public static NativeRect ToNativeRect(this RECT r) => new(r.left, r.top, r.right, r.bottom);

    public static HWND ToHwnd(this nint handle) => new(handle);

    public static unsafe nint ToHandle(this HWND hwnd) => (nint)hwnd.Value;
}
