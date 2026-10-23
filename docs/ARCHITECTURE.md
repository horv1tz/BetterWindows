# BetterWindows — техническая архитектура

> **Статус реализации.** Этот документ описывает целевой дизайн и перечень Windows API. Фактическая структура кода
> уточнена: см. [AGENTS.md](../AGENTS.md) (слои, порты, владение файлами) и [IPC.md](IPC.md). Отличия от текста ниже:
> `BetterWindows.Interop`/`Ipc` слиты в `BetterWindows.Windows` (namespace `BetterWindows.Native`) и `BetterWindows.Core`;
> вся логика сервисов вынесена в кроссплатформенный Core за интерфейсами‑портами (тестируется на Linux);
> протокол описан в `protocol/*.json` и генерируется в C# и TypeScript; поверхность WebView всего четыре
> (dock, menubar, overlay, desktop), все всплывающие меню рисует overlay.

Документ описывает, **как** реализуется каждая часть оболочки: процессы, окна, IPC,
сервисы и конкретные Windows API. План и порядок работ — в `docs/PLAN.md`.

---

## 1. Общая схема

```
┌───────────────────────────── BetterWindows.Host (WPF, .NET 8) ─────────────────────────────┐
│                                                                                            │
│  Окна‑хосты (каждое = WPF Window без рамки + WebView2):                                     │
│   • MenuBarWindow  × по одному на монитор   (AppBar, верх, WS_EX_NOACTIVATE|TOOLWINDOW)    │
│   • DockWindow     × 1 (или на монитор)     (AppBar/overlay, низ, auto‑hide)               │
│   • OverlayWindow  × 1                       (Spotlight / Launchpad / Mission Control /     │
│                                               Control Center — один WebView, разные роуты) │
│   • DesktopWindow  × по одному на монитор   (слой за иконками: обои, ярлыки, виджеты)      │
│                                                                                            │
│  Shell‑сервисы (BetterWindows.Core, без UI):                                                │
│   WindowTracker · AppCatalog · IconService · AppBarService · TaskbarControl ·              │
│   HotkeyService · HotCornerService · MediaService · SystemStatusService ·                  │
│   VirtualDesktopService · CaptureService · SearchService · SettingsStore · ProfileStore    │
│                                                                                            │
│  IPC‑шина: Host ⇄ WebView  (PostWebMessageAsJson / WebMessageReceived, JSON‑RPC‑подобно)   │
└────────────────────────────────────────────────────────────────────────────────────────────┘
          │ Win32 / COM / WinRT                                         │ HTTPS‑подобный
          ▼                                                             ▼ виртуальный хост
   explorer.exe (остаётся shell), DWM, приложения пользователя     https://app.local/… (wwwroot)
```

Принципы:

1. **Один процесс .NET**, несколько окон. WebView2 запускает свои процессы рендерера,
   но все WebView делят одно `CoreWebView2Environment` (одна папка user‑data, общий
   кэш, меньше памяти).
2. **Core ничего не знает о WebView.** Сервисы публикуют события и принимают
   команды; окна‑хосты лишь транслируют их в IPC. Это позволяет тестировать Core
   юнит‑тестами и подменять UI.
3. **UI = обычное веб‑приложение.** В браузере оно работает с mock‑IPC, поэтому
   верстать и отлаживать анимации можно без Windows‑хоста (и в этом контейнере).
4. **Всё недокументированное — за флагом** (`Features.*` в настройках) и с откатом.

---

## 2. Проекты и зависимости

| Проект | TFM | Ключевые пакеты |
|---|---|---|
| `BetterWindows.Host` | `net8.0-windows10.0.19041.0`, `UseWPF=true` | `Microsoft.Web.WebView2`, `H.NotifyIcon.Wpf` (иконка в трее), `Microsoft.Extensions.Hosting` (DI/логирование), `Serilog` |
| `BetterWindows.Core` | `net8.0-windows10.0.19041.0` | `NAudio` (Core Audio), `System.Data.OleDb` (Windows Search), `ManagedShell` (опционально, Apache‑2.0) |
| `BetterWindows.Interop` | `net8.0-windows` | `Microsoft.Windows.CsWin32` (генерация P/Invoke из `NativeMethods.txt`) + ручные COM‑интерфейсы |
| `BetterWindows.Ipc` | `net8.0` | только `System.Text.Json` — контракты сообщений |
| `ui/` | Node 20, Vite, TypeScript, Svelte | `fuse.js` (нечёткий поиск), `@lucide/svelte` |

