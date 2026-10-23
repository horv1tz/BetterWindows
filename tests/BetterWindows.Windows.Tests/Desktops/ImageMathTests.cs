using BetterWindows.Native;

namespace BetterWindows.Native.Tests.Desktops;

public class ImageMathTests
{
    [Theory]
    [InlineData(1920, 1080, 256, 256, 144)]
    [InlineData(1000, 1000, 256, 256, 256)]
    [InlineData(100, 50, 256, 100, 50)]        // never upscales
    [InlineData(3, 1000, 2, 2, 667)]           // rounding: 1000*2/3 = 666.67 -> 667
    [InlineData(5000, 1, 100, 100, 1)]         // never collapses to zero height
    public void FitSize_keeps_aspect_and_never_upscales(int w, int h, int max, int ew, int eh)
    {
        ImageMath.FitSize(w, h, max).Should().Be((ew, eh));
    }

    [Theory]
    [InlineData(0, 10, 10)]
    [InlineData(10, 0, 10)]
    [InlineData(10, 10, 0)]
    [InlineData(-5, 10, 10)]
    public void FitSize_invalid_input_is_zero(int w, int h, int max)
    {
        ImageMath.FitSize(w, h, max).Should().Be((0, 0));
    }

    [Fact]
    public void FitSize_handles_very_large_windows_without_overflow()
    {
        var (w, h) = ImageMath.FitSize(int.MaxValue, int.MaxValue, 256);
        w.Should().Be(256);
        h.Should().Be(256);
    }

    [Fact]
    public void IsValidSource_enforces_edge_and_pixel_caps()
    {
        ImageMath.IsValidSource(1920, 1080, 8192, 16_000_000).Should().BeTrue();
        ImageMath.IsValidSource(0, 1080, 8192, 16_000_000).Should().BeFalse();
        ImageMath.IsValidSource(8193, 10, 8192, 16_000_000).Should().BeFalse();
        ImageMath.IsValidSource(5000, 5000, 8192, 16_000_000).Should().BeFalse();
    }

    [Fact]
    public void IsAllBlack_ignores_alpha_and_finds_a_single_lit_pixel()
    {
        var black = new byte[4 * 100];
        for (int i = 3; i < black.Length; i += 4) black[i] = 0; // alpha undefined
        ImageMath.IsAllBlack(black).Should().BeTrue();

        black[4 * 57 + 1] = 1; // one green channel value in the middle
        ImageMath.IsAllBlack(black).Should().BeFalse();

        var alphaOnly = new byte[8];
        alphaOnly[3] = 255;
        alphaOnly[7] = 255;
        ImageMath.IsAllBlack(alphaOnly).Should().BeTrue();
    }

    [Fact]
    public void SetOpaque_writes_alpha_only()
    {
        var px = new byte[] { 1, 2, 3, 0, 4, 5, 6, 9 };
        ImageMath.SetOpaque(px);
        px.Should().Equal(1, 2, 3, 255, 4, 5, 6, 255);
    }

    [Fact]
    public void Crop_copies_the_requested_rectangle()
    {
        // 3x2 image, pixel value = its index in the first byte.
        var src = new byte[3 * 2 * 4];
        for (int p = 0; p < 6; p++) src[p * 4] = (byte)p;
        var crop = ImageMath.Crop(src, 3, 2, 1, 0, 2, 2);
        crop.Length.Should().Be(2 * 2 * 4);
        crop[0].Should().Be(1);
        crop[4].Should().Be(2);
        crop[8].Should().Be(4);
        crop[12].Should().Be(5);
    }

    [Theory]
    [InlineData(0, 0, 4, 2)]   // too wide
    [InlineData(-1, 0, 1, 1)]
    [InlineData(0, 0, 0, 1)]
    [InlineData(2, 1, 2, 1)]
    public void Crop_rejects_rectangles_outside_the_source(int x, int y, int w, int h)
    {
        var src = new byte[3 * 2 * 4];
        Action act = () => ImageMath.Crop(src, 3, 2, x, y, w, h);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Downscale_uniform_colour_stays_uniform()
    {
        var src = new byte[8 * 8 * 4];
        for (int p = 0; p < 64; p++) { src[p * 4] = 10; src[p * 4 + 1] = 200; src[p * 4 + 2] = 99; src[p * 4 + 3] = 0; }
        var dst = ImageMath.Downscale(src, 8, 8, 2, 2);
        for (int p = 0; p < 4; p++)
        {
            dst[p * 4].Should().Be(10);
            dst[p * 4 + 1].Should().Be(200);
            dst[p * 4 + 2].Should().Be(99);
            dst[p * 4 + 3].Should().Be(255);
        }
    }

    [Fact]
    public void Downscale_2x2_to_1x1_is_the_rounded_mean()
    {
        // Blue channel values 0, 0, 0, 255 -> mean 63.75 -> 64 (rounded), red 255,255,255,255 -> 255.
        byte[] src =
        {
            0, 0, 255, 0,   0, 0, 255, 0,
            0, 0, 255, 0,   255, 0, 255, 0,
        };
        var dst = ImageMath.Downscale(src, 2, 2, 1, 1);
        dst[0].Should().Be(64);
        dst[2].Should().Be(255);
        dst[3].Should().Be(255);
    }

    [Fact]
    public void Downscale_4x1_to_2x1_averages_adjacent_pairs()
    {
        byte[] src = { 0, 0, 0, 0,  100, 0, 0, 0,  200, 0, 0, 0,  50, 0, 0, 0 };
        var dst = ImageMath.Downscale(src, 4, 1, 2, 1);
        dst[0].Should().Be(50);   // (0+100)/2
        dst[4].Should().Be(125);  // (200+50)/2
    }

    [Fact]
    public void Downscale_rejects_short_buffers_and_bad_sizes()
    {
        Action short_ = () => ImageMath.Downscale(new byte[10], 2, 2, 1, 1);
        short_.Should().Throw<ArgumentException>();
        Action zero = () => ImageMath.Downscale(new byte[16], 2, 2, 0, 1);
        zero.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Downscale_is_exact_for_large_block_sums()
    {
        // 1000x1000 white block to 1x1: sum would overflow 32 bits without 64-bit accumulation (1e6 * 255 = 2.55e8 fits, but use 2000x2000 for margin).
        var src = Enumerable.Repeat((byte)254, 2000 * 2000 * 4).ToArray();
        var dst = ImageMath.Downscale(src, 2000, 2000, 1, 1);
        dst[0].Should().Be(254);
    }
}
