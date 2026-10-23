namespace BetterWindows.Native.Tests.Apps;

public class ShellTargetPolicyTests
{
    private static OpenTargetKind Classify(string? target, out string canonical, bool fileExists = false, bool dirExists = false)
        => ShellTargetPolicy.ClassifyOpenTarget(target, out canonical, _ => fileExists, _ => dirExists);

    [Theory]
    [InlineData("http://example.com", "http://example.com/")]
    [InlineData("https://example.com/a b?q=1&x=ä", "https://example.com/a%20b?q=1&x=%C3%A4")]
    [InlineData("  HTTPS://Example.COM/Path  ", "https://example.com/Path")]
    public void Http_and_https_are_allowed_and_canonicalised(string input, string canonical)
    {
        Classify(input, out var c).Should().Be(OpenTargetKind.Url);
        c.Should().Be(canonical);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("ftp://example.com/x")]
    [InlineData("mailto:a@b.c")]
    [InlineData("data:text/html;base64,AAAA")]
    [InlineData("http:/x")]
    [InlineData("http://")]
    [InlineData("https:///nohost")]
    [InlineData("calculator:")]
    [InlineData("search-ms:query=x")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("shell:startup")]
    [InlineData("shell:Common Startup")]
    [InlineData("shell:AppsFolder\\Foo")]
    [InlineData("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}")]
    [InlineData("cmd.exe")]
    [InlineData("notepad")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Everything_else_is_rejected(string? input)
        => Classify(input, out _, fileExists: true, dirExists: true).Should().Be(OpenTargetKind.Rejected);

    [Theory]
    [InlineData("ms-settings:")]
    [InlineData("ms-settings:network-wifi")]
    [InlineData("ms-settings:display")]
    [InlineData("MS-SETTINGS:privacy-webcam")]
    [InlineData("ms-settings:apps-volume?x=1&y=2")]
    public void Settings_uris_are_allowed(string input)
        => Classify(input, out var c).Should().Be(OpenTargetKind.SettingsUri);

    [Theory]
    [InlineData("ms-settings:foo bar")]
    [InlineData("ms-settings:foo\"bar")]
    [InlineData("ms-settings:foo\\bar")]
    [InlineData("ms-settings:foo;calc")]
    [InlineData("ms-settings:foo|calc")]
    [InlineData("ms-settings:foo`n")]
    public void Settings_uris_with_odd_characters_are_rejected(string input)
        => Classify(input, out _).Should().Be(OpenTargetKind.Rejected);

    [Fact]
    public void Exactly_two_shell_literals_are_accepted()
    {
        Classify("shell:MyComputerFolder", out var c1).Should().Be(OpenTargetKind.ShellSpecial);
        c1.Should().Be("shell:MyComputerFolder");
        Classify("shell:RecycleBinFolder", out _).Should().Be(OpenTargetKind.ShellSpecial);
        Classify("shell:mycomputerfolder", out _).Should().Be(OpenTargetKind.Rejected, "exact literals only");
        Classify("shell:MyComputerFolder\\x", out _).Should().Be(OpenTargetKind.Rejected);
        Classify("shell:Desktop", out _).Should().Be(OpenTargetKind.Rejected);
    }

    [Fact]
    public void Existing_absolute_files_and_folders_are_allowed()
    {
        Classify(@"C:\Users\me\doc.txt", out var c, fileExists: true).Should().Be(OpenTargetKind.File);
        c.Should().Be(@"C:\Users\me\doc.txt");
        Classify("C:/Users/me/../me/doc.txt", out c, fileExists: true).Should().Be(OpenTargetKind.File);
        c.Should().Be(@"C:\Users\me\doc.txt", "dot segments are resolved before the shell sees the path");
        Classify(@"D:\Projects", out c, dirExists: true).Should().Be(OpenTargetKind.Folder);
    }

    [Theory]
    [InlineData(@"C:\missing\file.txt")]
    [InlineData(@"relative\file.txt")]
    [InlineData(@"\\server\share\file.txt")]
    [InlineData(@"\\?\C:\file.txt")]
    [InlineData(@"\\.\pipe\x")]
    [InlineData(@"C:file.txt")]
    public void Missing_relative_unc_and_device_paths_are_rejected(string input)
        => Classify(input, out _, fileExists: input.StartsWith("C:\\missing") ? false : true, dirExists: false).Should().Be(OpenTargetKind.Rejected);

    [Fact]
    public void Control_characters_and_overlong_input_are_rejected()
    {
        Classify("https://exam\nple.com/", out _).Should().Be(OpenTargetKind.Rejected);
        Classify("https://example.com/\0", out _).Should().Be(OpenTargetKind.Rejected);
        Classify("https://example.com/" + new string('a', ShellTargetPolicy.MaxTargetLength), out _).Should().Be(OpenTargetKind.Rejected);
        Classify("C:\\a\tb", out _, fileExists: true).Should().Be(OpenTargetKind.Rejected);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("--flag value \"quoted arg\"", true)]
    [InlineData("a\nb", false)]
    [InlineData("a\0b", false)]
    [InlineData("a\rb", false)]
    public void Argument_safety(string? args, bool safe) => ShellTargetPolicy.AreArgumentsSafe(args).Should().Be(safe);

    [Fact]
    public void Overlong_arguments_are_rejected()
        => ShellTargetPolicy.AreArgumentsSafe(new string('x', ShellTargetPolicy.MaxArgumentsLength + 1)).Should().BeFalse();
}
