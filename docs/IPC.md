# IPC semantics

The message catalog (types, fields, directions) is generated: see [PROTOCOL.md](PROTOCOL.md). This document defines the
rules that the catalog cannot express. **Source of truth for shapes: `protocol/*.json`.** Change a shape → run
`node tools/gen-protocol.mjs` (CI fails on stale generated files).

## 1. Transport

* Four WebView2 surfaces load the same bundle: `https://app.local/index.html#/<surface>[/<monitorId>]`
  (`dock`, `menubar`, `overlay`, `desktop`, `settings`). In a normal browser the page falls back to the in-browser mock host.
* **Both directions carry JSON strings.** web→host: `chrome.webview.postMessage(JSON.stringify(envelope))`, read with
  `TryGetWebMessageAsString()`. host→web: `CoreWebView2.PostWebMessageAsString(json)` (the page `JSON.parse`s `event.data`).
* Envelope (`v` = protocol version):
  * event: `{ "v":1, "kind":"event", "type":"dock.state", "payload":{...} }` (either direction, fire-and-forget)
  * call: `{ "v":1, "kind":"call", "id":"c7", "type":"dock.pin", "payload":{...} }` (UI → host only)
  * result: `{ "v":1, "kind":"result", "id":"c7", "ok":true, "payload":{...} }` or `"ok":false,"error":{"code","message"}`
