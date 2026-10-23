using System.IO.Compression;
using System.Buffers.Binary;

namespace BetterWindows.Native;

/// <summary>
/// Minimal PNG writer for opaque 8-bit RGB images (colour type 2, filter 0, no interlace), so System.Drawing or WIC are not needed.
/// Output is a valid PNG: signature, IHDR, one IDAT zlib stream (ZLibStream writes the Adler-32), IEND, and CRC-32 on every chunk.
/// </summary>
public static class PngEncoder
{
    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>Encodes a BGRA buffer (alpha ignored, written as opaque RGB). Throws for invalid sizes.</summary>
    public static byte[] Encode(ReadOnlySpan<byte> bgra, int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "PNG dimensions must be positive");
        long pixels = (long)width * height;
        if (bgra.Length < pixels * ImageMath.BytesPerPixel) throw new ArgumentException("pixel buffer is too small", nameof(bgra));

        // Raw scanlines: one filter byte (0 = None) followed by RGB triples.
        int rowBytes = width * 3 + 1;
        var raw = new byte[(long)rowBytes * height];
        for (int y = 0; y < height; y++)
        {
            int dstRow = y * rowBytes;
            raw[dstRow] = 0;
            int srcRow = y * width * ImageMath.BytesPerPixel;
            for (int x = 0; x < width; x++)
            {
                int s = srcRow + x * ImageMath.BytesPerPixel;
                int d = dstRow + 1 + x * 3;
                raw[d] = bgra[s + 2];
                raw[d + 1] = bgra[s + 1];
                raw[d + 2] = bgra[s];
            }
        }

        byte[] compressed;
        using (var zlib = new MemoryStream())
        {
            using (var z = new ZLibStream(zlib, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw);
            compressed = zlib.ToArray();
        }

        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), (uint)height);
        header[8] = 8;  // bit depth
        header[9] = 2;  // colour type: RGB
        header[10] = 0; // compression: deflate
        header[11] = 0; // filter method 0
        header[12] = 0; // no interlace

        using var png = new MemoryStream(compressed.Length + 64);
        png.Write(Signature);
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed);
        WriteChunk(png, "IEND", ReadOnlySpan<byte>.Empty);
        return png.ToArray();
    }

    /// <summary>Writes one chunk: big-endian length, 4-byte ASCII type, data, CRC-32 over type and data.</summary>
    internal static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> typeBytes = stackalloc byte[4];
        for (int i = 0; i < 4; i++) typeBytes[i] = (byte)type[i];
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(len, (uint)data.Length);
        stream.Write(len);
        stream.Write(typeBytes);
        stream.Write(data);
        uint crc = Crc32.Compute(data, Crc32.Compute(typeBytes, 0));
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }
}

/// <summary>CRC-32 (ISO 3309 / PNG polynomial 0xEDB88320, reflected). Pure; the table is built once.</summary>
public static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    /// <summary>CRC of <paramref name="data"/>, continuing from <paramref name="previous"/> (0 for a fresh computation).</summary>
    public static uint Compute(ReadOnlySpan<byte> data, uint previous = 0)
    {
        uint crc = previous ^ 0xFFFFFFFFu;
        foreach (byte b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
