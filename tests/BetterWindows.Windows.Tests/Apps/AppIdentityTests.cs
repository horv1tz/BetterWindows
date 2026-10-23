namespace BetterWindows.Native.Tests.Apps;

public class AppIdentityTests
{
    [Fact]
    public void Ids_follow_the_protocol_rules()
    {
        AppIdentity.IdFromAumid("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App").Should().Be("aumid:Microsoft.WindowsCalculator_8wekyb3d8bbwe!App");
        AppIdentity.IdFromAumid("  Chrome  ").Should().Be("aumid:Chrome");
        AppIdentity.IdFromExePath(@"C:\Program Files\Foo\Foo.EXE").Should().Be(@"exe:c:\program files\foo\foo.exe");
    }

    [Fact]
    public void Parsing_name_for_apps_folder()
    {
        AppIdentity.AppsFolderParsingName("Foo!App").Should().Be(@"shell:AppsFolder\Foo!App");
        AppIdentity.AumidFromAppsFolderParsingName(@"shell:AppsFolder\Foo!App").Should().Be("Foo!App");
        AppIdentity.AumidFromAppsFolderParsingName(@"SHELL:APPSFOLDER\Foo!App").Should().Be("Foo!App");
        AppIdentity.AumidFromAppsFolderParsingName(@"C:\Users\x\Foo.lnk").Should().BeNull();
        AppIdentity.AumidFromAppsFolderParsingName(null).Should().BeNull();
        AppIdentity.AumidFromAppsFolderParsingName(@"shell:AppsFolder\").Should().BeNull();
    }

    [Theory]
    [InlineData("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", true)]
    [InlineData(@"{6D809377-6AF0-444B-8957-A3773F02200E}\Mozilla Firefox\firefox.exe", true)]
    [InlineData("Chrome", true)]
    [InlineData("308046B0AF4A39CB", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    [InlineData(@"C:\Windows\notepad.exe", false)]
    [InlineData("C:/Windows/notepad.exe", false)]
    [InlineData(@"\\server\share\x", false)]
    [InlineData(@"\evil", false)]
    [InlineData("shell:startup", false)]
    [InlineData("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", false)]
    [InlineData(@"..\..\x", false)]
    [InlineData(@"a\..\b", false)]
    [InlineData("a\"b", false)]
    [InlineData("a|b", false)]
    [InlineData("a*b", false)]
    [InlineData("a\nb", false)]
    public void Aumid_validation(string? aumid, bool valid) => AppIdentity.IsValidAumid(aumid).Should().Be(valid);

    [Fact]
    public void Aumid_longer_than_the_limit_is_rejected()
        => AppIdentity.IsValidAumid(new string('a', AppIdentity.MaxAumidLength + 1)).Should().BeFalse();

    [Theory]
    [InlineData(@"C:\Program Files\Foo\foo.exe", @"C:\Program Files\Foo\foo.exe")]
    [InlineData(@"c:\program files\foo\foo.exe", @"C:\program files\foo\foo.exe")]
    [InlineData("C:/Program Files//Foo/./bar/../foo.exe", @"C:\Program Files\Foo\foo.exe")]
    [InlineData("\"C:\\Program Files\\Foo\\foo.exe\"", @"C:\Program Files\Foo\foo.exe")]
    [InlineData(@"  C:\x\y\  ", @"C:\x\y")]
    [InlineData(@"C:\", @"C:\")]
    [InlineData(@"C:\a\..\..\b.exe", @"C:\b.exe")]
    [InlineData(@"C:\dir.\file.exe", @"C:\dir\file.exe")]
    public void Normalize_valid_paths(string input, string expected) => AppIdentity.NormalizeWindowsPath(input).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("notepad.exe")]
    [InlineData(@"..\foo.exe")]
    [InlineData(@"\Windows\notepad.exe")]
    [InlineData(@"\\server\share\foo.exe")]
    [InlineData(@"\\?\C:\foo.exe")]
    [InlineData("C:")]
    [InlineData("C:foo.exe")]
    [InlineData("http://example.com/")]
    [InlineData("C:\\a\u0001b")]
    public void Normalize_rejects_everything_that_is_not_an_absolute_local_path(string? input)
        => AppIdentity.NormalizeWindowsPath(input).Should().BeNull();

    [Fact]
    public void Path_pieces_do_not_depend_on_the_host_os()
    {
        AppIdentity.FileNameOf(@"C:\a\b\c.txt").Should().Be("c.txt");
        AppIdentity.FileNameOf("c.txt").Should().Be("c.txt");
        AppIdentity.ExtensionOf(@"C:\a\b\c.TXT").Should().Be(".txt");
        AppIdentity.ExtensionOf(@"C:\a.b\c").Should().Be("");
        AppIdentity.ExtensionOf(@"C:\a\.gitignore").Should().Be("");
        AppIdentity.ExtensionOf(@"C:\a\file.").Should().Be("");
        AppIdentity.DirectoryOf(@"C:\a\b\c.txt").Should().Be(@"C:\a\b");
        AppIdentity.DirectoryOf(@"C:\c.txt").Should().Be(@"C:\");
        AppIdentity.DirectoryOf(@"C:\").Should().BeNull();
    }

    [Fact]
    public void Packaged_aumids_contain_a_bang()
    {
        AppIdentity.IsPackagedAumid("Pkg_abc!App").Should().BeTrue();
        AppIdentity.IsPackagedAumid("Chrome").Should().BeFalse();
    }
}
