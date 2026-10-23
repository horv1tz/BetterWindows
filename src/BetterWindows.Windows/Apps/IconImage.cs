namespace BetterWindows.Native;

/// <summary>How the alpha channel of a raw 32bpp shell bitmap must be read.</summary>
internal enum AlphaKind
{
    /// <summary>Every alpha byte is 0: the bitmap carries no alpha channel (pre-Vista style 32bpp); treat it as fully opaque.</summary>
    None,
    /// <summary>Colour channels never exceed alpha: premultiplied ARGB, which is what IShellItemImageFactory returns.</summary>
    Premultiplied,
    /// <summary>Some colour channel exceeds alpha, which is impossible for premultiplied data: straight alpha.</summary>
    Straight,
}

/// <summary>
/// Pure pixel operations on top-down BGRA buffers used by the icon pipeline (alpha detection, un/premultiply, box downscale, plate trimming).
/// All buffers are 4 bytes per pixel in B,G,R,A order. Everything here is OS independent and unit tested.
/// </summary>
internal static class IconImage
{
    /// <summary>Classifies the alpha channel of a raw shell bitmap (see <see cref="AlphaKind"/>).</summary>
    public static AlphaKind DetectAlpha(ReadOnlySpan<byte> bgra)
    {
        bool anyAlpha = false;
        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            byte a = bgra[i + 3];
            if (a != 0) anyAlpha = true;
            // +3 tolerance: premultiplication rounding in the shell/GDI can leave a colour byte one or two above alpha.
            if (bgra[i] > a + 3 || bgra[i + 1] > a + 3 || bgra[i + 2] > a + 3)
            {
                // A pixel with a=0 but colour set also lands here; that is a bitmap that never wrote alpha but did write colour. Only call it
                // "straight" when at least one pixel has a non-zero alpha, otherwise it is the no-alpha case.
                if (a != 0) return AlphaKind.Straight;
            }
        }
        return anyAlpha ? AlphaKind.Premultiplied : AlphaKind.None;
    }

    /// <summary>Brings any shell bitmap into premultiplied BGRA in place (None -> opaque, Straight -> premultiplied).</summary>
    public static void NormalizeToPremultiplied(Span<byte> bgra, AlphaKind kind)
    {
        switch (kind)
        {
            case AlphaKind.None:
                for (int i = 3; i < bgra.Length; i += 4) bgra[i] = 255;
                break;
            case AlphaKind.Straight:
                Premultiply(bgra);
                break;
        }
    }

    public static void Premultiply(Span<byte> bgra)
    {
        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            int a = bgra[i + 3];
            if (a == 255) continue;
            bgra[i] = (byte)((bgra[i] * a + 127) / 255);
            bgra[i + 1] = (byte)((bgra[i + 1] * a + 127) / 255);
            bgra[i + 2] = (byte)((bgra[i + 2] * a + 127) / 255);
        }
    }

    public static void Unpremultiply(Span<byte> bgra)
    {
        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            int a = bgra[i + 3];
            if (a == 255) continue;
            if (a == 0)
            {
                bgra[i] = bgra[i + 1] = bgra[i + 2] = 0;
                continue;
            }
            bgra[i] = (byte)Math.Min(255, (bgra[i] * 255 + a / 2) / a);
            bgra[i + 1] = (byte)Math.Min(255, (bgra[i + 1] * 255 + a / 2) / a);
            bgra[i + 2] = (byte)Math.Min(255, (bgra[i + 2] * 255 + a / 2) / a);
        }
    }

    /// <summary>
    /// Area-average (box filter) downscale of a PREMULTIPLIED image; averaging in premultiplied space is what keeps transparent edges free of dark/white fringes.
    /// Only shrinks: when the target is larger than the source in either dimension, or identical, the source is returned unchanged.
    /// </summary>
    public static byte[] ResizeBoxPremultiplied(byte[] src, int w, int h, int tw, int th, out int outW, out int outH)
    {
        if (tw > w || th > h || (tw == w && th == h) || tw <= 0 || th <= 0)
        {
            outW = w;
            outH = h;
            return src;
        }
        outW = tw;
        outH = th;
        var dst = new byte[tw * th * 4];
        for (int ty = 0; ty < th; ty++)
        {
            double y0 = (double)ty * h / th, y1 = (double)(ty + 1) * h / th;
            for (int tx = 0; tx < tw; tx++)
            {
                double x0 = (double)tx * w / tw, x1 = (double)(tx + 1) * w / tw;
                double sb = 0, sg = 0, sr = 0, sa = 0, wsum = 0;
                for (int sy = (int)y0; sy < Math.Min(h, (int)Math.Ceiling(y1)); sy++)
                {
                    double wy = Math.Min(y1, sy + 1) - Math.Max(y0, sy);
                    for (int sx = (int)x0; sx < Math.Min(w, (int)Math.Ceiling(x1)); sx++)
                    {
                        double wx = Math.Min(x1, sx + 1) - Math.Max(x0, sx);
                        double wgt = wx * wy;
                        int p = (sy * w + sx) * 4;
                        sb += src[p] * wgt;
                        sg += src[p + 1] * wgt;
                        sr += src[p + 2] * wgt;
                        sa += src[p + 3] * wgt;
                        wsum += wgt;
                    }
                }
                int d = (ty * tw + tx) * 4;
                if (wsum > 0)
                {
                    dst[d] = (byte)Math.Clamp((int)Math.Round(sb / wsum), 0, 255);
                    dst[d + 1] = (byte)Math.Clamp((int)Math.Round(sg / wsum), 0, 255);
                    dst[d + 2] = (byte)Math.Clamp((int)Math.Round(sr / wsum), 0, 255);
                    dst[d + 3] = (byte)Math.Clamp((int)Math.Round(sa / wsum), 0, 255);
                }
            }
        }
        return dst;
    }

    /// <summary>Copies a sub-rectangle.</summary>
    public static byte[] Crop(byte[] src, int w, int x, int y, int cw, int ch)
    {
        var dst = new byte[cw * ch * 4];
        for (int row = 0; row < ch; row++)
            Buffer.BlockCopy(src, ((y + row) * w + x) * 4, dst, row * cw * 4, cw * 4);
        return dst;
    }

    /// <summary>
    /// Detects a UWP/MSIX tile "plate": an image whose visible content is one flat-coloured, (nearly) square, opaque plate surrounded by transparent padding.
    /// Returns the plate's bounding box so the caller can crop the padding away. False for everything else, in particular for logos on a transparent
    /// background and for images that already fill their canvas (cropping those would zoom into the artwork).
    /// Input is PREMULTIPLIED BGRA; plate pixels are opaque so premultiplied == straight there.
    /// </summary>
    public static bool TryFindFlatPlate(byte[] premul, int w, int h, out int x, out int y, out int cw, out int ch)
    {
        x = y = 0;
        cw = w;
        ch = h;
        if (w < 16 || h < 16) return false;

        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int py = 0; py < h; py++)
        {
            for (int px = 0; px < w; px++)
            {
                if (premul[(py * w + px) * 4 + 3] <= 8) continue;
                if (px < minX) minX = px;
                if (px > maxX) maxX = px;
                if (py < minY) minY = py;
                if (py > maxY) maxY = py;
            }
        }
        if (maxX < 0) return false;
        int bw = maxX - minX + 1, bh = maxY - minY + 1;
        if (bw >= w && bh >= h) return false;                          // nothing to trim
        if (bw < w / 4 || bh < h / 4) return false;                    // tiny glyph, not a plate
        if (Math.Abs(bw - bh) > Math.Max(2, Math.Max(bw, bh) / 32)) return false; // not square

        // The middle 60% of each of the four edges of the bounding box must be opaque and one flat colour (corners may be rounded/transparent).
        int refB = -1, refG = -1, refR = -1;
        bool Edge(int ex, int ey)
        {
            int p = (ey * w + ex) * 4;
            if (premul[p + 3] < 250) return false;
            if (refB < 0)
            {
                refB = premul[p]; refG = premul[p + 1]; refR = premul[p + 2];
                return true;
            }
            return Math.Abs(premul[p] - refB) <= 6 && Math.Abs(premul[p + 1] - refG) <= 6 && Math.Abs(premul[p + 2] - refR) <= 6;
        }
        int sx0 = minX + bw / 5, sx1 = maxX - bw / 5, sy0 = minY + bh / 5, sy1 = maxY - bh / 5;
        for (int i = sx0; i <= sx1; i++)
            if (!Edge(i, minY) || !Edge(i, maxY)) return false;
        for (int j = sy0; j <= sy1; j++)
            if (!Edge(minX, j) || !Edge(maxX, j)) return false;

        x = minX;
        y = minY;
        cw = bw;
        ch = bh;
        return true;
    }
}
