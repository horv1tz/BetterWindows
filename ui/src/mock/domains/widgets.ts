// Shared pieces of the ui-desktop mock domains (desktop.ts, weather.ts, reminders.ts): host taps, seed data and original artwork.
// Owner: ui-desktop. The default export is a no-op (domain files are auto-loaded by the MockHost); everything else is imported by the siblings.
import type { DesktopSettings, HostEventType, WidgetInstance, WidgetType } from '../../lib/protocol/generated';
import type { MockContext, MockHost } from '../host';

export default function register(host: MockHost) { taps(host); }

// ------------------------------------------------------------------------------------------------ host taps

type EmitTap = (payload: unknown) => void;
/** May return replacement params (e.g. to translate an anchor); returning undefined keeps them. */
type CallTap = (params: unknown, ctx: MockContext) => unknown;

export interface Taps {
  /** Observe every host.emit of `type` (after it was delivered). Returns an unsubscribe function. */
  onEmit(type: HostEventType, fn: EmitTap): () => void;
  /** Observe/rewrite a call before the handler runs. */
  onCall(type: string, fn: CallTap): () => void;
}

/**
 * The frozen MockHost has no observer API. Domains that must react to events emitted by OTHER domains (settings.changed from the settings
 * mock, overlay.show from the overlay mock, theme.changed ...) install these wrappers once; they only add behaviour around the originals.
 */
export function taps(host: MockHost): Taps {
  const h = host as any;
  if (h.__bwTaps) return h.__bwTaps as Taps;
  const emitTaps = new Map<string, Set<EmitTap>>();
  const callTaps = new Map<string, Set<CallTap>>();
  const origEmit = host.emit.bind(host) as (type: string, payload: unknown, filter?: unknown) => void;
  const origDispatch = host.dispatchCall.bind(host);
  h.emit = (type: string, payload: unknown, filter?: unknown) => {
    origEmit(type, payload, filter);
    for (const fn of [...(emitTaps.get(type) ?? [])]) { try { fn(payload); } catch (e) { console.error('[mock] emit tap threw', type, e); } }
  };
  h.dispatchCall = (ctx: MockContext, type: string, params: unknown) => {
    let p = params;
    for (const fn of [...(callTaps.get(type) ?? [])]) { try { const r = fn(p, ctx); if (r !== undefined) p = r; } catch (e) { console.error('[mock] call tap threw', type, e); } }
    return origDispatch(ctx, type, p);
  };
  const add = <T>(map: Map<string, Set<T>>, type: string, fn: T) => {
    let set = map.get(type);
    if (!set) map.set(type, (set = new Set()));
    set.add(fn);
    return () => { set!.delete(fn); };
  };
  const api: Taps = { onEmit: (type, fn) => add(emitTaps, type, fn), onCall: (type, fn) => add(callTaps, type, fn) };
  h.__bwTaps = api;
  return api;
}

// ------------------------------------------------------------------------------------------------ query flags

export const query = () => new URLSearchParams(location.search);
export const PRIMARY_MONITOR = '1';

// ------------------------------------------------------------------------------------------------ widget defaults

export const DEFAULT_WIDGET_CONFIG: Record<WidgetType, () => Record<string, unknown>> = {
  clock: () => ({ style: 'analog', seconds: false }),
  calendar: () => ({ weekNumbers: false }),
  weather: () => ({}),
  reminders: () => ({}),
  media: () => ({}),
  battery: () => ({}),
  notes: () => ({ text: '' }),
};

