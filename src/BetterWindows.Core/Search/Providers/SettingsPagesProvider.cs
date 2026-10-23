using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Search.Providers;

/// <summary>
/// Windows Settings pages (ms-settings: URIs) matched on their English and Russian names and keywords. Opening goes through IAppLauncher.Open after the URI is
/// checked with <see cref="Services.InputPolicy.IsMsSettingsUri"/>. The table is static data; keyword matches rank slightly below name matches.
/// </summary>
public sealed class SettingsPagesProvider : ISearchProvider
{
    public const double MinScore = 0.3;
    public const int MaxResults = 6;
    private const double KeywordDiscount = 0.9;

    /// <summary>One settings page: the URI, the English and Russian names, and space-separated keywords in each language.</summary>
    public sealed record Page(string Uri, string En, string Ru, string KeywordsEn, string KeywordsRu);

    /// <summary>The pages Spotlight offers (about 45; the list follows the Windows 11 settings app).</summary>
    public static readonly IReadOnlyList<Page> Pages = new[]
    {
        new Page("ms-settings:display", "Display", "Дисплей", "screen monitor resolution brightness night light scale", "экран монитор разрешение яркость ночной свет масштаб"),
        new Page("ms-settings:sound", "Sound", "Звук", "volume speakers microphone output input audio", "громкость динамики микрофон вывод ввод аудио"),
        new Page("ms-settings:notifications", "Notifications", "Уведомления", "alerts notification center banners", "оповещения центр уведомлений баннеры"),
        new Page("ms-settings:quiethours", "Focus", "Фокусировка внимания", "focus assist do not disturb quiet hours", "фокусировка внимания не беспокоить тихие часы"),
        new Page("ms-settings:powersleep", "Power & battery", "Питание и батарея", "power sleep battery energy screen timeout", "питание сон батарея энергия отключение экрана"),
        new Page("ms-settings:batterysaver", "Battery saver", "Режим экономии заряда", "battery saver energy", "экономия заряда батареи"),
        new Page("ms-settings:storagesense", "Storage", "Хранилище", "disk space cleanup storage sense", "диск место очистка хранилище"),
        new Page("ms-settings:crossdevice", "Nearby sharing", "Обмен с окружающими устройствами", "share nearby sharing bluetooth", "обмен поделиться рядом устройства"),
        new Page("ms-settings:multitasking", "Multitasking", "Многозадачность", "snap windows virtual desktops alt tab", "окна привязка виртуальные рабочие столы"),
        new Page("ms-settings:bluetooth", "Bluetooth", "Bluetooth", "devices pair wireless", "устройства сопряжение беспроводной"),
        new Page("ms-settings:printers", "Printers & scanners", "Принтеры и сканеры", "printer scanner print", "принтер сканер печать"),
        new Page("ms-settings:mousetouchpad", "Mouse", "Мышь", "pointer cursor scroll mouse", "указатель курсор прокрутка мышь"),
        new Page("ms-settings:devices-touchpad", "Touchpad", "Сенсорная панель", "trackpad gestures touchpad", "тачпад жесты сенсорная панель"),
        new Page("ms-settings:typing", "Typing", "Ввод текста", "keyboard autocorrect spelling typing", "клавиатура автозамена орфография ввод"),
        new Page("ms-settings:network-status", "Network & internet", "Сеть и Интернет", "network internet status proxy", "сеть интернет статус прокси"),
        new Page("ms-settings:network-wifi", "Wi-Fi", "Wi-Fi", "wireless wifi network networks", "беспроводная сеть wifi сети"),
        new Page("ms-settings:network-ethernet", "Ethernet", "Ethernet", "cable network lan", "кабель сеть проводная"),
        new Page("ms-settings:network-vpn", "VPN", "VPN", "virtual private network tunnel", "виртуальная частная сеть туннель"),
        new Page("ms-settings:network-airplanemode", "Airplane mode", "Режим «в самолете»", "airplane flight offline radio", "самолет авиарежим офлайн"),
        new Page("ms-settings:network-mobilehotspot", "Mobile hotspot", "Мобильный хот-спот", "hotspot tethering share connection", "точка доступа хот-спот раздача интернета"),
        new Page("ms-settings:personalization-background", "Background", "Фон", "wallpaper desktop background picture slideshow", "обои фон рабочий стол картинка"),
        new Page("ms-settings:personalization-colors", "Colors", "Цвета", "accent color dark light mode colours", "акцентный цвет темный светлый режим цвета"),
        new Page("ms-settings:themes", "Themes", "Темы", "theme appearance look", "тема оформление внешний вид"),
        new Page("ms-settings:lockscreen", "Lock screen", "Экран блокировки", "lock screen wallpaper notifications", "экран блокировки обои уведомления"),
        new Page("ms-settings:taskbar", "Taskbar", "Панель задач", "taskbar pin icons corner overflow", "панель задач закрепить значки"),
        new Page("ms-settings:personalization-start", "Start", "Пуск", "start menu recent folders", "меню пуск недавние папки"),
        new Page("ms-settings:defaultapps", "Default apps", "Приложения по умолчанию", "default browser file associations", "приложения по умолчанию браузер ассоциации файлов"),
        new Page("ms-settings:appsfeatures", "Installed apps", "Установленные приложения", "uninstall programs apps features remove", "удалить программы приложения установленные"),
        new Page("ms-settings:startupapps", "Startup apps", "Автозагрузка", "startup boot autostart launch at sign in", "автозагрузка запуск при входе"),
        new Page("ms-settings:yourinfo", "Your info", "Сведения об учетной записи", "account profile picture user", "учетная запись профиль пользователь"),
        new Page("ms-settings:emailandaccounts", "Email & accounts", "Электронная почта и учетные записи", "mail account microsoft account work school", "почта учетные записи"),
        new Page("ms-settings:signinoptions", "Sign-in options", "Параметры входа", "password pin windows hello fingerprint face", "пароль пин windows hello отпечаток лицо вход"),
        new Page("ms-settings:dateandtime", "Date & time", "Дата и время", "clock time zone date", "часы часовой пояс дата время"),
        new Page("ms-settings:regionlanguage", "Language & region", "Язык и регион", "language region format locale keyboard", "язык регион формат раскладка"),
        new Page("ms-settings:gaming-gamebar", "Gaming", "Игры", "xbox game bar capture games", "игры xbox игровая панель запись"),
        new Page("ms-settings:easeofaccess", "Accessibility", "Специальные возможности", "accessibility narrator magnifier contrast", "специальные возможности экранный диктор лупа контрастность"),
        new Page("ms-settings:privacy", "Privacy & security", "Конфиденциальность и безопасность", "privacy security permissions", "конфиденциальность безопасность разрешения"),
        new Page("ms-settings:privacy-webcam", "Camera", "Камера", "webcam privacy camera permissions", "камера веб-камера конфиденциальность"),
        new Page("ms-settings:privacy-microphone", "Microphone", "Микрофон", "mic privacy microphone permissions", "микрофон конфиденциальность"),
        new Page("ms-settings:privacy-location", "Location", "Расположение", "gps location privacy position", "местоположение геолокация конфиденциальность"),
        new Page("ms-settings:windowsupdate", "Windows Update", "Центр обновления Windows", "update upgrade patch security updates", "обновление центр обновления windows"),
        new Page("ms-settings:about", "About", "О системе", "about pc device specifications computer name", "о системе устройство характеристики имя компьютера"),
        new Page("ms-settings:recovery", "Recovery", "Восстановление", "reset restore recovery reinstall", "восстановление сброс переустановка"),
        new Page("ms-settings:activation", "Activation", "Активация", "license product key activation", "активация лицензия ключ продукта"),
        new Page("ms-settings:troubleshoot", "Troubleshoot", "Устранение неполадок", "fix problems troubleshooter diagnose", "неполадки исправление диагностика"),
        new Page("ms-settings:developers", "For developers", "Для разработчиков", "developer mode dev", "режим разработчика"),
    };

