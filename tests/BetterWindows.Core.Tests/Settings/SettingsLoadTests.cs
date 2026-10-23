using System.Text;
using System.Text.Json.Nodes;
using BetterWindows.Core.Config;
using BetterWindows.Core.Protocol;
using SettingsDto = BetterWindows.Core.Protocol.Settings;

namespace BetterWindows.Core.Tests.Config;

public class SettingsLoadTests
{
    [Fact]
    public void Missing_file_yields_defaults_and_creates_nothing_until_a_change()
    {
        using var f = new SettingsFixture();
        f.Service.Current.Dock.IconSize.Should().Be(56);
        f.Service.Current.Taskbar.Mode.Should().Be(TaskbarMode.Visible);
        File.Exists(f.ConfigFile).Should().BeFalse();
        f.Service.Status.PersistenceBlocked.Should().BeFalse();
    }

    [Fact]
    public void Tolerant_parse_accepts_comments_and_trailing_commas()
    {
        using var f = new SettingsFixture("""
            {
              // my dock
              "dock": { "iconSize": 72, /* big */ },
              "menuBar": { "showSeconds": true, },
            }
            """);
        f.Service.Current.Dock.IconSize.Should().Be(72);
        f.Service.Current.MenuBar.ShowSeconds.Should().BeTrue();
        f.Quarantined().Should().BeEmpty();
    }

    [Fact]
    public void Utf8_bom_is_accepted()
    {
        using var f = new SettingsFixture();
        f.WriteConfigBytes(new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("""{ "dock": { "iconSize": 40 } }""")).ToArray());
        f.NewService().Current.Dock.IconSize.Should().Be(40);
        f.Quarantined().Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n  ")]
    public void Empty_file_is_an_empty_document_not_corruption(string content)
    {
        using var f = new SettingsFixture(content);
        f.Service.Current.Dock.IconSize.Should().Be(56);
        f.Quarantined().Should().BeEmpty();
        File.Exists(f.ConfigFile).Should().BeTrue("an empty file is left alone");
    }

    [Fact]
    public void Corrupt_file_is_quarantined_and_defaults_are_used()
    {
        using var f = new SettingsFixture("{ this is not json");
        f.Service.Current.Dock.IconSize.Should().Be(56);
        var q = f.Quarantined();
        q.Should().HaveCount(1);
        Path.GetFileName(q[0]).Should().MatchRegex(@"^config\.corrupt-\d{8}T\d{9}\.json$");
        File.ReadAllText(q[0]).Should().Be("{ this is not json");
        File.Exists(f.ConfigFile).Should().BeFalse("the corrupt file was moved aside, not left to be overwritten");
        f.Service.Status.PersistenceBlocked.Should().BeFalse();
    }

    [Fact]
    public void Root_that_is_not_an_object_is_corrupt()
    {
        using var f = new SettingsFixture("[1,2,3]");
        f.Service.Current.Dock.IconSize.Should().Be(56);
        f.Quarantined().Should().HaveCount(1);
    }

    [Fact]
    public void Corrupt_file_falls_back_to_a_valid_backup_and_rewrites_config()
    {
        using var f = new SettingsFixture();
        Directory.CreateDirectory(f.Root);
        File.WriteAllText(f.BackupFile, """{ "version": 1, "dock": { "iconSize": 90 } }""");
        f.WriteConfig("garbage{{");
        var svc = f.NewService();
        svc.Current.Dock.IconSize.Should().Be(90);
        f.Quarantined().Should().HaveCount(1);
        f.PassDebounce();
        f.ReadConfigJson()["dock"]!["iconSize"]!.GetValue<int>().Should().Be(90, "the recovered state is written back as config.json");
    }

    [Fact]
    public void Oversized_file_is_quarantined_not_parsed()
    {
        using var f = new SettingsFixture();
        f.WriteConfigBytes(Encoding.UTF8.GetBytes("{\"x\":\"" + new string('a', 2 * 1024 * 1024 + 10) + "\"}"));
        f.NewService().Current.Dock.IconSize.Should().Be(56);
        f.Quarantined().Should().HaveCount(1);
    }

