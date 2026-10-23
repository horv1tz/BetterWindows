// Mock of the desktop layer: desktop.state per desktop surface (per monitor), icons (user shortcuts + desktop-folder items + specials),
// wallpapers and widgets, and every desktop.* call mutating settings.desktop like the real DesktopController would.
// Owner: ui-desktop. Query flags: ?widgets=0|none|all, ?desktop=off (enabled=false), ?monitors=2 is handled by the playground.
import {
  BridgeError,
} from '../../lib/ipc/bridge';
import type {
  DesktopCell, DesktopIcon, DesktopIconKind, DesktopSettings, DesktopState, WidgetInstance, WidgetSize, WidgetType,
} from '../../lib/protocol/generated';
import { APPS, appById, icon as appIcon } from '../fixtures';
import type { MockContext, MockHost } from '../host';
import {
  DEFAULT_WIDGET_CONFIG, PRIMARY_MONITOR, fileIcon, folderIcon, query, recycleBinIcon, seedDesktopSettings, taps, thisPcIcon, wallpaperArt,
} from './widgets';

const HOME = 'C:\\Users\\you';
/** Rows assumed by the mock when it auto-places desktop-folder items (the real host has its own rule; the UI re-flows anyway). */
const AUTO_ROWS = 7;

interface FolderItem { id: string; name: string; kind: DesktopIconKind; target: string; iconUrl: string }

/** Contents of the (fake) Desktop folder plus the shell specials, in auto-placement order. */
export const DESKTOP_ITEMS: readonly FolderItem[] = [
  { id: 'special:thisPc', name: 'This PC', kind: 'special', target: 'shell:MyComputerFolder', iconUrl: thisPcIcon() },
  { id: 'special:recycleBin', name: 'Recycle Bin', kind: 'special', target: 'shell:RecycleBinFolder', iconUrl: recycleBinIcon() },
  { id: `fs:${HOME.toLowerCase()}\\desktop\\projects`, name: 'Projects', kind: 'folder', target: `${HOME}\\Desktop\\Projects`, iconUrl: folderIcon('code') },
  { id: `fs:${HOME.toLowerCase()}\\desktop\\notes.txt`, name: 'notes.txt', kind: 'file', target: `${HOME}\\Desktop\\notes.txt`, iconUrl: fileIcon('txt') },
  { id: `fs:${HOME.toLowerCase()}\\desktop\\budget 2026 final (v2).xlsx`, name: 'Budget 2026 final (v2).xlsx', kind: 'file', target: `${HOME}\\Desktop\\Budget 2026 final (v2).xlsx`, iconUrl: fileIcon('xlsx') },
  { id: `fs:${HOME.toLowerCase()}\\desktop\\holiday.png`, name: 'holiday.png', kind: 'file', target: `${HOME}\\Desktop\\holiday.png`, iconUrl: fileIcon('png') },
];

const SHORTCUT_SEEDS = [
  { id: 'sc:documents', name: 'Documents', target: `${HOME}\\Documents`, x: 0, y: 2, icon: folderIcon('doc') },
  { id: 'sc:downloads', name: 'Downloads', target: `${HOME}\\Downloads`, x: 0, y: 3, icon: folderIcon('down') },
  { id: 'sc:spotify', name: 'Spotify', target: 'SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify', x: 0, y: 4 },
  { id: 'sc:vscode', name: 'Visual Studio Code', target: 'Microsoft.VisualStudioCode', x: 0, y: 5 },
  { id: 'sc:telegram', name: 'Telegram', target: 'Telegram.TelegramDesktop', x: 0, y: 6 },
];

export const isAbsolutePath = (p: string) => /^[a-zA-Z]:[\\/]/.test(p) || /^\\\\[^\\]+\\[^\\]+/.test(p);
const baseName = (p: string) => (p.split(/[\\/]/).filter(Boolean).pop() ?? p).replace(/\.(lnk|exe)$/i, '');

/** The icon artwork the real host would serve from /dyn/icon/... for a shortcut target. */
function shortcutIconUrl(name: string, target: string): string {
  const app = APPS.find((a) => a.aumid === target || a.id === target || a.path?.toLowerCase() === target.toLowerCase());
  if (app) return app.iconUrl;
  if (!/\.[a-z0-9]{1,5}$/i.test(target)) return folderIcon('none');
  const ext = target.split('.').pop()!.toLowerCase();
  return ext === 'exe' ? appIcon(name) : fileIcon(ext);
}

