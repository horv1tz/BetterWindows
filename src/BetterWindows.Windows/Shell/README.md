# Shell layer (win-shell stream)

Win32 implementations of `IWindowSystem`, `IMonitorProvider`, `ICursorProvider`, `ITaskbarController` (+ static `TaskbarRestorer`),
`IAppBarFactory`/`IAppBar`, `IHotkeyRegistrar`, `IInputSimulator`. Namespace `BetterWindows.Native`; shared helpers in `../Interop`.

## Threads
| thread | owner | what lives on it |
|---|---|---|
| `BetterWindows.WindowHooks` (STA, message loop) | `Win32WindowSystem` | hidden top-level window, 5 narrow `SetWinEventHook`s (+ the location hook only while someone subscribes to `FullscreenChanged`), shell-hook registration (flash / activation), 50 ms timers (throttle flush, full-screen debounce) |
| `BetterWindows.WindowEvents` | `Win32WindowSystem` | raises `WindowChanged` so a slow subscriber never stalls the hook thread |
| `BetterWindows.Monitors` (lazy) | `Win32MonitorProvider` | hidden top-level window for `WM_DISPLAYCHANGE` / `WM_DPICHANGED` / `WM_SETTINGCHANGE(SPI_SETWORKAREA)` (broadcasts never reach message-only windows) |
| `BetterWindows.Hotkeys` (lazy) | `Win32HotkeyRegistrar` | message-only window; `RegisterHotKey` must run on the thread that owns the window |
| `BetterWindows.TaskbarWatcher` (only while mode = Hidden) | `Win32TaskbarController` | `TaskbarCreated` + `WM_DISPLAYCHANGE` listener, `EVENT_OBJECT_SHOW` hook restricted to Explorer's process |

Everything else (enumeration, description, commands, `AppBar`, cursor, input) runs on the caller's thread. `AppBar` is thread-affine to the window's UI thread.

## Invariants worth knowing
* Taskbar: the original state is written to `%LOCALAPPDATA%\BetterWindows\state\taskbar.json` BEFORE the first mutation (and no mutation happens if that fails);
  `Restore`/`TaskbarRestorer.RestoreNow()` reads it in a fresh process, applies it exactly and deletes it. Hidden = `ABS_AUTOHIDE` + `SW_HIDE`, never `SPI_SETWORKAREA`.
* The dock never uses `ABM_SETAUTOHIDEBAR`.
* `RawWindow` expensive fields (title round trip, AUMID property store) are only fetched for visible/cloaked windows; UWP frames report the hosted app.
* Pure logic (event tables, throttle, full-screen rules, AppBar math, taskbar state machine, hotkey table, input sequences) is unit-tested on every OS;
  the `*Integration*` tests need a Windows desktop and skip themselves otherwise.
