using System.Text.Json.Nodes;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Core.Tests.Support.Fakes;

namespace BetterWindows.Core.Tests.Config;

public class ThemeServiceTests
{
    private sealed class Rig : IDisposable
    {
        public SettingsFixture Settings { get; } = new();
        public FakeThemeSource Source { get; } = new();
        public ThemeService Theme { get; }
        public List<ThemeState> Seen { get; } = new();
        public Rig() { Theme = new ThemeService(Settings.Service, Source); Theme.Changed += s => { lock (Seen) Seen.Add(s); }; }
        public Task Patch(string json) => Settings.Service.PatchAsync(JsonNode.Parse(json)!);
        public void Dispose() => Settings.Dispose();
        public async Task WaitForEvents(int count)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline) { lock (Seen) if (Seen.Count >= count) return; await Task.Delay(10); }
            throw new TimeoutException($"expected {count} theme events, saw {Seen.Count}");
        }
    }

    [Fact]
    public void Auto_follows_the_windows_app_theme_and_system_accent()
    {
        using var r = new Rig();
        r.Source.AppsUseLightTheme = false;
        r.Source.AccentColorHex = "#e81123";
        r.Theme.Current.Mode.Should().Be(ThemeMode.Dark);
        r.Theme.Current.Accent.Should().Be("#e81123");
        r.Source.AppsUseLightTheme = true;
        r.Theme.Current.Mode.Should().Be(ThemeMode.Light);
    }

    [Fact]
    public async Task Explicit_mode_and_accent_override_windows()
    {
        using var r = new Rig();
        r.Source.AppsUseLightTheme = true;
        await r.Patch("""{ "appearance": { "mode": "dark", "accent": "#112233" } }""");
        r.Theme.Current.Mode.Should().Be(ThemeMode.Dark);
        r.Theme.Current.Accent.Should().Be("#112233");
        await r.Patch("""{ "appearance": { "mode": "light" } }""");
        r.Source.AppsUseLightTheme = false;
        r.Theme.Current.Mode.Should().Be(ThemeMode.Light);
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    [InlineData(true, true, true, true)]
    public async Task Reduce_flags_are_the_setting_OR_the_windows_flag(bool setMotion, bool setTransp, bool winMotion, bool winTransp)
    {
        using var r = new Rig();
        await r.Patch($$"""{ "appearance": { "reduceMotion": {{setMotion.ToString().ToLowerInvariant()}}, "reduceTransparency": {{setTransp.ToString().ToLowerInvariant()}} } }""");
        r.Source.ReduceMotion = winMotion;
        r.Source.ReduceTransparency = winTransp;
        r.Theme.Current.ReduceMotion.Should().Be(setMotion || winMotion);
        r.Theme.Current.ReduceTransparency.Should().Be(setTransp || winTransp);
    }

    [Fact]
    public void A_garbage_system_accent_falls_back()
    {
        using var r = new Rig();
        r.Source.AccentColorHex = "not-a-colour";
        r.Theme.Current.Accent.Should().Be("#0a84ff");
        r.Source.AccentColorHex = "";
        r.Theme.Current.Accent.Should().Be("#0a84ff");
    }

    [Fact]
    public async Task Settings_changes_raise_Changed_only_when_the_effective_theme_changes()
    {
        using var r = new Rig();
        await r.Patch("""{ "appearance": { "mode": "dark" } }""");
        r.Seen.Should().HaveCount(1);
        r.Seen[0].Mode.Should().Be(ThemeMode.Dark);
        await r.Patch("""{ "dock": { "iconSize": 70 } }""");
        r.Seen.Should().HaveCount(1, "an unrelated setting must not republish the theme");
        await r.Patch("""{ "appearance": { "reduceMotion": true } }""");
        r.Seen.Should().HaveCount(2);
        r.Seen[1].ReduceMotion.Should().BeTrue();
    }

    [Fact]
    public async Task A_windows_personalisation_change_raises_Changed()
    {
        using var r = new Rig();
        r.Source.AppsUseLightTheme = false;
        r.Source.RaiseChanged();
        await r.WaitForEvents(1);
        r.Seen[0].Mode.Should().Be(ThemeMode.Dark);
        r.Source.AccentColorHex = "#00ff00";
        r.Source.RaiseChanged();
        await r.WaitForEvents(2);
        r.Seen[1].Accent.Should().Be("#00ff00");
        r.Source.RaiseChanged(); // nothing changed
        await Task.Delay(100);
        r.Seen.Should().HaveCount(2);
    }

    [Fact]
    public void A_throwing_theme_source_degrades_to_defaults()
    {
        using var s = new SettingsFixture();
        var theme = new ThemeService(s.Service, new ThrowingSource());
        theme.Current.Mode.Should().Be(ThemeMode.Light);
        theme.Current.Accent.Should().Be("#0a84ff");
    }

    private sealed class ThrowingSource : BetterWindows.Core.Platform.IThemeSource
    {
        public bool AppsUseLightTheme => throw new InvalidOperationException("registry");
        public string AccentColorHex => throw new InvalidOperationException("registry");
        public bool ReduceMotion => throw new InvalidOperationException("registry");
        public bool ReduceTransparency => throw new InvalidOperationException("registry");
        public event Action? Changed { add { } remove { } }
        public void SetAppsUseLightTheme(bool light) { }
    }
}