export const keyOf = (c: DesktopCell) => `${c.x},${c.y}`;

export default function register(host: MockHost) {
  const q = query();
  const t = taps(host);
  const primary = PRIMARY_MONITOR;
  let seeded = false;
  const iconCache = new Map<string, string>();

  /** Width of ONE monitor: the playground splits the viewport when ?monitors=2. */
  const monitorWidth = () => (Number(q.get('w') ?? 1920) || 1920) / (q.get('monitors') === '2' ? 2 : 1);

  /** One-time seed on the first desktop surface: enable the layer, add shortcuts and widgets, give the weather widget a location. */
  function ensureSeeded(): boolean {
    if (seeded) return false;
    seeded = true;
    const next = structuredClone(host.settings);
    const d = next.desktop;
    Object.assign(d, seedDesktopSettings(d, monitorWidth(), q.get('widgets') ?? 'default'));
    if (q.get('desktop') === 'off') d.enabled = false;
    if (!d.shortcuts.length) {
      d.shortcuts = SHORTCUT_SEEDS.map(({ id, name, target, x, y }) => ({ id, name, target, x, y }));
      d.iconSize = 64;
    }
    if (!next.weather.locationName && q.get('weather') !== 'none') {
      next.weather = { ...next.weather, locationName: 'Moscow', latitude: 55.7558, longitude: 37.6173 };
    }
    host.settings = next;
    return true;
  }

  const monitorOf = (id: string | null | undefined) => id || primary;

  function occupiedCells(d: DesktopSettings, monitorId: string): Set<string> {
    const s = new Set<string>();
    for (const sc of d.shortcuts) if (monitorOf(sc.monitorId) === monitorId) s.add(keyOf(sc));
    if (monitorId === primary && d.showDesktopFolder) for (const [id, c] of Object.entries(d.placements)) if (DESKTOP_ITEMS.some((i) => i.id === id)) s.add(keyOf(c));
    return s;
  }

  /** First free cell, column by column. */
  function autoCell(occupied: Set<string>): DesktopCell {
    for (let x = 0; ; x++) for (let y = 0; y < AUTO_ROWS; y++) if (!occupied.has(`${x},${y}`)) return { x, y };
  }

  /** Persist a placement for every desktop-folder item that has none yet (stable across rebuilds). */
  function withPlacements(d: DesktopSettings): DesktopSettings {
    if (!d.showDesktopFolder) return d;
    let placements = d.placements;
    const occupied = occupiedCells({ ...d, placements }, primary);
    for (const item of DESKTOP_ITEMS) {
      if (placements[item.id]) continue;
      const cell = autoCell(occupied);
      occupied.add(keyOf(cell));
      placements = { ...placements, [item.id]: cell };
    }
    return placements === d.placements ? d : { ...d, placements };
  }

  function wallpaperUrl(d: DesktopSettings, monitorId: string): string {
    const path = d.wallpapers[monitorId] ?? d.wallpaperPath;
    const seed = path ?? (monitorId === primary ? 'sonoma' : 'dusk');
    let url = iconCache.get(seed);
    if (!url) iconCache.set(seed, (url = wallpaperArt(seed)));
    return url;
  }

  function iconsFor(d: DesktopSettings, monitorId: string): DesktopIcon[] {
    const out: DesktopIcon[] = [];
    for (const sc of d.shortcuts) {
      if (monitorOf(sc.monitorId) !== monitorId) continue;
      out.push({ id: sc.id, name: sc.name, iconUrl: shortcutIconUrl(sc.name, sc.target), x: sc.x, y: sc.y, target: sc.target, kind: 'shortcut' });
    }
    if (monitorId === primary && d.showDesktopFolder) {
      for (const item of DESKTOP_ITEMS) {
        const c = d.placements[item.id] ?? { x: 0, y: 0 };
        out.push({ id: item.id, name: item.name, iconUrl: item.iconUrl, x: c.x, y: c.y, target: item.target, kind: item.kind });
      }
    }
    return out;
  }

  function stateFor(monitorId: string): DesktopState {
    const d = withPlacements(host.settings.desktop);
    return {
      enabled: d.enabled,
      wallpaperUrl: wallpaperUrl(d, monitorId),
      wallpaperFit: d.wallpaperFit,
      iconSize: d.iconSize,
      icons: iconsFor(d, monitorId),
      widgets: d.widgets.filter((w) => monitorOf(w.monitorId) === monitorId),
      monitorId,
    };
  }

  const pushTo = (ctx: MockContext) => host.emitTo(ctx, 'desktop.state', stateFor(monitorOf(ctx.monitorId)));
  const pushAll = () => { for (const b of host.bridges) if (b.ctx.surface === 'desktop') pushTo(b.ctx); };

  /** Apply a change to settings.desktop the way SettingsService would: new object, persisted, broadcast (the tap below pushes desktop.state). */
  function mutate<T>(fn: (d: DesktopSettings, root: typeof host.settings) => T): T {
    ensureSeeded();
    const next = structuredClone(host.settings);
    next.desktop = withPlacements(next.desktop);
    const r = fn(next.desktop, next);
    host.settings = next;
    host.emit('settings.changed', host.settings);
    return r;
  }

  host.onSurfaceReady((ctx) => {
    if (ctx.surface !== 'desktop') return;
    if (ensureSeeded()) host.emit('settings.changed', host.settings); // first desktop surface: tell the others about the seeded layer
    pushTo(ctx);
  });
  // Settings edited elsewhere (settings window) change the desktop too.
  t.onEmit('settings.changed', pushAll);

  // ---------------------------------------------------------------- validation helpers
  const str = (v: unknown, name: string, max = 1024): string => {
    if (typeof v !== 'string' || !v.length) throw new BridgeError('badRequest', `${name} is required`);
    if (v.length > max) throw new BridgeError('badRequest', `${name} is too long`);
    return v;
  };
  const num = (v: unknown, name: string): number => {
    if (typeof v !== 'number' || !Number.isFinite(v)) throw new BridgeError('badRequest', `${name} must be a finite number`);
    return v;
  };
  const cellNum = (v: unknown, name: string): number => {
    const n = num(v, name);
    if (!Number.isInteger(n) || n < 0 || n > 1000) throw new BridgeError('badRequest', `${name} must be a non-negative integer`);
    return n;
  };
  const SIZES: WidgetSize[] = ['small', 'medium', 'large'];
  const TYPES = Object.keys(DEFAULT_WIDGET_CONFIG) as WidgetType[];

  // ---------------------------------------------------------------- icons
  host.handle('desktop.moveIcon', (p) => {
    const id = str(p.id, 'id');
    const x = cellNum(p.x, 'x'), y = cellNum(p.y, 'y');
    mutate((d) => {
      const sc = d.shortcuts.find((s) => s.id === id);
      if (sc) { sc.x = x; sc.y = y; return; }
      if (!DESKTOP_ITEMS.some((i) => i.id === id) || !d.showDesktopFolder) throw new BridgeError('notFound', `no desktop icon '${id}'`);
      d.placements = { ...d.placements, [id]: { x, y } };
    });
  });

  host.handle('desktop.open', async (p, ctx) => {
    const id = str(p.id, 'id');
    const d = withPlacements(host.settings.desktop);
    const monitors = new Set([primary, ...d.shortcuts.map((s) => monitorOf(s.monitorId))]);
    const icon = [...monitors].flatMap((m) => iconsFor(d, m)).find((i) => i.id === id);
    if (!icon) throw new BridgeError('notFound', `no desktop icon '${id}'`);
    const app = appById(`aumid:${icon.target}`) ?? APPS.find((a) => a.aumid === icon.target);
    if (app) await host.dispatchCall(ctx, 'apps.launch', { appId: app.id });
    else console.info('[mock] desktop.open', icon.kind, icon.target);
  });

  host.handle('desktop.addShortcut', (p) => {
    const path = str(p.path, 'path', 520);
    if (!isAbsolutePath(path) && !APPS.some((a) => a.aumid === path)) throw new BridgeError('badRequest', 'path must be absolute');
    const monitorId = p.monitorId ?? undefined;
    return mutate((d) => {
      const m = monitorOf(monitorId);
      const cell = p.x !== undefined && p.y !== undefined ? { x: cellNum(p.x, 'x'), y: cellNum(p.y, 'y') } : autoCell(occupiedCells(d, m));
      const name = baseName(path);
      const sc = { id: `sc:${crypto.randomUUID()}`, name, target: path, x: cell.x, y: cell.y, ...(monitorId ? { monitorId } : {}) };
      d.shortcuts = [...d.shortcuts, sc];
      return { id: sc.id, name, iconUrl: shortcutIconUrl(name, path), x: sc.x, y: sc.y, target: path, kind: 'shortcut' as const };
    });
  });

  host.handle('desktop.removeShortcut', (p) => {
    const id = str(p.id, 'id');
    mutate((d) => {
      if (!d.shortcuts.some((s) => s.id === id)) throw new BridgeError('notFound', `no shortcut '${id}'`);
      d.shortcuts = d.shortcuts.filter((s) => s.id !== id);
    });
  });

  host.handle('desktop.setWallpaper', (p) => {
    const path = str(p.path, 'path', 520);
    if (!isAbsolutePath(path)) throw new BridgeError('badRequest', 'path must be absolute');
    mutate((d) => {
      if (p.monitorId) d.wallpapers = { ...d.wallpapers, [p.monitorId]: path };
      else { d.wallpaperPath = path; d.wallpapers = {}; }
    });
  });

  // ---------------------------------------------------------------- widgets
  host.handle('desktop.moveWidget', (p) => {
    const id = str(p.id, 'id');
    const x = num(p.x, 'x'), y = num(p.y, 'y');
    mutate((d) => {
      const w = d.widgets.find((x2) => x2.id === id);
      if (!w) throw new BridgeError('notFound', `no widget '${id}'`);
      w.x = Math.max(0, x); w.y = Math.max(0, y);
    });
  });

  host.handle('desktop.addWidget', (p) => {
    if (!TYPES.includes(p.type)) throw new BridgeError('badRequest', `unknown widget type '${String(p.type)}'`);
    if (p.size !== undefined && !SIZES.includes(p.size)) throw new BridgeError('badRequest', 'invalid size');
    return mutate((d): WidgetInstance => {
      const m = monitorOf(p.monitorId);
      const n = d.widgets.filter((w) => monitorOf(w.monitorId) === m).length;
      const w: WidgetInstance = {
        id: `w-${crypto.randomUUID().slice(0, 8)}`, type: p.type, size: p.size ?? 'medium',
        x: p.x !== undefined ? num(p.x, 'x') : 48 + 24 * n, y: p.y !== undefined ? num(p.y, 'y') : 80 + 24 * n,
        config: DEFAULT_WIDGET_CONFIG[p.type](), ...(p.monitorId ? { monitorId: p.monitorId } : {}),
      };
      d.widgets = [...d.widgets, w];
      return w;
    });
  });

  host.handle('desktop.removeWidget', (p) => {
    const id = str(p.id, 'id');
    mutate((d) => {
      if (!d.widgets.some((w) => w.id === id)) throw new BridgeError('notFound', `no widget '${id}'`);
      d.widgets = d.widgets.filter((w) => w.id !== id);
    });
  });

  host.handle('desktop.updateWidget', (p) => {
    const id = str(p.id, 'id');
    if (p.size !== undefined && !SIZES.includes(p.size)) throw new BridgeError('badRequest', 'invalid size');
    if (p.config !== undefined && (p.config === null || typeof p.config !== 'object' || Array.isArray(p.config))) throw new BridgeError('badRequest', 'config must be an object');
    if (p.config !== undefined && JSON.stringify(p.config).length > 64 * 1024) throw new BridgeError('badRequest', 'config too large');
    return mutate((d) => {
      const w = d.widgets.find((x) => x.id === id);
      if (!w) throw new BridgeError('notFound', `no widget '${id}'`);
      if (p.size !== undefined) w.size = p.size;
      if (p.config !== undefined) w.config = p.config; // REPLACES the whole config object, like the real host
      return structuredClone(w);
    });
  });
}
