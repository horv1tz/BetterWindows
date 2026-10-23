namespace BetterWindows.Native.Tests.Apps;

public class AutostartCommandTests
{
    [Fact]
    public void Build_quotes_the_exe_and_appends_the_flag()
        => AutostartCommand.Build(@"C:\Program Files\BetterWindows\BetterWindows.exe").Should().Be("\"C:\\Program Files\\BetterWindows\\BetterWindows.exe\" --autostart");

    [Theory]
    [InlineData(@"C:\Program Files\BW\BetterWindows.exe")]
    [InlineData(@"C:\BW\bw.exe")]
    [InlineData(@"D:\Pfad mit Leerzeichen\ü\bw.exe")]
    public void Extract_inverts_build(string exe)
        => AutostartCommand.ExtractExePath(AutostartCommand.Build(exe)).Should().Be(exe);

    [Theory]
    [InlineData(@"C:\BW\bw.exe --autostart", @"C:\BW\bw.exe")]
    [InlineData(@"C:\BW\bw.exe", @"C:\BW\bw.exe")]
    [InlineData(@"C:\BW\bw.exe /silent", @"C:\BW\bw.exe")]
    [InlineData(@"  C:\BW\bw.exe  ", @"C:\BW\bw.exe")]
    [InlineData(@"C:\Program Files\BW\bw.exe -x", @"C:\Program Files\BW\bw.exe")]
    public void Unquoted_legacy_values_are_understood(string command, string exe)
        => AutostartCommand.ExtractExePath(command).Should().Be(exe);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"unterminated")]
    [InlineData("\"\"")]
    public void Garbage_has_no_exe(string? command) => AutostartCommand.ExtractExePath(command).Should().BeNull();

    [Theory]
    [InlineData(@"C:\BW\bw.exe", @"c:\bw\BW.EXE", true)]
    [InlineData(@"C:\BW\bw.exe", @"C:/BW/bw.exe", true)]
    [InlineData(@"C:\BW\bw.exe", @"C:\BW\bw.exe\", true)]
    [InlineData(@"C:\BW\bw.exe", @"C:\BW\other.exe", false)]
    [InlineData(null, @"C:\BW\bw.exe", false)]
    [InlineData(@"C:\BW\bw.exe", null, false)]
    public void Path_comparison(string? a, string? b, bool same) => AutostartCommand.SamePath(a, b).Should().Be(same);

    [Theory]
    [InlineData(new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 0x03, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8 }, false)]
    [InlineData(new byte[] { 0x07, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8 }, false)]
    [InlineData(new byte[0], true)]
    public void Startup_approved_flag(byte[] data, bool approved) => AutostartCommand.IsApprovedByUser(data).Should().Be(approved);

    [Fact]
    public void Missing_startup_approved_value_means_enabled() => AutostartCommand.IsApprovedByUser(null).Should().BeTrue();
}
