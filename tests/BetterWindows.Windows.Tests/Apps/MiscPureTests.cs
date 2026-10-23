using BetterWindows.Core.Protocol;

namespace BetterWindows.Native.Tests.Apps;

public class MiscPureTests
{
    [Fact]
    public void Wallpaper_paths_must_be_absolute_existing_image_files()
    {
        DesktopWallpaperService.ValidateImagePath(@"C:\pics\a.jpg", _ => true).Should().Be(@"C:\pics\a.jpg");
        DesktopWallpaperService.ValidateImagePath("C:/pics/../pics/A.PNG", _ => true).Should().Be(@"C:\pics\A.PNG");
        DesktopWallpaperService.ValidateImagePath(@"C:\pics\a.jpg", _ => false).Should().BeNull("must exist");
        DesktopWallpaperService.ValidateImagePath(@"C:\pics\a.exe", _ => true).Should().BeNull("must be an image");
        DesktopWallpaperService.ValidateImagePath(@"C:\pics\noext", _ => true).Should().BeNull();
        DesktopWallpaperService.ValidateImagePath(@"pics\a.jpg", _ => true).Should().BeNull("must be absolute");
        DesktopWallpaperService.ValidateImagePath(@"\\server\share\a.jpg", _ => true).Should().BeNull("no network paths");
        DesktopWallpaperService.ValidateImagePath(null, _ => true).Should().BeNull();
    }

    [Fact]
    public void Catalog_signature_changes_with_the_apps_but_not_with_their_order_of_comparison()
    {
        var a = new AppEntry { Id = "aumid:A", Name = "A", Path = @"C:\a.exe" };
        var b = new AppEntry { Id = "aumid:B", Name = "B" };
        var s1 = ShellAppCatalogSource.Signature([a, b]);
        ShellAppCatalogSource.Signature([a, b]).Should().Be(s1);
        ShellAppCatalogSource.Signature([a]).Should().NotBe(s1);
        ShellAppCatalogSource.Signature([a, b with { Name = "B2" }]).Should().NotBe(s1);
        ShellAppCatalogSource.Signature([a, b with { IconUrl = "/dyn/x" }]).Should().Be(s1, "icon urls are set by Core and must not make the catalog look changed");
        ShellAppCatalogSource.Signature([]).Should().NotBeNullOrEmpty();
    }
}

public class LauncherPureTests
{
    [Theory]
    [InlineData(@"C:\Program Files\Foo\foo.exe", @"C:\Program Files\Foo")]
    [InlineData(@"C:\tools\run.BAT", @"C:\tools")]
    [InlineData(@"C:\x.exe", @"C:\")]
    [InlineData(@"C:\Users\me\Desktop\App.lnk", null)]
    [InlineData(@"C:\Users\me\Desktop\Site.url", null)]
    [InlineData(@"C:\Users\me\Documents\doc.txt", null)]
    public void Working_directory_is_the_exe_folder_for_programs_only(string path, string? expected)
        => ShellAppLauncher.WorkingDirectoryFor(path).Should().Be(expected);
}
