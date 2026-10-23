namespace BetterWindows.Native.Tests.Apps;

public class AppsIconImageTests
{
    private static byte[] Solid(int w, int h, byte b, byte g, byte r, byte a)
    {
        var px = new byte[w * h * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = b; px[i + 1] = g; px[i + 2] = r; px[i + 3] = a; }
        return px;
    }

    private static void Fill(byte[] px, int w, int x0, int y0, int x1, int y1, byte b, byte g, byte r, byte a)
    {
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                int i = (y * w + x) * 4;
                px[i] = b; px[i + 1] = g; px[i + 2] = r; px[i + 3] = a;
            }
    }

    [Fact]
    public void All_zero_alpha_means_no_alpha_channel()
        => AppsIconImage.DetectAlpha(Solid(2, 2, 10, 20, 30, 0)).Should().Be(AppsAlphaKind.None);

    [Fact]
    public void Colours_within_alpha_are_premultiplied()
        => AppsIconImage.DetectAlpha(Solid(2, 2, 50, 60, 70, 128)).Should().Be(AppsAlphaKind.Premultiplied);

    [Fact]
    public void A_colour_far_above_alpha_means_straight_alpha()
        => AppsIconImage.DetectAlpha(Solid(2, 2, 250, 250, 250, 100)).Should().Be(AppsAlphaKind.Straight);

    [Fact]
    public void Rounding_noise_does_not_flip_the_detection()
        => AppsIconImage.DetectAlpha(Solid(2, 2, 102, 101, 100, 100)).Should().Be(AppsAlphaKind.Premultiplied);

    [Fact]
    public void Fully_opaque_images_are_premultiplied_by_definition()
        => AppsIconImage.DetectAlpha(Solid(2, 2, 255, 255, 255, 255)).Should().Be(AppsAlphaKind.Premultiplied);

    [Fact]
    public void No_alpha_bitmaps_become_opaque()
    {
        var px = Solid(2, 1, 10, 20, 30, 0);
        AppsIconImage.NormalizeToPremultiplied(px, AppsAlphaKind.None);
        px.Where((_, i) => i % 4 == 3).Should().OnlyContain(a => a == 255);
        px[0].Should().Be(10);
    }

    [Fact]
    public void Straight_alpha_is_converted_to_premultiplied()
    {
        var px = Solid(1, 1, 200, 100, 50, 128);
        AppsIconImage.NormalizeToPremultiplied(px, AppsAlphaKind.Straight);
        px.Should().Equal((byte)((200 * 128 + 127) / 255), (byte)((100 * 128 + 127) / 255), (byte)((50 * 128 + 127) / 255), 128);
    }

    [Fact]
    public void Premultiply_then_unpremultiply_is_close_to_identity()
    {
        var original = Solid(1, 1, 200, 100, 50, 200);
        var px = (byte[])original.Clone();
        AppsIconImage.Premultiply(px);
        AppsIconImage.Unpremultiply(px);
        for (int i = 0; i < 3; i++) Math.Abs(px[i] - original[i]).Should().BeLessThanOrEqualTo(2);
        px[3].Should().Be(200);
    }

    [Fact]
    public void Unpremultiply_clears_colour_of_fully_transparent_pixels_and_keeps_opaque_ones()
    {
        byte[] px = [9, 9, 9, 0, 1, 2, 3, 255];
        AppsIconImage.Unpremultiply(px);
        px.Should().Equal(0, 0, 0, 0, 1, 2, 3, 255);
    }

    [Fact]
    public void Box_downscale_averages_blocks()
    {
        // 4x4: left half black opaque, right half white opaque -> 2x2 same split
        var px = new byte[4 * 4 * 4];
        Fill(px, 4, 0, 0, 2, 4, 0, 0, 0, 255);
        Fill(px, 4, 2, 0, 4, 4, 255, 255, 255, 255);
        var small = AppsIconImage.ResizeBoxPremultiplied(px, 4, 4, 2, 2, out var w, out var h);
        (w, h).Should().Be((2, 2));
        small.Should().Equal(0, 0, 0, 255, 255, 255, 255, 255, 0, 0, 0, 255, 255, 255, 255, 255);
    }

    [Fact]
    public void Box_downscale_of_a_checkerboard_gives_mid_grey()
    {
        var px = new byte[2 * 2 * 4];
        Fill(px, 2, 0, 0, 1, 1, 255, 255, 255, 255);
        Fill(px, 2, 1, 1, 2, 2, 255, 255, 255, 255);
        Fill(px, 2, 1, 0, 2, 1, 0, 0, 0, 255);
        Fill(px, 2, 0, 1, 1, 2, 0, 0, 0, 255);
        var small = AppsIconImage.ResizeBoxPremultiplied(px, 2, 2, 1, 1, out _, out _);
        small[0].Should().BeInRange(127, 128);
        small[3].Should().Be(255);
    }

    [Fact]
    public void Box_downscale_with_non_integer_ratio_keeps_flat_colour_flat()
    {
        var px = Solid(10, 10, 40, 80, 120, 255);
        var small = AppsIconImage.ResizeBoxPremultiplied(px, 10, 10, 3, 3, out var w, out var h);
        (w, h).Should().Be((3, 3));
        for (int i = 0; i < small.Length; i += 4)
        {
            small[i].Should().Be(40);
            small[i + 1].Should().Be(80);
            small[i + 2].Should().Be(120);
            small[i + 3].Should().Be(255);
        }
    }

    [Fact]
    public void Downscale_averages_in_premultiplied_space_so_transparent_black_does_not_darken()
    {
        // left pixel: opaque white; right pixel: fully transparent (premultiplied 0,0,0,0)
        byte[] px = [255, 255, 255, 255, 0, 0, 0, 0];
        var small = AppsIconImage.ResizeBoxPremultiplied(px, 2, 1, 1, 1, out _, out _);
        small.Should().Equal(128, 128, 128, 128); // premultiplied half-transparent white
        AppsIconImage.Unpremultiply(small);
        small.Should().Equal(255, 255, 255, 128);
    }

    [Fact]
    public void Resize_never_enlarges()
    {
        var px = Solid(4, 4, 1, 2, 3, 255);
        AppsIconImage.ResizeBoxPremultiplied(px, 4, 4, 8, 8, out var w, out var h).Should().BeSameAs(px);
        (w, h).Should().Be((4, 4));
        AppsIconImage.ResizeBoxPremultiplied(px, 4, 4, 4, 8, out w, out h).Should().BeSameAs(px, "a taller target is an enlargement");
        AppsIconImage.ResizeBoxPremultiplied(px, 4, 4, 4, 4, out w, out h).Should().BeSameAs(px);
        (w, h).Should().Be((4, 4));
        AppsIconImage.ResizeBoxPremultiplied(px, 4, 4, 4, 2, out w, out h).Should().NotBeSameAs(px, "shrinking along one axis is allowed");
        (w, h).Should().Be((4, 2));
    }

    [Fact]
    public void Crop_copies_the_rectangle()
    {
        var px = new byte[4 * 4 * 4];
        Fill(px, 4, 1, 1, 3, 3, 9, 8, 7, 255);
        var c = AppsIconImage.Crop(px, 4, 1, 1, 2, 2);
        c.Should().Equal(Enumerable.Repeat<byte>(0, 0).Concat(new byte[] { 9, 8, 7, 255, 9, 8, 7, 255, 9, 8, 7, 255, 9, 8, 7, 255 }));
    }

    private static byte[] PlateOnTransparent(int size, int pad, bool roundedCorners = false)
    {
        var px = new byte[size * size * 4]; // transparent
        Fill(px, size, pad, pad, size - pad, size - pad, 200, 100, 20, 255); // blue-ish plate (BGR order)
        // a white logo in the middle
        Fill(px, size, size / 2 - 4, size / 2 - 4, size / 2 + 4, size / 2 + 4, 255, 255, 255, 255);
        if (roundedCorners)
        {
            // clear the corner pixels of the plate
            foreach (var (x, y) in new[] { (pad, pad), (size - pad - 1, pad), (pad, size - pad - 1), (size - pad - 1, size - pad - 1) })
                Fill(px, size, x, y, x + 1, y + 1, 0, 0, 0, 0);
        }
        return px;
    }

    [Fact]
    public void A_flat_plate_with_transparent_padding_is_found()
    {
        var px = PlateOnTransparent(64, 8);
        AppsIconImage.TryFindFlatPlate(px, 64, 64, out var x, out var y, out var w, out var h).Should().BeTrue();
        (x, y, w, h).Should().Be((8, 8, 48, 48));
    }

    [Fact]
    public void A_plate_with_rounded_corners_is_still_a_plate()
    {
        var px = PlateOnTransparent(64, 8, roundedCorners: true);
        AppsIconImage.TryFindFlatPlate(px, 64, 64, out var x, out var y, out var w, out var h).Should().BeTrue();
        (x, y, w, h).Should().Be((8, 8, 48, 48));
    }

    [Fact]
    public void An_image_that_fills_its_canvas_is_not_trimmed()
    {
        var px = Solid(64, 64, 200, 100, 20, 255);
        AppsIconImage.TryFindFlatPlate(px, 64, 64, out _, out _, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void A_logo_on_a_transparent_background_is_not_a_plate()
    {
        // a multi-coloured glyph: edges of its bounding box are not one flat opaque colour
        var px = new byte[64 * 64 * 4];
        for (int y = 16; y < 48; y++)
            for (int x = 16; x < 48; x++)
            {
                bool circle = (x - 32) * (x - 32) + (y - 32) * (y - 32) < 15 * 15;
                if (circle) Fill(px, 64, x, y, x + 1, y + 1, (byte)(x * 5), (byte)(y * 5), 40, 255);
            }
        AppsIconImage.TryFindFlatPlate(px, 64, 64, out _, out _, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void A_non_square_opaque_block_is_not_a_plate()
    {
        var px = new byte[64 * 64 * 4];
        Fill(px, 64, 4, 20, 60, 44, 200, 100, 20, 255);
        AppsIconImage.TryFindFlatPlate(px, 64, 64, out _, out _, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void A_gradient_square_is_not_a_flat_plate()
    {
        var px = new byte[64 * 64 * 4];
        for (int y = 8; y < 56; y++)
            for (int x = 8; x < 56; x++)
                Fill(px, 64, x, y, x + 1, y + 1, (byte)(x * 4), 50, 50, 255);
        AppsIconImage.TryFindFlatPlate(px, 64, 64, out _, out _, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void Empty_and_tiny_images_are_not_plates()
    {
        AppsIconImage.TryFindFlatPlate(new byte[64 * 64 * 4], 64, 64, out _, out _, out _, out _).Should().BeFalse();
        AppsIconImage.TryFindFlatPlate(Solid(8, 8, 1, 1, 1, 255), 8, 8, out _, out _, out _, out _).Should().BeFalse();
    }
}
