using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace BetterWindows.Native.Interop;

/// <summary>
/// GDI helpers of the icon pipeline: HBITMAP -> BGRA bytes and HICON -> premultiplied BGRA. Every GDI object created here is deleted before returning
/// (no leaks, also on failure paths); every call that can fail is checked.
/// </summary>
internal static unsafe class AppsGdi
{
    /// <summary>Largest bitmap edge we are willing to read (a 256px icon or a 1024px thumbnail never gets near it); protects against absurd allocations.</summary>
    private const int MaxEdge = 4096;

    private static BITMAPINFO TopDown32(int width, int height)
    {
        BITMAPINFO bmi = default;
        bmi.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
        bmi.bmiHeader.biWidth = width;
        bmi.bmiHeader.biHeight = -height; // negative = top-down rows, matching our BGRA buffers and PNG scanline order
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = 0; // BI_RGB
        return bmi;
    }

    /// <summary>
    /// Copies a bitmap handle's pixels into a top-down BGRA array via GetDIBits (works for DIB sections and device-dependent bitmaps alike; for 32bpp DIB sections the
    /// alpha byte is preserved verbatim). The handle is NOT deleted here. Interpretation of the alpha channel is up to the caller (see <see cref="AppsIconImage.DetectAlpha"/>).
    /// </summary>
    public static bool TryReadBitmap(nint hbitmap, out int width, out int height, out byte[] bgra)
    {
        width = height = 0;
        bgra = [];
        if (hbitmap == 0) return false;

        var hbm = new HBITMAP(hbitmap);
        BITMAP bm = default;
        if (PInvoke.GetObject(new HGDIOBJ(hbitmap), sizeof(BITMAP), &bm) == 0) return false;
        int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
        if (w <= 0 || h <= 0 || w > MaxEdge || h > MaxEdge) return false;

        var buffer = new byte[w * h * 4];
        // GetDIBits needs a DC to resolve the colour table of device-dependent bitmaps; the screen DC is the documented choice. The bitmap must not be selected into another DC.
        HDC screen = PInvoke.GetDC(HWND.Null);
        if (screen.IsNull) return false;
        try
        {
            BITMAPINFO bmi = TopDown32(w, h);
            int lines;
            fixed (byte* p = buffer)
                lines = PInvoke.GetDIBits(screen, hbm, 0, (uint)h, p, &bmi, DIB_USAGE.DIB_RGB_COLORS);
            if (lines != h) return false;
        }
        finally
        {
            PInvoke.ReleaseDC(HWND.Null, screen);
        }
        width = w;
        height = h;
        bgra = buffer;
        return true;
    }

    /// <summary>Deletes a GDI bitmap handle if non-zero (ignores failure: nothing sensible to do).</summary>
    public static void DeleteBitmap(nint hbitmap)
    {
        if (hbitmap != 0) PInvoke.DeleteObject(new HGDIOBJ(hbitmap));
    }

