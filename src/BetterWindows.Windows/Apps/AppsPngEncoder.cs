using System.Buffers.Binary;
using System.IO.Compression;

namespace BetterWindows.Native;

/// <summary>
/// Minimal, dependency-free PNG writer (8-bit RGBA, no interlace). We encode ourselves instead of pulling in System.Drawing.Common or the WinRT BitmapEncoder:
/// it keeps the Windows layer free of extra packages, works on any thread without COM, and is unit-testable on Linux.
/// </summary>
internal static class AppsPngEncoder
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>
    /// Encodes a top-down 32bpp image given in BGRA byte order with STRAIGHT (non-premultiplied) alpha.
    /// </summary>
    /// <exception cref="ArgumentException">Dimensions are not positive or the buffer is too small.</exception>
    public static byte[] EncodeBgra(ReadOnlySpan<byte> bgra, int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentException("Image dimensions must be positive.");
        long needed = (long)width * height * 4;
        if (needed > int.MaxValue / 2 || bgra.Length < needed) throw new ArgumentException("Pixel buffer is smaller than width*height*4.");

        int stride = width * 4;
        // Filtered scanlines: 1 filter byte + stride bytes per row.
        var raw = new byte[(stride + 1) * height];
        var rgba = new byte[stride];
        var prev = new byte[stride];
        var candidate = new byte[stride];
        var best = new byte[stride];

        for (int y = 0; y < height; y++)
        {
            var src = bgra.Slice(y * stride, stride);
            for (int x = 0; x < stride; x += 4)
            {
                rgba[x] = src[x + 2];
                rgba[x + 1] = src[x + 1];
                rgba[x + 2] = src[x];
                rgba[x + 3] = src[x + 3];
            }

            // Adaptive filtering: pick the filter with the smallest sum of absolute (signed) residuals, the heuristic recommended by the PNG spec.
            int bestType = 0;
            long bestScore = long.MaxValue;
            for (int type = 0; type <= 4; type++)
            {
                ApplyFilter(type, rgba, prev, candidate);
                long score = 0;
                for (int i = 0; i < stride; i++)
                {
                    int v = (sbyte)candidate[i];
                    score += v < 0 ? -v : v;
                }
                if (score < bestScore)
                {
                    bestScore = score;
                    bestType = type;
                    Buffer.BlockCopy(candidate, 0, best, 0, stride);
                }
            }
            int o = y * (stride + 1);
            raw[o] = (byte)bestType;
            Buffer.BlockCopy(best, 0, raw, o + 1, stride);
            (prev, rgba) = (rgba, prev); // rgba becomes the next row's scratch, prev keeps this row's original bytes
        }

        using var ms = new MemoryStream(raw.Length / 2 + 128);
        ms.Write(Signature);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[4..], (uint)height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // colour type: truecolour with alpha
        ihdr[10] = 0; // compression: deflate
        ihdr[11] = 0; // filter method
        ihdr[12] = 0; // no interlace
        WriteChunk(ms, "IHDR"u8, ihdr);

        using (var z = new MemoryStream(raw.Length / 2 + 64))
        {
            using (var zs = new ZLibStream(z, CompressionLevel.Optimal, leaveOpen: true))
                zs.Write(raw, 0, raw.Length);
            WriteChunk(ms, "IDAT"u8, z.GetBuffer().AsSpan(0, (int)z.Length));
        }
        WriteChunk(ms, "IEND"u8, ReadOnlySpan<byte>.Empty);
        return ms.ToArray();
    }

    /// <summary>Reads width/height from a PNG header without decoding; false if the data is not a PNG.</summary>
    public static bool TryReadSize(ReadOnlySpan<byte> png, out int width, out int height)
    {
        width = height = 0;
        if (png.Length < 24 || !png[..8].SequenceEqual(Signature)) return false;
        if (!png.Slice(12, 4).SequenceEqual("IHDR"u8)) return false;
        width = (int)BinaryPrimitives.ReadUInt32BigEndian(png[16..]);
        height = (int)BinaryPrimitives.ReadUInt32BigEndian(png[20..]);
        return width > 0 && height > 0;
    }

    private static void ApplyFilter(int type, byte[] cur, byte[] prev, byte[] dest)
    {
        const int bpp = 4;
        int n = cur.Length;
        switch (type)
        {
            case 0:
                Buffer.BlockCopy(cur, 0, dest, 0, n);
                break;
            case 1: // Sub
                for (int i = 0; i < n; i++)
                    dest[i] = (byte)(cur[i] - (i >= bpp ? cur[i - bpp] : 0));
                break;
            case 2: // Up
                for (int i = 0; i < n; i++)
                    dest[i] = (byte)(cur[i] - prev[i]);
                break;
            case 3: // Average
                for (int i = 0; i < n; i++)
                {
                    int a = i >= bpp ? cur[i - bpp] : 0;
                    dest[i] = (byte)(cur[i] - ((a + prev[i]) >> 1));
                }
                break;
            default: // Paeth
                for (int i = 0; i < n; i++)
                {
                    int a = i >= bpp ? cur[i - bpp] : 0;
                    int b = prev[i];
                    int c = i >= bpp ? prev[i - bpp] : 0;
                    dest[i] = (byte)(cur[i] - Paeth(a, b, c));
                }
                break;
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) return a;
        return pb <= pc ? b : c;
    }

    private static void WriteChunk(Stream s, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(len, (uint)data.Length);
        s.Write(len);
        s.Write(type);
        s.Write(data);
        uint crc = 0xFFFFFFFFu;
        crc = UpdateCrc(crc, type);
        crc = UpdateCrc(crc, data);
        crc ^= 0xFFFFFFFFu;
        BinaryPrimitives.WriteUInt32BigEndian(len, crc);
        s.Write(len);
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }
}
