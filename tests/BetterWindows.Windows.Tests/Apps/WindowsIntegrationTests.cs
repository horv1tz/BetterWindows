using System.Diagnostics;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Native.Interop;
using Microsoft.Win32;
using Windows.Graphics.Imaging;

namespace BetterWindows.Native.Tests.Apps;

/// <summary>
/// Real-Windows integration tests of the apps stream (GitHub Actions windows-latest). Every test skips on other OSes and tolerates what a CI image may lack
/// (no indexing, no wallpaper, no desktop). Rules: never change user-visible state permanently; restore everything touched.
/// </summary>
public class WindowsIntegrationTests
{
    private static void RequireWindows() => Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");

    /// <summary>The bytes are a real PNG: our own strict decoder accepts it AND the operating system's decoder agrees about the size.</summary>
    private static async Task<(int W, int H)> AssertValidPngAsync(byte[]? png, int maxEdge)
    {
        png.Should().NotBeNull();
        png!.AsSpan(0, 8).ToArray().Should().Equal(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);
        PngEncoder.TryReadSize(png, out var w, out var h).Should().BeTrue();
        w.Should().BeInRange(8, maxEdge);
        h.Should().BeInRange(8, maxEdge);
        var (dw, dh, rgba) = PngTestDecoder.Decode(png!);
        (dw, dh).Should().Be((w, h));
        rgba.Where((_, i) => i % 4 == 3).Any(a => a != 0).Should().BeTrue("an icon must not be completely transparent");

        using var ms = new MemoryStream(png!);
        var decoder = await BitmapDecoder.CreateAsync(ms.AsRandomAccessStream());
        ((int)decoder.PixelWidth, (int)decoder.PixelHeight).Should().Be((w, h), "the OS decoder must read what we wrote");
        return (w, h);
    }

    // ------------------------------------------------------------------ catalog