/** Seed layout of the desktop for a monitor of the given DIP width: primary = widgets stacked in the right half, secondary = left. */
export function seedWidgets(width: number, mode: string): WidgetInstance[] {
  if (mode === '0' || mode === 'none') return [];
  const rightX = Math.max(260, width - 380 - 40);
  const leftX = Math.max(60, rightX - 12 - 184);
  const w = (id: string, type: WidgetType, size: WidgetInstance['size'], x: number, y: number, config?: Record<string, unknown>, monitorId?: string): WidgetInstance =>
    ({ id, type, size, x, y, config: config ?? DEFAULT_WIDGET_CONFIG[type](), ...(monitorId ? { monitorId } : {}) });
  const primary: WidgetInstance[] = [
    w('w-weather', 'weather', 'medium', rightX, 48),
    w('w-reminders', 'reminders', 'medium', rightX, 244),
    w('w-clock', 'clock', 'small', leftX, 48, { style: 'analog', seconds: true }),
    w('w-calendar', 'calendar', 'small', leftX, 244),
    w('w-notes', 'notes', 'small', leftX, 440, { text: 'Ideas for the weekend:\n- finish the dock polish\n- try the new wallpaper\n- call mum' }),
  ];
  if (mode === 'all') {
    primary.push(
      w('w-media', 'media', 'medium', rightX, 440),
      w('w-battery', 'battery', 'medium', rightX, 636),
    );
  }
  const secondary: WidgetInstance[] = [
    w('w2-clock', 'clock', 'medium', 60, 56, { style: 'digital', seconds: true }, '2'),
    w('w2-media', 'media', 'medium', 60, 252, undefined, '2'),
    w('w2-battery', 'battery', 'small', 452, 252, undefined, '2'),
    w('w2-weather', 'weather', 'large', 60, 448, undefined, '2'),
  ];
  return [...primary, ...secondary];
}

export function seedDesktopSettings(d: DesktopSettings, width: number, mode: string): DesktopSettings {
  return { ...d, enabled: true, widgets: seedWidgets(width, mode) };
}

// ------------------------------------------------------------------------------------------------ artwork (original, data URLs)

const svgUrl = (svg: string) => `data:image/svg+xml;utf8,${encodeURIComponent(svg)}`;

export function hash(s: string): number {
  let h = 5381;
  for (let i = 0; i < s.length; i++) h = ((h << 5) + h + s.charCodeAt(i)) | 0;
  return Math.abs(h);
}

/** "This PC": a monitor on a stand. */
export function thisPcIcon(): string {
  return svgUrl(`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 128 128"><defs>
<linearGradient id="s" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#5ec1ff"/><stop offset="1" stop-color="#3a63e0"/></linearGradient>
<linearGradient id="b" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#d9dde6"/><stop offset="1" stop-color="#9aa1b1"/></linearGradient></defs>
<rect x="14" y="20" width="100" height="68" rx="9" fill="url(#b)"/><rect x="19" y="25" width="90" height="58" rx="5" fill="url(#s)"/>
<path d="M19 66 C 45 52, 70 80, 109 58 L109 78 a5 5 0 0 1 -5 5 H24 a5 5 0 0 1 -5 -5Z" fill="#fff" fill-opacity=".16"/>
<rect x="54" y="88" width="20" height="14" fill="#a6adbc"/><rect x="38" y="100" width="52" height="8" rx="4" fill="url(#b)"/></svg>`);
}

/** "Recycle Bin": a translucent bin with ribs. */
export function recycleBinIcon(): string {
  return svgUrl(`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 128 128"><defs>
<linearGradient id="g" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#e9edf5"/><stop offset="1" stop-color="#9fb0c8"/></linearGradient></defs>
<path d="M36 40 H92 L88 108 a8 8 0 0 1 -8 7 H48 a8 8 0 0 1 -8 -7Z" fill="url(#g)" fill-opacity=".92"/>
<rect x="28" y="28" width="72" height="12" rx="6" fill="#c9d3e3"/><rect x="52" y="20" width="24" height="10" rx="5" fill="#b3bfd3"/>
<g stroke="#6f819e" stroke-width="4" stroke-linecap="round" opacity=".6"><path d="M54 54 V98"/><path d="M64 54 V98"/><path d="M74 54 V98"/></g></svg>`);
}