    private readonly Func<IReadOnlyList<Page>> _pages;

    public SettingsPagesProvider() : this(() => Pages) { }

    /// <summary>Test seam: a different page table.</summary>
    internal SettingsPagesProvider(Func<IReadOnlyList<Page>> pages) => _pages = pages;

    public SearchProviderId Id => SearchProviderId.Settings;
    public TimeSpan Timeout => TimeSpan.FromMilliseconds(200);

    public Task<IReadOnlyList<SearchHit>> QueryAsync(SearchQuery query, CancellationToken ct)
    {
        var scored = new List<(double Score, Page Page)>();
        foreach (var page in _pages())
        {
            if (ct.IsCancellationRequested) break;
            double score = Score(query.Text, page);
            if (score >= MinScore) scored.Add((Math.Round(score, 4), page));
        }

        IReadOnlyList<SearchHit> hits = scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Page.En, StringComparer.Ordinal)
            .Take(MaxResults)
            .Select(s => new SearchHit(
                Id,
                Title: Loc.Pick(query.Locale, s.Page.En, s.Page.Ru),
                Subtitle: Loc.Pick(query.Locale, "Settings", "Параметры"),
                IconUrl: null,
                Score: s.Score,
                Actions: new[] { new HitAction("open", Loc.Pick(query.Locale, "Open", "Открыть"), true, new OpenUriOperation(s.Page.Uri)) }))
            .ToArray();
        return Task.FromResult(hits);
    }

    /// <summary>Best of the names (full weight) and each keyword (discounted), in either language.</summary>
    internal static double Score(string text, Page page)
    {
        double best = Math.Max(
            FuzzyMatcher.MatchWithLayout(text, page.En)?.Score ?? 0,
            FuzzyMatcher.MatchWithLayout(text, page.Ru)?.Score ?? 0);
        foreach (var keyword in (page.KeywordsEn + " " + page.KeywordsRu).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            best = Math.Max(best, KeywordDiscount * (FuzzyMatcher.MatchWithLayout(text, keyword)?.Score ?? 0));
        }
        return best;
    }
}
