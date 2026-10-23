using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Search.Providers;

/// <summary>
/// System commands (lock, sleep, theme, taskbar, quit, ...) in English and Russian. Destructive commands (restart, shut down, log out) carry one action,
/// "confirm", that is NOT primary: the action runner executes it only when the UI names that action explicitly, so Enter alone never runs them.
/// </summary>
public sealed class CommandsProvider : ISearchProvider
{
    public const double MinScore = 0.3;
    public const int MaxResults = 6;
    public const string ConfirmActionId = "confirm";

    private sealed record Command(
        string En, string Ru, string KeywordsEn, string KeywordsRu, SearchOperation Operation, bool Destructive = false, string? ConfirmEn = null, string? ConfirmRu = null);

    private static readonly IReadOnlyList<Command> Commands = new[]
    {
        new Command("Lock Screen", "Заблокировать экран", "lock screen password away", "блокировка блокировать выход", new PowerOperation(PowerAction.Lock)),
        new Command("Sleep", "Сон", "suspend standby sleep", "спящий режим ждущий режим", new PowerOperation(PowerAction.Sleep)),
        new Command("Hibernate", "Гибернация", "hibernation hibernate", "гибернация гибернации", new PowerOperation(PowerAction.Hibernate)),
        new Command("Restart", "Перезагрузить", "reboot restart", "перезагрузка перезапуск", new PowerOperation(PowerAction.Restart), true, "Restart", "Перезагрузить"),
        new Command("Shut Down", "Выключить", "power off turn off shutdown", "выключение питание выключить", new PowerOperation(PowerAction.Shutdown), true, "Shut Down", "Выключить"),
        new Command("Log Out", "Выйти из системы", "sign out signout logout", "выход из системы учетная запись", new PowerOperation(PowerAction.Logout), true, "Log Out", "Выйти"),
        new Command("Quit BetterWindows", "Выйти из BetterWindows", "exit close shell quit", "закрыть выход завершить", new QuitOperation()),
        new Command("Show Desktop", "Показать рабочий стол", "desktop minimize all windows", "рабочий стол свернуть окна", new ShowDesktopOperation()),
        new Command("Dark Mode", "Тёмная тема", "dark theme night mode", "темная тема ночной режим", new ThemeOperation(ThemePreference.Dark)),
        new Command("Light Mode", "Светлая тема", "light theme day mode", "светлая тема дневной режим", new ThemeOperation(ThemePreference.Light)),
        new Command("Show Taskbar", "Показать панель задач", "windows taskbar show", "панель задач windows показать", new TaskbarOperation(TaskbarMode.Visible)),
        new Command("Hide Taskbar", "Скрыть панель задач", "windows taskbar hide autohide", "панель задач windows скрыть", new TaskbarOperation(TaskbarMode.Hidden)),
        new Command("BetterWindows Settings", "Настройки BetterWindows", "preferences settings better windows", "настройки параметры better windows", new ShowSettingsOperation(null)),
    };

    public SearchProviderId Id => SearchProviderId.Commands;
    public TimeSpan Timeout => TimeSpan.FromMilliseconds(200);

    public Task<IReadOnlyList<SearchHit>> QueryAsync(SearchQuery query, CancellationToken ct)
    {
        var scored = new List<(double Score, Command Command)>();
        foreach (var command in Commands)
        {
            if (ct.IsCancellationRequested) break;
            double score = Score(query.Text, command);
            if (score >= MinScore) scored.Add((Math.Round(score, 4), command));
        }

        IReadOnlyList<SearchHit> hits = scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Command.En, StringComparer.Ordinal)
            .Take(MaxResults)
            .Select(s => ToHit(s.Command, s.Score, query.Locale))
            .ToArray();
        return Task.FromResult(hits);
    }

    private static SearchHit ToHit(Command c, double score, string locale)
    {
        var actions = c.Destructive
            ? new[] { new HitAction(ConfirmActionId, Loc.Pick(locale, c.ConfirmEn ?? c.En, c.ConfirmRu ?? c.Ru), false, c.Operation) }
            : new[] { new HitAction("run", Loc.Pick(locale, "Run", "Выполнить"), true, c.Operation) };
        return new SearchHit(
            SearchProviderId.Commands,
            Title: Loc.Pick(locale, c.En, c.Ru),
            Subtitle: c.Destructive ? Loc.Pick(locale, "Needs confirmation", "Требуется подтверждение") : Loc.Pick(locale, "Command", "Команда"),
            IconUrl: null,
            Score: score,
            Actions: actions);
    }

    /// <summary>Best of the names and keywords in either language (names count fully, keywords a little less).</summary>
    private static double Score(string text, Command c)
    {
        double best = Math.Max(
            FuzzyMatcher.MatchWithLayout(text, c.En)?.Score ?? 0,
            FuzzyMatcher.MatchWithLayout(text, c.Ru)?.Score ?? 0);
        foreach (var keyword in (c.KeywordsEn + " " + c.KeywordsRu).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            best = Math.Max(best, 0.9 * (FuzzyMatcher.MatchWithLayout(text, keyword)?.Score ?? 0));
        }
        return best;
    }
}