/** A folder; the optional glyph is a small white mark on its front. */
export function folderIcon(glyph: 'none' | 'doc' | 'down' | 'code' = 'none'): string {
  const g = {
    none: '',
    doc: '<path d="M56 62 h16 l8 8 v22 a3 3 0 0 1 -3 3 H56 a3 3 0 0 1 -3 -3 V65 a3 3 0 0 1 3 -3Z" fill="#fff" fill-opacity=".92"/>',
    down: '<path d="M64 60 v26 M52 76 l12 12 l12 -12" fill="none" stroke="#fff" stroke-opacity=".95" stroke-width="7" stroke-linecap="round" stroke-linejoin="round"/>',
    code: '<path d="M56 66 l-10 12 l10 12 M72 66 l10 12 l-10 12" fill="none" stroke="#fff" stroke-opacity=".95" stroke-width="6" stroke-linecap="round" stroke-linejoin="round"/>',
  }[glyph];
  return svgUrl(`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 128 128"><defs>
<linearGradient id="back" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#49a8f5"/><stop offset="1" stop-color="#2a74d9"/></linearGradient>
<linearGradient id="front" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#7fd0ff"/><stop offset="1" stop-color="#3d95f0"/></linearGradient></defs>
<path d="M12 36 a8 8 0 0 1 8 -8 h27 l11 11 h51 a8 8 0 0 1 8 8 V98 a8 8 0 0 1 -8 8 H20 a8 8 0 0 1 -8 -8Z" fill="url(#back)"/>
<rect x="12" y="46" width="104" height="60" rx="8" fill="url(#front)"/><rect x="12" y="46" width="104" height="2" fill="#fff" fill-opacity=".5"/>${g}</svg>`);
}

/** A document page with a folded corner and a coloured type band. */
export function fileIcon(ext: string): string {
  const palette: Record<string, [string, string]> = { txt: ['#8e96a8', 'TXT'], xlsx: ['#2fa45a', 'XLS'], png: ['#8e5be0', 'PNG'], pdf: ['#e0483f', 'PDF'], docx: ['#3b73e0', 'DOC'] };
  const [color, label] = palette[ext] ?? ['#8e96a8', ext.slice(0, 3).toUpperCase()];
  return svgUrl(`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 128 128"><defs>
<linearGradient id="p" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#ffffff"/><stop offset="1" stop-color="#e3e7ee"/></linearGradient></defs>
<path d="M30 12 H78 L100 34 V108 a8 8 0 0 1 -8 8 H30 a8 8 0 0 1 -8 -8 V20 a8 8 0 0 1 8 -8Z" fill="url(#p)"/>
<path d="M78 12 V28 a6 6 0 0 0 6 6 H100Z" fill="#c5ccd8"/>
<g stroke="#b5bdcc" stroke-width="4" stroke-linecap="round"><path d="M38 52 H84"/><path d="M38 64 H84"/><path d="M38 76 H66"/></g>
<rect x="22" y="86" width="60" height="22" rx="5" fill="${color}"/><text x="52" y="102.5" font-family="Inter,Segoe UI,sans-serif" font-size="14" font-weight="700" text-anchor="middle" fill="#fff">${label}</text></svg>`);
}

interface Palette { sky: [string, string, string]; glow: string; hills: [string, string, string, string] }
const PALETTES: Record<string, Palette> = {
  sonoma: { sky: ['#1c2a6b', '#8a4fa0', '#f7a98a'], glow: '#ffd9a8', hills: ['#6b4a9c', '#4b3a86', '#2f2f6e', '#1b1f4d'] },
  dusk: { sky: ['#0b3a5b', '#1c7fa6', '#9fe0e3'], glow: '#e6fff7', hills: ['#2a8d9c', '#1c6e86', '#134f68', '#0b3350'] },
  ember: { sky: ['#2a1038', '#b33a5b', '#ffb36b'], glow: '#fff0c4', hills: ['#8a2f56', '#63224f', '#42184a', '#26103a'] },
  forest: { sky: ['#0e2a2d', '#2b6b58', '#c9e6a9'], glow: '#f5ffd0', hills: ['#3f8a5e', '#2d6b50', '#1f4f40', '#123329'] },
};

