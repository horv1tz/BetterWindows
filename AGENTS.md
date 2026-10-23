# Working on BetterWindows (humans and AI agents)

macOS-style shell on Windows 11: dock, menu bar, Spotlight, Launchpad, Mission Control, desktop widgets.
Read first: `docs/PLAN.md` (goals), `docs/ARCHITECTURE.md` (design), **`docs/IPC.md`** (protocol rules), `docs/PROTOCOL.md` (generated catalog).

## Layers (and why)

```
ui/ (Svelte 5 + TS)  ⇄  protocol/*.json (single source of truth → generated C# + TS)  ⇄  BetterWindows.Core (pure logic, net8.0)
                                                                                   ⇅ ports (interfaces)
                                                      BetterWindows.Windows (Win32/WinRT, ns BetterWindows.Native)    BetterWindows.Host (WPF + WebView2)
```
* **Core** is cross-platform and fully unit-testable here on Linux: controllers (the feature slices), services, IPC router,
  settings, search, window filtering, dock model… They talk to the OS only through **ports** (`Core/Platform/Ports.*.cs`).
* **Windows** implements the ports with Win32/WinRT. It compiles on Linux (`EnableWindowsTargeting`) but can only RUN on Windows
  (GitHub Actions `windows-latest` runs `tests/BetterWindows.Windows.Tests`).
