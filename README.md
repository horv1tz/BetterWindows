# BetterWindows

Оболочка в стиле macOS поверх Windows 11: **док**, **строка меню**, **Spotlight**, **Launchpad**, **Mission Control**, **виджеты рабочего стола**
и **окно настроек**. Windows остаётся операционной системой, `explorer.exe` остаётся shell: мы заменяем только видимые элементы интерфейса
(как это делает Familiar Desktop), поэтому всё можно в любой момент вернуть одной горячей клавишей.

> Статус: ранняя альфа. Логика, протокол и интерфейс написаны и покрыты автотестами; поведение окон на настоящем рабочем столе
> Windows нужно подтвердить вручную по чек‑листу в [docs/DECISIONS.md](docs/DECISIONS.md).

## Архитектура в двух словах

```
ui/ (Svelte 5 + TS)  ⇄  protocol/*.json (единый контракт → C# + TypeScript + справочник)  ⇄  BetterWindows.Core (чистая логика, net8.0)
                                                                      порты (интерфейсы) ⇅
                                              BetterWindows.Windows (Win32/WinRT)      BetterWindows.Host (WPF + WebView2)
```

* **Core** — кроссплатформенная логика (контроллеры функций, настройки, поиск, модель дока, маршрутизатор сообщений). Тестируется на любой ОС.
* **Windows** — тонкий слой реализаций портов на Win32/WinRT (окна, панель задач, AppBar, иконки, звук, батарея, медиа…).
* **Host** — процесс `BetterWindows.exe`: окна поверхностей (док, строка меню, оверлей, рабочий стол, настройки), мост WebView2, трей, жизненный цикл.
* **ui** — пять поверхностей на одном бандле; в обычном браузере работает с встроенным мок‑хостом (удобно разрабатывать и делать скриншоты).

Подробности: [AGENTS.md](AGENTS.md) (слои, правила, команды) · [docs/IPC.md](docs/IPC.md) (протокол) · [docs/PROTOCOL.md](docs/PROTOCOL.md) (каталог сообщений) ·
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) · [docs/PLAN.md](docs/PLAN.md) (план и риски) · [docs/DECISIONS.md](docs/DECISIONS.md) (решения и чек‑лист проверки).

## Сборка и тесты

```bash
# .NET 8 SDK + Node 22
dotnet build BetterWindows.sln -c Release          # Windows‑проекты собираются и на Linux/macOS (только компиляция)
dotnet test tests/BetterWindows.Core.Tests          # основная логика
dotnet test tests/BetterWindows.Windows.Tests       # интеграционные тесты: выполняются только на Windows
node tools/gen-protocol.mjs --check                 # контракт актуален
cd ui && npm install && npm run check && npm test && npm run build
cd ui && npm run dev                                # http://localhost:5173/#/playground — все поверхности вместе (мок‑хост)
```

## Как попробовать на Windows 11

Каждый запуск CI на GitHub собирает самодостаточный архив `BetterWindows-win-x64.zip` (артефакт запуска, вкладка *Actions*).
Распакуйте и запустите `BetterWindows.exe`.

| Действие | Клавиши |
|---|---|
| Spotlight | `Alt+Space` |
| Launchpad | `Ctrl+Alt+L` |
| Mission Control | `Ctrl+Alt+M` |
| Показать/скрыть панель Windows | `Ctrl+Alt+Shift+T` |
| Выйти и вернуть панель Windows | `Ctrl+Alt+Q` |

Аварийное восстановление: `BetterWindows.exe --restore-taskbar`. Диагностика платформы: `BetterWindows.exe --diagnose`. Безопасный режим: `--safe-mode`.
Настройки и логи: `%LOCALAPPDATA%\BetterWindows`.

## Лицензия и торговые марки

Проект независимый, не связан с Apple и Microsoft. Используются собственные иконки и шрифт Inter; ассеты Apple не применяются.
