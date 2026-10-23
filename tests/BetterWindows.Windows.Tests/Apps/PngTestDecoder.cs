using System.Buffers.Binary;
using System.IO.Compression;

namespace BetterWindows.Native.Tests.Apps;

/// <summary>Independent minimal PNG decoder for the tests (8-bit RGBA, no interlace, all five filters, CRC verified). Deliberately separate from the encoder.</summary>
internal static class PngTestDecoder
{
    public static (int Width, int Height, byte[] Rgba) Decode(byte[] png)
    {
        byte[] sig = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        png.AsSpan(0, 8).SequenceEqual(sig).Should().BeTrue("PNG signature");

        int pos = 8, width = 0, height = 0;
        using var idat = new MemoryStream();
        bool sawEnd = false;
        while (pos < png.Length)
        {
            int len = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos));
            string type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
            var data = png.AsSpan(pos + 8, len);
            uint crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos + 8 + len));
            Crc32(png.AsSpan(pos + 4, 4 + len)).Should().Be(crc, $"CRC of {type}");
            if (type == "IHDR")
            {
                width = (int)BinaryPrimitives.ReadUInt32BigEndian(data);
                height = (int)BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
                data[8].Should().Be(8);
                data[9].Should().Be(6);
                data[12].Should().Be(0);
            }
            else if (type == "IDAT") idat.Write(data);
            else if (type == "IEND") sawEnd = true;
            pos += 12 + len;
        }
        sawEnd.Should().BeTrue();

        idat.Position = 0;
        using var z = new ZLibStream(idat, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        z.CopyTo(raw);
        var bytes = raw.ToArray();
        int stride = width * 4;
        bytes.Length.Should().Be((stride + 1) * height);

        var rgba = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            int filter = bytes[y * (stride + 1)];
            for (int x = 0; x < stride; x++)
            {
                int cur = bytes[y * (stride + 1) + 1 + x];
                int a = x >= 4 ? rgba[y * stride + x - 4] : 0;
                int b = y > 0 ? rgba[(y - 1) * stride + x] : 0;
                int c = x >= 4 && y > 0 ? rgba[(y - 1) * stride + x - 4] : 0;
                int v = filter switch
                {
                    0 => cur,
                    1 => cur + a,
                    2 => cur + b,
                    3 => cur + ((a + b) >> 1),
                    4 => cur + Paeth(a, b, c),
                    _ => throw new InvalidDataException("bad filter " + filter),
                };
                rgba[y * stride + x] = (byte)v;
            }
        }
        return (width, height, rgba);
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }

    /// <summary>BGRA (straight) -> RGBA, the order the decoder returns.</summary>
    public static byte[] ToRgba(byte[] bgra)
    {
        var r = new byte[bgra.Length];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            r[i] = bgra[i + 2];
            r[i + 1] = bgra[i + 1];
            r[i + 2] = bgra[i];
            r[i + 3] = bgra[i + 3];
        }
        return r;
    }
}
