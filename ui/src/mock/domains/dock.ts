import { BridgeError } from '../../lib/ipc/bridge';
import type { AppEntry, DockBounce, DockItem, DockState, PinnedItem, PopoverKind, WindowRef } from '../../lib/protocol/generated';
import { APPS, DEFAULT_PIN_NAMES, appById, appByName, icon, win } from '../fixtures';
import type { MockHost } from '../host';

/**
 * Mock of the DockController (core-windows): the dock model (pinned apps, running apps, folders, minimised windows) and every dock.* call.
 * It behaves like the real host would: state comes from settings.dock.pins + a fixture set of running windows, every mutation re-pushes
 * dock.state to the dock and overlay surfaces, pin changes go through settings.changed, launches show a `launching` placeholder and a
 * dock.bounce, and bad payloads are rejected with BridgeError codes (badRequest / notFound).
 * Dev switches (query string): ?thumbs=0 (no window thumbnails), ?attention=<app name> (flash that app),
 *   ?dockpop=dockMenu:<app name> | windowList:<app name> on a lone #/overlay page (opens the popover as if the dock had).
 */

const FOLDER_DOWNLOADS = 'C:\\Users\\you\\Downloads';
const MAX_STR = 1024;

/** An original folder glyph (rounded tab + body) as a data URL, used for folder items. */
export function folderIcon(hue = 208): string {
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 128 128"><defs><linearGradient id="b" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="hsl(${hue} 85% 66%)"/><stop offset="1" stop-color="hsl(${hue} 80% 48%)"/></linearGradient><linearGradient id="t" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="hsl(${hue} 70% 78%)"/><stop offset="1" stop-color="hsl(${hue} 70% 62%)"/></linearGradient></defs><rect width="128" height="128" rx="29" fill="hsl(${hue} 30% 22%)" fill-opacity=".0"/><path d="M14 40a10 10 0 0 1 10-10h24l10 11h46a10 10 0 0 1 10 10v8H14z" fill="url(#t)"/><rect x="14" y="46" width="100" height="58" rx="10" fill="url(#b)"/><rect x="14" y="46" width="100" height="6" rx="3" fill="#fff" fill-opacity=".28"/></svg>`;
  return `data:image/svg+xml;utf8,${encodeURIComponent(svg)}`;
}

/** A fake window screenshot (title bar, sidebar, text lines) as a data URL, standing in for /dyn/thumb/<handle>. */
export function thumbOf(title: string, seed = 0): string {
  let h = 7;
  for (const ch of title) h = (h * 31 + ch.charCodeAt(0)) | 0;
  const hue = Math.abs(h + seed) % 360;
  const lines = Array.from({ length: 5 }, (_, i) => `<rect x="${86}" y="${52 + i * 18}" width="${150 - ((i * 37 + Math.abs(h)) % 70)}" height="6" rx="3" fill="hsl(${hue} 12% 70%)"/>`).join('');
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="320" height="200" viewBox="0 0 320 200"><rect width="320" height="200" fill="hsl(${hue} 25% 94%)"/><rect width="320" height="26" fill="hsl(${hue} 30% 86%)"/><circle cx="14" cy="13" r="4" fill="hsl(${hue} 10% 60%)"/><circle cx="28" cy="13" r="4" fill="hsl(${hue} 10% 70%)"/><rect x="0" y="26" width="70" height="174" fill="hsl(${hue} 28% 90%)"/><rect x="86" y="36" width="120" height="9" rx="4.5" fill="hsl(${hue} 55% 52%)"/>${lines}<rect x="86" y="150" width="90" height="30" rx="8" fill="hsl(${hue} 60% 62%)" fill-opacity=".85"/></svg>`;
  return `data:image/svg+xml;utf8,${encodeURIComponent(svg)}`;
}

/** The fixture world: Explorer x2, Edge x3, Terminal, VS Code x2, Spotify (minimised), Notepad. */
export function fixtureWindows(thumbs = true): WindowRef[] {
  const w = (app: string, title: string, o: Partial<WindowRef> = {}) => {
    const r = win(app, title, o);
    if (thumbs) r.thumbUrl = thumbOf(title);
    return r;
  };
  const list = [
    w('Microsoft Edge', 'BetterWindows - GitHub - Microsoft Edge', { active: true }),
    w('Microsoft Edge', 'Inbox (3) - Mail'),
    w('Microsoft Edge', 'Dock design references'),
    w('Visual Studio Code', 'dock.ts - BetterWindows - Visual Studio Code'),
    w('Visual Studio Code', 'Surface.svelte - BetterWindows - Visual Studio Code'),
    w('Terminal', 'PowerShell'),
    w('File Explorer', 'Documents'),
    w('File Explorer', 'Downloads'),
    w('Notepad', 'Untitled - Notepad'),
    w('Spotify', 'Spotify Premium', { minimized: true }),
  ];
  return list;
}

export function defaultPins(): PinnedItem[] {
  const apps: PinnedItem[] = DEFAULT_PIN_NAMES.map((n) => {
    const a = appByName(n);
    return { id: a.id, kind: 'app', target: a.aumid ?? a.path ?? a.id, name: a.name };
  });
  return [...apps, { id: `dir:${FOLDER_DOWNLOADS}`, kind: 'folder', target: FOLDER_DOWNLOADS, name: 'Downloads' }];
}

export interface DockModelInput {
  pins: PinnedItem[];
  /** All top-level windows, most recently active first. */
  windows: WindowRef[];
  /** Launch order of running apps (keeps the unpinned part of the dock stable while windows are re-activated). */
  runningOrder?: string[];
  launching?: ReadonlySet<string>;
  attention?: ReadonlySet<string>;
  catalog?: AppEntry[];
}

const lastSegment = (p: string) => p.replace(/[\\/]+$/, '').split(/[\\/]/).pop() || p;

/** Computes DockState exactly like the real DockModel: pinned apps in user order, running-but-unpinned apps, folders; minimised windows apart. */
export function computeDockState(input: DockModelInput): DockState {
  const catalog = input.catalog ?? APPS;
  const launching = input.launching ?? new Set<string>();
  const attention = input.attention ?? new Set<string>();
  const byApp = new Map<string, WindowRef[]>();
  for (const w of input.windows) { const l = byApp.get(w.appId) ?? []; l.push(w); byApp.set(w.appId, l); }
  const pinnedIds = new Set(input.pins.filter((p) => p.kind === 'app').map((p) => p.id));
  const known = (id: string) => catalog.find((a) => a.id === id);

  const appItem = (id: string, name: string, iconUrl: string, pinned: boolean): DockItem => {
    const windows = byApp.get(id) ?? [];
    return {
      id, kind: 'app', appId: id, name, iconUrl, pinned, running: windows.length > 0,
      active: windows.some((w) => w.active), attention: attention.has(id), windows: windows.map((w) => ({ ...w })), launching: launching.has(id),
    };
  };

  const items: DockItem[] = [];
  for (const p of input.pins) {
    if (p.kind !== 'app') continue;
    const a = known(p.id);
    items.push(appItem(p.id, a?.name ?? p.name ?? lastSegment(p.target), a?.iconUrl ?? icon(p.name ?? lastSegment(p.target)), true));
  }
  const order = input.runningOrder ?? [...byApp.keys()];
  const extra = [...new Set([...order, ...byApp.keys(), ...launching])].filter((id) => !pinnedIds.has(id) && (byApp.has(id) || launching.has(id)));
  const unpinned = extra.map((id) => {
    const a = known(id);
    return appItem(id, a?.name ?? lastSegment(id), a?.iconUrl ?? icon(lastSegment(id)), false);
  });
  const folders: DockItem[] = input.pins.filter((p) => p.kind === 'folder').map((p) => ({
    id: p.id, kind: 'folder', name: p.name ?? lastSegment(p.target), iconUrl: folderIcon(), target: p.target, pinned: true, running: false,
    active: false, attention: false, windows: [], launching: false,
  }));

  const sep = (n: number): DockItem => ({ id: `separator:${n}`, kind: 'separator', name: '', iconUrl: '', pinned: false, running: false, active: false, attention: false, windows: [], launching: false });
  const out: DockItem[] = [...items];
  if (unpinned.length) { if (out.length) out.push(sep(1)); out.push(...unpinned); }
  if (folders.length) { if (out.length) out.push(sep(2)); out.push(...folders); }
  const minimized = input.windows.filter((w) => w.minimized).map((w) => ({ ...w }));
  const active = out.find((i) => i.active);
  return { items: out, minimized, activeItemId: active?.id };
}

export interface WorldHooks {
  getPins(): PinnedItem[];
  setPins(pins: PinnedItem[]): void;
  /** State changed (windows, launching, attention): re-push dock.state. */
  changed(): void;
  bounce(b: DockBounce): void;
  setActiveApp(appId: string | null): void;
  log?(msg: string, ...rest: unknown[]): void;
  /** Delay before a launched app shows its first window (ms). 0 = synchronous (tests). */
  launchDelayMs?: number;
}

const bad = (m: string) => new BridgeError('badRequest', m);
const notFound = (m: string) => new BridgeError('notFound', m);

function needString(v: unknown, what: string): string {
  if (typeof v !== 'string' || v.length === 0) throw bad(`${what} must be a non-empty string`);
  if (v.length > MAX_STR) throw bad(`${what} is too long`);
  return v;
}

const isAbsolutePath = (p: string) => /^[A-Za-z]:[\\/]/.test(p) || p.startsWith('\\\\') || p.startsWith('/');

/** The mutable world behind the mock: windows, launches, attention. All dock.* calls are methods; BridgeErrors mirror the real host. */
export class DockWorld {
  windows: WindowRef[];
  runningOrder: string[];
  launching = new Set<string>();
  attention = new Set<string>();
  private timers = new Set<ReturnType<typeof setTimeout>>();

  constructor(private hooks: WorldHooks, windows: WindowRef[] = fixtureWindows()) {
    this.windows = windows;
    this.runningOrder = [...new Set(windows.map((w) => w.appId))];
    const active = windows.find((w) => w.active);
    if (active) hooks.setActiveApp(active.appId);
  }

  state(): DockState {
    return computeDockState({ pins: this.hooks.getPins(), windows: this.windows, runningOrder: this.runningOrder, launching: this.launching, attention: this.attention });
  }

  dispose() { for (const t of this.timers) clearTimeout(t); this.timers.clear(); }

  // ----- lookups -------------------------------------------------------------------------------------------------

  private item(itemId: unknown): DockItem {
    const id = needString(itemId, 'itemId');
    const it = this.state().items.find((i) => i.id === id && i.kind !== 'separator');
    if (!it) throw notFound(`no dock item '${id}'`);
    return it;
  }
  private windowOf(handle: unknown): WindowRef {
    const h = needString(handle, 'handle');
    const w = this.windows.find((x) => x.handle === h);
    if (!w) throw notFound(`no window '${h}'`);
    return w;
  }
  private appWindows(appId: string): WindowRef[] { return this.windows.filter((w) => w.appId === appId); }

  // ----- window state --------------------------------------------------------------------------------------------

  private focus(w: WindowRef) {
    for (const x of this.windows) x.active = false;
    w.minimized = false;
    w.active = true;
    this.windows = [w, ...this.windows.filter((x) => x !== w)];
    this.attention.delete(w.appId);
    this.hooks.setActiveApp(w.appId);
  }
  private afterRemoval(wasActive: boolean) {
    if (!wasActive) return;
    const next = this.windows.find((w) => !w.minimized);
    if (next) this.focus(next);
    else { for (const x of this.windows) x.active = false; this.hooks.setActiveApp(null); }
  }
  private launchApp(appId: string) {
    const app = appById(appId);
    this.launching.add(appId);
    this.hooks.bounce({ itemId: appId, kind: 'launch' });
    this.hooks.changed();
    const open = () => {
      this.timers.clear();
      this.launching.delete(appId);
      const w = win(app?.name ?? appId, `${app?.name ?? appId}`);
      if (w.thumbUrl === undefined && this.windows.some((x) => x.thumbUrl)) w.thumbUrl = thumbOf(w.title);
      this.windows = [w, ...this.windows];
      if (!this.runningOrder.includes(appId)) this.runningOrder.push(appId);
      this.focus(w);
      this.hooks.changed();
    };
    const delay = this.hooks.launchDelayMs ?? 900;
    if (delay <= 0) open(); else this.timers.add(setTimeout(open, delay));
  }

  // ----- dock.* calls --------------------------------------------------------------------------------------------

  activate(itemId: unknown) {
    const it = this.item(itemId);
    if (it.kind === 'folder') { this.hooks.log?.('open folder in Explorer', it.target); return; }
    if (it.launching) return;
    const ws = this.appWindows(it.id);
    if (ws.length === 0) { this.launchApp(it.id); return; }
    const top = ws[0];
    if (ws.length === 1 && top.active && !top.minimized) { top.minimized = true; top.active = false; this.afterRemoval(true); }
    else this.focus(top);
    this.hooks.changed();
  }
  activateWindow(handle: unknown) {
    const w = this.windowOf(handle);
    if (w.elevated) throw new BridgeError('failed', 'cannot control an elevated window');
    this.focus(w);
    this.hooks.changed();
  }
  closeWindow(handle: unknown) {
    const w = this.windowOf(handle);
    if (w.elevated) throw new BridgeError('failed', 'cannot control an elevated window');
    this.windows = this.windows.filter((x) => x !== w);
    if (!this.appWindows(w.appId).length) this.attention.delete(w.appId);
    this.afterRemoval(w.active);
    this.hooks.changed();
  }
  pin(appId: unknown, index?: number) {
    const id = needString(appId, 'appId');
    if (index !== undefined && index !== null && (!Number.isInteger(index) || index < 0)) throw bad('index must be a non-negative integer');
    const catalogApp = appById(id);
    if (!catalogApp && !this.windows.some((w) => w.appId === id)) throw notFound(`unknown app '${id}'`);
    const pins = this.hooks.getPins();
    if (pins.some((p) => p.id === id)) return;
    const apps = pins.filter((p) => p.kind === 'app');
    const folders = pins.filter((p) => p.kind === 'folder');
    const at = index === undefined || index === null ? apps.length : Math.min(index, apps.length);
    apps.splice(at, 0, { id, kind: 'app', target: catalogApp?.aumid ?? catalogApp?.path ?? id, name: catalogApp?.name ?? lastSegment(id) });
    this.hooks.setPins([...apps, ...folders]);
  }
  unpin(itemId: unknown) {
    const id = needString(itemId, 'itemId');
    const pins = this.hooks.getPins();
    if (!pins.some((p) => p.id === id)) throw notFound(`'${id}' is not pinned`);
    this.hooks.setPins(pins.filter((p) => p.id !== id));
  }
  reorder(itemIds: unknown) {
    if (!Array.isArray(itemIds) || itemIds.some((x) => typeof x !== 'string')) throw bad('itemIds must be an array of strings');
    const pins = this.hooks.getPins();
    const ids = itemIds as string[];
    if (ids.length !== pins.length || new Set(ids).size !== ids.length || !pins.every((p) => ids.includes(p.id))) throw bad('itemIds must list every pinned item exactly once');
    const byId = new Map(pins.map((p) => [p.id, p] as const));
    const next = ids.map((i) => byId.get(i)!);
    // folders always stay behind the apps, like the real model
    this.hooks.setPins([...next.filter((p) => p.kind === 'app'), ...next.filter((p) => p.kind === 'folder')]);
  }
  newWindow(itemId: unknown) {
    const it = this.item(itemId);
    if (it.kind !== 'app') throw new BridgeError('unsupported', 'folders have no windows');
    if (!this.appWindows(it.id).length) { this.launchApp(it.id); return; }
    const app = appById(it.id);
    const n = this.appWindows(it.id).length + 1;
    const w = win(app?.name ?? it.name, `${app?.name ?? it.name} (${n})`);
    if (this.windows.some((x) => x.thumbUrl)) w.thumbUrl = thumbOf(w.title, n);
    this.windows = [w, ...this.windows];
    this.focus(w);
    this.hooks.changed();
  }
  /** itemId may be ANY running app id (the app menu uses it), not only a visible dock item. */
  quit(itemId: unknown) {
    const id = needString(itemId, 'itemId');
    const ws = this.appWindows(id);
    if (!ws.length) throw notFound(`'${id}' is not running`);
    const wasActive = ws.some((w) => w.active);
    this.windows = this.windows.filter((w) => w.appId !== id);
    this.attention.delete(id);
    this.afterRemoval(wasActive);
    this.hooks.changed();
  }
  hide(itemId: unknown) {
    const id = needString(itemId, 'itemId');
    const ws = this.appWindows(id);
    if (!ws.length) throw notFound(`'${id}' is not running`);
    const wasActive = ws.some((w) => w.active);
    for (const w of ws) { w.minimized = true; w.active = false; }
    this.afterRemoval(wasActive);
    this.hooks.changed();
  }
  revealInExplorer(itemId: unknown) {
    const it = this.item(itemId);
    this.hooks.log?.('reveal in Explorer', it.target ?? it.id);
  }
  pinPath(path: unknown, index?: number) {
    const p = needString(path, 'path');
    if (!isAbsolutePath(p)) throw bad('path must be absolute');
    const pins = this.hooks.getPins();
    const isApp = /\.(exe|lnk)$/i.test(p);
    const id = isApp ? `exe:${p.toLowerCase()}` : `dir:${p}`;
    if (pins.some((x) => x.id === id)) return;
    const apps = pins.filter((x) => x.kind === 'app');
    const folders = pins.filter((x) => x.kind === 'folder');
    if (isApp) {
      const at = index === undefined || index === null ? apps.length : Math.max(0, Math.min(index, apps.length));
      apps.splice(at, 0, { id, kind: 'app', target: p, name: lastSegment(p).replace(/\.(exe|lnk)$/i, '') });
    } else {
      folders.push({ id, kind: 'folder', target: p, name: lastSegment(p) });
    }
    this.hooks.setPins([...apps, ...folders]);
  }

  /** Dev/test hook: make an app flash (the real host derives this from FlashWindowEx). */
  flash(appId: string, on = true) {
    if (on) { this.attention.add(appId); this.hooks.bounce({ itemId: appId, kind: 'attention' }); } else this.attention.delete(appId);
    this.hooks.changed();
  }
}

export default function register(host: MockHost) {
  const q = new URLSearchParams(location.search);
  const thumbs = q.get('thumbs') !== '0';

  // seed settings.dock.pins (like a first run of the real host)
  if (host.settings.dock.pins.length === 0) host.settings = { ...host.settings, dock: { ...host.settings.dock, pins: defaultPins() } };

  const dockOrOverlay = (c: { surface: string }) => c.surface === 'dock' || c.surface === 'overlay';
  const push = () => host.emit('dock.state', world.state(), dockOrOverlay);
  const world: DockWorld = new DockWorld({
    getPins: () => host.settings.dock.pins,
    setPins: (pins) => { host.settings = { ...host.settings, dock: { ...host.settings.dock, pins } }; host.emit('settings.changed', host.settings); },
    changed: push,
    bounce: (b) => host.emit('dock.bounce', b, (c) => c.surface === 'dock'),
    setActiveApp: (id) => (host as any).setActiveApp?.(id),
    log: (m, ...r) => console.info('[mock] dock:', m, ...r),
  }, fixtureWindows(thumbs));
  (host as any).dockWorld = world;

  // pin changes made through settings.patch (settings UI) must refresh the dock too
  const emit = host.emit.bind(host) as MockHost['emit'];
  host.emit = ((type: any, payload: any, filter?: any) => {
    emit(type, payload, filter);
    if (type === 'settings.changed') push();
  }) as MockHost['emit'];

  host.handle('dock.activate', (p) => world.activate(p?.itemId));
  host.handle('dock.activateWindow', (p) => world.activateWindow(p?.handle));
  host.handle('dock.closeWindow', (p) => world.closeWindow(p?.handle));
  host.handle('dock.pin', (p) => world.pin(p?.appId, p?.index));
  host.handle('dock.unpin', (p) => world.unpin(p?.itemId));
  host.handle('dock.reorder', (p) => world.reorder(p?.itemIds));
  host.handle('dock.newWindow', (p) => world.newWindow(p?.itemId));
  host.handle('dock.quit', (p) => world.quit(p?.itemId));
  host.handle('dock.hide', (p) => world.hide(p?.itemId));
  host.handle('dock.revealInExplorer', (p) => world.revealInExplorer(p?.itemId));
  host.handle('dock.pinPath', (p) => world.pinPath(p?.path, p?.index));

  host.onUiEvent('surface.resize', (p, ctx) => { if (ctx.surface === 'dock') (host as any).lastDockResize = p; });

  // Another mock domain (loaded after this one) may have reset the settings: seed once more on the first dock/overlay ready if the pins are still empty.
  let reseeded = false;
  host.onSurfaceReady((ctx) => {
    if (!reseeded && (ctx.surface === 'dock' || ctx.surface === 'overlay')) {
      reseeded = true;
      if (host.settings.dock.pins.length === 0) { host.settings = { ...host.settings, dock: { ...host.settings.dock, pins: defaultPins() } }; host.emit('settings.changed', host.settings); }
    }
    if (ctx.surface === 'dock' || ctx.surface === 'overlay') host.emitTo(ctx, 'dock.state', world.state());
    if (ctx.surface === 'dock') {
      const flashing = q.get('attention');
      if (flashing) { const a = APPS.find((x) => x.name === flashing); if (a) setTimeout(() => world.flash(a.id), 400); }
    }
    if (ctx.surface === 'overlay') {
      const spec = q.get('dockpop'); // e.g. dockMenu:Microsoft Edge
      if (!spec) return;
      const [kind, ...rest] = spec.split(':');
      const app = APPS.find((a) => a.name === rest.join(':'));
      const itemId = app?.id ?? (rest.join(':').startsWith('dir:') ? rest.join(':') : undefined);
      const w = Number(q.get('w') ?? window.innerWidth), h = Number(q.get('h') ?? window.innerHeight);
      const idx = Math.max(0, world.state().items.filter((i) => i.kind !== 'separator').findIndex((i) => i.id === itemId));
      const anchor = q.get('dockpos') === 'left'
        ? { x: 8, y: Math.round(h / 2 - 300 + idx * 62), width: 72, height: 56 }
        : { x: Math.round(w / 2 - 340 + idx * 62), y: h - 80, width: 56, height: 72 };
      setTimeout(() => (host as any).showOverlay?.({ mode: 'popover', popover: { kind: kind as PopoverKind, anchor, itemId } }), 120);
    }
  });
}
