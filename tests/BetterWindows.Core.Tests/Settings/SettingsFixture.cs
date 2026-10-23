using System.Text;
using System.Text.Json.Nodes;
using BetterWindows.Core.Config;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Core.Tests.Support;

namespace BetterWindows.Core.Tests.Config;

/// <summary>A SettingsService over an isolated temp directory and a manual clock (no CoreHarness: the store is tested on its own).</summary>
public sealed class SettingsFixture : IDisposable
{
    private readonly List<SettingsService> _services = new();
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "bw-settings-" + Guid.NewGuid().ToString("N"));
    public AppPaths Paths { get; }
    public ManualTimeProvider Time { get; } = new();
    public SettingsService Service { get; private set; }
    public string ConfigFile => Paths.ConfigFile;
    public string BackupFile => Paths.ConfigFile + ".bak";

    public SettingsFixture(string? initialConfig = null, IReadOnlyList<SettingsMigration>? migrations = null, int? targetVersion = null)
    {
        Paths = new AppPaths(Root);
        if (initialConfig is not null) WriteConfig(initialConfig);
        Service = NewService(migrations, targetVersion);
    }

    /// <summary>A fresh service instance over the same directory (simulates an app restart).</summary>
    public SettingsService NewService(IReadOnlyList<SettingsMigration>? migrations = null, int? targetVersion = null)
    {
        var s = new SettingsService(Paths, Time, null, migrations ?? SettingsMigrations.Default, targetVersion ?? SettingsMigrations.CurrentVersion);
        _services.Add(s);
        Service = s;
        return s;
    }

    public void WriteConfig(string text) { Directory.CreateDirectory(Root); File.WriteAllText(ConfigFile, text, new UTF8Encoding(false)); }
    public void WriteConfigBytes(byte[] bytes) { Directory.CreateDirectory(Root); File.WriteAllBytes(ConfigFile, bytes); }
    public string ReadConfig() => File.ReadAllText(ConfigFile);
    public JsonObject ReadConfigJson() => (JsonObject)JsonNode.Parse(ReadConfig())!;
    public string[] Quarantined() => Directory.Exists(Root) ? Directory.GetFiles(Root, "config.corrupt-*.json").OrderBy(f => f, StringComparer.Ordinal).ToArray() : Array.Empty<string>();
    public void SaveNow() => Service.Flush();
    public void PassDebounce() => Time.Advance(TimeSpan.FromMilliseconds(350));

    public static JsonNode Patch(string json) => JsonNode.Parse(json)!;

    public void Dispose()
    {
        foreach (var s in _services) s.Dispose();
        try { if (Directory.Exists(Root)) Directory.Delete(Root, true); } catch { /* best effort */ }
    }
}
