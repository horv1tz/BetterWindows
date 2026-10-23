using BetterWindows.Core.Platform;

namespace BetterWindows.Native.Tests.Apps;

/// <summary>Runs on any OS: the source only needs two folders and (optionally) a name resolver.</summary>
public class DesktopFolderSourceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bw-desktop-" + Guid.NewGuid().ToString("N"));
    private readonly string _user;
    private readonly string _common;

    public DesktopFolderSourceTests()
    {
        _user = Path.Combine(_root, "Desktop");
        _common = Path.Combine(_root, "Public Desktop");
        Directory.CreateDirectory(_user);
        Directory.CreateDirectory(_common);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception) { }
    }

    private ShellDesktopFolderSource Create() => new(null, [_user, _common], parsing => parsing.Contains("20D04FE0") ? "This PC (test)" : "Recycle Bin (test)");

    private static void Touch(string path) => File.WriteAllText(path, "x");

    /// <summary>Hidden on every OS: Windows attribute, or a dot-name on Unix where that is what .NET reports as Hidden.</summary>
    private static string CreateHiddenFile(string dir, string name)
    {
        if (OperatingSystem.IsWindows())
        {
            var p = Path.Combine(dir, name);
            Touch(p);
            File.SetAttributes(p, FileAttributes.Hidden);
            return p;
        }
        var dot = Path.Combine(dir, "." + name);
        Touch(dot);
        return dot;
    }

    [Fact]
    public void Specials_come_first_with_their_shell_targets()
    {
        using var source = Create();
        var items = source.Enumerate();
        items[0].Should().Be(new DesktopEntry("special:thisPc", "This PC (test)", "shell:MyComputerFolder", true, true));
        items[1].Should().Be(new DesktopEntry("special:recycleBin", "Recycle Bin (test)", "shell:RecycleBinFolder", true, true));
        items.Should().HaveCount(2);
    }

    [Fact]
    public void Files_and_folders_of_both_desktops_are_listed_folders_first_in_natural_order()
    {
        Directory.CreateDirectory(Path.Combine(_user, "Zeta"));
        Directory.CreateDirectory(Path.Combine(_common, "alpha"));
        Touch(Path.Combine(_user, "file10.txt"));
        Touch(Path.Combine(_user, "file2.txt"));
        Touch(Path.Combine(_common, "Beta.txt"));
        using var source = Create();

        var items = source.Enumerate().Where(e => !e.IsSpecial).ToList();

        items.Select(e => e.Name).Should().Equal("alpha", "Zeta", "Beta.txt", "file2.txt", "file10.txt");
        items.Select(e => e.IsFolder).Should().Equal(true, true, false, false, false);
        items.Should().OnlyContain(e => !e.IsSpecial);
    }

    [Fact]
    public void Id_is_fs_plus_lowercased_full_path_and_target_is_the_full_path()
    {
        var file = Path.Combine(_user, "MixedCase.TXT");
        Touch(file);
        using var source = Create();
        var e = source.Enumerate().Single(x => !x.IsSpecial);
        e.Id.Should().Be("fs:" + file.ToLowerInvariant());
        e.Target.Should().Be(file);
        e.Name.Should().Be("MixedCase.TXT", "other extensions are kept");
    }

    [Fact]
    public void Shortcuts_lose_lnk_and_url_extensions_like_in_explorer()
    {
        var lnk = Path.Combine(_user, "My App.lnk");
        Touch(lnk);
        Touch(Path.Combine(_user, "Site.URL"));
        Touch(Path.Combine(_user, "readme.txt"));
        using var source = Create();
        var items = source.Enumerate().Where(e => !e.IsSpecial).ToList();
        items.Select(e => e.Name).Should().BeEquivalentTo("My App", "Site", "readme.txt");
        items.Single(e => e.Name == "My App").Target.Should().Be(lnk, "a .lnk stays a .lnk: nothing is resolved");
    }

    [Fact]
    public void Desktop_ini_hidden_and_system_items_are_skipped()
    {
        Touch(Path.Combine(_user, "desktop.ini"));
        Touch(Path.Combine(_common, "DESKTOP.INI"));
        CreateHiddenFile(_user, "secret.txt");
        Touch(Path.Combine(_user, "visible.txt"));
        using var source = Create();
        source.Enumerate().Where(e => !e.IsSpecial).Select(e => e.Name).Should().Equal("visible.txt");
    }

    [Fact]
    public void Missing_folders_are_tolerated()
    {
        using var source = new ShellDesktopFolderSource(null, [Path.Combine(_root, "nope"), _user], _ => null);
        var act = () => source.Enumerate();
        act.Should().NotThrow();
        source.Enumerate().Count(e => e.IsSpecial).Should().Be(2);
    }

    [Fact]
    public void Special_names_fall_back_to_en_or_ru_when_the_shell_cannot_resolve_them()
    {
        var old = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("ru-RU");
            using (var ru = new ShellDesktopFolderSource(null, [_user], _ => null))
                ru.Enumerate().Where(e => e.IsSpecial).Select(e => e.Name).Should().Equal("Этот компьютер", "Корзина");
            System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("en-US");
            using (var en = new ShellDesktopFolderSource(null, [_user], _ => null))
                en.Enumerate().Where(e => e.IsSpecial).Select(e => e.Name).Should().Equal("This PC", "Recycle Bin");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = old;
        }
    }

    [Fact]
    public void Resolver_exceptions_do_not_break_enumeration()
    {
        using var source = new ShellDesktopFolderSource(null, [_user], _ => throw new InvalidOperationException("boom"));
        source.Enumerate().Count(e => e.IsSpecial).Should().Be(2);
    }

    [Fact]
    public void Display_name_rules()
    {
        ShellDesktopFolderSource.DisplayName("a.lnk", false).Should().Be("a");
        ShellDesktopFolderSource.DisplayName("a.LNK", false).Should().Be("a");
        ShellDesktopFolderSource.DisplayName("a.url", false).Should().Be("a");
        ShellDesktopFolderSource.DisplayName(".lnk", false).Should().Be(".lnk", "never produce an empty name");
        ShellDesktopFolderSource.DisplayName("a.lnk", true).Should().Be("a.lnk", "folders keep their name");
        ShellDesktopFolderSource.DisplayName("a.b.txt", false).Should().Be("a.b.txt");
    }

    [Fact]
    public async Task Changes_raise_a_single_debounced_event()
    {
        using var source = Create();
        int count = 0;
        var first = new TaskCompletionSource();
        source.Changed += () =>
        {
            Interlocked.Increment(ref count);
            first.TrySetResult();
        };

        for (int i = 0; i < 8; i++) Touch(Path.Combine(_user, $"new{i}.txt"));

        (await Task.WhenAny(first.Task, Task.Delay(5000))).Should().BeSameAs(first.Task, "the file system watcher must report the change");
        await Task.Delay(800); // longer than the debounce window: a burst must have been coalesced into very few events
        count.Should().BeInRange(1, 2);
        source.Enumerate().Count(e => !e.IsSpecial).Should().Be(8);
    }

    [Fact]
    public async Task Desktop_ini_churn_is_ignored()
    {
        using var source = Create();
        int count = 0;
        source.Changed += () => Interlocked.Increment(ref count);
        Touch(Path.Combine(_user, "desktop.ini"));
        File.WriteAllText(Path.Combine(_user, "desktop.ini"), "changed");
        await Task.Delay(900);
        count.Should().Be(0);
    }

    [Fact]
    public void Dispose_is_idempotent_and_stops_events()
    {
        var source = Create();
        int count = 0;
        source.Changed += () => Interlocked.Increment(ref count);
        source.Dispose();
        source.Dispose();
        Touch(Path.Combine(_user, "late.txt"));
        Thread.Sleep(600);
        count.Should().Be(0);
    }
}
