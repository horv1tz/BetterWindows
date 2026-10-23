using BetterWindows.Core.Platform;
using BetterWindows.Core.Services;
using BetterWindows.Core.Tests.Dock;
using BetterWindows.Core.Tests.Support.Fakes;

namespace BetterWindows.Core.Tests.WindowTracking;

public class AppIdentityResolverTests
{
    private readonly FakeAppCatalog _catalog = new();
    private AppIdentityResolver Resolver() => new(_catalog);
    private static RawWindow Win(string? exe = @"C:\Apps\Foo\foo.exe", string? aumid = null, string title = "Foo window", string cls = "FooWindow", int pid = 100) =>
        FakeWindowSystem.Win(1, title, exe, aumid, pid) with { ClassName = cls };

    [Fact]
    public void An_explicit_aumid_resolves_through_the_catalog()
    {
        var entry = _catalog.Add("Calculator", aumid: "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App");
        var id = Resolver().Resolve(Win(exe: null, aumid: "microsoft.windowscalculator_8wekyb3d8bbwe!app", cls: "ApplicationFrameWindow"));
        id.AppId.Should().Be(entry.Id);
        id.Name.Should().Be("Calculator");
        id.Entry.Should().BeSameAs(entry);
    }

    [Fact]
    public void The_exe_path_resolves_through_the_catalog_when_there_is_no_aumid()
    {
        var entry = _catalog.Add("Foo Studio", path: @"C:\Apps\Foo\foo.exe");
        var id = Resolver().Resolve(Win(exe: @"c:\APPS\foo\FOO.EXE"));
        id.AppId.Should().Be(entry.Id);
        id.Name.Should().Be("Foo Studio");
    }

    [Fact]
    public void An_aumid_that_is_not_in_the_catalog_falls_back_to_the_exe_path_entry()
    {
        // Chrome sets its own AUMID per profile; the Start menu shortcut is keyed by exe path
        var entry = _catalog.Add("Google Chrome", path: @"C:\Chrome\chrome.exe");
        var id = Resolver().Resolve(Win(exe: @"C:\Chrome\chrome.exe", aumid: "Chrome.ABC123"));
        id.AppId.Should().Be(entry.Id);
    }

    [Fact]
    public void The_aumid_wins_over_the_exe_path_when_both_are_in_the_catalog()
    {
        var byAumid = _catalog.Add("By AUMID", aumid: "Vendor.App!Main");
        _catalog.Add("By path", path: @"C:\Apps\Foo\foo.exe");
        Resolver().Resolve(Win(aumid: "Vendor.App!Main")).AppId.Should().Be(byAumid.Id);
    }

    [Fact]
    public void An_unknown_exe_gets_a_synthesised_exe_id_and_a_prettified_name()
    {
        var id = Resolver().Resolve(Win(exe: @"C:\Tools\My_Cool-Tool.exe"));
        id.AppId.Should().Be(@"exe:c:\tools\my_cool-tool.exe");
        id.Name.Should().Be("My Cool Tool");
        id.Entry.Should().BeNull();
    }

    [Fact]
    public void An_unknown_aumid_with_an_exe_gets_an_aumid_id_and_the_exe_name()
    {
        var id = Resolver().Resolve(Win(exe: @"C:\Tools\WindowsTerminal.exe", aumid: "Vendor.Term!App"));
        id.AppId.Should().Be("aumid:Vendor.Term!App");
        id.Name.Should().Be("Windows Terminal");
    }

    [Fact]
    public void An_unknown_aumid_without_a_path_is_named_after_the_window_title()
    {
        var id = Resolver().Resolve(Win(exe: null, aumid: "Vendor.Thing!App", title: "  Thing Viewer "));
        id.AppId.Should().Be("aumid:Vendor.Thing!App");
        id.Name.Should().Be("Thing Viewer");
    }

    [Fact]
    public void An_unknown_aumid_without_path_and_title_is_named_after_the_aumid()
    {
        var id = Resolver().Resolve(Win(exe: null, aumid: "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", title: ""));
        id.Name.Should().Be("Windows Calculator");
    }

