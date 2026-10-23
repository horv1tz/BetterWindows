using BetterWindows.Core.Protocol;
using BetterWindows.Core.Resources;

namespace BetterWindows.Core.Tests.Resources;

public class DynRequestParserTests
{
    private static DynRequest Parse(string url)
    {
        DynRequestParser.TryParse(url, out var r).Should().BeTrue(url);
        return r;
    }

    private static void Rejected(string url) => DynRequestParser.TryParse(url, out _).Should().BeFalse(url);

    [Theory]
    [InlineData("aumid:Microsoft.WindowsTerminal_8wekyb3d8bbwe!App")]
    [InlineData(@"exe:c:\program files\app\app.exe")]
    [InlineData("aumid:A B&C=D?E#F/G")]
    [InlineData("aumid:Ünïcode-приложение")]
    public void Every_url_built_by_DynUrls_parses_back_to_the_same_key(string appId)
    {
        var app = (AppIconRequest)Parse(DynUrls.AppIcon(appId, 64));
        app.AppId.Should().Be(appId);
        app.Size.Should().Be(64);
        ((PathIconRequest)Parse(DynUrls.PathIcon(@"C:\Users\me\My Docs\a&b#1.txt", 256))).Path.Should().Be(@"C:\Users\me\My Docs\a&b#1.txt");
        ((WindowIconRequest)Parse(DynUrls.WindowIcon(197890, 32))).Handle.Should().Be(197890);
        var thumb = (ThumbRequest)Parse(DynUrls.WindowThumb(12345, 480, 7));
        (thumb.Handle, thumb.Width, thumb.Rev).Should().Be((12345L, 480, "7"));
        ((ArtRequest)Parse(DynUrls.MediaArt(42))).Serial.Should().Be(42);
        var wp = (WallpaperRequest)Parse(DynUrls.Wallpaper("m1a2b3c4d", 3));
        (wp.MonitorId, wp.Rev).Should().Be(("m1a2b3c4d", "3"));
        ((TrayIconRequest)Parse(DynUrls.TrayIcon("app/tray:1", 9))).Id.Should().Be("app/tray:1");
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("/dyn")]
    [InlineData("/dyn/")]
    [InlineData("/dyn/icon")]
    [InlineData("/dyn/icon/app")]
    [InlineData("/dyn/icon/app/")]
    [InlineData("/dyn/icon/nope/x")]
    [InlineData("/dyn/icon/app/x/y")]
    [InlineData("/dyn/icon/window/x/y")]
    [InlineData("/dyn/icon//app/x")]
    [InlineData("/dyn/nope/1")]
    [InlineData("/dyn/thumb")]
    [InlineData("/dyn/thumb/1/2")]
    [InlineData("/dyn/art")]
    [InlineData("/dyn/art/1/2")]
    [InlineData("/dyn/wallpaper")]
    [InlineData("/dyn/tray/a/b")]
    [InlineData("/other/icon/app/x")]
    [InlineData("dyn/icon/app/x")]
    [InlineData("https://app.local/dyn/icon/app/x")]
    [InlineData("/DYN/icon/app/x")]
    [InlineData("/dyn/Icon/app/x")]
    [InlineData("/dyn/icon/app/..")]
    [InlineData("/dyn/icon/app/%2E%2E")]
    [InlineData("/dyn/icon/path/..")]
    [InlineData("/dyn/icon/app/x#frag")]
    [InlineData("/dyn/icon/path/C:\\Windows\\x.exe")]
    [InlineData("/dyn/icon/app/a b")]
    [InlineData("/dyn/icon/app/a%0Ab")]
    [InlineData("/dyn/icon/app/a%00b")]
    [InlineData("/dyn/icon/app/a\tb")]
    public void Anything_that_is_not_one_of_the_documented_shapes_is_unknown(string url) => Rejected(url);

