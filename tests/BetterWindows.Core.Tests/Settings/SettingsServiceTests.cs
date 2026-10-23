using System.Text.Json.Nodes;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using SettingsDto = BetterWindows.Core.Protocol.Settings;

namespace BetterWindows.Core.Tests.Config;

public class SettingsServiceTests
{
    private static PinnedItem Pin(string n) => new() { Id = "aumid:" + n, Kind = PinnedKind.App, Target = n };

    // ---- persistence -------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Changes_are_written_after_the_debounce_not_before()
    {
        using var f = new SettingsFixture();
        await f.Service.UpdateAsync(s => s with { Dock = s.Dock with { IconSize = 70 } });
        File.Exists(f.ConfigFile).Should().BeFalse();
        f.Time.Advance(TimeSpan.FromMilliseconds(250));
        File.Exists(f.ConfigFile).Should().BeFalse("still inside the 300 ms quiet period");
        f.Time.Advance(TimeSpan.FromMilliseconds(60));
        f.ReadConfigJson()["dock"]!["iconSize"]!.GetValue<int>().Should().Be(70);
        Directory.GetFiles(f.Root, "*.tmp").Should().BeEmpty("atomic writes leave no temp files");
    }

    [Fact]
    public async Task A_burst_of_changes_collapses_into_one_write()
    {
        using var f = new SettingsFixture();
        for (var i = 0; i < 6; i++)
        {
            await f.Service.UpdateAsync(s => s with { Dock = s.Dock with { IconSize = 40 + i } });
            f.Time.Advance(TimeSpan.FromMilliseconds(100));
        }
        File.Exists(f.ConfigFile).Should().BeFalse("every change restarted the quiet period");
        f.Time.Advance(TimeSpan.FromMilliseconds(250));
        f.ReadConfigJson()["dock"]!["iconSize"]!.GetValue<int>().Should().Be(45);
        File.Exists(f.BackupFile).Should().BeFalse("one write only: there was no previous file to back up");
    }

    [Fact]
    public async Task Continuous_edits_cannot_postpone_the_write_beyond_the_max_delay()
    {
        using var f = new SettingsFixture();
        for (var i = 0; i < 40 && !File.Exists(f.ConfigFile); i++)
        {
            await f.Service.UpdateAsync(s => s with { Dock = s.Dock with { IconSize = 40 + i % 50 } });
            f.Time.Advance(TimeSpan.FromMilliseconds(200));
        }
        File.Exists(f.ConfigFile).Should().BeTrue();
    }

    [Fact]
    public async Task Flush_writes_immediately_and_is_idempotent()
    {
        using var f = new SettingsFixture();
        await f.Service.UpdateAsync(s => s with { MenuBar = s.MenuBar with { ShowSeconds = true } });
        f.Service.Flush();
        f.ReadConfigJson()["menuBar"]!["showSeconds"]!.GetValue<bool>().Should().BeTrue();
        var stamp = File.GetLastWriteTimeUtc(f.ConfigFile);
        f.Service.Flush();
        File.GetLastWriteTimeUtc(f.ConfigFile).Should().Be(stamp, "nothing pending: nothing written");
    }

    [Fact]
    public async Task Dispose_flushes_pending_changes()
    {
        using var f = new SettingsFixture();
        await f.Service.UpdateAsync(s => s with { Dock = s.Dock with { IconSize = 99 } });
        f.Service.Dispose();
        f.ReadConfigJson()["dock"]!["iconSize"]!.GetValue<int>().Should().Be(99);
    }

    [Fact]
    public async Task The_written_file_round_trips_through_a_new_instance()
    {
        using var f = new SettingsFixture();
        await f.Service.PatchAsync(JsonNode.Parse("""{ "dock": { "position": "left", "pins": [ { "id": "aumid:X", "kind": "app", "target": "X", "name": "Ex" } ] }, "desktop": { "wallpapers": { "m1": "C:\\w.png" } } }""")!);
        f.Service.Flush();
        var fresh = f.NewService();
        ProtocolJson.Serialize(fresh.Current).Should().Be(ProtocolJson.Serialize(f.Service.Current));
        fresh.Current.Dock.Pins[0].Name.Should().Be("Ex");
    }