* JSON rules: camelCase property names; enums are lowerCamelCase strings; unknown properties are ignored by both sides (forward compatible); numbers are doubles
  (never put 64-bit ints on the wire). The host omits nulls. On INPUT: `int` fields accept any JSON number or numeric string (rounded half away from zero);
  an explicit `null` never overwrites a non-nullable member (a null reference becomes the type's default; a null for an enum/number is a `badRequest`);
  a missing field of a UI-input type that has no default is a `badRequest` (the generator marks them `[JsonRequired]`: `{}` is not a valid `dock.pin`);
  non-finite doubles are written as `0`. Always `DeepClone()` a `JsonNode` before attaching it to another tree.
* Every envelope carries `v`. The host answers a call with a different `v` with `badRequest` ("protocol mismatch") and drops such events; the UI compares
  `SurfaceInit.protocolVersion` with its own and shows an error instead of rendering on mismatch.
* Call ids are unique per page load: `c<bootId>.<n>`. Results are delivered to the CHANNEL that sent the call (a re-created window with the same surface id never
  receives an old page's results).

### 1.1 How the page and `/dyn/*` are served (important)

`WebResourceRequested` is **not raised** for hosts mapped with `SetVirtualHostNameToFolderMapping` (documented WebView2 behaviour), so the host must NOT use
that mapping. Instead the bridge registers `AddWebResourceRequestedFilter("https://app.local/*", CoreWebView2WebResourceContext.All)` and answers EVERY request
itself (with a `GetDeferral()`): paths under `/dyn/` come from `IDynamicResourceProvider`; everything else is a static file of the UI bundle (`wwwroot`),
with strict path normalisation (no `..`, no absolute paths, only inside wwwroot), correct content types, `/` -> `index.html`, 404 otherwise.
In dev mode (`--ui-url`) the page is navigated to the dev server origin and `/dyn/*` of THAT origin is intercepted the same way.

## 2. Identifiers

| thing | wire form |
|---|---|
| window (HWND) | decimal string, e.g. `"197890"` (`Hwnd.ToWire` / treat as opaque in the UI) |
| app id | `aumid:<AUMID>` (packaged or known AUMID) or `exe:<lowercased full exe path>`; folders `dir:<path>` |
| dock item id | the app id of a pinned/running app; `dir:<path>`; separators `separator:<n>` |
| monitor id | `MonitorInfo.id` (stable string, "1", "2", …; the primary monitor is not necessarily "1") |
| search request | UI-chosen string; a new `search.query` implicitly cancels the previous one |

## 3. Surface lifecycle and initial state

1. The page loads, creates its bridge, calls `surface.ready { surface, monitorId? }`.
2. The host replies with `SurfaceInit` (theme, settings, monitor, feature flags, locale).
3. **After the reply** the host pushes the *current state* of every event the surface kind consumes (list below) with
   `PublishTo(surface, …)`.
4. Events marked *state event* in PROTOCOL.md (`STATE_EVENT_TYPES`) are cached by the UI bridge and **replayed to late subscribers**, so ordering between the initial push and a component's `on()` does not matter (lazily mounted popovers included). Transient events (`dock.bounce`, `overlay.*`, `search.results`, …) are never replayed. Afterwards state events are pushed on change. **All state events carry the full state** (idempotent snapshots, never
   deltas): the UI replaces its mirror. The host coalesces bursts (≥ 50 ms apart for window-derived state).
5. Pushed on ready, per surface kind:
   * `dock`: `dock.state`
   * `menubar`: `activeApp.changed`, `status.battery`, `status.network`, `status.audio`, `status.brightness`, `status.bluetooth`, `media.state`, `tray.state`
   * `overlay`: `dock.state`, `activeApp.changed`, `status.*`, `media.state`, `reminders.changed`, `apps.changed`
   * every kind also receives `settings.changed`, `theme.changed`, `locale.changed`
   * `settings`: nothing extra (it reads via `settings.get`, `apps.list`, `weather.search`, `system.about`, `diag.report`); the host sends `settings.navigate` when the window is already open
   * `desktop`: `desktop.state` (per monitor), `reminders.changed`, `status.battery`, `status.bluetooth`, `media.state`
   The host never posts events to a page before its `surface.ready` reply has been posted. State events are published ONLY through `IStatePublisher`
   (per-topic lock: the last posted snapshot is always the newest; identical consecutive snapshots are not re-posted; trailing-edge coalescing; initial push per the
   protocol's `initial` lists). Never compare protocol records with `==`: compare serialised JSON.
6. Settings changes (from any surface or the file) → `settings.changed` to everyone; theme-relevant changes also → `theme.changed`.

## 4. Coordinates and sizes

* Everything the UI sends/receives is in **DIPs** (CSS px at the surface's own DPI scale), relative to the surface window's
  client area — except `MonitorInfo.boundsPx/workAreaPx`, which are physical pixels in virtual-screen space.
* `PopoverRequest.anchor` is relative to the **source** surface's client area in `overlay.open`; the host translates it and
  sends it relative to the overlay's client area in `overlay.show`.
* The overlay window covers one whole monitor (the monitor of the source surface / the cursor for hotkeys).

## 5. Window behaviour contracts (UI ⇄ host)

### Dock
* The dock window is transparent and as small as possible to keep the rest of the screen clickable. The **UI owns the size**:
  it sends `surface.resize { width, height }` (DIPs) whenever the dock's content or magnification headroom changes
  (idle: bar only; while the pointer is over the dock: bar + headroom for magnified icons and tooltips).
  The host clamps to the monitor, keeps the window centred on its edge and (if `reserveSpace` and not auto-hide) re-registers the AppBar for
  `barThickness` only (the UI states it: the always-visible bar's extent from the docked edge in DIPs; headroom and tooltips are never reserved).
* `surface.shape` (dock and menubar): the UI reports its glass panels (`rects`, rounded) and interactive-but-not-glass areas (`hitRects`, e.g.
  magnified icons that overflow the panel, tooltips). Host contract (normative): window region = union(rects, hitRects) in physical px (DIP x scale) via
  `SetWindowRgn`, so everything else is click-through; backdrop = `DWMWA_SYSTEMBACKDROP_TYPE` (transient/acrylic) on Windows 11 22H2+ (the undocumented
  accent API only behind a feature flag on older builds). `DwmEnableBlurBehindWindow` MUST NOT be used (no blur since Windows 8). A whole-window backdrop cannot
  be restricted to sub-rects, so `SurfaceInit.glassOverflow` tells the UI whether `hitRects` stay un-blurred: **if false, the UI must keep ALL dock content
  (magnified icons, tooltips) inside the glass rects** (grow the panel instead of overflowing it). Overlay/settings never get a region; desktop has none.
* Auto-hide: the HOST owns pointer detection at the screen edge (it polls the cursor) and the dock window geometry → `dock.reveal { revealed }`.
  `ISurfaceManager.SetDockRevealed` is only a Core override (keep it revealed while a dockMenu popover is open). The UI animates in/out and then
  sends `surface.resize` to the collapsed size.
* Fullscreen suppression is OWNED BY THE HOST (per monitor, from the AppBar fullscreen notification and `IWindowSystem.FullscreenChanged`): it hides that
  monitor's menubar/dock (and un-reserves their space) and sends `surface.visibility`. Core hides surfaces only for its own reasons (reason-keyed).
* `dock.bounce` plays a launch/attention animation. `dock.state` is the single source of truth for icons/indicators.

### Overlay and popovers
* Dock menus, window lists and all menu-bar popovers (Apple menu, Control Center, calendar, …) render **inside the overlay
  surface**, never in the small dock/menubar windows. Flow: source surface → `overlay.open { mode:'popover', popover:{kind,anchor,…} }`
  → host shows the overlay window (activatable, full monitor, topmost) → `overlay.show` → UI renders the popover at the anchor.
  A click on the transparent backdrop, `Esc`, or deactivation → UI calls `overlay.close` (the host also sends `overlay.hide`
  when a hotkey toggles it or another app is activated).
* Spotlight, Launchpad and Mission Control are `mode`s of the same overlay.
* While the overlay is active, the host does **not** report the overlay itself as the foreground app (`activeApp.changed`
  keeps the previous app so the menu bar title does not flicker).

### Menu bar
* Always an AppBar on each monitor (28 DIP). Right-hand items are driven by `status.*`/`media.state`/`tray.state`; clicking
  an item opens a popover via `overlay.open`.

## 6. Search flow
`search.query {requestId, query}` → returns immediately. The host streams `search.results {requestId, results[], complete}`:
each event holds the **complete merged and ordered** list so far; providers that finish later (files) add to it. The UI shows
only events whose `requestId` equals its latest query. `search.perform {requestId, resultId, actionId?}` runs an action
(the host then hides the overlay itself). `search.recent` fills the empty-query state.

## 7. Mission Control flow
Open: `overlay.show{mode:'missionControl'}` → UI `mc.snapshot` → renders windows grouped by desktop with `thumbUrl` when
present; later `mc.thumbnail {handle, thumbUrl}` events add missing thumbnails. `mc.activateWindow` / `mc.closeWindow` /
`mc.switchDesktop` / `mc.moveWindow` / `mc.createDesktop` / `mc.removeDesktop`. If `desktopsSupported` is false, only the current
desktop exists and the desktop strip is hidden.

## 8. Dynamic resources (`/dyn/…`)
Served from memory by the host (`WebResourceRequested`, `DenyCors`); URL builders live in `DynUrls` (C#) and
`lib/dyn.ts` (TS) — keep them identical. Images are PNG unless stated; unknown/failed resources → 404 (the UI shows a
fallback tile). Cache: immutable per URL (the `rev` query busts it).

| URL | content |
|---|---|
| `/dyn/icon/app/<appId>?s=<px>` | app icon (catalog or running) |
| `/dyn/icon/window/<handle>?s=<px>` | icon of a window |
| `/dyn/icon/path/<path>?s=<px>` | shell icon of a file/folder |
| `/dyn/thumb/<handle>?w=<px>&rev=<n>` | window snapshot |
| `/dyn/art/<rev>` | current media cover art |
| `/dyn/wallpaper/<monitorId>?rev=<n>` | wallpaper (jpeg/png) |
| `/dyn/tray/<id>?rev=<n>` | tray icon |

**Security and caching of `/dyn/*` (normative).** Every response carries `X-Content-Type-Options: nosniff`, `Content-Security-Policy: default-src 'none'; sandbox`
and no CORS headers. Only `image/png`, `image/jpeg`, `image/webp`, `image/gif`, `image/bmp` are ever served (never svg/html: media apps control cover-art bytes); cover
art and wallpapers are decoded and RE-ENCODED (art <= 1024 px and <= 2 MB; wallpaper: path through `InputPolicy`, allowed extensions png/jpg/jpeg/bmp/webp/gif with matching
magic bytes, <= 64 MB, long edge <= 4096). Size buckets: `s` -> {32,64,128,256,512}, `w` -> {160,240,320,480,640,960} (round up); unknown query keys are ignored; in-memory LRU
caps 64 MB thumbnails / 16 MB icons. `Cache-Control: no-store` for thumb, art, wallpaper, window icons and tray icons; `private, max-age=31536000, immutable` only for
`icon/app` and `icon/path` whose URLs carry a content-hash `rev`. Thumbnail `rev` is one process-global counter incremented per capture (never per window: HWNDs are recycled).
`/dyn/thumb` and `/dyn/icon/window` require `IWindowTracker.Find(handle)` (own windows and unknown handles -> 404) and are served only while the overlay is visible/healthy.
Path/exe icon requests run with a 3 s timeout off the UI thread. The shared WebView2 profile is InPrivate (nothing persists).

## 8.1 Hotkey strings

`Mod+Mod+Key`. Modifiers: `Ctrl`/`Control`, `Alt`, `Shift`, `Win`/`Super` (case-insensitive, any order on input; canonical output order `Ctrl+Alt+Shift+Win+Key`).
Keys: `A`-`Z`, `0`-`9`, `F1`-`F24`, `Space`, `Enter`, `Tab`, `Esc`, `Backspace`, `Delete`, `Insert`, `Home`, `End`, `PageUp`, `PageDown`, `Up`, `Down`, `Left`, `Right`,
punctuation by name (`Backtick`, `Minus`, `Equals`, `LeftBracket`, `RightBracket`, `Backslash`, `Semicolon`, `Quote`, `Comma`, `Period`, `Slash`), `Num0`-`Num9`.
At least one modifier unless the key is `F1`-`F24`. Empty string = unassigned. The C# parser (`HotkeyParser`, core-status) and the TS one (settings UI) implement exactly this.

## 9. Errors
`{ ok:false, error:{ code, message } }` — codes: `badRequest` (invalid/missing payload), `notFound` (unknown call or entity),
`unsupported` (feature/OS not available), `failed` (operation failed), `forbidden` (the call is not allowed from this surface kind: protocol `surfaces` list),
`busy` (rate limit or too many calls in flight), `cancelled` (surface closed / page reloaded), `timeout` (UI-side), `internal` (unexpected exception). The UI shows nothing for `unsupported`, degrades the affected control.

## 10. Versioning
`ProtocolInfo.Version` / `PROTOCOL_VERSION` is bumped on any breaking change (removed/renamed field, changed meaning). Additive
changes (new optional field, new message) do not bump it. The host refuses a UI bundle whose version differs (logged, surface
shows an error), since both ship in the same installer.

## 11. Limits and flood control (enforced in `MessageRouter`, constants in `Core/Ipc/Limits.cs`)

Max message 1 MiB characters (dropped and logged); per-surface token bucket 200 msg/s (burst 400) for everything, plus `surface.log` 20/s, `surface.shape` 30/s,
`surface.resize` 60/s; at most 32 calls in flight per surface (excess -> `busy`); `surface.log` message <= 2 KiB, data <= 4 KiB, control characters replaced before logging;
`surface.shape` <= 64 `rects` and <= 64 `hitRects`, finite coordinates clamped to the window, radius <= min(w,h)/2 (each rect becomes a GDI region: the process-wide GDI
handle limit would otherwise break every window); `surface.resize` finite, 1..monitor size, dock only; `search.query` <= 256 chars (files provider starts after 120 ms
idle, one file search per overlay session); `settings.patch` <= 256 KB. `ISurfaceChannel.Post` is bounded: state events coalesce to the latest per type, transient events
drop the oldest beyond 64, nothing is queued before `surface.ready` or while the surface is failed.

## 12. Settings validation (normative; implemented once as `SettingsValidator` in core-settings)

`settings.patch` is RFC 7386 JSON Merge Patch against the serialised current settings: objects merge recursively (including `map<>` fields), arrays and primitives replace,
`null` DELETES the key (nullable -> null, non-nullable -> its default, map entry removed). A key that does not exist in the schema -> `badRequest` (naming the path); keys that
already exist in the FILE but not in the schema are preserved (<= 256 KB). `version` is never patchable. The same validator runs on every write, on file load and on reload.
Numeric/length/pattern limits live in `protocol/settings.json` (`min`, `max`, `maxItems`, `maxLength`, `pattern`) and are generated into `SettingsLimits` (C#) and `SETTINGS_LIMITS`
(TS), so Core and the settings UI share one source. Rules on top: non-finite numbers -> default, over-long arrays -> truncated, strings <= 512 chars (paths <= 4096), every path
through `InputPolicy.TryNormalizeLocalPath` (invalid entries dropped), unique ids for pins/shortcuts/widgets/folders, `spotlight.webSearchUrl` must be http(s) with exactly one
`{query}` (substituted with `Uri.EscapeDataString`) else the default, `spotlight.providers` a subset of `SearchProviderId`, hotkeys parsed by `HotkeyParser` (`exit` and
`toggleTaskbar` must be non-empty, parseable and unique, otherwise reset to their defaults), unknown ENUM strings or wrong types reset only that field (never the whole file),
`appearance.accent` matches `^(system|#rrggbb[aa])$` (it goes into a CSS variable). Persistence: `IAppPaths.ConfigFile`, atomic writes with `.bak`, corrupt file quarantined as
`config.corrupt-<utc>.json` (keep 5) and NEVER overwritten unquarantined, size cap 2 MB, debounced 300 ms, flushed on exit, last writer wins across sessions.

## 13. Threading (normative)

1. The Host calls `IMessageRouter.DispatchAsync` from `WebMessageReceived` on the UI thread and does NOT await it. The router parses and validates on the caller thread, then
   STARTS the handler on the thread pool in arrival order per surface (one queue + one reader per channel; slow handlers never delay the start of later ones). The returned
   task completes after the reply is posted.
2. `CallContext.Cancellation` is cancelled when the surface closes, when the same surface sends `surface.ready` again, or when the caller's token is. Long handlers observe it.
3. Port events (`WindowChanged`, `Pressed`, `*.Changed`, `FullscreenChanged`, `MonitorsChanged`, ...) run on OS-owned threads (hook thread, WndProc, WinRT). Subscribers return in
   < 1 ms and never block, never call `TryDescribe`/COM/`Post`/`Publish` synchronously: they only `IStateTopic.Invalidate()` or `Task.Run`. The Windows layer raises events
   through `SafeEvent.Raise` (a Core exception must never unwind into a hook/WndProc thread).
4. No controller calls `IEventHub`/`IStatePublisher`/`ISurfaceManager`/ports while holding its own lock. Snapshot functions passed to `IStatePublisher.Declare` are cheap, pure and
   never call the publisher.
5. `ISettingsService.Changed` is raised after the write gate is released; subscribers must not wait on `UpdateAsync` synchronously and must converge.
6. `SurfaceRegistry.SurfaceClosed/ChannelClosed` run on the UI thread: handlers are quick.
7. Stop order: `ISurfaceHost.Stop` -> `ShellRuntime.Stop` (controllers, then services) -> taskbar/desktop-icon `Restore` -> DI dispose. `IAppLifecycle.Exit()` never blocks and Stop
   never waits for in-flight calls.
8. Core is free-threaded: there is NO Core dispatcher thread. Only the Host owns the WPF dispatcher.

## 14. Security rules for UI-supplied data (the UI is untrusted)

Use `Core/Services/InputPolicy` everywhere; nobody invents their own rules.
* **Never** pass a UI string to `ShellExecute`/`Process.Start`. `system.open` accepts only http/https URLs (`IsSafeWebUrl`) and ms-settings URIs (`IsMsSettingsUri`); any path or
  other scheme -> `badRequest`. Files and folders are opened only by id (`search.perform`, `desktop.open`, `dock.activate`, `apps.reveal`, `dock.revealInExplorer`), resolved from
  host-originated data. No UI-supplied arguments are ever appended to a launch (`apps.launch` has no `args`).
* App ids: `aumid:` must exist in the catalog or a running window; `exe:` only if in the catalog, a tracked window, or a path GRANTED this session (returned by `system.pickPath`);
  otherwise `notFound`. `IAppLauncher` re-checks: refuses UNC, extended-length and device-namespace paths, `shell:`/`file:`/other schemes (exactly two `shell:` literals for the
  desktop specials are allowed), and never uses verb `runas`.
* Handles: every window handle goes through `IWindowTracker.Find(handle)` (non-null, not own process), then the process id is compared with the tracked entry before
  Close/Activate/Minimize/Move; else `notFound`. The same lookup guards `/dyn/icon/window` and `/dyn/thumb`. `IWindowSystem` refuses own-process windows and shell classes.
* Paths: `TryNormalizeLocalPath` at the call, again on settings load and at use time (UNC would leak the user's NTLM credentials and stall icon extraction). UNC only if it
  came from `system.pickPath`.
* Monitor ids must be in `IMonitorProvider.GetMonitors()` (opaque `m<8 hex>`, see `MonitorIdentity`) else `badRequest`.
* Per-surface authorization is declared per message in `protocol/*.json` (`surfaces`); the router answers `forbidden`. The sender kind is ONLY what the Host assigned at attach;
  `surface.ready` params are informational and a mismatch is rejected. `diag.report` and `--diagnose` are strictly read-only.

## 15. Startup and safety invariants (owner: host-lifecycle; SystemController applies the mode)

1. Parse early CLI (`--restore-taskbar`, `--diagnose`, `--guardian`, `--smoke`) WITHOUT taking the mutex. 2. Refuse to run elevated (message; never relaunch elevated).
3. `Local\BetterWindows.<name>` mutex (per session); a second launch signals the first (named event) to open Settings, then exits 0. 4. If a restore journal's owner is dead, run both
restorers. 5. Crash counter in `state/startup.json`: >= 3 starts within 10 minutes of an unclean exit = SAFE MODE (taskbar visible, defaults/backup config, features off, settings opened
on diagnostics); the counter resets after 60 s of healthy running. 6. With `--autostart`, wait up to 30 s for `Shell_TrayWnd` (the Run value is written as `"<exe>" --autostart`).
7. WebView2 runtime check (see section 16). 8. Create surfaces. 9. Only AFTER the dock and menu bar completed `surface.ready` AND at least one of the `exit`/`toggleTaskbar`
hotkeys registered successfully may the taskbar be hidden (otherwise the mode stays visible and diag.report says why). 10. `system.quit` ALWAYS restores taskbar and desktop icons.
Restore journals are per Windows session (`taskbar.<sessionId>.json`, `desktopicons.<sessionId>.json`, fields `{ownerPid, ownerStartTime, sessionId, original...}`): `Restore()` acts
only if the owner is dead or is the current process; `Dispose()` never restores in a non-owner process; `--restore-taskbar` first signals the live owner (`Local\BetterWindows.Quit`),
waits 5 s, then force-restores. Before the first hide the host starts `BetterWindows.exe --guardian <ownerPid>` detached (Explorer as parent so `taskkill /t` does not take it): it
waits on the owner and then runs both restorers (owner-checked: it cannot undo a newer instance).

## 16. WebView2 hardening and failure policy (normative; owner: host-lifecycle)

Settings: `AreHostObjectsAllowed=false`, `AreDefaultScriptDialogsEnabled=false`, `IsPasswordAutosaveEnabled=false`, `IsGeneralAutofillEnabled=false`, `IsSwipeNavigationEnabled=false`,
`IsStatusBarEnabled=false`, `IsBuiltInErrorPageEnabled=false`, no context menu/zoom/accelerators, DevTools only with `--devtools`/DEBUG; controller `AllowExternalDrop=false` (dropping
a file must not navigate the surface to `file:///`). Events: `NavigationStarting`/`FrameNavigationStarting` cancel unless the origin is `https://app.local` (dev: the `--ui-url`
origin); `NewWindowRequested` handled (an http(s) target may only go through the `system.open` validator); `PermissionRequested` deny all; `DownloadStarting` cancel; a second filter
`*` answers every non-app.local request with 403. `WebMessageReceived` accepts only when `e.Source` is the allowed origin's index.html, catches `ArgumentException` from
`TryGetWebMessageAsString` and checks the length before `DispatchAsync`. A CSP header on `index.html` (`default-src 'self'; img-src 'self' data: blob:; connect-src 'none'`)
forbids network access from the UI.
Failure policy (`IWebViewBridge.Failed`): `RuntimeMissing` -> native MessageBox with the Evergreen download link, exit code 2, taskbar untouched; `Render*`/`Gpu` failures ->
`ReloadAsync` (max 3 per 60 s, `Unresponsive` after 5 s), keep the registry channel, pending calls are cancelled by the new handshake; `BrowserProcessExited` -> recreate the
environment once and re-attach EVERY surface; `ReadyTimeout` (10 s) and `ProtocolMismatch` -> the surface shows an error page and the failure is logged and visible in diag.report.