`app.manifest`: `dpiAwareness=PerMonitorV2`, `longPathAware`, без `requireAdministrator`.

Почему WinRT‑TFM 19041: даёт проекции `Windows.Media.Control`, `Windows.Networking.Connectivity`,
`Windows.System.Power`, `Windows.Graphics.Capture`, `Windows.Devices.*` без дополнительных пакетов.

---

## 3. Окна‑хосты и WebView2

### 3.1 Прозрачность, блюр, поведение окна

Требования к доку/строке меню: прозрачный фон со скруглениями, блюр за ними,
не отбирать фокус, не появляться в Alt+Tab, быть поверх окон.

- Стили: `WindowStyle=None`, `ResizeMode=NoResize`, `ShowInTaskbar=false`,
  `Topmost=true`; через `SetWindowLongPtr(GWL_EXSTYLE)` добавить
  `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`. `WS_EX_NOACTIVATE` критичен: иначе клик по
  доку делает активным наш процесс, и «имя активного приложения» в строке меню
  превращается в «BetterWindows».
- Прозрачность: **не** использовать `AllowsTransparency=True` с классическим
  `WebView2` (слойное окно ломает рендер и производительность HWND‑хоста). Два
  рабочих варианта, выбираем по результату спайка A:
  1. `WebView2CompositionControl` (WPF, композиция через Visual‑слой) +
     `DefaultBackgroundColor = Transparent` + `AllowsTransparency=True`;
  2. классический `WebView2` + окно без слоя + `DwmExtendFrameIntoClientArea(hwnd, {-1,-1,-1,-1})`,
     `DefaultBackgroundColor = Transparent`.
  В обоих случаях переменная окружения `WEBVIEW2_DEFAULT_BACKGROUND_COLOR=00FFFFFF`
  убирает белую вспышку до инициализации. Допускается только полная прозрачность
  (alpha 0 или 255) — полупрозрачный фон рисуем уже в CSS.
- Блюр/акрил: Windows 11 22H2+ — `DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_TRANSIENTWINDOW)`;
  на более старых сборках — `SetWindowCompositionAttribute(ACCENT_ENABLE_ACRYLICBLURBEHIND)`
  (недокументировано → за флагом). Скругления — CSS (окно прозрачно) либо
  `DWMWA_WINDOW_CORNER_PREFERENCE`.
- Тень под доком — CSS `filter: drop-shadow` внутри прозрачного окна.

### 3.2 Настройка CoreWebView2

```csharp
var env = await CoreWebView2Environment.CreateAsync(
    userDataFolder: Path.Combine(LocalAppData, "BetterWindows", "WebView2"));

await webView.EnsureCoreWebView2Async(env);
var s = webView.CoreWebView2.Settings;
s.AreDefaultContextMenusEnabled = false; s.AreBrowserAcceleratorKeysEnabled = false;
s.IsZoomControlEnabled = false; s.IsStatusBarEnabled = false; s.IsPinchZoomEnabled = false;
s.AreDevToolsEnabled = Debugger.IsAttached;
webView.CoreWebView2.SetVirtualHostNameToFolderMapping("app.local", wwwrootPath,
    CoreWebView2HostResourceAccessKind.DenyCors);
webView.CoreWebView2.Navigate("https://app.local/index.html#/dock");
```

Один `wwwroot`, разные поверхности выбираются хэш‑роутом (`#/dock`, `#/menubar`,
`#/overlay`, `#/desktop`). Картинки, которых нет на диске (иконки окон, снимки окон),
отдаём через `WebResourceRequested` для `https://app.local/dyn/*` прямо из памяти.

### 3.3 IPC

Протокол — простой JSON «конверт», общий для всех поверхностей:

