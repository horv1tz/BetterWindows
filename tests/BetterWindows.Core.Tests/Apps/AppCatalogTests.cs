using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Core.Tests.Support;
using BetterWindows.Core.Tests.Support.Fakes;

namespace BetterWindows.Core.Tests.Apps;

public class AppCatalogTests
{
    private static AppEntry A(string name, string? aumid = null, string? path = null) => FakeAppCatalogSource.App(name, aumid, path);

    private static (AppCatalog Catalog, FakeAppCatalogSource Source, ManualTimeProvider Time) Make(params AppEntry[] apps)
    {
        var src = new FakeAppCatalogSource();
        src.Apps.AddRange(apps);
        var time = new ManualTimeProvider();
        return (new AppCatalog(src, time), src, time);
    }

    [Fact]
    public async Task Entries_get_icon_urls_and_are_sorted_case_insensitively()
    {
        var (c, _, _) = Make(A("zeta", path: @"C:\z.exe"), A("Alpha", path: @"C:\a.exe"), A("beta", path: @"C:\b.exe"), A("Äpfel", path: @"C:\ae.exe"));
        await c.RefreshAsync();
        c.Apps.Select(a => a.Name).Should().ContainInOrder("Alpha", "beta", "zeta");
        foreach (var a in c.Apps) a.IconUrl.Should().Be(DynUrls.AppIcon(a.Id, 128));
        c.Apps.Single(a => a.Name == "Alpha").IconUrl.Should().Be("/dyn/icon/app/exe%3Ac%3A%5Ca.exe?s=128");
    }

    [Fact]
    public async Task Duplicates_by_id_collapse_and_keep_the_better_information()
    {
        var (c, _, _) = Make(
            A("App", "Contoso.App!Main") with { Path = null, Category = null },
            A("App (copy)", "Contoso.App!Main") with { Path = @"C:\app\app.exe", Category = "Tools" },
            A("Other", path: @"C:\o.exe"));
        await c.RefreshAsync();
        c.Apps.Should().HaveCount(2);
        var app = c.FindById("aumid:Contoso.App!Main")!;
        app.Name.Should().Be("App");
        app.Path.Should().Be(@"C:\app\app.exe");
        app.Category.Should().Be("Tools");
    }

    [Fact]
    public async Task Ids_aumids_and_paths_are_indexed_case_insensitively()
    {
        var (c, _, _) = Make(A("Edge", "Microsoft.Edge!App", @"C:\Program Files\Edge\edge.exe"), A("Tool", path: @"c:\tools\tool.exe"));
        await c.RefreshAsync();
        c.FindById("AUMID:microsoft.edge!app")!.Name.Should().Be("Edge");
        c.FindByAumid("microsoft.edge!APP")!.Name.Should().Be("Edge");
        c.FindByExePath(@"c:\program files\EDGE\edge.exe")!.Name.Should().Be("Edge");
        c.FindByExePath("C:/Tools/Tool.exe")!.Name.Should().Be("Tool");
        c.FindById("exe:c:\\tools\\tool.exe")!.Name.Should().Be("Tool");
        c.FindById("nope").Should().BeNull();
        c.FindById("").Should().BeNull();
        c.FindByAumid("").Should().BeNull();
        c.FindByExePath("  ").Should().BeNull();
        c.FindByExePath(@"C:\missing.exe").Should().BeNull();
    }

    [Fact]
    public async Task Entries_without_an_id_or_a_name_are_skipped()
    {
        var (c, _, _) = Make(new AppEntry { Id = "", Name = "x" }, new AppEntry { Id = "exe:c:\\a.exe", Name = "  " }, A("Good", path: @"C:\g.exe"));
        await c.RefreshAsync();
        c.Apps.Should().ContainSingle().Which.Name.Should().Be("Good");
    }

    [Fact]
    public async Task The_first_access_starts_a_background_scan_and_Changed_announces_the_result()
    {
        var (c, _, _) = Make(A("One", path: @"C:\1.exe"));
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        c.Changed += () => changed.TrySetResult();
        c.Apps.Should().NotBeNull(); // may still be empty: the scan runs in the background
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        c.Apps.Should().HaveCount(1);
    }

    [Fact]
    public async Task Changed_is_raised_only_when_the_list_changed()
    {
        var (c, src, _) = Make(A("One", path: @"C:\1.exe"));
        var n = 0;
        await c.RefreshAsync();
        c.Changed += () => n++;
        await c.RefreshAsync();
        n.Should().Be(0, "same content");
        src.Apps.Add(A("Two", path: @"C:\2.exe"));
        await c.RefreshAsync();
        n.Should().Be(1);
        src.Apps.RemoveAt(0);
        await c.RefreshAsync();
        n.Should().Be(2);
        c.Apps.Select(a => a.Name).Should().Equal("Two");
    }

