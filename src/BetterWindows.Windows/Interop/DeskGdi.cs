using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.Storage.Xps;

namespace BetterWindows.Native.Interop;

/// <summary>
/// GDI part of the window capture. Every GDI object created here is selected out and deleted on every path (the DC, the DIB section).
/// </summary>
internal static unsafe class DeskGdi
{
    /// <summary>PW_RENDERFULLCONTENT from winuser.h (0x00000002).</summary>
    private const uint PwRenderFullContent = 0x00000002;

    /// <summary>
    /// Renders the window into a new 32 bpp top-down DIB and copies the pixels out. Returns null when GDI or PrintWindow fails.
    /// The copy means the DIB can be freed immediately, so no GDI handle outlives this call.
    /// </summary>
    internal static byte[]? PrintWindowBgra(nint hwnd, int width, int height)
    {
        if (width <= 0 || height <= 0) return null;

        var bmi = new BITMAPINFO();
        bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
        bmi.bmiHeader.biWidth = width;
        bmi.bmiHeader.biHeight = -height; // negative height = rows stored top-down, the layout ImageMath expects
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = 0;  // BI_RGB, uncompressed

        // A compatible memory DC needs no window DC: PrintWindow draws into whatever DC it is given.
        HDC memDc = PInvoke.CreateCompatibleDC(default);
        if (memDc.IsNull) return null;
        try
        {
            void* bits = null;
            HBITMAP dib = PInvoke.CreateDIBSection(memDc, &bmi, DIB_USAGE.DIB_RGB_COLORS, &bits, default, 0);
            if (dib.IsNull || bits is null) return null;
            try
            {
                HGDIOBJ previous = PInvoke.SelectObject(memDc, dib);
                try
                {
                    // PW_RENDERFULLCONTENT (2) asks the window for its composed content, which DirectComposition and GPU windows need.
                    if (!PInvoke.PrintWindow(new HWND(hwnd), memDc, (PRINT_WINDOW_FLAGS)PwRenderFullContent)) return null;
                    var copy = new byte[(long)width * height * 4];
                    new ReadOnlySpan<byte>(bits, copy.Length).CopyTo(copy);
                    return copy;
                }
                finally
                {
                    // Deselect before DeleteObject: GDI refuses to delete a bitmap that is selected into a DC.
                    PInvoke.SelectObject(memDc, previous);
                }
            }
            finally
            {
                PInvoke.DeleteObject(dib);
            }
        }
        finally
        {
            PInvoke.DeleteDC(memDc);
        }
    }
}
