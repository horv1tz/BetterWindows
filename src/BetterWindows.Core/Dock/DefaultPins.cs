using BetterWindows.Core.Protocol;
using BetterWindows.Core.WindowTracking;

namespace BetterWindows.Core.Dock;

/// <summary>
/// First-run seeding of the dock: picks well-known Windows apps out of the installed-app catalog. Every candidate is matched in three tiers - AUMID pattern,
/// executable file name, display name (English and Russian) - so it works on localised systems and when an app ships under a different AUMID.
/// Used when <c>dock.pins</c> is empty and <c>general.firstRunDone</c> is false (see <see cref="ShouldSeed"/>).
/// </summary>
public static class DefaultPins
{
    private sealed record Candidate(string[] AumidContains, string[] ExeNames, string[] DisplayNames);

    // Order = dock order. Each slot lists alternatives: the first alternative the catalog contains wins, the others are skipped (Terminal falls back to PowerShell,
    // then Command Prompt). AUMID fragments are matched case-insensitively with "contains"; the trailing "_" anchors the package name
    // ("Microsoft.WindowsTerminal_" does not match Windows Terminal Preview).
    private const string System32 = "{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}";
    private static readonly Candidate[][] Slots =
    {
        // File Explorer
        new[] { new Candidate(new[] { "Microsoft.Windows.Explorer" }, new[] { "explorer.exe" }, new[] { "File Explorer", "Проводник", "Windows Explorer" }) },
        // Microsoft Edge
        new[] { new Candidate(new[] { "Microsoft.MicrosoftEdge.Stable_", "!MSEdge", "Microsoft.MicrosoftEdge_" }, new[] { "msedge.exe" }, new[] { "Microsoft Edge" }) },
        // Terminal
        new[]
        {
            new Candidate(new[] { "Microsoft.WindowsTerminal_" }, new[] { "wt.exe", "WindowsTerminal.exe" }, new[] { "Terminal", "Windows Terminal", "Терминал", "Терминал Windows" }),
            new Candidate(new[] { System32 + @"\WindowsPowerShell\v1.0\powershell.exe" }, new[] { "powershell.exe" }, new[] { "Windows PowerShell" }),
            new Candidate(new[] { System32 + @"\cmd.exe" }, new[] { "cmd.exe" }, new[] { "Command Prompt", "Командная строка" }),
        },
        // Notepad
        new[] { new Candidate(new[] { "Microsoft.WindowsNotepad_" }, new[] { "notepad.exe" }, new[] { "Notepad", "Блокнот" }) },
        // Calculator
        new[] { new Candidate(new[] { "Microsoft.WindowsCalculator_" }, new[] { "calc.exe", "CalculatorApp.exe" }, new[] { "Calculator", "Калькулятор" }) },
        // Settings
        new[] { new Candidate(new[] { "windows.immersivecontrolpanel_" }, new[] { "SystemSettings.exe" }, new[] { "Settings", "Параметры" }) },
        // Microsoft Store
        new[] { new Candidate(new[] { "Microsoft.WindowsStore_" }, new[] { "WinStore.App.exe" }, new[] { "Microsoft Store" }) },
        // Photos
        new[] { new Candidate(new[] { "Microsoft.Windows.Photos_" }, new[] { "Microsoft.Photos.exe" }, new[] { "Photos", "Фотографии", "Фото" }) },
        // Mail
        new[] { new Candidate(new[] { "microsoft.windowscommunicationsapps_8wekyb3d8bbwe!microsoft.windowslive.mail", "Microsoft.OutlookForWindows_" }, Array.Empty<string>(), new[] { "Mail", "Почта", "Outlook" }) },
    };

    /// <summary>Seed only on a fresh profile: nothing pinned yet and first run not completed (a user who removed every pin is never re-seeded).</summary>
    public static bool ShouldSeed(Settings settings) => !settings.General.FirstRunDone && settings.Dock.Pins.Count == 0;

    /// <summary>The default pins available in <paramref name="catalog"/>, in dock order. Slots the catalog cannot fill are skipped; ids are unique.</summary>
    public static IReadOnlyList<PinnedItem> Seed(IReadOnlyList<AppEntry> catalog)
    {
        var result = new List<PinnedItem>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var slot in Slots)
        {
            foreach (var candidate in slot)
            {
                var entry = Match(catalog, candidate);
                if (entry is null) continue;
                var target = !string.IsNullOrWhiteSpace(entry.Aumid) ? entry.Aumid! : entry.Path;
                if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(entry.Id)) continue;
                if (ids.Add(entry.Id)) result.Add(new PinnedItem { Id = entry.Id, Kind = PinnedKind.App, Target = target, Name = entry.Name });
                break; // this slot is filled (or its app is already pinned through another slot)
            }
        }
        return result.Take(PinsEditor.MaxPins).ToList();
    }

    private static AppEntry? Match(IReadOnlyList<AppEntry> catalog, Candidate c)
    {
        foreach (var fragment in c.AumidContains)
        {
            var hit = catalog.FirstOrDefault(e => !string.IsNullOrEmpty(e.Aumid) && e.Aumid.Contains(fragment, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return hit;
        }
        foreach (var exe in c.ExeNames)
        {
            var hit = catalog.FirstOrDefault(e => AppNames.IsFile(e.Path, exe));
            if (hit is not null) return hit;
        }
        foreach (var name in c.DisplayNames)
        {
            var hit = catalog.FirstOrDefault(e => string.Equals(e.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return hit;
        }
        return null;
    }
}