    [SkippableFact]
    public async Task Catalog_lists_apps_with_id_and_name()
    {
        RequireWindows();
        using var catalog = new ShellAppCatalogSource();
        var apps = await catalog.ScanAsync();

        apps.Should().NotBeEmpty("every Windows has at least one Start menu app");
        apps.Should().OnlyContain(a => !string.IsNullOrWhiteSpace(a.Id) && !string.IsNullOrWhiteSpace(a.Name));
        apps.Should().OnlyContain(a => a.Id.StartsWith("aumid:") || a.Id.StartsWith("exe:"));
        apps.Select(a => a.Id.ToLowerInvariant()).Should().OnlyHaveUniqueItems();
        apps.Where(a => a.Aumid != null).Should().OnlyContain(a => a.Id == "aumid:" + a.Aumid);
        apps.Where(a => a.Aumid == null).Should().OnlyContain(a => a.Id == "exe:" + a.Path!.ToLowerInvariant());
        apps.Where(a => a.Path != null).Should().OnlyContain(a => Path.IsPathFullyQualified(a.Path!));
        apps.Where(a => a.Packaged).Should().OnlyContain(a => a.Aumid != null);
        apps.Should().OnlyContain(a => a.IconUrl == "", "Core sets the icon url");
        apps.Should().NotContain(a => a.Name.StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase));
    }

    [SkippableFact]
    public async Task Catalog_scan_is_stable_and_never_throws_on_cancellation()
    {
        RequireWindows();
        using var catalog = new ShellAppCatalogSource();
        var first = await catalog.ScanAsync();
        var second = await catalog.ScanAsync();
        second.Select(a => a.Id).Should().Equal(first.Select(a => a.Id));

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var act = async () => await catalog.ScanAsync(cts.Token);
        await act.Should().NotThrowAsync();
    }

    // ------------------------------------------------------------------ icons

    [SkippableFact]
    public async Task Icon_of_the_first_app_is_a_valid_png_at_64_and_256()
    {
        RequireWindows();
        using var catalog = new ShellAppCatalogSource();
        var apps = await catalog.ScanAsync();
        Skip.If(apps.Count == 0, "no apps to take an icon of");
        using var icons = new ShellIconSource();

        // The first app might legitimately have no icon on a stripped CI image: take the first one that has one and require the SAME app to work at both sizes.
        AppEntry? app = null;
        foreach (var candidate in apps.Take(15))
        {
            var key = candidate.Aumid != null ? new IconKey { Aumid = candidate.Aumid, Path = candidate.Path } : new IconKey { Path = candidate.Path };
            if (await icons.GetIconPngAsync(key, 64) != null) { app = candidate; break; }
        }
        Skip.If(app == null, "none of the first apps produced an icon");

        var appKey = app!.Aumid != null ? new IconKey { Aumid = app.Aumid, Path = app.Path } : new IconKey { Path = app.Path };
        var small = await AssertValidPngAsync(await icons.GetIconPngAsync(appKey, 64), 64);
        var big = await AssertValidPngAsync(await icons.GetIconPngAsync(appKey, 256), 256);
        small.W.Should().BeLessThanOrEqualTo(big.W);
    }

    [SkippableFact]
    public async Task Icon_of_notepad_exe_is_a_valid_png_at_64_and_256()
    {
        RequireWindows();
        var notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        Skip.IfNot(File.Exists(notepad), "notepad.exe not present");
        using var icons = new ShellIconSource();
        var key = new IconKey { Path = notepad };
        await AssertValidPngAsync(await icons.GetIconPngAsync(key, 64), 64);
        await AssertValidPngAsync(await icons.GetIconPngAsync(key, 256), 256);
        // repeated request is served from the cache with identical bytes
        (await icons.GetIconPngAsync(key, 64)).Should().Equal(await icons.GetIconPngAsync(key, 64));
    }

    [SkippableFact]
    public async Task Icons_for_bad_keys_are_null_not_exceptions()
    {
        RequireWindows();
        using var icons = new ShellIconSource();
        (await icons.GetIconPngAsync(new IconKey { Path = @"C:\definitely\missing\file.exe" }, 64)).Should().BeNull();
        (await icons.GetIconPngAsync(new IconKey { Path = @"\\server\share\file.exe" }, 64)).Should().BeNull("network paths are refused");
        (await icons.GetIconPngAsync(new IconKey { Path = "relative.exe" }, 64)).Should().BeNull();
        (await icons.GetIconPngAsync(new IconKey { Aumid = "No.Such.App_xyz!App" }, 64)).Should().BeNull();
        (await icons.GetIconPngAsync(new IconKey { Aumid = @"..\..\evil" }, 64)).Should().BeNull();
        (await icons.GetIconPngAsync(new IconKey { WindowHandle = 0x7FFFFFF1 }, 64)).Should().BeNull("not a window");
        (await icons.GetIconPngAsync(new IconKey(), 64)).Should().BeNull();
    }

    [SkippableFact]
    public async Task Window_icon_comes_from_WM_GETICON()
    {
        RequireWindows();
        using var window = new TestIconWindow(withIcon: true);
        using var icons = new ShellIconSource();
        var png = await icons.GetIconPngAsync(new IconKey { WindowHandle = window.Handle }, 64);
        var (w, h) = await AssertValidPngAsync(png, 64);
        w.Should().Be(h);
        w.Should().BeInRange(16, 64, "IDI_APPLICATION is the system icon size (32 px at 100% scaling); icons smaller than the request are not enlarged");
    }

    [SkippableFact]
    public async Task Window_without_icon_falls_back_to_its_process_icon()
    {
        RequireWindows();
        using var window = new TestIconWindow(withIcon: false);
        using var icons = new ShellIconSource();
        var png = await icons.GetIconPngAsync(new IconKey { WindowHandle = window.Handle }, 64);
        // The window belongs to the test host process, whose exe has an icon (or the generic exe icon): a PNG either way.
        png.Should().NotBeNull();
        await AssertValidPngAsync(png, 64);
    }

    [SkippableFact]
    public async Task Image_files_get_a_thumbnail_at_preview_size()
    {
        RequireWindows();
        var dir = Path.Combine(Path.GetTempPath(), "bw-icon-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // a 300x100 red PNG written with our own encoder
            var bgra = new byte[300 * 100 * 4];
            for (int i = 0; i < bgra.Length; i += 4) { bgra[i + 2] = 255; bgra[i + 3] = 255; }
            var file = Path.Combine(dir, "wide.png");
            await File.WriteAllBytesAsync(file, PngEncoder.EncodeBgra(bgra, 300, 100));

            using var icons = new ShellIconSource();
            var png = await icons.GetIconPngAsync(new IconKey { Path = file }, 256);
            png.Should().NotBeNull();
            PngEncoder.TryReadSize(png!, out var w, out var h).Should().BeTrue();
            // A thumbnail keeps the aspect ratio of the picture (3:1); a generic icon would be square.
            if (w != h) ((double)w / h).Should().BeApproximately(3.0, 0.3);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ------------------------------------------------------------------ launcher

    [SkippableFact]
    public void Launcher_refuses_everything_that_is_not_allow_listed()
    {
        RequireWindows();
        using var launcher = new ShellAppLauncher();
        foreach (var bad in new[]
                 {
                     "javascript:alert(1)", "file:///C:/Windows/System32/cmd.exe", "cmd.exe", "powershell -c calc", "shell:startup", "shell:AppsFolder\\x",
                     @"C:\definitely\missing.exe", @"\\server\share\x.exe", "relative.txt", "", "  ", "ftp://example.com/x", "calc:", "http://", "mailto:a@b.c",
                 })
        {
            launcher.Open(bad).Should().BeFalse($"'{bad}' must be refused");
        }
    }

    [SkippableFact]
    public void Launch_reports_not_found_and_failed_without_throwing()
    {
        RequireWindows();
        using var launcher = new ShellAppLauncher();
        launcher.Launch(LaunchTarget.FromPath(@"C:\definitely\missing\app.exe")).Should().Be(LaunchResult.NotFound);
        launcher.Launch(LaunchTarget.FromAumid("No.Such.App_xyz!App")).Should().Be(LaunchResult.NotFound);
        launcher.Launch(new LaunchTarget(null, null)).Should().Be(LaunchResult.NotFound);
        launcher.Launch(LaunchTarget.FromPath("notepad.exe")).Should().Be(LaunchResult.Failed, "relative paths are refused");
        launcher.Launch(LaunchTarget.FromPath(@"\\server\share\x.exe")).Should().Be(LaunchResult.Failed);
        launcher.Launch(LaunchTarget.FromAumid(@"..\..\evil")).Should().Be(LaunchResult.Failed);
        launcher.Launch(LaunchTarget.FromAumid("shell:startup")).Should().Be(LaunchResult.Failed);
        var notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        launcher.Launch(LaunchTarget.FromPath(notepad), "line\nbreak").Should().Be(LaunchResult.Failed, "multi-line arguments are refused before anything starts");
    }

    [SkippableFact]
    public void Open_of_a_bogus_settings_uri_returns_without_exception()
    {
        RequireWindows();
        using var launcher = new ShellAppLauncher();
        var act = () => launcher.Open("ms-settings:bogus-page-that-does-not-exist");
        act.Should().NotThrow();
        launcher.Open("ms-settings:bad value with spaces").Should().BeFalse();
    }

    [SkippableFact]
    public void Reveal_refuses_missing_and_relative_paths()
    {
        RequireWindows();
        using var launcher = new ShellAppLauncher();
        launcher.RevealInExplorer(@"C:\definitely\missing\file.txt").Should().BeFalse();
        launcher.RevealInExplorer("relative.txt").Should().BeFalse();
        launcher.RevealInExplorer(@"\\server\share\file.txt").Should().BeFalse();
        launcher.RevealInExplorer("").Should().BeFalse();
    }

    [SkippableFact]
    public void Launching_a_classic_exe_starts_it()
    {
        RequireWindows();
        var notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        Skip.IfNot(File.Exists(notepad), "notepad.exe not present");
        var before = DateTime.Now.AddSeconds(-2);
        using var launcher = new ShellAppLauncher();
        try
        {
            launcher.Launch(LaunchTarget.FromPath(notepad)).Should().Be(LaunchResult.Started);
            Thread.Sleep(1500);
        }
        finally
        {
            // Clean up whatever we started (classic and packaged Notepad have different process names).
            foreach (var name in new[] { "notepad", "Notepad" })
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try { if (p.StartTime >= before) p.Kill(); }
                    catch (Exception) { /* exited or protected */ }
                    finally { p.Dispose(); }
                }
        }
    }

    // ------------------------------------------------------------------ autostart

    [SkippableFact]
    public void Autostart_round_trip_on_the_real_key_restores_the_original_value()
    {
        RequireWindows();
        const string run = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        const string name = "BetterWindows";

        var originalRun = ReadRaw(run, name);
        var originalApproved = ReadRaw(approved, name);
        try
        {
            var svc = new RegistryAutostartService();
            svc.SetEnabled(true);
            Skip.If(ReadRaw(run, name).Value == null, "cannot write to HKCU Run on this machine");
            svc.IsEnabled.Should().BeTrue();
            ((string)ReadRaw(run, name).Value!).Should().StartWith("\"").And.EndWith("\" --autostart");

            svc.SetEnabled(true); // idempotent
            svc.IsEnabled.Should().BeTrue();

            svc.SetEnabled(false);
            svc.IsEnabled.Should().BeFalse();
            ReadRaw(run, name).Value.Should().BeNull();

            svc.SetEnabled(false); // removing twice is fine
        }
        finally
        {
            WriteRaw(run, name, originalRun);
            WriteRaw(approved, name, originalApproved);
        }
    }

    [SkippableFact]
    public void Autostart_semantics_on_a_scratch_key()
    {
        RequireWindows();
        var root = @"Software\BetterWindows.Tests\" + Guid.NewGuid().ToString("N");
        var run = root + @"\Run";
        var approved = root + @"\Approved";
        const string name = "BetterWindows";
        try
        {
            var svc = new RegistryAutostartService(null, @"C:\Test Dir\BetterWindows.exe", run, approved);
            svc.IsEnabled.Should().BeFalse("missing key");

            svc.SetEnabled(true);
            Skip.If(ReadRaw(run, name).Value == null, "cannot write to HKCU on this machine");
            ReadRaw(run, name).Value.Should().Be("\"C:\\Test Dir\\BetterWindows.exe\" --autostart");
            svc.IsEnabled.Should().BeTrue();

            // A value that points at another exe is not "our" autostart.
            WriteRaw(run, name, (@"""C:\Other\BetterWindows.exe"" --autostart", RegistryValueKind.String));
            svc.IsEnabled.Should().BeFalse();

            // Disabled in Task Manager / Settings (StartupApproved first byte odd).
            WriteRaw(run, name, ("\"C:\\Test Dir\\BetterWindows.exe\" --autostart", RegistryValueKind.String));
            WriteRaw(approved, name, (new byte[] { 3, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8 }, RegistryValueKind.Binary));
            svc.IsEnabled.Should().BeFalse("the user switched it off");
            WriteRaw(approved, name, (new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary));
            svc.IsEnabled.Should().BeTrue();

            // Enabling from our settings clears a stale "disabled" flag.
            WriteRaw(approved, name, (new byte[] { 3, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8 }, RegistryValueKind.Binary));
            svc.SetEnabled(true);
            svc.IsEnabled.Should().BeTrue();

            svc.SetEnabled(false);
            svc.IsEnabled.Should().BeFalse();
            ReadRaw(run, name).Value.Should().BeNull();
            ReadRaw(approved, name).Value.Should().BeNull();
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\BetterWindows.Tests", throwOnMissingSubKey: false);
        }
    }

    [SkippableFact]
    public void Autostart_without_an_exe_path_does_not_throw()
    {
        RequireWindows();
        var root = @"Software\BetterWindows.Tests\" + Guid.NewGuid().ToString("N");
        try
        {
            var svc = new RegistryAutostartService(null, null, root + @"\Run", root + @"\Approved");
            var act = () => svc.SetEnabled(true);
            act.Should().NotThrow();
            svc.IsEnabled.Should().BeFalse();
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\BetterWindows.Tests", throwOnMissingSubKey: false);
        }
    }

    private static (object? Value, RegistryValueKind Kind) ReadRaw(string path, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path, writable: false);
        var v = key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return v == null ? (null, RegistryValueKind.Unknown) : (v, key!.GetValueKind(name));
    }

    private static void WriteRaw(string path, string name, (object? Value, RegistryValueKind Kind) state)
    {
        if (state.Value == null)
        {
            using var existing = Registry.CurrentUser.OpenSubKey(path, writable: true);
            existing?.DeleteValue(name, throwOnMissingValue: false);
            return;
        }
        using var key = Registry.CurrentUser.CreateSubKey(path, writable: true);
        key.SetValue(name, state.Value, state.Kind);
    }

    // ------------------------------------------------------------------ file search

    [SkippableFact]
    public async Task File_search_streams_hits_or_nothing_when_the_index_is_unavailable()
    {
        RequireWindows();
        var search = new WindowsSearchFileSearch();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var hits = new List<FileHit>();
        await foreach (var hit in search.SearchAsync("notepad", 10, cts.Token)) hits.Add(hit);

        if (!search.IsAvailable) { hits.Should().BeEmpty(); return; } // acceptable: no/disabled index on this machine
        hits.Count.Should().BeLessThanOrEqualTo(10);
        hits.Should().OnlyContain(h => Path.IsPathFullyQualified(h.Path) && h.Name.Length > 0);
        hits.Select(h => h.Path.ToLowerInvariant()).Should().OnlyHaveUniqueItems();
    }

    [SkippableFact]
    public async Task File_search_handles_empty_hostile_and_cancelled_queries()
    {
        RequireWindows();
        var search = new WindowsSearchFileSearch();

        var empty = new List<FileHit>();
        await foreach (var hit in search.SearchAsync("   ", 5)) empty.Add(hit);
        empty.Should().BeEmpty();

        // hostile text must neither throw nor change the query
        var act = async () => { await foreach (var _ in search.SearchAsync("x' OR 1=1 --", 5)) { } };
        await act.Should().NotThrowAsync();

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var sw = Stopwatch.StartNew();
        try { await foreach (var _ in search.SearchAsync("notepad", 5, cts.Token)) { } }
        catch (OperationCanceledException) { }
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [SkippableFact]
    public void File_search_availability_is_cached()
    {
        RequireWindows();
        var search = new WindowsSearchFileSearch();
        var first = search.IsAvailable;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++) search.IsAvailable.Should().Be(first);
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1), "the probe runs once, not per call");
    }

    // ------------------------------------------------------------------ wallpaper

    [SkippableFact]
    public async Task Wallpaper_path_is_null_or_an_existing_file_for_every_monitor()
    {
        RequireWindows();
        using var wallpaper = new DesktopWallpaperService();
        wallpaper.GetWallpaperPath("mdeadbeef").Should().BeNull("unknown monitor id");
        wallpaper.GetWallpaperPath("").Should().BeNull();

        using var sta = new StaWorker("test-wallpaper-ids");
        var ids = await sta.RunAsync(() =>
        {
            var result = new List<string>();
            var dw = AppsCom.CreateInstance<IAppsDesktopWallpaper>(AppsShellIds.ClsidDesktopWallpaper, typeof(IAppsDesktopWallpaper).GUID);
            if (dw == null) return result;
            try
            {
                dw.GetMonitorDevicePathCount(out var count);
                for (uint i = 0; i < count; i++)
                {
                    if (dw.GetMonitorDevicePathAt(i, out var p) < 0) continue;
                    var path = AppsCom.TakeString(p);
                    if (!string.IsNullOrEmpty(path)) result.Add(MonitorIdentity.IdFromDevicePath(path));
                }
            }
            finally { AppsCom.Release(dw); }
            return result;
        });
        Skip.If(ids.Count == 0, "IDesktopWallpaper reports no monitors (headless CI)");
        foreach (var id in ids)
        {
            var path = wallpaper.GetWallpaperPath(id);
            if (path != null) File.Exists(path).Should().BeTrue();
        }
    }

    [SkippableFact]
    public void Setting_a_wallpaper_with_invalid_input_changes_nothing_and_does_not_throw()
    {
        RequireWindows();
        using var wallpaper = new DesktopWallpaperService();
        var act = () =>
        {
            wallpaper.SetWallpaper(@"C:\definitely\missing.jpg", null);
            wallpaper.SetWallpaper("relative.jpg", null);
            wallpaper.SetWallpaper(Path.Combine(Environment.SystemDirectory, "notepad.exe"), null);
            wallpaper.SetWallpaper("", "m00000000");
        };
        act.Should().NotThrow();
    }

    // ------------------------------------------------------------------ desktop icons

    [SkippableFact]
    public void Hiding_and_restoring_the_icon_list_round_trips_and_marks_state()
    {
        RequireWindows();
        Skip.IfNot(AppsDesktopWindows.TryFind(out var layout) && layout.ListView != 0, "no Explorer desktop icon list on this machine (headless session)");
        Skip.IfNot(AppsDesktopWindows.HasVisibleStyle(layout.ListView), "the icon list is already hidden by the user; leaving it alone");

        var dir = Path.Combine(Path.GetTempPath(), "bw-icons-" + Guid.NewGuid().ToString("N"));
        var state = new DesktopIconsState(dir);
        var controller = new Win32DesktopIconsController(null, state);
        try
        {
            controller.SetNativeIconsVisible(false);
            AppsDesktopWindows.TryFind(out layout).Should().BeTrue();
            AppsDesktopWindows.HasVisibleStyle(layout.ListView).Should().BeFalse("the SysListView32 is hidden");
            state.MarkerExists.Should().BeTrue("the marker is written before hiding");
            state.ReadSnapshot()!.ListViewWasVisible.Should().BeTrue();

            // simulate the next start after a crash: a fresh restorer with nothing but the files on disk
            DesktopIconsRestorer.Restore(new DesktopIconsState(dir)).Should().BeTrue();
            AppsDesktopWindows.HasVisibleStyle(layout.ListView).Should().BeTrue("restored by the stateless restorer");
            state.MarkerExists.Should().BeFalse();
            File.Exists(state.SnapshotPath).Should().BeFalse();

            // idempotent
            DesktopIconsRestorer.Restore(new DesktopIconsState(dir)).Should().BeTrue();

            // the controller's own path
            controller.SetNativeIconsVisible(false);
            AppsDesktopWindows.HasVisibleStyle(layout.ListView).Should().BeFalse();
            controller.SetNativeIconsVisible(true);
            AppsDesktopWindows.HasVisibleStyle(layout.ListView).Should().BeTrue();
            state.MarkerExists.Should().BeFalse();
        }
        finally
        {
            controller.Restore();
            controller.Dispose();
            DesktopIconsRestorer.Restore(state);
            if (AppsDesktopWindows.TryFind(out var l) && l.ListView != 0 && !AppsDesktopWindows.HasVisibleStyle(l.ListView))
                AppsDesktopWindows.TryShow(l.ListView, show: true); // last resort: never leave the user's desktop icons hidden
            try { Directory.Delete(dir, recursive: true); } catch (Exception) { }
        }
    }

    [SkippableFact]
    public void Layer_attach_rejects_invalid_windows()
    {
        RequireWindows();
        using var controller = new Win32DesktopIconsController(null, new DesktopIconsState(Path.Combine(Path.GetTempPath(), "bw-icons-" + Guid.NewGuid().ToString("N"))));
        controller.TryAttachDesktopLayer(0, new NativeRect(0, 0, 100, 100)).Should().BeFalse();
        controller.TryAttachDesktopLayer(0x7FFFFFF1, new NativeRect(0, 0, 100, 100)).Should().BeFalse();
        using var window = new TestIconWindow(withIcon: false);
        controller.TryAttachDesktopLayer((nint)window.Handle, new NativeRect(0, 0, 0, 0)).Should().BeFalse("empty bounds");
        var act = () => controller.DetachDesktopLayer(0x7FFFFFF1);
        act.Should().NotThrow();
    }

    [SkippableFact]
    public void Controller_dispose_is_idempotent_and_restore_without_hide_is_a_no_op()
    {
        RequireWindows();
        var dir = Path.Combine(Path.GetTempPath(), "bw-icons-" + Guid.NewGuid().ToString("N"));
        var controller = new Win32DesktopIconsController(null, new DesktopIconsState(dir));
        controller.Restore();
        controller.Restore();
        controller.Dispose();
        controller.Dispose();
        Directory.Exists(dir).Should().BeFalse("nothing was ever persisted");
    }

    // ------------------------------------------------------------------ desktop folder

    [SkippableFact]
    public void Desktop_source_lists_the_two_specials_with_shell_names()
    {
        RequireWindows();
        using var source = new ShellDesktopFolderSource();
        var items = source.Enumerate();
        items.Should().Contain(e => e.Id == "special:thisPc" && e.Target == "shell:MyComputerFolder" && e.Name.Length > 0);
        items.Should().Contain(e => e.Id == "special:recycleBin" && e.Target == "shell:RecycleBinFolder" && e.Name.Length > 0);
        items.Select(e => e.Id).Should().OnlyHaveUniqueItems();
    }

    // ------------------------------------------------------------------ STA plumbing

    [SkippableFact]
    public async Task Sta_worker_runs_items_in_order_on_one_sta_thread()
    {
        RequireWindows();
        using var worker = new StaWorker("test-sta");
        var a = await worker.RunAsync(() => (Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState()));
        var b = await worker.RunAsync(() => (Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState()));
        a.Item2.Should().Be(ApartmentState.STA);
        b.Item1.Should().Be(a.Item1);
        var failing = async () => await worker.RunAsync<int>(() => throw new InvalidOperationException("x"));
        await failing.Should().ThrowAsync<InvalidOperationException>();
        (await worker.RunAsync(() => 5)).Should().Be(5, "an exception in one item does not kill the worker");
    }

    [SkippableFact]
    public async Task Sta_pool_bounds_concurrency_and_retires_hung_workers()
    {
        RequireWindows();
        using var pool = new StaPool("test-pool", 2);
        int running = 0, maxRunning = 0;
        Func<int> work = () =>
        {
            int now = Interlocked.Increment(ref running);
            int seen;
            while ((seen = Volatile.Read(ref maxRunning)) < now && Interlocked.CompareExchange(ref maxRunning, now, seen) != seen) { }
            Thread.Sleep(100);
            Interlocked.Decrement(ref running);
            return 1;
        };
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => pool.RunAsync(work, TimeSpan.FromSeconds(5), CancellationToken.None)));
        results.Should().OnlyContain(r => r == 1);
        maxRunning.Should().BeLessThanOrEqualTo(2);

        // A hung item times out ...
        var gate = new ManualResetEventSlim(false);
        var hung = async () => await pool.RunAsync(() => { gate.Wait(); return 0; }, TimeSpan.FromMilliseconds(200), CancellationToken.None);
        await hung.Should().ThrowAsync<TimeoutException>();
        // ... and the pool keeps working (a replacement worker is created).
        (await pool.RunAsync(() => 42, TimeSpan.FromSeconds(5), CancellationToken.None)).Should().Be(42);
        gate.Set();

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancelled = async () => await pool.RunAsync(() => 1, TimeSpan.FromSeconds(5), cts.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
    }
}
