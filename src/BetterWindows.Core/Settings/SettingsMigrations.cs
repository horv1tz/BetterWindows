using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace BetterWindows.Core.Config;

/// <summary>One schema upgrade step: rewrites a raw config root from version <see cref="From"/> to <c>From + 1</c> (the runner sets <c>version</c> afterwards).</summary>
public sealed record SettingsMigration(int From, Action<JsonObject> Apply);

/// <summary>
/// Schema migrations keyed by <c>Settings.version</c> (docs/IPC.md section 12). Rules: a file WITHOUT a (valid) version is a hand-written file of the current schema and is not migrated;
/// a version NEWER than <see cref="CurrentVersion"/> is never migrated or downgraded (unknown parts are kept); an older version runs every step in order, after the caller has
/// written <c>config.v{n}.bak</c>. A step that throws aborts the migration: the caller then keeps the original file untouched and starts from defaults for the broken parts.
/// </summary>
public static class SettingsMigrations
{
    /// <summary>The schema version this build writes (matches the default of <c>Settings.version</c> in protocol/settings.json).</summary>
    public const int CurrentVersion = 1;

    /// <summary>All known steps. v0 -> v1: the pre-release layout (flat <c>dock.size</c>, <c>taskbar.hide</c>, <c>hotkeys.quit</c>, top-level <c>launcherHotkey</c>) became the typed sections.</summary>
    public static readonly IReadOnlyList<SettingsMigration> Default = new[] { new SettingsMigration(0, V0ToV1) };

    /// <summary>The version recorded in <paramref name="root"/>; <paramref name="target"/> when absent or not an integer (hand-written file of the current schema).</summary>
    public static int ReadVersion(JsonObject root, int target)
    {
        if (root["version"] is JsonValue v && v.TryGetValue<int>(out var n)) return n < 0 ? target : n;
        if (root["version"] is JsonValue d && d.TryGetValue<double>(out var dn) && Math.Abs(dn - Math.Round(dn)) < 1e-9 && dn is >= 0 and <= int.MaxValue) return (int)dn;
        return target;
    }

    /// <summary>Applies the steps from <paramref name="from"/> up to <paramref name="target"/> and stamps <c>version</c>. Returns true if anything ran.</summary>
    public static bool Migrate(JsonObject root, int from, int target, IReadOnlyList<SettingsMigration> steps, ILogger log)
    {
        if (from >= target) return false;
        for (var v = from; v < target; v++)
        {
            var step = steps.FirstOrDefault(s => s.From == v);
            if (step is null) { log.LogWarning("No settings migration from version {From}; continuing with the next version", v); }
            else step.Apply(root);
            root["version"] = v + 1;
            log.LogInformation("Settings migrated from version {From} to {To}", v, v + 1);
        }
        return true;
    }

    private static void V0ToV1(JsonObject root)
    {
        if (root["dock"] is JsonObject dock && dock.TryGetPropertyValue("size", out var size))
        {
            if (!dock.ContainsKey("iconSize") && size is not null) dock["iconSize"] = size.DeepClone();
            dock.Remove("size");
        }
        if (root["taskbar"] is JsonObject taskbar && taskbar.TryGetPropertyValue("hide", out var hide))
        {
            if (!taskbar.ContainsKey("mode") && hide is JsonValue hv && hv.TryGetValue<bool>(out var hidden)) taskbar["mode"] = hidden ? "hidden" : "visible";
            taskbar.Remove("hide");
        }
        if (root["hotkeys"] is JsonObject hotkeys && hotkeys.TryGetPropertyValue("quit", out var quit))
        {
            if (!hotkeys.ContainsKey("exit") && quit is not null) hotkeys["exit"] = quit.DeepClone();
            hotkeys.Remove("quit");
        }
        if (root.TryGetPropertyValue("launcherHotkey", out var launcher))
        {
            var spotlight = root["spotlight"] as JsonObject ?? new JsonObject();
            if (!spotlight.ContainsKey("hotkey") && launcher is not null) spotlight["hotkey"] = launcher.DeepClone();
            root["spotlight"] = spotlight;
            root.Remove("launcherHotkey");
        }
    }
}
