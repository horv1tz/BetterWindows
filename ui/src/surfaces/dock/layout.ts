import { createFisheye, influenceRadius, maxSideShift, type Fisheye } from './magnify';

/**
 * Dock geometry in DIPs. Pure functions, no DOM.
 *
 * Coordinates: the "strip" is the row (bottom dock) or column (left/right dock) of items along the MAIN axis, starting at 0.
 * The "cross" axis points from the docked screen edge towards the screen centre. The rest pill is the strip plus `pad` on every side.
 */
export type DockPos = 'bottom' | 'left' | 'right';
export type SlotKind = 'item' | 'sep' | 'tile';

export interface Metrics {
  /** Icon box (settings.dock.iconSize, possibly reduced to fit the monitor). */
  size: number;
  /** Spacing between neighbouring slots on the main axis. */
  gap: number;
  /** Padding between the pill edge and the icons (all four sides). */
  pad: number;
  /** Separator slot length on the main axis (the line itself is 1 px, centred). */
  sepLen: number;
  /** Separator line length on the cross axis. */
  sepCross: number;
  /** Minimised-window tile extents in screen terms (landscape). */
  tileW: number;
  tileH: number;
  /** Distance between the screen edge and the pill. */
  edgeGap: number;
  /** Transparent safety margin around the pill inside the window (room for the 0.5 px border / soft shadow). */
  margin: number;
  /** Pill corner radius. */
  radius: number;
  /** Height reserved for the tooltip bubble plus its gap to the icons. */
  tipRoom: number;
}

export function metricsFor(size: number): Metrics {
  const s = Math.round(size);
  return {
    size: s,
    gap: Math.max(2, Math.round(s * 0.1)),
    pad: Math.max(6, Math.round(s * 0.18)),
    sepLen: 5,
    sepCross: Math.round(s * 0.72),
    tileW: Math.round(s * 1.12),
    tileH: Math.round(s * 0.76),
    edgeGap: 8,
    margin: 6,
    radius: Math.max(14, Math.min(26, Math.round(s * 0.36))),
    tipRoom: 40,
  };
}

/** Pill thickness on the cross axis at rest. */
export function pillThickness(m: Metrics): number { return m.size + 2 * m.pad; }
/** The always-visible bar extent from the docked edge (what the host may reserve as AppBar). */
export function barThickness(m: Metrics): number { return m.edgeGap + pillThickness(m); }

export interface Slot {
  key: string;
  kind: SlotKind;
  /** Rest start on the main axis (strip coordinates). */
  start: number;
  /** Rest length on the main axis. */
  len: number;
  /** Rest extent on the cross axis. */
  cross: number;
}

export interface SlotSpec { key: string; kind: SlotKind }

export function slotExtent(kind: SlotKind, m: Metrics, pos: DockPos): { len: number; cross: number } {
  if (kind === 'sep') return { len: m.sepLen, cross: m.sepCross };
  if (kind === 'tile') return pos === 'bottom' ? { len: m.tileW, cross: m.tileH } : { len: m.tileH, cross: m.tileW };
  return { len: m.size, cross: m.size };
}

export interface StripLayout { slots: Slot[]; length: number }

export function layoutStrip(specs: SlotSpec[], m: Metrics, pos: DockPos): StripLayout {
  const slots: Slot[] = [];
  let at = 0;
  for (const s of specs) {
    const e = slotExtent(s.kind, m, pos);
    slots.push({ key: s.key, kind: s.kind, start: at, len: e.len, cross: e.cross });
    at += e.len + m.gap;
  }
  return { slots, length: Math.max(0, at - (specs.length ? m.gap : 0)) };
}

/** Pill length on the main axis at rest. */
export function restPillLength(strip: StripLayout, m: Metrics): number { return Math.max(strip.length, m.size * 0.5) + 2 * m.pad; }