    [Fact]
    public async Task Unknown_fields_survive_a_save_at_every_level()
    {
        using var f = new SettingsFixture("""
            { "version": 1, "futureRoot": { "a": [1, 2, 3] }, "dock": { "iconSize": 60, "futureDock": "keep" }, "general": { "future": true } }
            """);
        await f.Service.UpdateAsync(s => s with { Dock = s.Dock with { IconSize = 61 } });
        f.Service.Flush();
        var j = f.ReadConfigJson();
        j["futureRoot"]!["a"]!.ToJsonString().Should().Be("[1,2,3]");
        j["dock"]!["futureDock"]!.GetValue<string>().Should().Be("keep");
        j["general"]!["future"]!.GetValue<bool>().Should().BeTrue();
        j["dock"]!["iconSize"]!.GetValue<int>().Should().Be(61);
        f.NewService().Current.Dock.IconSize.Should().Be(61);
    }

    [Fact]
    public async Task A_null_reset_removes_the_key_from_the_file()
    {
        using var f = new SettingsFixture();
        await f.Service.PatchAsync(JsonNode.Parse("""{ "desktop": { "wallpaperPath": "C:\\a.png" } }""")!);
        f.Service.Flush();
        f.ReadConfigJson()["desktop"]!.AsObject().ContainsKey("wallpaperPath").Should().BeTrue();
        await f.Service.PatchAsync(JsonNode.Parse("""{ "desktop": { "wallpaperPath": null } }""")!);
        f.Service.Flush();
        f.ReadConfigJson()["desktop"]!.AsObject().ContainsKey("wallpaperPath").Should().BeFalse();
    }

    [Fact]
    public async Task The_previous_version_of_the_file_is_kept_as_a_backup()
    {
        using var f = new SettingsFixture();
        await f.Service.UpdateAsync(s => s with { Dock = s.Dock with { IconSize = 50 } });
        f.Service.Flush();
        await f.Service.UpdateAsync(s => s with { Dock = s.Dock with { IconSize = 60 } });
        f.Service.Flush();
        JsonNode.Parse(File.ReadAllText(f.BackupFile))!["dock"]!["iconSize"]!.GetValue<int>().Should().Be(50);
        f.ReadConfigJson()["dock"]!["iconSize"]!.GetValue<int>().Should().Be(60);
    }

