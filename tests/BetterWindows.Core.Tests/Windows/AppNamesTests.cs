using BetterWindows.Core.WindowTracking;

namespace BetterWindows.Core.Tests.WindowTracking;

public class AppNamesTests
{
    [Theory]
    [InlineData(@"C:\Program Files\Foo\WindowsTerminal.exe", "Windows Terminal")]
    [InlineData(@"C:\apps\my_cool-app.exe", "My Cool App")]
    [InlineData(@"C:\apps\XMLParser.exe", "XML Parser")]
    [InlineData(@"C:\apps\VSCodium.exe", "VS Codium")]
    [InlineData(@"C:\apps\iTunes.exe", "iTunes")]
    [InlineData(@"C:\apps\notepad++.exe", "Notepad++")]
    [InlineData(@"C:\apps\sublime_text.exe", "Sublime Text")]
    [InlineData(@"C:\apps\Paint.NET.exe", "Paint.NET")]
    [InlineData(@"C:\apps\foo.EXE", "Foo")]
    [InlineData("tool", "Tool")]
    public void FromExePath_prettifies_the_file_name(string path, string expected) => AppNames.FromExePath(path).Should().Be(expected);

    [Theory]
    [InlineData(@"C:\Windows\explorer.exe", "File Explorer")]
    [InlineData(@"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", "Microsoft Edge")]
    [InlineData(@"C:\Windows\System32\cmd.exe", "Command Prompt")]
    public void FromExePath_knows_common_executables(string path, string expected) => AppNames.FromExePath(path).Should().Be(expected);

    [Theory]
    [InlineData("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", "Windows Calculator")]
    [InlineData("Chrome", "Chrome")]
    [InlineData("com.squirrel.slack.slack", "Slack")]
    [InlineData("  Microsoft.Photos_8wekyb3d8bbwe!App ", "Photos")]
    public void FromAumid_derives_a_readable_name(string aumid, string expected) => AppNames.FromAumid(aumid).Should().Be(expected);

    [Theory]
    [InlineData("  \"C:\\Apps\\Foo.exe\"  ", @"C:\Apps\Foo.exe")]
    [InlineData("C:/Apps/Foo.exe", @"C:\Apps\Foo.exe")]
    [InlineData(@"\\?\C:\Apps\Foo.exe", @"C:\Apps\Foo.exe")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void CleanPath_tolerates_quotes_spaces_slashes_and_the_extended_prefix(string? input, string? expected) => AppNames.CleanPath(input).Should().Be(expected);

    [Fact]
    public void PathKey_is_lower_case() => AppNames.PathKey(@" C:\Apps\FOO.exe ").Should().Be(@"c:\apps\foo.exe");

    [Theory]
    [InlineData(@"C:\a\b\c.exe", "c.exe")]
    [InlineData(@"C:\a\b\", "b")]
    [InlineData("c.exe", "c.exe")]
    public void FileName_splits_on_windows_separators_even_on_linux(string path, string expected) => AppNames.FileName(path).Should().Be(expected);

    [Fact]
    public void IsFile_compares_file_names_case_insensitively() => AppNames.IsFile(@"C:\Windows\EXPLORER.EXE", "explorer.exe").Should().BeTrue();
}