    [Fact]
    public void Absurdly_long_urls_are_rejected()
    {
        Rejected("/dyn/icon/app/" + new string('a', 9000));
        Rejected("/dyn/icon/app/" + new string('a', 4300));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("+5")]
    [InlineData("abc")]
    [InlineData("1e3")]
    [InlineData("1%202")]
    [InlineData("99999999999999999999")]
    [InlineData("12345678901234567890")]
    public void Window_handles_are_plain_positive_decimal_numbers(string handle)
    {
        Rejected($"/dyn/thumb/{handle}?w=480");
        Rejected($"/dyn/icon/window/{handle}?s=64");
    }

    [Fact]
    public void Art_serials_are_plain_non_negative_ints()
    {
        Rejected("/dyn/art/-1");
        Rejected("/dyn/art/x");
        Rejected("/dyn/art/99999999999");
        Parse("/dyn/art/0").Should().BeOfType<ArtRequest>();
    }

    [Theory]
    [InlineData("", 128)]
    [InlineData("?s=1", 32)]
    [InlineData("?s=32", 32)]
    [InlineData("?s=33", 64)]
    [InlineData("?s=64", 64)]
    [InlineData("?s=100", 128)]
    [InlineData("?s=129", 256)]
    [InlineData("?s=192", 256)]
    [InlineData("?s=257", 512)]
    [InlineData("?s=512", 512)]
    [InlineData("?s=100000", 512)]
    [InlineData("?s=99999999999999999999999", 512)]
    [InlineData("?s=-5", 32)]
    [InlineData("?s=0", 32)]
    [InlineData("?s=abc", 128)]
    [InlineData("?s=", 128)]
    [InlineData("?s=64&s=256", 64)]
    [InlineData("?x=1&s=64&y=2", 64)]
    public void Icon_sizes_round_up_to_buckets(string query, int expected) => ((AppIconRequest)Parse("/dyn/icon/app/aumid%3AX" + query)).Size.Should().Be(expected);

    [Theory]
    [InlineData("", 480)]
    [InlineData("?w=1", 160)]
    [InlineData("?w=160", 160)]
    [InlineData("?w=161", 240)]
    [InlineData("?w=300", 320)]
    [InlineData("?w=481", 640)]
    [InlineData("?w=700", 960)]
    [InlineData("?w=960", 960)]
    [InlineData("?w=100000", 960)]
    [InlineData("?w=999999999999999999999", 960)]
    [InlineData("?w=-1", 160)]
    [InlineData("?w=NaN", 480)]
    public void Thumbnail_widths_round_up_to_buckets(string query, int expected) => ((ThumbRequest)Parse("/dyn/thumb/42" + query)).Width.Should().Be(expected);

    [Theory]
    [InlineData("?rev=12", "12")]
    [InlineData("?rev=abc-DEF_9", "abc-DEF_9")]
    [InlineData("?rev=", null)]
    [InlineData("?rev=a/b", null)]
    [InlineData("?rev=a%20b", null)]
    [InlineData("?rev=" + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", null)]
    [InlineData("?other=1", null)]
    public void The_rev_parameter_is_a_short_token_or_ignored(string query, string? expected) => Parse("/dyn/thumb/42" + query).Rev.Should().Be(expected);

    [Fact]
    public void Percent_decoding_happens_exactly_once()
    {
        ((AppIconRequest)Parse("/dyn/icon/app/aumid%253AX")).AppId.Should().Be("aumid%3AX");
        ((PathIconRequest)Parse("/dyn/icon/path/C%3A%5C%252E%252E")).Path.Should().Be(@"C:\%2E%2E");
    }

    [Fact]
    public void Unknown_query_keys_are_ignored_and_the_first_duplicate_wins()
    {
        var t = (ThumbRequest)Parse("/dyn/thumb/5?evil=../../x&w=240&w=960&rev=1&rev=2");
        (t.Width, t.Rev).Should().Be((240, "1"));
    }
}