    [Fact]
    public async Task The_written_file_is_indented_utf8_without_bom()
    {
        using var f = new SettingsFixture();
        await f.Service.UpdateAsync(s => s with { Weather = s.Weather with { LocationName = "Москва" } });
        f.Service.Flush();
        var bytes = File.ReadAllBytes(f.ConfigFile);
        bytes.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF });
        System.Text.Encoding.UTF8.GetString(bytes).Should().Contain("\n").And.Contain("Москва");
    }

    // ---- update / changed --------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Update_clamps_and_cannot_change_the_version()
    {
        using var f = new SettingsFixture();
        var s = await f.Service.UpdateAsync(x => x with { Version = 42, Dock = x.Dock with { IconSize = 9999, MagnifiedSize = -1 } });
        s.Version.Should().Be(1);
        s.Dock.IconSize.Should().Be(128);
        s.Dock.MagnifiedSize.Should().Be(128);
    }

    [Fact]
    public async Task A_throwing_mutation_propagates_and_changes_nothing()
    {
        using var f = new SettingsFixture();
        var act = async () => await f.Service.UpdateAsync(_ => throw new InvalidOperationException("nope"));
        await act.Should().ThrowAsync<InvalidOperationException>();
        var nul = async () => await f.Service.UpdateAsync(_ => null!);
        await nul.Should().ThrowAsync<InvalidOperationException>();
        f.Service.Current.Dock.IconSize.Should().Be(56);
        (await f.Service.UpdateAsync(x => x with { Dock = x.Dock with { IconSize = 60 } })).Dock.IconSize.Should().Be(60);
    }

    [Fact]
    public async Task Changed_carries_the_stored_value_and_is_not_raised_for_a_no_op()
    {
        using var f = new SettingsFixture();
        var seen = new List<int>();
        f.Service.Changed += s => seen.Add(s.Dock.IconSize);
        await f.Service.UpdateAsync(x => x with { Dock = x.Dock with { IconSize = 60 } });
        await f.Service.UpdateAsync(x => x with { Dock = x.Dock with { IconSize = 60 } });
        await f.Service.UpdateAsync(x => x);
        await f.Service.UpdateAsync(x => x with { Dock = x.Dock with { IconSize = 61 } });
        seen.Should().Equal(60, 61);
    }

    [Fact]
    public async Task Changed_is_raised_outside_the_write_lock()
    {
        using var f = new SettingsFixture();
        var inner = 0;
        f.Service.Changed += s =>
        {
            if (s.Dock.IconSize != 70) return;
            // another thread writes while we are inside the handler and we WAIT for it: this deadlocks if the handler runs under the lock
            Task.Run(() => f.Service.UpdateAsync(x => x with { Dock = x.Dock with { IconSize = 71 } })).Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
            inner++;
        };
        await f.Service.UpdateAsync(x => x with { Dock = x.Dock with { IconSize = 70 } });
        inner.Should().Be(1);
        f.Service.Current.Dock.IconSize.Should().Be(71);
    }

    [Fact]
    public async Task A_throwing_subscriber_does_not_stop_the_others_or_the_write()
    {
        using var f = new SettingsFixture();
        var ok = 0;
        f.Service.Changed += _ => throw new InvalidOperationException("subscriber bug");
        f.Service.Changed += _ => ok++;
        var s = await f.Service.UpdateAsync(x => x with { MenuBar = x.MenuBar with { Translucent = false } });
        s.MenuBar.Translucent.Should().BeFalse();
        ok.Should().Be(1);
    }

    [Fact]
    public async Task A_nested_update_inside_Changed_reaches_a_fixed_point_and_events_stay_ordered()
    {
        using var f = new SettingsFixture();
        var seen = new List<int>();
        f.Service.Changed += s =>
        {
            seen.Add(s.Dock.IconSize);
            if (s.Dock.IconSize < 60) _ = f.Service.UpdateAsync(x => x with { Dock = x.Dock with { IconSize = x.Dock.IconSize + 1 } });
        };
        await f.Service.UpdateAsync(x => x with { Dock = x.Dock with { IconSize = 57 } });
        f.Service.Current.Dock.IconSize.Should().Be(60);
        seen.Should().BeInAscendingOrder().And.EndWith(60);
    }

    // ---- concurrency ---------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Parallel_updates_lose_nothing()
    {
        using var f = new SettingsFixture();
        const int n = 80;
        await Task.WhenAll(Enumerable.Range(0, n).Select(i => Task.Run(() =>
            f.Service.UpdateAsync(s => s with { Dock = s.Dock with { Pins = s.Dock.Pins.Append(Pin("P" + i)).ToArray() } }))));
        f.Service.Current.Dock.Pins.Select(p => p.Id).Should().OnlyHaveUniqueItems().And.HaveCount(n);
        f.Service.Flush();
        f.NewService().Current.Dock.Pins.Should().HaveCount(n);
    }

    [Fact]
    public async Task Parallel_patches_and_updates_end_with_a_consistent_value_and_the_last_event_is_the_newest()
    {
        using var f = new SettingsFixture();
        var last = 0;
        var lastSeen = new object();
        f.Service.Changed += s => { lock (lastSeen) last = s.Dock.Pins.Count + s.Desktop.Shortcuts.Count * 1000; };
        var tasks = new List<Task>();
        for (var i = 0; i < 40; i++)
        {
            var k = i;
            tasks.Add(Task.Run(() => f.Service.UpdateAsync(s => s with { Dock = s.Dock with { Pins = s.Dock.Pins.Append(Pin("U" + k)).ToArray() } })));
            tasks.Add(Task.Run(() => f.Service.PatchAsync(JsonNode.Parse($$"""{ "desktop": { "wallpapers": { "m{{k}}": "C:\\w{{k}}.png" } } }""")!)));
        }
        await Task.WhenAll(tasks);
        f.Service.Current.Dock.Pins.Should().HaveCount(40);
        f.Service.Current.Desktop.Wallpapers.Should().HaveCount(40);
        // the single-flight drain delivers the newest value last, whatever interleaving happened
        SpinWait.SpinUntil(() => { lock (lastSeen) return last == 40; }, TimeSpan.FromSeconds(5)).Should().BeTrue();
    }

    [Fact]
    public async Task Concurrent_reads_during_writes_always_see_a_whole_value()
    {
        using var f = new SettingsFixture();
        var stop = false;
        var reader = Task.Run(() =>
        {
            while (!Volatile.Read(ref stop))
            {
                var s = f.Service.Current;
                (s.Dock.MagnifiedSize >= s.Dock.IconSize).Should().BeTrue("iconSize and magnifiedSize change together");
            }
        });
        for (var i = 0; i < 300; i++) await f.Service.UpdateAsync(s => s with { Dock = s.Dock with { IconSize = 32 + i % 90, MagnifiedSize = 32 + i % 90 } });
        Volatile.Write(ref stop, true);
        await reader;
    }

    // ---- reload / external edits ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Reload_picks_up_an_external_edit_and_raises_Changed()
    {
        using var f = new SettingsFixture();
        await f.Service.UpdateAsync(s => s with { Dock = s.Dock with { IconSize = 50 } });
        f.Service.Flush();
        var seen = new List<int>();
        f.Service.Changed += s => seen.Add(s.Dock.IconSize);
        f.WriteConfig("""{ "version": 1, "dock": { "iconSize": 90 } }""");
        f.Service.Reload();
        f.Service.Current.Dock.IconSize.Should().Be(90);
        seen.Should().Equal(90);
    }

    [Fact]
    public async Task Reload_ignores_our_own_write()
    {
        using var f = new SettingsFixture();
        await f.Service.UpdateAsync(s => s with { Dock = s.Dock with { IconSize = 50 } });
        f.Service.Flush();
        await f.Service.UpdateAsync(s => s with { Dock = s.Dock with { IconSize = 51 } }); // pending, not yet on disk
        var n = 0;
        f.Service.Changed += _ => n++;
        f.Service.Reload(); // what the file watcher does after our own write: the content hash matches, so the pending 51 must NOT be overwritten
        f.Service.Current.Dock.IconSize.Should().Be(51);
        n.Should().Be(0);
    }

    [Fact]
    public void Reload_with_identical_content_raises_nothing()
    {
        using var f = new SettingsFixture("""{ "dock": { "iconSize": 60 } }""");
        _ = f.Service.Current;
        var n = 0;
        f.Service.Changed += _ => n++;
        f.WriteConfig("""{ "dock": { "iconSize": 60 }, "x": 1 }""");
        f.Service.Reload();
        n.Should().Be(0, "the effective value did not change");
    }

    [Fact]
    public async Task An_external_edit_wins_over_a_pending_unsaved_change()
    {
        using var f = new SettingsFixture();
        await f.Service.UpdateAsync(s => s with { Dock = s.Dock with { IconSize = 50 } });
        f.Service.Flush();
        await f.Service.UpdateAsync(s => s with { MenuBar = s.MenuBar with { ShowSeconds = true } }); // pending
        f.WriteConfig("""{ "version": 1, "dock": { "iconSize": 90 } }""");
        f.Service.Reload();
        f.Service.Current.Dock.IconSize.Should().Be(90);
        f.Service.Current.MenuBar.ShowSeconds.Should().BeFalse("last writer wins: the pending change is discarded, not written over the user's edit");
        f.PassDebounce();
        f.ReadConfigJson()["dock"]!["iconSize"]!.GetValue<int>().Should().Be(90);
    }

    [Fact]
    public async Task A_corrupt_external_edit_is_quarantined_the_running_value_is_kept_and_the_file_is_recreated()
    {
        using var f = new SettingsFixture();
        await f.Service.UpdateAsync(s => s with { Dock = s.Dock with { IconSize = 50 } });
        f.Service.Flush();
        f.WriteConfig("{ half a file");
        f.Service.Reload();
        f.Service.Current.Dock.IconSize.Should().Be(50);
        f.Quarantined().Should().HaveCount(1);
        f.Time.Advance(TimeSpan.FromSeconds(1)); // the real file watcher may have queued another (harmless) reload on the manual clock: let every debounce settle
        f.ReadConfigJson()["dock"]!["iconSize"]!.GetValue<int>().Should().Be(50);
    }

    [Fact]
    public async Task A_deleted_config_is_recreated_from_memory()
    {
        using var f = new SettingsFixture();
        await f.Service.UpdateAsync(s => s with { Dock = s.Dock with { IconSize = 50 } });
        f.Service.Flush();
        File.Delete(f.ConfigFile);
        f.Service.Reload();
        f.Service.Current.Dock.IconSize.Should().Be(50);
        f.Time.Advance(TimeSpan.FromSeconds(1));
        File.Exists(f.ConfigFile).Should().BeTrue();
    }

    [Fact]
    public async Task The_file_watcher_reloads_external_edits_after_the_debounce()
    {
        using var f = new SettingsFixture();
        await f.Service.UpdateAsync(s => s with { Dock = s.Dock with { IconSize = 50 } });
        f.Service.Flush();
        var changed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Service.Changed += s => changed.TrySetResult(s.Dock.IconSize);
        File.WriteAllText(f.ConfigFile, """{ "version": 1, "dock": { "iconSize": 88 } }""");
        // the real FileSystemWatcher reports on its own thread; the debounce timer is on the manual clock, so keep advancing it until the reload fired
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!changed.Task.IsCompleted && DateTime.UtcNow < deadline)
        {
            f.Time.Advance(TimeSpan.FromMilliseconds(300));
            await Task.Delay(50);
        }
        changed.Task.IsCompleted.Should().BeTrue("the watcher should have noticed the edit");
        (await changed.Task).Should().Be(88);
    }

    [Fact]
    public async Task Our_own_atomic_write_does_not_reload_through_the_watcher()
    {
        using var f = new SettingsFixture();
        var n = 0;
        f.Service.Changed += _ => n++;
        await f.Service.UpdateAsync(s => s with { Dock = s.Dock with { IconSize = 50 } });
        f.PassDebounce(); // writes the file; the watcher will report it
        for (var i = 0; i < 10; i++) { f.Time.Advance(TimeSpan.FromMilliseconds(300)); await Task.Delay(30); }
        n.Should().Be(1, "only the original update raised Changed; the write-back was recognised as ours");
    }

    // ---- diagnostics status ----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Status_reports_the_file_and_the_last_load()
    {
        using var f = new SettingsFixture("""{ "dock": { "iconSize": 61, "position": "nope" } }""");
        _ = f.Service.Current;
        var st = f.Service.Status;
        st.FileExists.Should().BeTrue();
        st.PersistenceBlocked.Should().BeFalse();
        st.LastLoad.Should().Contain("config.json").And.Contain("reset");
        await Task.CompletedTask;
    }
}