const hsl = (h: number, s: number, l: number) => `hsl(${Math.round(h) % 360} ${s}% ${l}%)`;

function paletteFor(seed: string): Palette {
  if (PALETTES[seed]) return PALETTES[seed];
  const keys = Object.keys(PALETTES);
  const base = PALETTES[keys[hash(seed) % keys.length]];
  const rot = (hash(seed + 'h') % 70) - 35;
  const shift = (c: string) => {
    // rotate the hue of a #rrggbb colour
    const n = parseInt(c.slice(1), 16);
    const r = (n >> 16) / 255, g = ((n >> 8) & 255) / 255, b = (n & 255) / 255;
    const max = Math.max(r, g, b), min = Math.min(r, g, b), l = (max + min) / 2, d = max - min;
    let hh = 0;
    if (d) hh = max === r ? ((g - b) / d) % 6 : max === g ? (b - r) / d + 2 : (r - g) / d + 4;
    const s = d ? d / (1 - Math.abs(2 * l - 1)) : 0;
    return hsl(hh * 60 + rot, Math.round(s * 100), Math.round(l * 100));
  };
  return { sky: base.sky.map(shift) as Palette['sky'], glow: shift(base.glow), hills: base.hills.map(shift) as Palette['hills'] };
}

/** A soft dusk-landscape wallpaper: sky gradient, glow at the horizon and four layered hills. Deterministic per seed. */
export function wallpaperArt(seed: string, w = 1920, h = 1200): string {
  const p = paletteFor(seed);
  const r = (k: string, lo: number, hi: number) => lo + ((hash(seed + k) % 1000) / 1000) * (hi - lo);
  const sunX = r('sx', 0.25, 0.75) * w;
  const horizon = h * r('hz', 0.5, 0.6);
  const hill = (i: number, base: number, amp: number, fill: string) => {
    const y0 = base + r(`h${i}a`, -amp, amp) * 0.4;
    const c = (k: string) => base + r(`h${i}${k}`, -amp, amp);
    return `<path d="M0 ${y0} C ${w * 0.2} ${c('b')}, ${w * 0.35} ${c('c')}, ${w * 0.55} ${c('d')} S ${w * 0.9} ${c('e')}, ${w} ${c('f')} V${h} H0Z" fill="${fill}"/>`;
  };
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}" viewBox="0 0 ${w} ${h}" preserveAspectRatio="xMidYMid slice"><defs>
<linearGradient id="sky" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="${p.sky[0]}"/><stop offset=".55" stop-color="${p.sky[1]}"/><stop offset="1" stop-color="${p.sky[2]}"/></linearGradient>
<radialGradient id="glow" cx="${sunX / w}" cy="${horizon / h}" r=".55"><stop offset="0" stop-color="${p.glow}" stop-opacity=".95"/><stop offset=".35" stop-color="${p.glow}" stop-opacity=".28"/><stop offset="1" stop-color="${p.glow}" stop-opacity="0"/></radialGradient>
<linearGradient id="shade" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#000" stop-opacity="0"/><stop offset="1" stop-color="#000" stop-opacity=".35"/></linearGradient></defs>
<rect width="${w}" height="${h}" fill="url(#sky)"/><rect width="${w}" height="${h}" fill="url(#glow)"/>
<circle cx="${sunX}" cy="${horizon - h * 0.04}" r="${h * 0.045}" fill="${p.glow}" fill-opacity=".9"/>
${hill(0, horizon, h * 0.08, p.hills[0])}${hill(1, horizon + h * 0.12, h * 0.09, p.hills[1])}${hill(2, horizon + h * 0.25, h * 0.08, p.hills[2])}${hill(3, horizon + h * 0.37, h * 0.07, p.hills[3])}
<rect width="${w}" height="${h}" fill="url(#shade)"/></svg>`;
  return svgUrl(svg);
}
