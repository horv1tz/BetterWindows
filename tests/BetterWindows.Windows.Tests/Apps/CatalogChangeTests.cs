namespace BetterWindows.Native.Tests.Apps;

/// <summary>Change detection of the app catalog (file system watchers + debounce) needs no COM, so it runs on every OS.</summary>
public class CatalogChangeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bw-startmenu-" + Guid.NewGuid().ToString("N"));
    private readonly string _programs;

    public CatalogChangeTests()
    {
        _programs = Path.Combine(_root, "Start Menu", "Programs");
        Directory.CreateDirectory(_programs);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception) { }
    }

    private ShellAppCatalogSource Create() => new(null, [Path.Combine(_root, "Start Menu")]);

    [Fact]
    public async Task New_shortcut_raises_one_debounced_changed_event()
    {
        using var source = Create();
        int count = 0;
        var first = new TaskCompletionSource();
        source.Changed += () => { Interlocked.Increment(ref count); first.TrySetResult(); };

        var started = DateTime.UtcNow;
        for (int i = 0; i < 10; i++) File.WriteAllText(Path.Combine(_programs, $"app{i}.lnk"), "x");

        (await Task.WhenAny(first.Task, Task.Delay(6000))).Should().BeSameAs(first.Task);
        (DateTime.UtcNow - started).Should().BeGreaterThan(TimeSpan.FromMilliseconds(900), "debounced by about one second");
        await Task.Delay(1500);
        count.Should().Be(1, "a burst of changes is coalesced");
    }

    [Fact]
    public async Task Shortcuts_in_sub_folders_and_deletions_are_noticed()
    {
        var sub = Path.Combine(_programs, "Vendor");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "tool.lnk"), "x");
        using var source = Create();
        var raised = new SemaphoreSlim(0);
        source.Changed += () => raised.Release();

        File.Delete(Path.Combine(sub, "tool.lnk"));
        (await raised.WaitAsync(TimeSpan.FromSeconds(6))).Should().BeTrue();
    }

    [Fact]
    public async Task Unrelated_files_do_not_raise_changed()
    {
        using var source = Create();
        int count = 0;
        source.Changed += () => Interlocked.Increment(ref count);
        File.WriteAllText(Path.Combine(_programs, "notes.txt"), "x");
        File.WriteAllText(Path.Combine(_programs, "data.ini"), "x");
        await Task.Delay(1800);
        count.Should().Be(0);
    }

    [Fact]
    public async Task Unsubscribed_handlers_are_not_called_and_dispose_is_idempotent()
    {
        var source = Create();
        int count = 0;
        void Handler() => Interlocked.Increment(ref count);
        source.Changed += Handler;
        source.Changed -= Handler;
        File.WriteAllText(Path.Combine(_programs, "a.lnk"), "x");
        await Task.Delay(1800);
        count.Should().Be(0);
        source.Dispose();
        source.Dispose();
    }

    [Fact]
    public async Task Missing_start_menu_folders_are_tolerated()
    {
        using var source = new ShellAppCatalogSource(null, [Path.Combine(_root, "does-not-exist")]);
        source.Changed += () => { };
        var act = async () => await source.ScanAsync();
        await act.Should().NotThrowAsync();
    }

    [SkippableFact]
    public async Task Scan_never_throws_even_where_the_shell_is_unavailable()
    {
        Skip.If(OperatingSystem.IsWindows(), "on Windows the real scan is covered by the integration tests");
        using var source = Create();
        var apps = await source.ScanAsync();
        apps.Should().BeEmpty("there is no shell here, and the contract is an empty list instead of an exception");
    }
}
