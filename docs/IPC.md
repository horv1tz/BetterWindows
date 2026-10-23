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
* JSON rules: camelCase property names; enums are lowerCamelCase strings; `null`/absent are equivalent (the host omits nulls);
  unknown properties are ignored by both sides (forward compatible); numbers are doubles (never put 64-bit ints on the wire).

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
   * `settings`: nothing extra (it reads via `settings.get`, `apps.list`, `weather.search`, `system.about`, `diag.report`); the host sends `settings.navigate` when the window is already open
   * `desktop`: `desktop.state` (per monitor), `reminders.changed`, `status.battery`, `status.bluetooth`, `media.state`
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
  The host clamps to the monitor, keeps the window centred on its edge and (if `reserveSpace`) re-registers the AppBar for
  the *bar* thickness only (headroom is never reserved).
* `surface.shape` (all surfaces): the UI reports its glass panels (rounded rects). The host sets DWM blur-behind on exactly
  those regions and uses them as the window region so that transparent pixels are click-through.
* Auto-hide: the host owns pointer detection at the screen edge → `dock.reveal { revealed }`. The UI animates in/out and then
  sends `surface.resize` to the collapsed size.
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

## 9. Errors
`{ ok:false, error:{ code, message } }` — codes: `badRequest` (invalid/missing payload), `notFound` (unknown call or entity),
`unsupported` (feature/OS not available), `failed` (operation failed), `cancelled`, `timeout` (UI-side), `internal`
(unexpected exception). The UI shows nothing for `unsupported`, degrades the affected control.

## 10. Versioning
`ProtocolInfo.Version` / `PROTOCOL_VERSION` is bumped on any breaking change (removed/renamed field, changed meaning). Additive
changes (new optional field, new message) do not bump it. The host refuses a UI bundle whose version differs (logged, surface
shows an error), since both ship in the same installer.
