using BetterWindows.Core.Services;

namespace BetterWindows.Core.Tests.Services;

public class InputPolicyTests
{
    [Theory]
    [InlineData(@"C:\Users\me\file.txt", @"C:\Users\me\file.txt")]
    [InlineData(@"c:/Users/me/a/../b.txt", @"C:\Users\me\b.txt")]
    [InlineData(@"D:\a\.\b", @"D:\a\b")]
    [InlineData(@"C:\", @"C:\")]
    [InlineData(@"C:\Program Files (x86)\App\app.exe", @"C:\Program Files (x86)\App\app.exe")]
    public void Valid_local_paths_are_canonicalised(string input, string expected)
    {
        InputPolicy.TryNormalizeLocalPath(input, out var p).Should().BeTrue();
        p.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"\\server\share\x")]
    [InlineData("//server/share/x")]
    [InlineData(@"\\?\C:\x")]
    [InlineData(@"\\.\PhysicalDrive0")]
    [InlineData(@"relative\path")]
    [InlineData(@"C:relative")]
    [InlineData(@"C:\a\..\..\b")]
    [InlineData(@"C:\file.txt:stream")]
    [InlineData(@"C:\dir\*.exe")]
    [InlineData(@"C:\dir\a?.exe")]
    [InlineData("C:\\dir\\a\u0001b")]
    [InlineData(@"C:\CON")]
    [InlineData(@"C:\dir\nul.txt")]
    [InlineData(@"C:\dir\COM1")]
    [InlineData(@"C:\a<b")]
    [InlineData(@"C:\a|b")]
    [InlineData("file:///C:/x")]
    [InlineData("http://example.com/x")]
    public void Dangerous_or_non_local_paths_are_rejected(string? input)
    {
        InputPolicy.TryNormalizeLocalPath(input, out _).Should().BeFalse();
    }

    [Fact]
    public void Absurdly_long_paths_are_rejected()
    {
        InputPolicy.TryNormalizeLocalPath(@"C:\" + new string('a', 40000), out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("https://example.com/a?b=1", true)]
    [InlineData("http://example.com", true)]
    [InlineData("HTTPS://EXAMPLE.COM", true)]
    [InlineData("ftp://example.com", false)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("ms-msdt:/id PCWDiagnostic", false)]
    [InlineData("search-ms:query=x", false)]
    [InlineData("https://user:pass@example.com", false)]
    [InlineData("https:///nohost", false)]
    [InlineData("https://example.com/\u0001", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Web_url_policy(string? input, bool ok) => InputPolicy.IsSafeWebUrl(input, out _).Should().Be(ok);

    [Fact]
    public void Web_url_length_is_capped() => InputPolicy.IsSafeWebUrl("https://example.com/" + new string('a', 3000), out _).Should().BeFalse();

    [Theory]
    [InlineData("ms-settings:display", true)]
    [InlineData("ms-settings:network-wifi", true)]
    [InlineData("ms-settings:privacy-webcam?x=1", true)]
    [InlineData("ms-settings:", false)]
    [InlineData("ms-settings:../x", false)]
    [InlineData("MS-SETTINGS:display", false)]
    [InlineData("ms-settings:display;calc", false)]
    [InlineData("ms-settings:display\n", false)]
    [InlineData("shell:startup", false)]
    [InlineData(null, false)]
    public void Ms_settings_policy(string? input, bool ok) => InputPolicy.IsMsSettingsUri(input).Should().Be(ok);

    [Theory]
    [InlineData("aumid:Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", AppIdKind.Aumid, "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App")]
    [InlineData(@"exe:c:\windows\system32\notepad.exe", AppIdKind.ExePath, @"C:\windows\system32\notepad.exe")]
    [InlineData(@"dir:C:\Users\me\Downloads", AppIdKind.Directory, @"C:\Users\me\Downloads")]
    public void App_ids_parse(string id, AppIdKind kind, string value)
    {
        InputPolicy.TryParseAppId(id, out var k, out var v).Should().BeTrue();
        k.Should().Be(kind);
        v.Should().Be(value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("aumid:")]
    [InlineData("exe:\\\\server\\share\\x.exe")]
    [InlineData("exe:relative.exe")]
    [InlineData("dir:\\\\?\\C:\\x")]
    [InlineData("foo:bar")]
    [InlineData("exe:C:\\a\u0002")]
    public void Bad_app_ids_are_rejected(string id) => InputPolicy.TryParseAppId(id, out _, out _).Should().BeFalse();

    [Fact]
    public void Truncate_cuts_and_removes_control_characters()
    {
        InputPolicy.Truncate("ab\ncd\re", 4).Should().Be("ab c");
        InputPolicy.Truncate(null, 5).Should().Be("");
    }

    [Theory]
    [InlineData(5, 0, 10, 1, 5)]
    [InlineData(double.NaN, 0, 10, 1, 1)]
    [InlineData(double.PositiveInfinity, 0, 10, 1, 1)]
    [InlineData(11, 0, 10, 1, 1)]
    public void FiniteOr_never_lets_garbage_through(double v, double min, double max, double fb, double expected) =>
        InputPolicy.FiniteOr(v, min, max, fb).Should().Be(expected);
}
