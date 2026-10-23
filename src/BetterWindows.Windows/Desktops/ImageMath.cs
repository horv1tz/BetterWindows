namespace BetterWindows.Native;

/// <summary>
/// Pixel math for captured windows, on 32-bit BGRA buffers (the layout of a top-down 32 bpp DIB section). Pure and allocation-light; unit-tested on any OS.
/// </summary>
public static class ImageMath
{
    /// <summary>Bytes per pixel in the BGRA buffers used here.</summary>
    public const int BytesPerPixel = 4;

    /// <summary>
    /// Target size that fits <paramref name="maxWidth"/> with the aspect ratio kept. Never upscales. Returns (0, 0) for invalid input.
    /// Uses 64-bit arithmetic so large windows cannot overflow the height computation.
    /// </summary>
    public static (int Width, int Height) FitSize(int srcWidth, int srcHeight, int maxWidth)
    {
        if (srcWidth <= 0 || srcHeight <= 0 || maxWidth <= 0) return (0, 0);
        if (srcWidth <= maxWidth) return (srcWidth, srcHeight);
        long h = ((long)srcHeight * maxWidth + srcWidth / 2) / srcWidth; // rounded
        return (maxWidth, (int)Math.Clamp(h, 1, int.MaxValue));
    }

    /// <summary>True when the source dimensions are positive, each edge is at most <paramref name="maxEdge"/> and the pixel count is at most <paramref name="maxPixels"/>.</summary>
    public static bool IsValidSource(int width, int height, int maxEdge, long maxPixels) =>
        width > 0 && height > 0 && width <= maxEdge && height <= maxEdge && (long)width * height <= maxPixels;

    /// <summary>
    /// True when every pixel's B, G and R are zero. Protected content (DRM video, secure desktop) and cloaked windows come back as black;
    /// a legitimately black window is treated the same way, which only costs a fallback to the cached image or the app icon.
    /// The alpha byte is ignored because PrintWindow leaves it undefined.
    /// </summary>
    public static bool IsAllBlack(ReadOnlySpan<byte> bgra)
    {
        for (int i = 0; i + 2 < bgra.Length; i += BytesPerPixel)
        {
            if (bgra[i] != 0 || bgra[i + 1] != 0 || bgra[i + 2] != 0) return false;
        }
        return true;
    }

    /// <summary>Sets the alpha byte of every pixel to 255. PrintWindow's DIB alpha is undefined, and a PNG with alpha would show black or transparent fringes.</summary>
    public static void SetOpaque(Span<byte> bgra)
    {
        for (int i = 3; i < bgra.Length; i += BytesPerPixel) bgra[i] = 255;
    }

    /// <summary>Copies a rectangle out of a BGRA buffer. Throws <see cref="ArgumentOutOfRangeException"/> when the rectangle is not inside the source.</summary>
    public static byte[] Crop(ReadOnlySpan<byte> src, int srcWidth, int srcHeight, int x, int y, int width, int height)
    {
        if (srcWidth <= 0 || srcHeight <= 0 || width <= 0 || height <= 0 || x < 0 || y < 0 || x + width > srcWidth || y + height > srcHeight)
            throw new ArgumentOutOfRangeException(nameof(x), "crop rectangle is outside the source");
        if (src.Length < (long)srcWidth * srcHeight * BytesPerPixel) throw new ArgumentException("source buffer is too small", nameof(src));
        var dst = new byte[width * height * BytesPerPixel];
        int rowBytes = width * BytesPerPixel;
        for (int row = 0; row < height; row++)
        {
            int srcOffset = ((y + row) * srcWidth + x) * BytesPerPixel;
            src.Slice(srcOffset, rowBytes).CopyTo(dst.AsSpan(row * rowBytes, rowBytes));
        }
        return dst;
    }

    /// <summary>
    /// Area-average (box filter) downscale. Each destination pixel is the mean of the source block that maps onto it, so no aliasing
    /// appears on text-heavy windows. Alpha is written as 255. Throws for inconsistent sizes.
    /// </summary>
    public static byte[] Downscale(ReadOnlySpan<byte> src, int srcWidth, int srcHeight, int dstWidth, int dstHeight)
    {
        if (srcWidth <= 0 || srcHeight <= 0 || dstWidth <= 0 || dstHeight <= 0) throw new ArgumentOutOfRangeException(nameof(dstWidth), "sizes must be positive");
        if (src.Length < (long)srcWidth * srcHeight * BytesPerPixel) throw new ArgumentException("source buffer is too small", nameof(src));
        var dst = new byte[dstWidth * dstHeight * BytesPerPixel];
        for (int dy = 0; dy < dstHeight; dy++)
        {
            int y0 = (int)((long)dy * srcHeight / dstHeight);
            int y1 = Math.Min(srcHeight, Math.Max(y0 + 1, (int)((long)(dy + 1) * srcHeight / dstHeight)));
            for (int dx = 0; dx < dstWidth; dx++)
            {
                int x0 = (int)((long)dx * srcWidth / dstWidth);
                int x1 = Math.Min(srcWidth, Math.Max(x0 + 1, (int)((long)(dx + 1) * srcWidth / dstWidth)));
                long sumB = 0, sumG = 0, sumR = 0;
                for (int sy = y0; sy < y1; sy++)
                {
                    int row = sy * srcWidth * BytesPerPixel;
                    for (int sx = x0; sx < x1; sx++)
                    {
                        int p = row + sx * BytesPerPixel;
                        sumB += src[p];
                        sumG += src[p + 1];
                        sumR += src[p + 2];
                    }
                }
                long count = (long)(x1 - x0) * (y1 - y0);
                int d = (dy * dstWidth + dx) * BytesPerPixel;
                dst[d] = (byte)((sumB + count / 2) / count);
                dst[d + 1] = (byte)((sumG + count / 2) / count);
                dst[d + 2] = (byte)((sumR + count / 2) / count);
                dst[d + 3] = 255;
            }
        }
        return dst;
    }
}
