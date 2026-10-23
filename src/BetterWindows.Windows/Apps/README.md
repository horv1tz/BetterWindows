# Apps stream (`BetterWindows.Native`, folder `Apps/`)

App catalog, icons, launching, file search, autostart, wallpaper, desktop icons and the desktop folder listing.
Everything here implements a port from `Core/Platform/Ports.Apps.cs`; the logic that does not need Windows lives in pure
`internal` classes with unit tests that run on any OS, the rest is covered by `SkippableFact` integration tests
(`tests/BetterWindows.Windows.Tests/Apps`, executed on GitHub Actions `windows-latest`).

| port | class | notes |
|---|---|---|
| `IAppCatalogSource` | `ShellAppCatalogSource` | `shell:AppsFolder` via `SHGetKnownFolderItem` -> `BHID_EnumItems` on a dedicated STA thread; `.lnk` fallback through `IShellLinkW`; `AppCatalogFilter` builds entries and drops noise |
| `IIconSource` | `ShellIconSource` | `IShellItemImageFactory.GetImage` (apps, paths, image thumbnails >= 192 px) or `WM_GETICON` -> class icon -> exe icon (windows); pure pixel pipeline in `AppsIconImage`, own `AppsPngEncoder` |
| `IAppLauncher` | `ShellAppLauncher` | `ShellExecuteEx` (verb NULL, never elevates); every target re-validated by `ShellTargetPolicy` |
| `IFileSearch` | `WindowsSearchFileSearch` | OLE DB `Search.CollatorDSO` / `SYSTEMINDEX`; SQL built by `FileSearchQuery` (escaping tested) |
| `IAutostartService` | `RegistryAutostartService` | `HKCU\...\Run\BetterWindows` + `StartupApproved\Run`; command line logic in `AutostartCommand` |
| `IWallpaperService` | `DesktopWallpaperService` | `IDesktopWallpaper` (STA), monitors mapped with `MonitorIdentity` |
| `IDesktopIconsController` | `Win32DesktopIconsController` + static `DesktopIconsRestorer` | the only code that knows Progman / WorkerW / SHELLDLL_DefView (`Interop/AppsDesktopWindows.cs`) |
| `IDesktopFolderSource` | `ShellDesktopFolderSource` | user + public desktop folders and the two specials |

## Threading

* Shell COM (`IShellItem*`, `IShellLink`, `IDesktopWallpaper`, `ShellExecuteEx`) only ever runs on STA threads owned by `AppsStaWorker` / `AppsStaPool`
  (`Interop/AppsStaWorker.cs`). The catalog, launcher, wallpaper and desktop-folder source have one worker each; icons use a pool of 2 with a 3 s execution timeout
  (a hung handler retires its thread and a fresh one takes over).
* Port methods that are synchronous (`Launch`, `Open`, `RevealInExplorer`, wallpaper, `Enumerate`) block the caller until the STA thread is done, with a deadline.
* The desktop controller owns one extra thread with a message loop (hidden top-level window): it receives `TaskbarCreated`, runs a 2 s watchdog tick, and is the only
  place that calls into Explorer repeatedly. Calls on windows owned by other threads use `SWP_ASYNCWINDOWPOS`, and nothing holds the controller lock while sending a
  synchronous message to a Host window.

## State on disk (`%LOCALAPPDATA%\BetterWindows\state`)

| file | meaning |
|---|---|
| `desktop-icons-hidden` | marker, written BEFORE Explorer's icon list is hidden, removed after it is shown again |
| `desktopicons.json` | snapshot: was the list visible before we touched it (`listViewWasVisible`), handle, pid, time |

`DesktopIconsRestorer.RestoreNow()` needs nothing but these files and is safe to call from crash paths and at start-up. The persisted Explorer setting
"Show desktop icons" (`HideIcons`) is never written: only `ShowWindow(SW_HIDE/SW_SHOW)` on the `SysListView32`.

## Desktop layer (WorkerW technique) in one paragraph

`TryAttachDesktopLayer` sends `0x052C` (wParam `0xD`, lParam `1`) to Progman, finds the window that hosts `SHELLDLL_DefView` (a top-level `WorkerW`, or `Progman` itself on
Windows 11 24H2 where the view is a direct child of Progman) and makes the Host window a child of it (`WS_POPUP` -> `WS_CHILD` before `SetParent`, checked with `GetParent`
because a top-level window's previous parent is legitimately NULL). Z-order against the icon view follows the icon state: native icons hidden -> on top of the view (the
layer receives the mouse); visible -> directly below it (icons stay clickable, the layer is still above the wallpaper). If that parent refuses, the `WorkerW` that follows
it (the classic wallpaper window) is tried; if nothing works the method returns false and the Host uses its `HWND_BOTTOM` fallback. After a shell restart or when a recorded
parent disappears, `ShellRestarted` is raised and the Host must attach again.

## Known platform facts relied on (verify on a real machine when changing)

* `IShellItemImageFactory` returns premultiplied 32bpp ARGB; the pipeline detects "no alpha" and "straight alpha" bitmaps defensively.
* The CsWin32 imports in this repo do not set `SetLastError`; use `Marshal.GetLastSystemError()` immediately after a failing call.
* `StartupApproved\Run`: first byte even = enabled, odd = disabled.
* Windows Search SQL has no ESCAPE clause: wildcard characters in user input are removed instead of escaped (see `FileSearchQuery`).
