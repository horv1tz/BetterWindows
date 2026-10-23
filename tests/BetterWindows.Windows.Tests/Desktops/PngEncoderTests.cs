using System.Buffers.Binary;
using System.IO.Compression;
using BetterWindows.Native;

namespace BetterWindows.Native.Tests.Desktops;

public class PngEncoderTests
{
    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    [Fact]
    public void Crc32_matches_the_published_check_values()
    {
        Crc32.Compute("123456789"u8).Should().Be(0xCBF43926u);
        Crc32.Compute("IEND"u8).Should().Be(0xAE426082u); // the constant CRC of every PNG IEND chunk
        Crc32.Compute(ReadOnlySpan<byte>.Empty).Should().Be(0u);
    }

    [Fact]
    public void Crc32_continuation_equals_one_pass()
    {
        var data = "hello, png world"u8.ToArray();
        var whole = Crc32.Compute(data);
        var split = Crc32.Compute(data.AsSpan(7), Crc32.Compute(data.AsSpan(0, 7)));
        split.Should().Be(whole);
    }

    [Fact]
    public void Encode_produces_a_structurally_valid_png_that_round_trips_the_pixels()
    {
        const int w = 5, h = 3;
        var bgra = new byte[w * h * 4];
        var rnd = new Random(1234);
        rnd.NextBytes(bgra);
        for (int i = 3; i < bgra.Length; i += 4) bgra[i] = 0; // alpha is ignored by the encoder

        var png = PngEncoder.Encode(bgra, w, h);
        var chunks = ParseChunks(png);

        chunks.Select(c => c.Type).Should().Equal("IHDR", "IDAT", "IEND");
        var ihdr = chunks[0].Data;
        BinaryPrimitives.ReadUInt32BigEndian(ihdr.AsSpan(0, 4)).Should().Be(w);
        BinaryPrimitives.ReadUInt32BigEndian(ihdr.AsSpan(4, 4)).Should().Be(h);
        ihdr[8].Should().Be(8);  // bit depth
        ihdr[9].Should().Be(2);  // RGB
        ihdr[10].Should().Be(0);
        ihdr[11].Should().Be(0);
        ihdr[12].Should().Be(0);

        using var inflated = new MemoryStream();
        using (var z = new ZLibStream(new MemoryStream(chunks[1].Data), CompressionMode.Decompress))
        {
            z.CopyTo(inflated);
        }
        var raw = inflated.ToArray();
        raw.Length.Should().Be(h * (1 + w * 3));
        for (int y = 0; y < h; y++)
        {
            raw[y * (1 + w * 3)].Should().Be(0, "filter type None");
            for (int x = 0; x < w; x++)
            {
                int d = y * (1 + w * 3) + 1 + x * 3;
                int s = (y * w + x) * 4;
                raw[d].Should().Be(bgra[s + 2]);
                raw[d + 1].Should().Be(bgra[s + 1]);
                raw[d + 2].Should().Be(bgra[s]);
            }
        }
    }

    [Fact]
    public void Encode_output_starts_with_the_png_signature()
    {
        var png = PngEncoder.Encode(new byte[4], 1, 1);
        png.Take(8).Should().Equal(Signature);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    public void Encode_rejects_non_positive_dimensions(int w, int h)
    {
        Action act = () => PngEncoder.Encode(new byte[16], w, h);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Encode_rejects_short_buffers()
    {
        Action act = () => PngEncoder.Encode(new byte[7], 2, 1);
        act.Should().Throw<ArgumentException>();
    }

    private sealed record Chunk(string Type, byte[] Data);

    /// <summary>Parses the chunk stream and verifies the signature, lengths and every CRC.</summary>
    private static List<Chunk> ParseChunks(byte[] png)
    {
        png.Take(8).Should().Equal(Signature);
        var list = new List<Chunk>();
        int pos = 8;
        while (pos < png.Length)
        {
            uint len = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos, 4));
            string type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
            var data = png.AsSpan(pos + 8, (int)len).ToArray();
            uint storedCrc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos + 8 + (int)len, 4));
            uint computed = Crc32.Compute(png.AsSpan(pos + 4, 4 + (int)len));
            computed.Should().Be(storedCrc, "CRC of chunk " + type);
            list.Add(new Chunk(type, data));
            pos += 12 + (int)len;
        }
        pos.Should().Be(png.Length, "no trailing bytes after IEND");
        return list;
    }
}
