import { BridgeError } from '../../lib/ipc/bridge';
import type { DesktopInfo, McSnapshot, McSnapshotParams, McWindow, Rect } from '../../lib/protocol/generated';
import type { MockHost } from '../host';
import { APPS } from '../fixtures';

/**
 * Mission Control mock (owner: ui-overlay-mc). Behaves like the real host's MissionControlController:
 *  - mc.snapshot numbers every snapshot with a rev that only grows; the same rev stream feeds mc.changed;
 *  - mc.changed (transient, overlay only) follows every mutation, and a live simulation adds/removes a window now and then;
 *  - thumbUrl is always set (served lazily by the host); a few windows point at a URL that fails, which exercises the
 *    icon fallback tile;
 *  - mc.thumbnail is NOT sent (the real host does not publish it).
 *
 * Query flags (on the page URL, before the hash):
 *   ?desktops=0      desktops are not supported: one desktop, no strip, windows have no desktopId
 *   ?moving=0        mc.moveWindow answers 'unsupported' (Windows versions that refuse cross-desktop moves)
 *   ?mcWindows=24    total number of windows (default 11; extra windows are generated deterministically)
 *   ?thumbs=0        every thumbnail URL fails (exercises the fallback tiles for the whole grid)
 *   ?mcLive=0        no simulated mc.changed events
 */

interface Win {
  handle: string;
  title: string;
  appName: string;
  iconUrl: string;
  desktopId?: string;
  bounds: Rect;
  minimized: boolean;
  active: boolean;
  noThumb: boolean;
  broken: boolean;
  hue: number;
}

const MONITOR_ID = '1';

/** Seed layout: five windows ordered most-recently-active first. Bounds fit a 1440x900 monitor. */
const SEED: Array<{ app: string; title: string; desk: number; b: [number, number, number, number]; minimized?: boolean; broken?: boolean; noThumb?: boolean }> = [
  { app: 'Visual Studio Code', title: 'BetterWindows — README.md', desk: 0, b: [120, 90, 980, 620] },
  { app: 'Microsoft Edge', title: 'Inbox — Microsoft Edge', desk: 0, b: [60, 40, 860, 560] },
  { app: 'Terminal', title: 'PowerShell 7', desk: 0, b: [760, 300, 600, 380] },
  { app: 'Spotify', title: 'Liked Songs', desk: 1, b: [40, 40, 420, 640] },
  { app: 'Mail', title: 'Inbox — Mail', desk: 1, b: [520, 90, 860, 620] },
  { app: 'Settings', title: 'Display', desk: 1, b: [1000, 30, 400, 520] },
  { app: 'Calculator', title: 'Calculator', desk: 1, b: [400, 500, 320, 440], minimized: true, noThumb: true },
  { app: 'Word', title: 'Quarterly report.docx — Word', desk: 2, b: [240, 70, 820, 760] },
  { app: 'Excel', title: 'Budget 2026.xlsx — Excel', desk: 2, b: [60, 220, 700, 520] },
  { app: 'Photos', title: 'Holiday 2025', desk: 2, b: [880, 160, 520, 420] },
  { app: 'Notepad', title: 'Draft notes', desk: 2, b: [1000, 600, 380, 260], broken: true },
];

const EXTRA_APPS = ['Discord', 'Slack', 'Telegram', 'Paint', 'Snipping Tool', 'Clock', 'Weather', 'Steam', 'Firefox', 'PowerPoint', 'VLC media player', 'OBS Studio'];