    /// <summary>
    /// Renders an HICON (owned by someone else: NOT destroyed here) into premultiplied BGRA with a correct alpha channel, whatever the icon format (32bpp alpha, 24bpp+AND mask,
    /// monochrome). Uses the classic two-pass technique: draw the icon once on opaque black and once on opaque white; on black a pixel is colour*alpha, on white it is
    /// colour*alpha + 255*(1-alpha), so alpha = 255 - (white - black). That derives transparency from the icon's mask for icons that carry no alpha channel, and it
    /// does not depend on how GDI treats the destination alpha byte.
    /// </summary>
    public static bool TryRenderIcon(nint hicon, out int width, out int height, out byte[] premultipliedBgra)
    {
        width = height = 0;
        premultipliedBgra = [];
        if (hicon == 0) return false;
        var icon = new HICON(hicon);

        ICONINFOEXW info = default;
        info.cbSize = (uint)sizeof(ICONINFOEXW);
        if (!PInvoke.GetIconInfoEx(icon, &info)) return false;

        int w, h;
        try
        {
            // GetIconInfoEx returns COPIES of the icon's bitmaps that the caller must delete. A colour icon has hbmColor; a monochrome icon only has a double-height hbmMask.
            var probe = !info.hbmColor.IsNull ? info.hbmColor : info.hbmMask;
            BITMAP bm = default;
            if (probe.IsNull || PInvoke.GetObject(new HGDIOBJ(probe.Value), sizeof(BITMAP), &bm) == 0) return false;
            w = bm.bmWidth;
            h = info.hbmColor.IsNull ? Math.Abs(bm.bmHeight) / 2 : Math.Abs(bm.bmHeight);
        }
        finally
        {
            if (!info.hbmColor.IsNull) PInvoke.DeleteObject(new HGDIOBJ(info.hbmColor.Value));
            if (!info.hbmMask.IsNull) PInvoke.DeleteObject(new HGDIOBJ(info.hbmMask.Value));
        }
        if (w <= 0 || h <= 0 || w > 1024 || h > 1024) return false;

        if (!DrawOnSolid(icon, w, h, 0x00, out var onBlack)) return false;
        if (!DrawOnSolid(icon, w, h, 0xFF, out var onWhite)) return false;

        var result = new byte[w * h * 4];
        for (int i = 0; i < result.Length; i += 4)
        {
            int diff = (onWhite[i] - onBlack[i] + onWhite[i + 1] - onBlack[i + 1] + onWhite[i + 2] - onBlack[i + 2]) / 3;
            int a = Math.Clamp(255 - diff, 0, 255);
            // Premultiplied colours can never exceed alpha.
            result[i] = (byte)Math.Min(onBlack[i], a);
            result[i + 1] = (byte)Math.Min(onBlack[i + 1], a);
            result[i + 2] = (byte)Math.Min(onBlack[i + 2], a);
            result[i + 3] = (byte)a;
        }
        width = w;
        height = h;
        premultipliedBgra = result;
        return true;
    }

    private static bool DrawOnSolid(HICON icon, int w, int h, byte fill, out byte[] pixels)
    {
        pixels = [];
        HDC dc = PInvoke.CreateCompatibleDC(HDC.Null);
        if (dc.IsNull) return false;
        HBITMAP dib = HBITMAP.Null;
        HGDIOBJ previous = default;
        try
        {
            BITMAPINFO bmi = TopDown32(w, h);
            void* bits = null;
            dib = PInvoke.CreateDIBSection(dc, &bmi, DIB_USAGE.DIB_RGB_COLORS, &bits, HANDLE.Null, 0);
            if (dib.IsNull || bits == null) return false;
            previous = PInvoke.SelectObject(dc, new HGDIOBJ(dib.Value));
            if (previous.IsNull) return false;

            new Span<byte>(bits, w * h * 4).Fill(fill); // DIB sections start zeroed (black); white needs an explicit fill
            // DI_NORMAL = DI_IMAGE | DI_MASK: draw the colour image through the mask. Explicit width/height so the icon is drawn 1:1.
            if (!PInvoke.DrawIconEx(dc, 0, 0, icon, w, h, 0, HBRUSH.Null, DI_FLAGS.DI_NORMAL)) return false;
            PInvoke.GdiFlush(); // GDI batches drawing; flush before we read the DIB memory directly

            var copy = new byte[w * h * 4];
            new ReadOnlySpan<byte>(bits, copy.Length).CopyTo(copy);
            pixels = copy;
            return true;
        }
        finally
        {
            // Deselect before deleting: a bitmap that is selected into a DC cannot be deleted.
            if (!previous.IsNull) PInvoke.SelectObject(dc, previous);
            if (!dib.IsNull) PInvoke.DeleteObject(new HGDIOBJ(dib.Value));
            PInvoke.DeleteDC(dc);
        }
    }
}