```jsonc
// host → web (событие)
{ "v": 1, "kind": "event", "type": "windows.changed", "payload": { "windows": [ … ] } }
// web → host (команда с ответом)
{ "v": 1, "kind": "call", "id": "c42", "type": "apps.launch", "payload": { "id": "…" } }
{ "v": 1, "kind": "result", "id": "c42", "ok": true, "payload": null }
```

- Host: `CoreWebView2.PostWebMessageAsJson` / `WebMessageReceived`.
  `AddHostObjectToScript` не используем (COM‑marshalling, медленно, хрупко).
- Контракты в `BetterWindows.Ipc` (C# records) и зеркально в `ui/src/lib/ipc/types.ts`
  (генерировать из C# через `TypeGen`/`Reinforced.Typings` или держать вручную с тестом
  на совпадение схем).
- В браузере `window.chrome.webview` отсутствует → подключается `MockBridge` с
  фикстурами (список окон, иконки, батарея), что позволяет UI‑разработку без хоста.
- Большие бинарные данные (снимки окон) не гоняем через JSON: web запрашивает
  `https://app.local/dyn/thumb/{hwnd}?t=…`, host отвечает PNG из `CaptureService`.

---

## 4. Сервисы Core и Windows API

### 4.1 WindowTracker — список окон «как в панели задач»

Начальный снимок — `EnumWindows`, далее только события (никакого polling):

- `SetWinEventHook(EVENT_MIN..EVENT_MAX, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS)`,
  интересующие события: `EVENT_OBJECT_CREATE/DESTROY/SHOW/HIDE`, `EVENT_SYSTEM_FOREGROUND`,
  `EVENT_OBJECT_NAMECHANGE`, `EVENT_SYSTEM_MINIMIZESTART/END`, `EVENT_OBJECT_CLOAKED/UNCLOAKED`,
  `EVENT_OBJECT_LOCATIONCHANGE` (троттлинг, нужен для детекции полноэкранных окон и
  для оверлея «светофор»).
- Дополнительно `RegisterShellHookWindow` + `WM_SHELLHOOKMESSAGE`
  (`HSHELL_WINDOWCREATED/DESTROYED/ACTIVATED`, `HSHELL_FLASH` — «приложение требует
  внимания», `HSHELL_REDRAW`). Это именно то, чем пользуется панель задач; ManagedShell
  реализует это готово.

Фильтр «показывать в доке» (эмпирика панели задач):

```
IsWindowVisible(h)
&& !cloaked                                  // DwmGetWindowAttribute(DWMWA_CLOAKED) == 0
&& GetAncestor(h, GA_ROOTOWNER) == h        // top-level без владельца (или владелец скрыт)
&& !(exstyle & WS_EX_TOOLWINDOW) || (exstyle & WS_EX_APPWINDOW)
&& class not in { "Progman", "WorkerW", "Shell_TrayWnd", "Windows.UI.Core.CoreWindow"(cloaked) , наши }
&& (ITaskbarList‑hidden не учитываем: такие окна не видны через публичный API; редкий случай)
```

Идентичность приложения (для группировки и иконки):

1. `SHGetPropertyStoreForWindow(h)` → `PKEY_AppUserModel_ID` (явный AUMID окна);
2. иначе `GetApplicationUserModelId(hProcess)` (упакованные/UWP);
3. иначе путь exe: `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` +
   `QueryFullProcessImageName`. UWP‑окна хостятся в `ApplicationFrameHost.exe` — для
   них берём AUMID из дочернего `Windows.UI.Core.CoreWindow`.

Команды:

- Активировать: окна у нас `WS_EX_NOACTIVATE`, поэтому `SetForegroundWindow` обычно
  проходит; если нет — рецепт панели задач: `AllowSetForegroundWindow`/
  `AttachThreadInput(our, fg)` → `SetForegroundWindow` → `Detach`, и как последнее
  средство `SwitchToThisWindow(h, true)` (недокументировано, но стабильно 20 лет).
  Свёрнутое окно сначала `ShowWindow(SW_RESTORE)`.
- Свернуть/развернуть: `ShowWindow(SW_MINIMIZE / SW_MAXIMIZE / SW_RESTORE)`.
- Закрыть: `PostMessage(h, WM_CLOSE)`; «закрыть все» — по всем окнам группы.
- Окна процессов с повышенными правами: команды молча не сработают (UIPI) — показываем
  подсказку «запущено от администратора».

### 4.2 AppCatalog — «все приложения»

- Основной источник — папка `shell:AppsFolder` (`SHGetKnownFolderItem(FOLDERID_AppsFolder)`
  → `IShellItem::BindToHandler(BHID_EnumItems)`): даёт **все** приложения из меню
  Пуск, включая UWP/MSIX, с `PKEY_AppUserModel_ID` и `PKEY_ItemNameDisplay`.
- Дополнительно сканируем `.lnk` в `%ProgramData%\Microsoft\Windows\Start Menu` и
  `%AppData%\Microsoft\Windows\Start Menu` (для «папок» Launchpad) через `IShellLink`.
- Слежение за изменениями — `FileSystemWatcher` на обе папки + переобход `AppsFolder`
  раз в N минут и при `WM_SETTINGCHANGE`.
- Запуск: `ShellExecuteEx("shell:AppsFolder\\{AUMID}")` работает и для Win32, и для
  UWP; для UWP можно также `IApplicationActivationManager::ActivateApplication`.
- Dock‑pins хранятся как список `{ aumid | path }`, порядок — пользовательский.

### 4.3 IconService

- Любой элемент оболочки (exe, lnk, `shell:AppsFolder\…`) →
  `SHCreateItemFromParsingPath` → `IShellItemImageFactory::GetImage(256×256, SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK)`.
  Для UWP даёт плитку; плоский фон плитки можно обрезать/скруглить на стороне UI.
- Фолбэк для окна без известного приложения: `SendMessageTimeout(WM_GETICON, ICON_BIG)` →
  `GetClassLongPtr(GCLP_HICON)`.
- Кэш: PNG в `%LOCALAPPDATA%\BetterWindows\icons\{sha1(id)}@{size}.png` + in‑memory LRU;
  UI получает `https://app.local/dyn/icon/{id}`.

### 4.4 AppBarService — резервирование места

- `SHAppBarMessage`: `ABM_NEW` (с `uCallbackMessage` = `RegisterWindowMessage`),
  `ABM_QUERYPOS` → `ABM_SETPOS` при каждом изменении размера/монитора, `ABM_REMOVE`
  при закрытии; `ABM_WINDOWPOSCHANGED` после `WM_WINDOWPOSCHANGED`.
- Колбэки: `ABN_POSCHANGED` (перерасчёт), `ABN_FULLSCREENAPP` (lParam=1 → прячем
  полосы, 0 → возвращаем).
- Строка меню — всегда appbar (приложения не должны лезть под неё). Док — по
  настройке: appbar (окна не перекрывают) или overlay (как macOS с автоскрытием).
- Мультимонитор: по экземпляру окна на монитор; пересоздание на
  `SystemEvents.DisplaySettingsChanged` / `WM_DISPLAYCHANGE` / `WM_DPICHANGED`.
- При использовании ManagedShell всё это даёт класс `AppBarWindow`.

### 4.5 TaskbarControl — штатная панель задач

Два режима, переключаемые в настройках:

1. **Auto‑hide** (безопасный): `ABM_SETSTATE(ABS_AUTOHIDE)`. Панель живёт у нижнего
   края, док ставим над ней или на другой край. Трей и Пуск остаются доступны.
2. **Hidden** (по умолчанию для «mac‑режима»):
   `FindWindow("Shell_TrayWnd")` + все `FindWindowEx(null, prev, "Shell_SecondaryTrayWnd")`
   → `ShowWindow(SW_HIDE)`; затем `SystemParametersInfo(SPI_SETWORKAREA)` на полный
   экран (минус наши appbar’ы — они пересчитают сами).
   - Перезапуск Explorer: ловим `RegisterWindowMessage("TaskbarCreated")` и прячем снова.
   - Windows периодически показывает панель (Win‑клавиша, Пуск): подписка на
     `EVENT_OBJECT_SHOW` для hwnd панели → повторный `SW_HIDE` с задержкой, кроме
     случая, когда пользователь сам вызвал «показать панель».
   - Восстановление: при штатном выходе, в `AppDomain.UnhandledException`,
     `TaskScheduler.UnobservedTaskException`, `SessionEnding`, а также при
     **следующем старте**, если предыдущий завершился некорректно (маркер‑файл
     `running.lock`). Отдельная команда `BetterWindows.exe --restore-taskbar` для
     ручного спасения; хоткей `Ctrl+Alt+Shift+T` — показать/спрятать панель.

Win‑клавиша по‑прежнему открывает штатный Пуск (это ОК; перехват Win через
`WH_KEYBOARD_LL` — опция за флагом, по умолчанию выключена).

### 4.6 Трей‑иконки сторонних приложений (фаза 2+)

Без штатной панели `Shell_NotifyIcon` из приложений уходит «в никуда». Варианты:

- **ManagedShell `NotificationArea`**: создаёт своё окно класса `Shell_TrayWnd` с
  `TrayNotifyWnd`, обрабатывает `WM_COPYDATA` с `NOTIFYICONDATA`, рассылает
  `TaskbarCreated`, чтобы приложения перерегистрировали иконки; даёт
  `ObservableCollection<NotifyIcon>` с иконкой, тултипом, кликами и меню. Оценить
  на 24H2/25H2 (спайк E). Это самый дешёвый путь.
- Фолбэк: пункт «Показать скрытые значки» временно показывает штатную панель.

### 4.7 HotkeyService / HotCornerService

- Глобальные хоткеи — `RegisterHotKey` (без хуков): `Alt+Space` (Spotlight; штатное
  системное меню окна по Alt+Space при этом перекрывается — как у PowerToys Run),
  `Ctrl+Up` (Mission Control), `F4`‑подобный для Launchpad, `Ctrl+Alt+Q` (выход),
  `Ctrl+Alt+Shift+T` (панель Windows). Все переназначаемы.
- Горячие углы — таймер 50 мс + `GetCursorPos` (дёшево, без `WH_MOUSE_LL`),
  срабатывание при удержании в углу ≥ 150 мс, учёт мультимонитора.
- Жесты тачпада: Windows отдаёт их сам; пользователю предлагаем назначить
  3‑пальцевые жесты на наши хоткеи в «Параметры → Сенсорная панель».

### 4.8 MediaService

`GlobalSystemMediaTransportControlsSessionManager.RequestAsync()` →
`GetCurrentSession()`, события `MediaPropertiesChanged`, `PlaybackInfoChanged`,
`SessionsChanged`; обложка — `Thumbnail.OpenReadAsync()` → PNG в `dyn/`.
Команды: `TryPlayAsync/TryPauseAsync/TrySkipNextAsync/TrySkipPreviousAsync`.
Работает со Spotify, браузерами, Groove, VLC (с плагином).

### 4.9 SystemStatusService

| Что | API |
|---|---|
| Батарея | `Windows.System.Power.PowerManager` (`RemainingChargePercent`, `BatteryStatus`, события) ; фолбэк `GetSystemPowerStatus` |
| Сеть | `NetworkInformation.GetInternetConnectionProfile()` → `GetSignalBars()`, `IsWlanConnectionProfile`, SSID; событие `NetworkStatusChanged` |
| Громкость / устройство | NAudio `MMDeviceEnumerator` + `IAudioEndpointVolume` (уровень, mute, `OnVolumeNotification`); переключение устройства — `IPolicyConfig` (недокументировано, за флагом) |
| Яркость | WMI `WmiMonitorBrightness/‑Methods` (встроенные дисплеи); DDC/CI (`SetVCPFeature`) для внешних — позже |
| Bluetooth | `Windows.Devices.Enumeration` + `BluetoothDevice.FromIdAsync` → статус/заряд (где есть `PKEY_Bluetooth_Battery`) |
| Тема | `HKCU\…\Themes\Personalize\AppsUseLightTheme` + `WM_SETTINGCHANGE("ImmersiveColorSet")` |
| Питание | `LockWorkStation`, `SetSuspendState`, `ExitWindowsEx` (нужна `SE_SHUTDOWN_NAME`), `shutdown.exe /r /t 0` как фолбэк |

Для переключателей без публичного API (Wi‑Fi on/off, Bluetooth on/off, Night light)
открываем точную страницу `ms-settings:` — честно и без админ‑прав.

### 4.10 VirtualDesktopService

- Публичный COM `IVirtualDesktopManager` (`CLSID_VirtualDesktopManager`):
  `IsWindowOnCurrentVirtualDesktop`, `GetWindowDesktopId`, `MoveWindowToDesktop`
  (для чужих окон может отказывать — проверить).
- Переключение рабочего стола: `SendInput` `Win+Ctrl+←/→` (надёжно на любой сборке).
- Список/имена рабочих столов, перенос чужих окон: внутренние интерфейсы
  `IVirtualDesktopManagerInternal` (GUID меняются между сборками) — только за флагом
  `Features.InternalVirtualDesktops`, с таблицей GUID по build number и
  самоотключением при ошибке.

### 4.11 CaptureService — снимки окон для Mission Control

- Быстрый снимок: `PrintWindow(h, hdc, PW_RENDERFULLCONTENT)` (работает для UWP и
  GPU‑окон на Windows 8.1+; ~5–20 мс на окно) → downscale → PNG в память.
- Живые превью (позже): `Windows.Graphics.Capture` (`GraphicsCaptureItem` из HWND
  через `IGraphicsCaptureItemInterop`), `IsBorderRequired=false` на 20H1+.
- Почему не `DwmRegisterThumbnail`: DWM рисует превью на поверхности нашего
  top‑level окна, а WebView2 — дочерний HWND над ней, превью окажутся под UI
  (airspace). Вариант с отдельным окном‑оверлеем для превью оставляем как план B.

### 4.12 SearchService (Spotlight)

Провайдеры, каждый отдаёт результаты асинхронно с рангом:

- Apps — `AppCatalog` + `fuse.js`‑подобный скоринг на стороне host (или UI);
- Files — Windows Search через OLE DB: провайдер `Search.CollatorDSO`, запрос
  `SELECT TOP 20 System.ItemPathDisplay, System.ItemNameDisplay FROM SYSTEMINDEX WHERE System.FileName LIKE '…%'`
  (используется и в PowerToys Run; индекс должен быть включён);
- Calculator — свой парсер выражений;
- Settings — таблица `ms-settings:`‑URI с ключевыми словами;
- Shell commands — сон/блокировка/выход/перезапуск Explorer;
- Web — открыть браузер с запросом.

### 4.13 DesktopService (фаза 5)

- Обои: `IDesktopWallpaper` (`CLSID_DesktopWallpaper`) — по мониторам, позиция, цвет.
- Скрыть штатные иконки: `SHELLDLL_DefView` → `SendMessage(WM_COMMAND, 0x7402)`
  (toggle) или реестр `HideIcons` + уведомление; восстанавливаем при выходе.
- Слой рабочего стола: классический WorkerW‑трюк (`Progman` ← `SendMessageTimeout(0x052C)`,
  поиск `WorkerW` после окна с `SHELLDLL_DefView`, `SetParent(ours, workerW)`).
  На 24H2 структура окон менялась → спайк G; план B — окно `HWND_BOTTOM` с
  `WS_EX_NOACTIVATE`, удерживаемое внизу Z‑порядка по `EVENT_OBJECT_LOCATIONCHANGE`.
- Ярлыки — только ссылки (`.lnk`/пути) на оригиналы; удаление ярлыка не трогает файл.

### 4.14 Виджеты

Виджет = папка `widgets/<id>/` с `manifest.json` (`id`, `name`, `size`, `permissions`:
`network`, `system.battery`, `media`, …) и `index.html`. Загружается в `<iframe sandbox>`
внутри поверхности `desktop`; доступ к host‑данным — через прокси IPC с проверкой
`permissions`. Встроенные: clock, calendar, weather (Open‑Meteo), reminders (локальный
JSON), media, battery, notes.

### 4.15 Внешний вид чужих окон (фаза 6, experimental)

- «Светофор»: для активного окна создаём маленькое окно‑оверлей (прозрачное,
  `WS_EX_NOACTIVATE|TOOLWINDOW`), владелец = целевое окно (`SetWindowLongPtr(GWLP_HWNDPARENT)`),
  чтобы оно всегда было над ним и двигалось по Z‑порядку; позиция — по
  `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` + `EVENT_OBJECT_LOCATIONCHANGE`.
  Кнопки шлют `WM_CLOSE` / `SW_MINIMIZE` / `WM_SYSCOMMAND SC_MAXIMIZE`.
- Скругления и цвет заголовка: `DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE / DWMWA_CAPTION_COLOR / DWMWA_BORDER_COLOR)`
  на чужих HWND — работает не для всех окон (приложения с кастомным заголовком
  игнорируют), проверяем HRESULT и откатываем.
- Глобальное меню: `System.Windows.Automation` — у foreground‑окна ищем `ControlType.MenuBar`,
  читаем пункты, вызываем `ExpandCollapsePattern`/`InvokePattern`. Работает только для
  классических Win32‑меню; для остальных показываем базовое меню приложения.

### 4.16 SettingsStore / ProfileStore

- `%LOCALAPPDATA%\BetterWindows\config.json` (`System.Text.Json`, схема с версией,
  миграции), `FileSystemWatcher` для горячей перезагрузки, UI правит через IPC
  `settings.set`.
- Профили: `profiles/<name>/` (config, pins, widgets, wallpaper); переключение — без
  перезапуска процесса (пересоздаём поверхности).
- Темы: `themes/<name>.json` → CSS‑переменные (`--bw-bg`, `--bw-radius`, …).

---

## 5. Жизненный цикл процесса

```
старт ─► Mutex (единственный экземпляр) ─► проверка running.lock (аварийный выход? → restore)
     ─► загрузка настроек ─► CoreWebView2Environment ─► Core‑сервисы (DI, IHostedService)
     ─► окна (MenuBar → Dock → Desktop → Overlay скрыт) ─► TaskbarControl.Apply()
     ─► иконка в трее ─► работа …
выход (Ctrl+Alt+Q / трей / SessionEnding / исключение)
     ─► TaskbarControl.Restore() ─► ABM_REMOVE ─► показать иконки рабочего стола
     ─► снять хуки ─► удалить running.lock
```

- Watchdog: отдельный лёгкий процесс не нужен; достаточно маркер‑файла и
  `--restore-taskbar`. (Опционально — Task Scheduler задача «при входе» для автозапуска
  вместо `Run`‑ключа, чтобы стартовать после Explorer.)
- Логи — Serilog в `%LOCALAPPDATA%\BetterWindows\logs\`, ротация 7 дней.
- Производительность: idle — ни одного таймера чаще 1 с, кроме hot‑corners (50 мс,
  отключаемый); Core свободно‑поточный (выделенного потока Core нет): подписчики событий портов
  возвращаются за < 1 мс и только инвалидируют состояние или делают `Task.Run`; тяжёлое (иконки, снимки) —
  `Task.Run` с лимитом параллелизма (см. docs/IPC.md, раздел 13).

---

## 6. Сборка, тесты, релиз

- `dotnet build` + `npm run build` (Vite → `wwwroot`), один `BetterWindows.sln`.
- Тесты: `BetterWindows.Core.Tests` (xUnit) — фильтр окон на фиктивных данных,
  группировка по AUMID, миграции настроек, парсер калькулятора; UI — Vitest +
  Playwright в браузере с `MockBridge`.
- Ручная матрица QA: Windows 11 23H2 / 24H2 / 25H2 и актуальный Insider;
  DPI 100/125/150 %; 1 и 2 монитора (разный DPI); ноутбук (батарея, Wi‑Fi, яркость);
  перезапуск Explorer; полноэкранная игра; `taskkill /f` нашего процесса.
- CI (GitHub Actions, `windows-latest`): build → tests → Velopack pack → артефакт;
  релиз по тегу `v*` → GitHub Release (feed для автообновления).
- Распространение: Velopack `Setup.exe` (per‑user, без UAC), позже winget;
  подпись кода обязательна до публичной беты (SmartScreen).