    [Fact]
    public void An_application_frame_window_without_an_aumid_is_grouped_by_class_and_process()
    {
        var id = Resolver().Resolve(Win(exe: null, aumid: null, title: "Mystery", cls: "ApplicationFrameWindow", pid: 4242));
        id.AppId.Should().Be("frame:ApplicationFrameWindow:4242");
        id.Name.Should().Be("Mystery");
    }

    [Fact]
    public void ApplicationFrameHost_as_the_exe_is_never_treated_as_the_app()
    {
        var id = Resolver().Resolve(Win(exe: @"C:\Windows\System32\ApplicationFrameHost.exe", aumid: null, title: "Some UWP", cls: "Whatever", pid: 7));
        id.AppId.Should().StartWith("frame:");
    }

    [Fact]
    public void A_window_without_exe_or_aumid_such_as_an_elevated_process_is_grouped_by_process()
    {
        var id = Resolver().Resolve(Win(exe: null, aumid: null, title: "Task Manager", cls: "TaskManagerWindow", pid: 55));
        id.AppId.Should().Be("proc:55");
        id.Name.Should().Be("Task Manager");
    }

    [Theory]
    [InlineData("  \"C:\\Apps\\Foo\\foo.exe\"  ")]
    [InlineData("C:/Apps/Foo/foo.exe")]
    [InlineData(@"\\?\C:\Apps\Foo\foo.exe")]
    [InlineData(@"C:\APPS\FOO\FOO.EXE")]
    public void Path_variants_resolve_to_the_same_id(string exe)
    {
        Resolver().Resolve(Win(exe: exe)).AppId.Should().Be(@"exe:c:\apps\foo\foo.exe");
    }

    [Fact]
    public void Whitespace_and_quotes_around_the_aumid_are_ignored()
    {
        var entry = _catalog.Add("App", aumid: "Vendor.App!Main");
        Resolver().Resolve(Win(exe: null, aumid: "  \"Vendor.App!Main\" ")).AppId.Should().Be(entry.Id);
    }

    [Fact]
    public void Results_are_cached_per_aumid_and_exe_until_the_catalog_changes()
    {
        var r = Resolver();
        var w = Win(exe: @"C:\Apps\Foo\foo.exe");
        var first = r.Resolve(w);
        r.Resolve(w with { Title = "other title" }).Should().BeSameAs(first, "the cache key is (aumid, exe)");

        var entry = _catalog.Add("Foo Studio", aumid: "Vendor.Foo!App", path: @"C:\Apps\Foo\foo.exe");
        r.Resolve(w).Should().BeSameAs(first, "still cached until the catalog announces a change");
        _catalog.RaiseChanged();
        var after = r.Resolve(w);
        after.AppId.Should().Be(entry.Id);
        after.Name.Should().Be("Foo Studio");
    }

    [Fact]
    public void A_throwing_catalog_degrades_to_the_synthesised_identity()
    {
        var r = new AppIdentityResolver(new ThrowingCatalog());
        r.Resolve(Win(exe: @"C:\Tools\tool.exe")).AppId.Should().Be(@"exe:c:\tools\tool.exe");
    }

    [Fact]
    public void Disposing_the_resolver_detaches_from_the_catalog()
    {
        var r = Resolver();
        r.Dispose();
        var w = Win(exe: @"C:\Apps\Foo\foo.exe");
        var first = r.Resolve(w);
        _catalog.Add("Foo Studio", path: @"C:\Apps\Foo\foo.exe");
        _catalog.RaiseChanged();
        r.Resolve(w).Should().BeSameAs(first);
    }

    private sealed class ThrowingCatalog : IAppCatalog
    {
        public IReadOnlyList<BetterWindows.Core.Protocol.AppEntry> Apps => Array.Empty<BetterWindows.Core.Protocol.AppEntry>();
        public BetterWindows.Core.Protocol.AppEntry? FindById(string id) => throw new InvalidOperationException();
        public BetterWindows.Core.Protocol.AppEntry? FindByAumid(string aumid) => throw new InvalidOperationException();
        public BetterWindows.Core.Protocol.AppEntry? FindByExePath(string exePath) => throw new InvalidOperationException();
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
        public event Action? Changed { add { } remove { } }
    }
}
