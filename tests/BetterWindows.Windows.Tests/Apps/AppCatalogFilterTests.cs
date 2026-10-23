namespace BetterWindows.Native.Tests.Apps;

public class AppCatalogFilterTests
{
    [Fact]
    public void Classic_app_with_aumid_and_target()
    {
        var e = AppCatalogFilter.TryBuild(@"{6D809377-6AF0-444B-8957-A3773F02200E}\Foo\foo.exe", "Foo", @"C:\Program Files\Foo\foo.exe");
        e.Should().NotBeNull();
        e!.Id.Should().Be(@"aumid:{6D809377-6AF0-444B-8957-A3773F02200E}\Foo\foo.exe");
        e.Aumid.Should().Be(@"{6D809377-6AF0-444B-8957-A3773F02200E}\Foo\foo.exe");
        e.Path.Should().Be(@"C:\Program Files\Foo\foo.exe");
        e.Name.Should().Be("Foo");
        e.Packaged.Should().BeFalse();
        e.IconUrl.Should().BeEmpty("Core fills the icon url");
        e.Category.Should().BeNull();
    }

    [Fact]
    public void Packaged_app_has_no_path_and_is_flagged()
    {
        var e = AppCatalogFilter.TryBuild("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", "Calculator", null);
        e.Should().NotBeNull();
        e!.Id.Should().Be("aumid:Microsoft.WindowsCalculator_8wekyb3d8bbwe!App");
        e.Packaged.Should().BeTrue();
        e.Path.Should().BeNull();
    }

    [Fact]
    public void Package_identity_hint_marks_packaged_even_without_a_bang()
        => AppCatalogFilter.TryBuild("SomeAumid", "X", null, packagedHint: true)!.Packaged.Should().BeTrue();

    [Fact]
    public void Without_aumid_the_id_is_the_lowercased_exe_path()
    {
        var e = AppCatalogFilter.TryBuild(null, "Tool", @"C:\Tools\Tool.EXE");
        e.Should().NotBeNull();
        e!.Id.Should().Be(@"exe:c:\tools\tool.exe");
        e.Aumid.Should().BeNull();
        e.Path.Should().Be(@"C:\Tools\Tool.EXE");
    }

    [Fact]
    public void Entries_with_nothing_to_launch_are_dropped()
    {
        AppCatalogFilter.TryBuild(null, "Ghost", null).Should().BeNull();
        AppCatalogFilter.TryBuild("", "Ghost", "").Should().BeNull();
        AppCatalogFilter.TryBuild("shell:startup", "Bad", null).Should().BeNull("an invalid AUMID is not an identity");
    }

    [Fact]
    public void Missing_name_falls_back_to_the_file_name()
    {
        AppCatalogFilter.TryBuild(null, "  ", @"C:\Tools\mytool.exe")!.Name.Should().Be("mytool");
        AppCatalogFilter.TryBuild("Pkg!App", "", null).Should().BeNull("no name and no path to derive one from");
    }

    [Theory]
    [InlineData(@"C:\Docs\readme.txt")]
    [InlineData(@"C:\Docs\help.chm")]
    [InlineData(@"C:\Docs\manual.pdf")]
    [InlineData(@"C:\Docs\Visit website.url")]
    [InlineData(@"C:\Docs\index.html")]
    [InlineData(@"C:\Docs\folder")]
    public void Document_and_link_targets_are_not_apps(string target)
        => AppCatalogFilter.TryBuild("SomeAumid", "Documentation", target).Should().BeNull();

    [Theory]
    [InlineData(@"C:\Program Files\Foo\unins000.exe", "Foo Uninstaller")]
    [InlineData(@"C:\Program Files\Foo\uninst.exe", "Remove Foo")]
    [InlineData(@"C:\Program Files\Foo\Uninstall.exe", "Foo")]
    [InlineData(@"C:\Windows\System32\msiexec.exe", "Foo")]
    [InlineData(@"C:\Program Files\Foo\foo.exe", "Uninstall Foo")]
    [InlineData(@"C:\Program Files\Foo\foo.exe", "Удалить Foo")]
    [InlineData(@"C:\Program Files\Foo\foo.exe", "Readme")]
    [InlineData(@"C:\Program Files\Foo\foo.exe", "Release Notes")]
    [InlineData(@"C:\Program Files\Foo\foo.exe", "License Agreement")]
    [InlineData(@"C:\Program Files\Foo\foo.exe", "Visit our website")]
    public void Noise_is_filtered(string target, string name)
        => AppCatalogFilter.TryBuild("Some.Aumid", name, target).Should().BeNull();

    [Theory]
    [InlineData(@"C:\Program Files\Revo\revo.exe", "Revo Uninstaller")]
    [InlineData(@"C:\Windows\System32\notepad.exe", "Notepad")]
    [InlineData(@"C:\Windows\System32\cmd.exe", "Command Prompt")]
    [InlineData(@"C:\Windows\System32\mmc.exe", "Help")]
    [InlineData(@"C:\Program Files\Foo\foo.bat", "Foo Script")]
    [InlineData(@"C:\Windows\System32\compmgmt.msc", "Computer Management")]
    public void Real_apps_survive_the_filter(string target, string name)
        => AppCatalogFilter.TryBuild("Some.Aumid", name, target).Should().NotBeNull();

    [Fact]
    public void Environment_variables_in_the_target_are_expanded()
    {
        Environment.SetEnvironmentVariable("BW_TEST_APPS_DIR", @"C:\Expanded");
        try
        {
            AppCatalogFilter.TryBuild(null, "T", @"%BW_TEST_APPS_DIR%\t.exe")!.Path.Should().Be(@"C:\Expanded\t.exe");
        }
        finally
        {
            Environment.SetEnvironmentVariable("BW_TEST_APPS_DIR", null);
        }
    }

    [Fact]
    public void Relative_or_unc_targets_are_dropped_to_a_pathless_entry_or_null()
    {
        // An AUMID app whose target is not an absolute local path keeps its AUMID but gets no Path.
        var e = AppCatalogFilter.TryBuild("Some.Aumid", "X", @"\\server\share\x.exe");
        e.Should().NotBeNull();
        e!.Path.Should().BeNull();
        AppCatalogFilter.TryBuild(null, "X", @"\\server\share\x.exe").Should().BeNull("no usable identity at all");
    }

    [Fact]
    public void Long_names_are_capped()
        => AppCatalogFilter.TryBuild("A", new string('x', 1000), null)!.Name.Length.Should().Be(256);

    [Theory]
    [InlineData(@"C:\a\b.exe", true)]
    [InlineData(@"C:\a\b.CMD", true)]
    [InlineData(@"C:\a\b.msc", true)]
    [InlineData(@"C:\a\b.txt", false)]
    [InlineData(@"C:\a\b", false)]
    [InlineData(null, false)]
    public void Launchable_extensions(string? path, bool expected) => AppCatalogFilter.IsLaunchableTarget(path).Should().Be(expected);
}