export default function register(host: MockHost) {
  const q = new URLSearchParams(location.search);
  const supported = q.get('desktops') !== '0';
  const moving = q.get('moving') !== '0';
  const thumbsOk = q.get('thumbs') !== '0';
  const live = q.get('mcLive') !== '0';
  const totalWindows = Math.max(1, Math.min(60, Number(q.get('mcWindows') ?? SEED.length) || SEED.length));
  // The monitor is the overlay's viewport (the overlay covers its monitor): default to the browser window size.
  const monW = Number(q.get('w') ?? innerWidth) || 1440;
  const monH = Number(q.get('h') ?? innerHeight) || 900;
  // The seed layout is designed for 1440x900: scale it to the monitor so it always fits.
  const k = Math.min(monW / 1440, monH / 900);

  const deskCount = supported ? 3 : 1;
  let desktops: DesktopInfo[] = Array.from({ length: deskCount }, (_, i) => ({ id: `d${i + 1}`, name: `Desktop ${i + 1}`, index: i, current: i === 0 }));
  let deskSeq = deskCount;
  let windows: Win[] = [];
  let rev = 0;
  let handleSeq = 60000;
  let liveTick = 0;
  let liveStarted = false;
  const simulated: string[] = [];

  const mkWin = (app: string, title: string, desk: number, b: [number, number, number, number], flags: { minimized?: boolean; broken?: boolean; noThumb?: boolean } = {}): Win => {
    const a = APPS.find((x) => x.name === app) ?? APPS[0]!;
    const handle = String(handleSeq++);
    return {
      handle,
      title,
      appName: a.name,
      iconUrl: a.iconUrl,
      desktopId: supported ? desktops[Math.min(desk, desktops.length - 1)]!.id : undefined,
      bounds: { x: b[0], y: b[1], width: b[2], height: b[3] },
      minimized: !!flags.minimized,
      active: false,
      noThumb: !!flags.noThumb,
      broken: !!flags.broken || !thumbsOk,
      hue: hueOf(a.name),
    };
  };

  const seedWindows = () => {
    windows = SEED.map((s) => mkWin(s.app, s.title, s.desk, s.b.map((v) => Math.round(v * k)) as [number, number, number, number], s));
    windows[0]!.active = true;
    // extra windows (deterministic) when more than the seed count is requested
    let seed = 7;
    const rnd = () => ((seed = (seed * 16807) % 2147483647) / 2147483647);
    for (let i = windows.length; i < totalWindows; i++) {
      const w = 360 + Math.floor(rnd() * 400);
      const h = 240 + Math.floor(rnd() * 320);
      const x = Math.floor(rnd() * Math.max(1, monW - w));
      const y = Math.floor(rnd() * Math.max(1, monH - h - 40));
      const app = EXTRA_APPS[i % EXTRA_APPS.length]!;
      windows.push(mkWin(app, `${app} — window ${i + 1}`, i % deskCount, [x, y, w, h], { noThumb: i % 9 === 0 }));
    }
  };
  seedWindows();

  const toWire = (w: Win): McWindow => ({
    handle: w.handle,
    title: w.title,
    appId: APPS.find((a) => a.name === w.appName)?.id ?? '',
    appName: w.appName,
    iconUrl: w.iconUrl,
    thumbUrl: w.noThumb ? undefined : w.broken ? `/dyn/thumb/${w.handle}?rev=1` : thumbDataUrl(w),
    ...(supported ? { desktopId: w.desktopId } : {}),
    bounds: { ...w.bounds },
    minimized: w.minimized,
    active: w.active,
    monitorId: MONITOR_ID,
  });

  /** A full snapshot; every call takes the next rev (the host numbers snapshots per overlay show). */
  const snapshot = (monitorId: string | null | undefined): McSnapshot => ({
    desktops: desktops.map((d) => ({ ...d })),
    desktopsSupported: supported,
    windows: monitorId && monitorId !== MONITOR_ID ? [] : windows.map(toWire),
    rev: ++rev,
  });

  const emitChanged = () => host.emit('mc.changed', snapshot(null), (c) => c.surface === 'overlay');

  const find = (handle: string): Win => {
    const w = windows.find((x) => x.handle === handle);
    if (!w) throw new BridgeError('notFound', `no window ${handle}`);
    return w;
  };

  host.handle('mc.snapshot', (p: McSnapshotParams | undefined) => {
    startLive();
    return snapshot(p?.monitorId ?? null);
  });

  host.handle('mc.activateWindow', (p) => {
    const w = find(p.handle);
    windows.forEach((x) => (x.active = x === w));
    w.minimized = false;
    if (supported) desktops = desktops.map((d) => ({ ...d, current: d.id === w.desktopId }));
    emitChanged();
  });

  host.handle('mc.closeWindow', (p) => {
    find(p.handle);
    windows = windows.filter((x) => x.handle !== p.handle);
    emitChanged();
  });

  host.handle('mc.switchDesktop', (p) => {
    const d = desktops.find((x) => x.id === p.desktopId);
    if (!d) throw new BridgeError('notFound', `no desktop ${p.desktopId}`);
    desktops = desktops.map((x) => ({ ...x, current: x.id === d.id }));
    emitChanged();
  });

  host.handle('mc.moveWindow', (p) => {
    if (!moving) throw new BridgeError('unsupported', 'moving windows between desktops is not supported on this Windows version');
    const w = find(p.handle);
    if (!supported) throw new BridgeError('unsupported', 'desktops are not supported');
    if (!desktops.some((d) => d.id === p.desktopId)) throw new BridgeError('notFound', `no desktop ${p.desktopId}`);
    w.desktopId = p.desktopId;
    emitChanged();
  });

  host.handle('mc.createDesktop', () => {
    if (!supported) throw new BridgeError('unsupported', 'desktops are not supported');
    if (desktops.length >= 9) throw new BridgeError('failed', 'too many desktops');
    const d: DesktopInfo = { id: `d${++deskSeq}`, name: `Desktop ${desktops.length + 1}`, index: desktops.length, current: false };
    desktops = [...desktops, d];
    emitChanged();
    return { ...d };
  });

  host.handle('mc.removeDesktop', (p) => {
    if (!supported) throw new BridgeError('unsupported', 'desktops are not supported');
    if (desktops.length <= 1) throw new BridgeError('badRequest', 'the last desktop cannot be removed');
    const gone = desktops.find((d) => d.id === p.desktopId);
    if (!gone) throw new BridgeError('notFound', `no desktop ${p.desktopId}`);
    let remaining = desktops.filter((d) => d.id !== gone.id);
    if (gone.current) remaining = remaining.map((d, i) => ({ ...d, current: i === 0 }));
    const target = remaining.find((d) => d.current)!.id;
    windows = windows.map((w) => (w.desktopId === gone.id ? { ...w, desktopId: target } : w));
    desktops = remaining.map((d, i) => ({ ...d, index: i }));
    emitChanged();
  });

  /** A simulated desktop: every ~9 s a new window opens on the current desktop, or the last simulated one closes. */
  function startLive() {
    if (liveStarted || !live) return;
    liveStarted = true;
    setInterval(() => {
      liveTick++;
      if (liveTick % 2 === 1 || simulated.length === 0) {
        const app = ['Notepad', 'Calculator', 'Clock'][liveTick % 3]!;
        const cur = desktops.find((d) => d.current)?.id;
        const w = mkWin(app, `${app} (new)`, 0, [200 + (liveTick * 37) % 400, 140 + (liveTick * 23) % 260, 420, 300]);
        w.desktopId = cur;
        windows = [w, ...windows];
        simulated.push(w.handle);
      } else {
        const h = simulated.pop()!;
        windows = windows.filter((w) => w.handle !== h);
      }
      emitChanged();
    }, 9000);
  }
}

