namespace BetterWindows.Native.Tests.Apps;

/// <summary>The pure part of the icon pipeline (raw shell bitmap -> PNG) and the small policy helpers of <see cref="ShellIconSource"/>.</summary>
public class IconPipelineTests
{
    [Fact]
    public void Premultiplied_shell_bitmap_becomes_a_straight_alpha_png()
    {
        // 2x1: opaque red, and 50% transparent red in PREMULTIPLIED form (B,G,R,A = 0,0,128,128)
        byte[] raw = [0, 0, 255, 255, 0, 0, 128, 128];
        var png = ShellIconSource.Finish(raw, 2, 1, 64, alreadyPremultiplied: null, trimPlate: false);
        var (w, h, rgba) = PngTestDecoder.Decode(png!);
        (w, h).Should().Be((2, 1));
        rgba.Should().Equal(255, 0, 0, 255, 255, 0, 0, 128);
    }

    [Fact]
    public void No_alpha_channel_bitmap_comes_out_opaque()
    {
        byte[] raw = [10, 20, 30, 0, 40, 50, 60, 0];
        var (_, _, rgba) = PngTestDecoder.Decode(ShellIconSource.Finish(raw, 2, 1, 64, null, false)!);
        rgba.Should().Equal(30, 20, 10, 255, 60, 50, 40, 255);
    }

    [Fact]
    public void Oversized_bitmaps_are_scaled_down_to_the_requested_size_keeping_aspect()
    {
        var raw = new byte[256 * 128 * 4];
        for (int i = 0; i < raw.Length; i += 4) { raw[i] = 100; raw[i + 1] = 100; raw[i + 2] = 100; raw[i + 3] = 255; }
        var png = ShellIconSource.Finish(raw, 256, 128, 64, null, false);
        AppsPngEncoder.TryReadSize(png!, out var w, out var h).Should().BeTrue();
        (w, h).Should().Be((64, 32));
    }

    [Fact]
    public void Small_bitmaps_are_not_upscaled()
    {
        var raw = new byte[32 * 32 * 4];
        Array.Fill(raw, (byte)255);
        AppsPngEncoder.TryReadSize(ShellIconSource.Finish(raw, 32, 32, 256, null, false)!, out var w, out var h).Should().BeTrue();
        (w, h).Should().Be((32, 32));
    }

    [Fact]
    public void Plate_trimming_crops_transparent_padding_around_a_flat_plate_only_when_asked()
    {
        var raw = new byte[64 * 64 * 4];
        for (int y = 8; y < 56; y++)
            for (int x = 8; x < 56; x++)
            {
                int i = (y * 64 + x) * 4;
                raw[i] = 200; raw[i + 1] = 100; raw[i + 2] = 20; raw[i + 3] = 255;
            }
        AppsPngEncoder.TryReadSize(ShellIconSource.Finish((byte[])raw.Clone(), 64, 64, 256, null, trimPlate: true)!, out var w, out var h).Should().BeTrue();
        (w, h).Should().Be((48, 48));
        AppsPngEncoder.TryReadSize(ShellIconSource.Finish((byte[])raw.Clone(), 64, 64, 256, null, trimPlate: false)!, out w, out h).Should().BeTrue();
        (w, h).Should().Be((64, 64));
    }

    [Fact]
    public void Garbage_input_yields_null_not_an_exception()
    {
        ShellIconSource.Finish([], 0, 0, 64, null, false).Should().BeNull();
        ShellIconSource.Finish(new byte[4], 2, 2, 64, null, false).Should().BeNull();
    }

    [Theory]
    [InlineData(@"C:\pics\a.PNG", true)]
    [InlineData(@"C:\pics\a.jpg", true)]
    [InlineData(@"C:\pics\a.jpeg", true)]
    [InlineData(@"C:\pics\a.bmp", true)]
    [InlineData(@"C:\pics\a.gif", true)]
    [InlineData(@"C:\pics\a.webp", true)]
    [InlineData(@"C:\pics\a.tif", true)]
    [InlineData(@"C:\pics\a.tiff", true)]
    [InlineData(@"C:\pics\a.svg", false)]
    [InlineData(@"C:\docs\a.docx", false)]
    [InlineData(@"C:\docs\png", false)]
    public void Image_file_detection(string path, bool image) => ShellIconSource.IsImageFile(path).Should().Be(image);

    [Fact]
    public void Flags_are_icon_only_except_for_image_thumbnails_at_preview_sizes()
    {
        const uint iconOnly = 0x4, biggerOk = 0x1;
        ShellIconSource.IconFlags(64, isImage: false).Should().Be(iconOnly | biggerOk);
        ShellIconSource.IconFlags(64, isImage: true).Should().Be(iconOnly | biggerOk, "small sizes are icon rows, not previews");
        ShellIconSource.IconFlags(191, isImage: true).Should().Be(iconOnly | biggerOk);
        ShellIconSource.IconFlags(192, isImage: true).Should().Be(biggerOk, "no SIIGBF_ICONONLY: a real thumbnail");
        ShellIconSource.IconFlags(256, isImage: true).Should().Be(biggerOk);
        ShellIconSource.IconFlags(256, isImage: false).Should().Be(iconOnly | biggerOk);
    }

    [Fact]
    public void Cache_keys_distinguish_kind_and_size_and_skip_windows()
    {
        ShellIconSource.CacheKey(new Core.Platform.IconKey { Aumid = "A!B" }, 64).Should().NotBe(ShellIconSource.CacheKey(new Core.Platform.IconKey { Aumid = "A!B" }, 128));
        ShellIconSource.CacheKey(new Core.Platform.IconKey { Path = @"C:\A.exe" }, 64).Should().Be(ShellIconSource.CacheKey(new Core.Platform.IconKey { Path = @"c:\a.EXE" }, 64));
        ShellIconSource.CacheKey(new Core.Platform.IconKey { WindowHandle = 42 }, 64).Should().BeNull();
        ShellIconSource.CacheKey(new Core.Platform.IconKey { Path = "shell:RecycleBinFolder" }, 64).Should().BeNull("the bin icon follows its content");
        ShellIconSource.CacheKey(new Core.Platform.IconKey(), 64).Should().BeNull();
    }
}