export interface MagnifySpec {
  enabled: boolean;
  /** magnifiedSize / size, >= 1 */
  scale: number;
  fisheye: Fisheye;
  /** Largest icon extent on the cross axis (px). */
  maxIcon: number;
}

export function magnifySpec(m: Metrics, enabled: boolean, iconSize: number, magnifiedSize: number): MagnifySpec {
  const maxIcon = Math.max(m.size, Math.round(m.size * (magnifiedSize / Math.max(iconSize, 1))));
  const fisheye = enabled ? createFisheye(m.size, maxIcon, influenceRadius(m.size, m.gap)) : { amp: 0, radius: 1 };
  return { enabled: enabled && maxIcon > m.size, scale: maxIcon / m.size, fisheye, maxIcon };
}

export type DockMode = 'idle' | 'hover' | 'drag';

export interface SizingInput {
  pos: DockPos;
  strip: StripLayout;
  m: Metrics;
  mag: MagnifySpec;
  mode: DockMode;
  /** Largest bounce amplitude that must stay inside the window (px) while an item hops. */
  hop: number;
  /** Extra main-axis room while a drag may insert an item (px). */
  dragExtra: number;
  /** Monitor extents in DIPs (the host clamps to them anyway; we pre-clamp so that bar thickness stays truthful). */
  monitorMain: number;
  /** autoHide and currently slid out. */
  collapsed: boolean;
}

export interface WindowSizing { width: number; height: number; barThickness: number }

export const COLLAPSED_SIZE = 4;
/** Extra room above the bar while dragging an icon out of the dock (unpin zone). */
export const DRAG_HEADROOM = 120;

/** The window size the dock asks the host for (docs/IPC.md section 5): idle = bar only, hover = bar + magnification headroom + tooltip room. */
export function windowSizing(i: SizingInput): WindowSizing {
  const { m } = i;
  const bar = barThickness(m);
  if (i.collapsed) return { width: COLLAPSED_SIZE, height: COLLAPSED_SIZE, barThickness: COLLAPSED_SIZE };
  const restMain = restPillLength(i.strip, m);
  let main = restMain + 2 * m.margin;
  let cross = bar + m.margin;
  if (i.mode !== 'idle') {
    const side = i.mag.enabled ? Math.ceil(maxSideShift(i.mag.fisheye)) + 2 : 0;
    const headroom = Math.max(i.mag.enabled ? i.mag.maxIcon - m.size : 0, i.hop);
    main += 2 * side;
    cross += headroom + m.tipRoom;
    if (i.mode === 'drag') {
      main += i.dragExtra;
      cross = Math.max(cross, bar + DRAG_HEADROOM + m.margin);
    }
  } else {
    if (i.hop > 0) cross += i.hop;
  }
  main = Math.min(Math.ceil(main), Math.max(i.monitorMain, 64));
  cross = Math.ceil(cross);
  return i.pos === 'bottom' ? { width: main, height: cross, barThickness: bar } : { width: cross, height: main, barThickness: bar };
}

/**
 * The largest icon size (<= requested) for which the hovering dock still fits `monitorMain` DIPs along its main axis
 * (macOS shrinks the dock the same way when there are too many apps). `count` = number of icon slots, `extras` = other slot lengths.
 */
export function fitIconSize(requested: number, monitorMain: number, specs: SlotSpec[], pos: DockPos, magnifiedSize: number, magnification: boolean): number {
  const min = 24;
  for (let size = Math.max(min, Math.round(requested)); size > min; size -= 1) {
    const m = metricsFor(size);
    const strip = layoutStrip(specs, m, pos);
    const mag = magnifySpec(m, magnification, requested, magnifiedSize);
    const side = mag.enabled ? Math.ceil(maxSideShift(mag.fisheye)) + 2 : 0;
    if (restPillLength(strip, m) + 2 * side + 2 * m.margin <= monitorMain * 0.98) return size;
  }
  return min;
}