* **Host** is the WPF/WebView2 process: surfaces (windows), the WebView bridge, lifecycle, tray icon, DI composition.
* **ui/** renders the four surfaces; in a normal browser it runs against an in-browser **mock host** (`ui/src/mock`).

> **Namespace gotcha:** the Windows layer's namespace root is `BetterWindows.Native` (project `BetterWindows.Windows`). A namespace
> `BetterWindows.Windows` would shadow CsWin32's `Windows.Win32` and the WinRT `Windows.*` projections everywhere under
> `BetterWindows.*`. Never create a namespace segment named `Windows`. In files use `using Windows.Win32;` at the top.

## Commands

```bash
export PATH=/opt/dotnet:$PATH DOTNET_ROOT=/opt/dotnet DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
dotnet build BetterWindows.sln                          # everything (Windows projects compile-check on Linux)
dotnet test tests/BetterWindows.Core.Tests              # Core tests (run these constantly)
dotnet test tests/BetterWindows.Windows.Tests           # skips on Linux; real on Windows CI
node tools/gen-protocol.mjs [--check]                   # regenerate C# + TS + docs/PROTOCOL.md after editing protocol/*.json
cd ui && npm install && npm run check && npm test       # svelte-check + vitest
cd ui && npm run build                                  # -> src/BetterWindows.Host/wwwroot
cd ui && node scripts/shot.mjs --route '#/dock' --out shots/dock.png --theme dark --steps 'hover:.dock-item:3;wait:300'
                                                        # screenshot a surface against the mock host (then LOOK at the PNG)
```
Dev server: `cd ui && npm run dev` → `http://localhost:5173/#/dock` (also `#/menubar`, `#/overlay`, `#/desktop`, `#/playground`).
Query flags for the mock: `?theme=dark|light`, `?lang=ru`, `?w=1920&h=1080`, `?latency=200`.

## Ownership rules (parallel work)

Several streams work at the same time, each in its **own git worktree/branch**. Merges must be conflict-free, so:

* Edit only files you own (table below). Need something in a file you don't own? Add it to your final report under
  `contractRequests`, do NOT edit it. The integrator applies requests.
* **Never change** `protocol/*.json`, generated files, `Core/Platform/Ports.*.cs`, `Core/Services/Contracts.cs`,
  `Core/Ipc/**`, `Directory.*.props`, the `.sln` or CI. These are frozen contracts. If a contract is wrong or insufficient,
  work around it and report it.
* You MAY add new files anywhere inside your own folders, new tests in your own test folders (namespace
  `BetterWindows.Core.Tests.<Area>`), and NuGet/npm dependencies only if listed as allowed for your stream.
* The stub files you own say `STUB — owner: …`; replace their contents entirely, keep the public type names and the DI
  registrations (`AddBetterWindowsCore` / `AddBetterWindowsPlatform` already reference them).
* Commit on your branch with clear messages. Do not push, do not rebase onto others, do not touch `master`.

### Streams → owned paths

| stream | owns |
|---|---|
| core-windows | `Core/Windows/**`, `Core/Dock/**`, `Core/Services/{WindowTracker,AppIdentityResolver}.cs`, `Controllers/{DockController,ActiveAppController}.cs`, `tests/Core.Tests/{Windows,Dock}/**` |
| core-settings | `Core/Settings/**`, `Core/Diagnostics/**`, `Core/Resources/**`, `Core/Services/{SettingsService,ThemeService,AppCatalog,DynamicResourceRouter}.cs`, `Controllers/{Surface,System,Apps}Controller.cs` (SurfaceController owns surface.ready/resize/log/shape, settings.get/patch/openWindow, theme.changed + settings.changed broadcasts), `tests/Core.Tests/{Settings,Apps,Resources,Diagnostics}/**` |
| core-search | `Core/Search/**`, `Controllers/SearchController.cs`, `tests/Core.Tests/Search/**` |
| core-status | `Core/Status/**`, `Core/Input/**`, `Core/Overlay/**`, `Controllers/{Status,Media,Tray,Overlay,Hotkey,HotCorner}Controller.cs`, `tests/Core.Tests/{Status,Input,Overlay}/**` |
| core-desktop | `Core/Desktop/**`, `Core/Widgets/**`, `Core/MissionControl/**`, `Controllers/{MissionControl,Desktop,Weather,Reminders}Controller.cs`, `tests/Core.Tests/{Desktop,Widgets,MissionControl}/**` |
| win-shell | `Windows/Shell/**`, `Windows/Interop/**` (shared helpers: create files, don't edit others'), `tests/Windows.Tests/Shell/**`, its block in `NativeMethods.txt` |
| win-apps | `Windows/Apps/**`, `tests/Windows.Tests/Apps/**`, its block in `NativeMethods.txt` |
| win-status | `Windows/Status/**`, `tests/Windows.Tests/Status/**`, its block in `NativeMethods.txt` |
| win-desktops | `Windows/Desktops/**`, `tests/Windows.Tests/Desktops/**`, its block in `NativeMethods.txt` |
| host-lifecycle | `Host/App.xaml(.cs)`, `Host/Lifecycle/**`, `Host/Bridge/**`, `Host/Composition/**`, `Host/Logging/**`, `Host/Tray/**`, `Host/Cli/**` |
| host-surfaces | `Host/Surfaces/**` (dock, menubar, overlay, desktop AND the settings window), `Host/Interop/**` |
| ui-dock | `ui/src/surfaces/dock/**`, `popovers/{DockMenu,WindowList}.svelte`, `ui/src/mock/domains/dock.ts` (dock.state from pins + fixture windows, dock.* calls, bounce) |
| ui-menubar | `ui/src/surfaces/menubar/**`, `popovers/AppleMenu.svelte`, `ui/src/mock/domains/menubar.ts` (activeApp.changed + fixtures for the app menu) |
| ui-overlay-search | `ui/src/surfaces/overlay/Surface.svelte`, `overlay/{spotlight,launchpad}/**`, `ui/src/mock/domains/search.ts` (apps.ts is frozen) |
| ui-overlay-mc | `ui/src/surfaces/overlay/missioncontrol/**`, `popovers/DesktopMenu.svelte`, `ui/src/mock/domains/mc.ts` |
| ui-status-popovers | `popovers/{ControlCenter,Calendar,Battery,Network,Sound,Bluetooth,MediaPopover}.svelte` (+ helpers in `popovers/status/**`); uses the frozen `mock/domains/status.ts`; no mock file of its own |
| ui-settings | `ui/src/surfaces/settings/**`, `ui/src/mock/domains/settings.ts` |
| ui-desktop | `ui/src/surfaces/{desktop,playground}/**`, `ui/src/mock/domains/{desktop,widgets,weather,reminders}.ts` |

Shared UI helpers (`ui/src/lib/**`, `ui/src/mock/{host,mockBridge,fixtures}.ts`, `mock/domains/{common,apps,status}.ts`, `styles/tokens.css`, `overlay/popovers/{Popover,PopoverFrame}.svelte`) are frozen; put stream-specific helpers in your surface folder.

## Quality bar

* **Correctness over breadth.** Each feature must work end-to-end on its own layer: Core with tests (fakes for ports), UI with
  mock host + screenshot you have looked at, Windows code with the documented Win32 contract and defensive handling.
* C#: nullable on, **no warnings**, file-scoped namespaces, `ILogger<T>` for diagnostics (never `Console`), `CancellationToken`
  on async APIs, no `async void` except event handlers, no blocking `.Result` on UI/dispatcher threads, dispose handles/COM.
  Public types get a one-line XML doc saying what and why.
* Windows layer specifics: prefer documented APIs; undocumented ones only behind `Features.*` flags with a safe fallback.
  Check every HRESULT/BOOL. Never throw from event callbacks or WndProc. Release every handle/COM object. Everything you
  change in system state (taskbar visibility, work area, desktop icons, registry) must be **restorable and restored**.
  Be thread-aware: SetWinEventHook needs a message loop thread; COM needs the right apartment.
  Comment the *why* of every non-obvious Win32 call (what documented behaviour you rely on).
* UI: Svelte 5 runes, TypeScript strict, no new dependencies unless allowed, accessible (focus, `aria-*`, keyboard),
  respects `reduceMotion`/`reduceTransparency`, light + dark themes, 60 fps animations (transform/opacity only),
  pixel-polished macOS-inspired look (no Apple assets or the Apple logo; use own glyphs/Lucide). Every user-visible string goes
  through `lib/i18n` if present, else a local `strings.ts` with `en` + `ru`.
* Tests: every pure-logic class has tests incl. edge cases; controllers are tested through `CoreHarness` (see
  `tests/Core.Tests/Support`) driving the same protocol the UI uses.
* Security: the UI is untrusted input — validate every call payload (paths, URIs, ids), never execute arbitrary strings.
  `system.open`/launch targets must be validated (http/https/ms-settings/file paths only).
* Do not leave TODO stubs in your area. If something cannot be done, implement the safe degraded behaviour and report it.

## Reporting

Your final message is a structured report (the orchestrator gives the schema): what you built, tests added/passing counts,
what is verified vs. unverifiable here, known gaps, and `contractRequests`.