    [Fact]
    public async Task Source_changes_are_debounced_by_500_ms()
    {
        var (c, src, time) = Make(A("One", path: @"C:\1.exe"));
        await c.RefreshAsync();
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        c.Changed += () => changed.TrySetResult();
        src.Apps.Add(A("Two", path: @"C:\2.exe"));
        src.RaiseChanged();
        time.Advance(TimeSpan.FromMilliseconds(300));
        src.RaiseChanged(); // restarts the quiet period
        time.Advance(TimeSpan.FromMilliseconds(300));
        await Task.Delay(100);
        changed.Task.IsCompleted.Should().BeFalse("600 ms passed but the second event restarted the 500 ms quiet period");
        time.Advance(TimeSpan.FromMilliseconds(250));
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        c.Apps.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_failing_scan_keeps_the_previous_list()
    {
        var src = new ThrowingSource { Apps = { A("One", path: @"C:\1.exe") } };
        var c = new AppCatalog(src, new ManualTimeProvider());
        await c.RefreshAsync();
        src.Fail = true;
        var n = 0;
        c.Changed += () => n++;
        await c.RefreshAsync();
        c.Apps.Should().HaveCount(1);
        n.Should().Be(0);
        src.Fail = false;
        src.Apps.Add(A("Two", path: @"C:\2.exe"));
        await c.RefreshAsync();
        c.Apps.Should().HaveCount(2);
    }

    [Fact]
    public async Task Concurrent_refreshes_share_scans_and_a_request_during_a_scan_gets_a_fresh_one()
    {
        var gate = new GatedSource();
        gate.Apps.Add(A("One", path: @"C:\1.exe"));
        var c = new AppCatalog(gate, new ManualTimeProvider());
        var first = c.RefreshAsync();
        await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = c.RefreshAsync(); // arrives mid-scan
        var third = c.RefreshAsync();
        gate.Apps.Add(A("Two", path: @"C:\2.exe"));
        gate.Release();
        await Task.WhenAll(first, second, third).WaitAsync(TimeSpan.FromSeconds(5));
        gate.Scans.Should().Be(2, "one in flight + exactly one follow-up for the mid-scan requests");
        c.Apps.Should().HaveCount(2, "the follow-up scan saw the app added during the first one");
    }

    [Fact]
    public async Task Cancelling_the_wait_does_not_break_the_shared_scan()
    {
        var gate = new GatedSource();
        gate.Apps.Add(A("One", path: @"C:\1.exe"));
        var c = new AppCatalog(gate, new ManualTimeProvider());
        using var cts = new CancellationTokenSource();
        var waiting = c.RefreshAsync(cts.Token);
        await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        await ((Func<Task>)(() => waiting)).Should().ThrowAsync<OperationCanceledException>();
        gate.Release();
        await c.RefreshAsync();
        c.Apps.Should().HaveCount(1);
    }

    [Fact]
    public async Task Dispose_unsubscribes_from_the_source()
    {
        var (c, src, time) = Make(A("One", path: @"C:\1.exe"));
        await c.RefreshAsync();
        c.Dispose();
        src.Apps.Add(A("Two", path: @"C:\2.exe"));
        src.RaiseChanged();
        time.Advance(TimeSpan.FromSeconds(2));
        await Task.Delay(50);
        c.Apps.Should().HaveCount(1);
    }

    private sealed class ThrowingSource : IAppCatalogSource
    {
        public List<AppEntry> Apps { get; } = new();
        public bool Fail { get; set; }
        public event Action? Changed { add { } remove { } }
        public Task<IReadOnlyList<AppEntry>> ScanAsync(CancellationToken ct = default) => Fail ? throw new IOException("start menu locked") : Task.FromResult<IReadOnlyList<AppEntry>>(Apps.ToArray());
    }

    private sealed class GatedSource : IAppCatalogSource
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _scans;
        public List<AppEntry> Apps { get; } = new();
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Scans => Volatile.Read(ref _scans);
        public event Action? Changed { add { } remove { } }
        public void Release() => _release.TrySetResult();
        public async Task<IReadOnlyList<AppEntry>> ScanAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _scans);
            var snapshot = Apps.ToArray(); // what this scan "sees" when it starts
            Started.TrySetResult();
            await _release.Task.WaitAsync(ct);
            return Scans == 1 ? snapshot : Apps.ToArray();
        }
    }
}