    [Fact]
    public void Only_five_quarantined_copies_are_kept()
    {
        using var f = new SettingsFixture();
        for (var i = 0; i < 8; i++)
        {
            f.WriteConfig("broken" + i);
            f.Time.Advance(TimeSpan.FromMilliseconds(5));
            _ = f.NewService().Current;
        }
        f.Quarantined().Should().HaveCount(5);
    }

    [Fact]
    public async Task An_unreadable_file_blocks_saving_and_is_never_overwritten()
    {
        using var f = new SettingsFixture("""{ "dock": { "iconSize": 61 } }""");
        var original = f.ReadConfig();
        using (var lockStream = new FileStream(f.ConfigFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var svc = f.NewService();
            svc.Current.Dock.IconSize.Should().Be(56, "the file could not be read");
            svc.Status.PersistenceBlocked.Should().BeTrue();
            await svc.UpdateAsync(s => s with { Dock = s.Dock with { IconSize = 100 } });
            f.PassDebounce();
            svc.Flush();
        }
        f.ReadConfig().Should().Be(original, "an unreadable file must not be replaced by defaults");
    }

    [Fact]
    public void A_wrong_typed_value_resets_only_that_field()
    {
        using var f = new SettingsFixture("""
            { "dock": { "iconSize": "huge", "position": "left", "autoHide": 5, "magnification": false },
              "menuBar": "nonsense",
              "spotlight": { "maxResults": 12 } }
            """);
        var s = f.Service.Current;
        s.Dock.IconSize.Should().Be(56, "wrong type: only this field resets");
        s.Dock.AutoHide.Should().BeFalse();
        s.Dock.Position.Should().Be(DockPosition.Left);
        s.Dock.Magnification.Should().BeFalse();
        s.MenuBar.Should().BeEquivalentTo(new MenuBarSettings());
        s.Spotlight.MaxResults.Should().Be(12);
        f.Quarantined().Should().BeEmpty("bad values are not corruption");
    }

    [Fact]
    public void An_unknown_enum_string_resets_only_that_field()
    {
        using var f = new SettingsFixture("""{ "dock": { "position": "sideways", "iconSize": 64 }, "taskbar": { "mode": "autoHide" } }""");
        f.Service.Current.Dock.Position.Should().Be(DockPosition.Bottom);
        f.Service.Current.Dock.IconSize.Should().Be(64);
        f.Service.Current.Taskbar.Mode.Should().Be(TaskbarMode.AutoHide);
    }

    [Fact]
    public void Explicit_nulls_become_defaults()
    {
        using var f = new SettingsFixture("""{ "dock": { "iconSize": null, "pins": null }, "desktop": { "wallpaperPath": null, "wallpapers": null }, "spotlight": { "providers": null, "webSearchUrl": null } }""");
        var s = f.Service.Current;
        s.Dock.IconSize.Should().Be(56);
        s.Dock.Pins.Should().BeEmpty();
        s.Desktop.WallpaperPath.Should().BeNull();
        s.Spotlight.Providers.Should().HaveCount(7);
        s.Spotlight.WebSearchUrl.Should().Be(new SpotlightSettings().WebSearchUrl);
    }

    [Fact]
    public void Property_names_are_matched_case_insensitively_on_load()
    {
        using var f = new SettingsFixture("""{ "Dock": { "IconSize": 80 } }""");
        f.Service.Current.Dock.IconSize.Should().Be(80);
    }

    [Fact]
    public void Out_of_range_numbers_in_the_file_are_clamped_without_rewriting_the_file()
    {
        using var f = new SettingsFixture("""{ "dock": { "iconSize": 5000, "magnifiedSize": 1 } }""");
        f.Service.Current.Dock.IconSize.Should().Be(128);
        f.Service.Current.Dock.MagnifiedSize.Should().Be(128);
        f.PassDebounce();
        f.ReadConfig().Should().Contain("5000", "loading alone never rewrites the user's file");
    }

    // ---- migrations --------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_version_0_file_is_migrated_and_the_original_is_backed_up_once()
    {
        const string v0 = """{ "version": 0, "dock": { "size": 70 }, "taskbar": { "hide": false }, "hotkeys": { "quit": "Ctrl+Alt+K" }, "launcherHotkey": "Ctrl+Space", "custom": 7 }""";
        using var f = new SettingsFixture(v0);
        var s = f.Service.Current;
        s.Dock.IconSize.Should().Be(70);
        s.Taskbar.Mode.Should().Be(TaskbarMode.Visible);
        s.Hotkeys.Exit.Should().Be("Ctrl+Alt+K");
        s.Spotlight.Hotkey.Should().Be("Ctrl+Space");
        s.Version.Should().Be(1);
        File.ReadAllText(Path.Combine(f.Root, "config.v0.bak")).Should().Be(v0);
        f.PassDebounce();
        var written = f.ReadConfigJson();
        written["version"]!.GetValue<int>().Should().Be(1);
        written["dock"]!.AsObject().ContainsKey("size").Should().BeFalse();
        written["custom"]!.GetValue<int>().Should().Be(7, "unknown keys survive migration");
        // the backup is written once: a second start does not replace it
        File.WriteAllText(Path.Combine(f.Root, "config.v0.bak"), "keep");
        f.WriteConfig(v0);
        f.NewService().Current.Dock.IconSize.Should().Be(70);
        File.ReadAllText(Path.Combine(f.Root, "config.v0.bak")).Should().Be("keep");
    }

    [Fact]
    public void A_file_without_a_version_is_current_and_not_migrated()
    {
        using var f = new SettingsFixture("""{ "dock": { "size": 70, "iconSize": 50 } }""");
        f.Service.Current.Dock.IconSize.Should().Be(50);
        Directory.GetFiles(f.Root, "config.v*.bak").Should().BeEmpty();
    }

    [Fact]
    public async Task A_newer_version_is_not_migrated_downgraded_or_stripped()
    {
        using var f = new SettingsFixture("""{ "version": 99, "dock": { "iconSize": 70, "futureThing": { "a": 1 } }, "futureSection": [1,2] }""");
        f.Service.Current.Version.Should().Be(99);
        f.Service.Current.Dock.IconSize.Should().Be(70);
        await f.Service.UpdateAsync(s => s with { MenuBar = s.MenuBar with { ShowSeconds = true } });
        f.PassDebounce();
        var written = f.ReadConfigJson();
        written["version"]!.GetValue<int>().Should().Be(99);
        written["futureSection"]!.ToJsonString().Should().Be("[1,2]");
        written["dock"]!["futureThing"]!["a"]!.GetValue<int>().Should().Be(1);
        Directory.GetFiles(f.Root, "config.v*.bak").Should().BeEmpty();
    }

    [Fact]
    public void Migration_steps_run_in_order_up_to_the_target_version()
    {
        var steps = new[]
        {
            new SettingsMigration(1, r => r["log"] = ((r["log"] as JsonValue)?.GetValue<string>() ?? "") + "1>2;"),
            new SettingsMigration(2, r => r["log"] = ((r["log"] as JsonValue)?.GetValue<string>() ?? "") + "2>3;"),
        };
        using var f = new SettingsFixture("""{ "version": 1 }""", steps, targetVersion: 3);
        f.Service.Current.Version.Should().Be(3);
        f.PassDebounce();
        var written = f.ReadConfigJson();
        written["log"]!.GetValue<string>().Should().Be("1>2;2>3;");
        written["version"]!.GetValue<int>().Should().Be(3);
        File.Exists(Path.Combine(f.Root, "config.v1.bak")).Should().BeTrue();
    }

    [Fact]
    public void A_failing_migration_keeps_the_original_and_still_loads()
    {
        var steps = new[] { new SettingsMigration(1, _ => throw new InvalidOperationException("boom")) };
        using var f = new SettingsFixture("""{ "version": 1, "dock": { "iconSize": 77 } }""", steps, targetVersion: 2);
        f.Service.Current.Dock.IconSize.Should().Be(77);
        f.Quarantined().Should().BeEmpty();
        File.Exists(Path.Combine(f.Root, "config.v1.bak")).Should().BeTrue();
    }

    [Fact]
    public void Unknown_content_beyond_the_budget_is_dropped()
    {
        using var f = new SettingsFixture("{ \"junk\": \"" + new string('x', 300 * 1024) + "\", \"dock\": { \"iconSize\": 70 } }");
        f.Service.Current.Dock.IconSize.Should().Be(70);
        f.PassDebounce();
        f.ReadConfigJson().ContainsKey("junk").Should().BeFalse();
    }
}
