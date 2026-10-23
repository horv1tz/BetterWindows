namespace BetterWindows.Native.Tests.Apps;

public class AppsPngEncoderTests
{
    private static byte[] Gradient(int w, int h)
    {
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                px[i] = (byte)(x * 255 / Math.Max(1, w - 1));      // B
                px[i + 1] = (byte)(y * 255 / Math.Max(1, h - 1));  // G
                px[i + 2] = (byte)((x + y) & 0xFF);                // R
                px[i + 3] = (byte)(255 - (x * 200 / Math.Max(1, w))); // A
            }
        return px;
    }

    [Fact]
    public void Starts_with_the_png_signature_and_reports_its_size()
    {
        var png = AppsPngEncoder.EncodeBgra(Gradient(5, 3), 5, 3);
        png.AsSpan(0, 8).ToArray().Should().Equal(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);
        AppsPngEncoder.TryReadSize(png, out var w, out var h).Should().BeTrue();
        (w, h).Should().Be((5, 3));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 2)]
    [InlineData(16, 16)]
    [InlineData(64, 64)]
    [InlineData(256, 256)]
    [InlineData(7, 300)]
    public void Round_trips_pixels_exactly(int w, int h)
    {
        var bgra = Gradient(w, h);
        var png = AppsPngEncoder.EncodeBgra(bgra, w, h);
        var (dw, dh, rgba) = PngTestDecoder.Decode(png);
        (dw, dh).Should().Be((w, h));
        rgba.Should().Equal(PngTestDecoder.ToRgba(bgra));
    }

    [Fact]
    public void Random_noise_round_trips_through_every_filter_type()
    {
        var rng = new Random(1234);
        var bgra = new byte[40 * 40 * 4];
        rng.NextBytes(bgra);
        var (_, _, rgba) = PngTestDecoder.Decode(AppsPngEncoder.EncodeBgra(bgra, 40, 40));
        rgba.Should().Equal(PngTestDecoder.ToRgba(bgra));
    }

    [Fact]
    public void Transparent_pixels_stay_transparent_and_colours_are_not_swapped()
    {
        // one red opaque pixel (B=0,G=0,R=255,A=255) and one fully transparent pixel
        byte[] bgra = [0, 0, 255, 255, 0, 0, 0, 0];
        var (_, _, rgba) = PngTestDecoder.Decode(AppsPngEncoder.EncodeBgra(bgra, 2, 1));
        rgba.Should().Equal(255, 0, 0, 255, 0, 0, 0, 0);
    }

    [Fact]
    public void Flat_images_compress_well()
    {
        var bgra = new byte[256 * 256 * 4];
        Array.Fill(bgra, (byte)0x80);
        AppsPngEncoder.EncodeBgra(bgra, 256, 256).Length.Should().BeLessThan(2000);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    public void Invalid_dimensions_throw(int w, int h)
    {
        var act = () => AppsPngEncoder.EncodeBgra(new byte[16], w, h);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Too_small_buffer_throws()
    {
        var act = () => AppsPngEncoder.EncodeBgra(new byte[15], 2, 2);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TryReadSize_rejects_garbage()
    {
        AppsPngEncoder.TryReadSize(new byte[] { 1, 2, 3 }, out _, out _).Should().BeFalse();
        AppsPngEncoder.TryReadSize(new byte[40], out _, out _).Should().BeFalse();
        var jpegish = new byte[40];
        jpegish[0] = 0xFF;
        jpegish[1] = 0xD8;
        AppsPngEncoder.TryReadSize(jpegish, out _, out _).Should().BeFalse();
    }
}