function hueOf(s: string): number {
  let h = 5381;
  for (let i = 0; i < s.length; i++) h = ((h << 5) + h + s.charCodeAt(i)) | 0;
  return Math.abs(h) % 360;
}

/** A window "screenshot" as an SVG data URL: wallpaper gradient, window frame with traffic lights, title and content lines. */
function thumbDataUrl(w: Win): string {
  const W = 480;
  const H = Math.min(900, Math.max(200, Math.round((W * w.bounds.height) / w.bounds.width)));
  const bar = 26;
  const h2 = (w.hue + 60) % 360;
  const lines: string[] = [];
  let seed = w.hue + w.handle.length * 13;
  const rnd = () => ((seed = (seed * 16807) % 2147483647) / 2147483647);
  for (let i = 0; i < 9; i++) {
    const lw = (0.35 + rnd() * 0.55) * (W - 150);
    lines.push(`<rect x="${136}" y="${bar + 30 + i * 22}" width="${lw.toFixed(1)}" height="8" rx="4" fill="#c9ccd6"/>`);
  }
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}">`
    + `<defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="hsl(${w.hue} 70% 62%)"/><stop offset="1" stop-color="hsl(${h2} 65% 36%)"/></linearGradient></defs>`
    + `<rect width="${W}" height="${H}" fill="url(#g)"/>`
    + `<rect x="10" y="10" width="${W - 20}" height="${H - 20}" rx="10" fill="#ffffff" fill-opacity="0.97"/>`
    + `<rect x="10" y="10" width="${W - 20}" height="${bar}" rx="10" fill="#e6e7ec"/>`
    + `<rect x="10" y="${bar}" width="${W - 20}" height="8" fill="#e6e7ec"/>`
    + `<circle cx="26" cy="23" r="5" fill="#ff5f57"/><circle cx="41" cy="23" r="5" fill="#febc2e"/><circle cx="56" cy="23" r="5" fill="#28c840"/>`
    + `<text x="${W / 2}" y="27" font-family="Segoe UI, Inter, sans-serif" font-size="12" text-anchor="middle" fill="#4a4d57">${escapeXml(w.title)}</text>`
    + `<rect x="10" y="${bar + 8}" width="108" height="${H - bar - 18}" fill="#f2f3f7"/>`
    + `<rect x="22" y="${bar + 28}" width="84" height="8" rx="4" fill="${`hsl(${w.hue} 70% 60%)`}"/>`
    + lines.join('')
    + `</svg>`;
  return `data:image/svg+xml;utf8,${encodeURIComponent(svg)}`;
}

function escapeXml(s: string): string {
  return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}
