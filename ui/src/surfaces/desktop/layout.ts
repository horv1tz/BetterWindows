// Pure geometry for the desktop surface: icon grid, drag planning, marquee, selection and the widget grid.
// No DOM and no Svelte in here so every rule is unit-testable (layout.test.ts).
import type { Settings, WidgetSize } from '$lib/protocol/generated';

export interface Point { x: number; y: number }
export interface Size { width: number; height: number }
export interface Box { x: number; y: number; width: number; height: number }
export interface Insets { top: number; right: number; bottom: number; left: number }
/** A grid cell: x = column, y = row (the same meaning as DesktopIcon.x / DesktopIcon.y). */
export interface Cell { x: number; y: number }

export const MENUBAR_HEIGHT = 28;
export const GRID_PAD = 8;
export const MIN_ICON = 32;
export const MAX_ICON = 128;

export const clamp = (v: number, lo: number, hi: number) => Math.min(Math.max(v, lo), Math.max(lo, hi));
export const cellKey = (c: Cell) => `${c.x},${c.y}`;

/** Space the desktop must leave free for the other shell surfaces (menu bar on top, a reserved dock on its edge). */
export function surfaceInsets(s: Pick<Settings, 'menuBar' | 'dock'>): Insets {
  const top = s.menuBar.enabled ? MENUBAR_HEIGHT : 0;
  const dock = s.dock.enabled && !s.dock.autoHide ? Math.round(clamp(s.dock.iconSize, 32, 128) + 28) : 0;
  return {
    top,
    bottom: s.dock.position === 'bottom' ? dock : 0,
    left: s.dock.position === 'left' ? dock : 0,
    right: s.dock.position === 'right' ? dock : 0,
  };
}

// ---------------------------------------------------------------- icon grid

export interface GridMetrics {
  iconSize: number;
  cellW: number;
  cellH: number;
  cols: number;
  rows: number;
  originX: number;
  originY: number;
}

/** Cell size follows the icon size (room for a 2-line label); the grid starts below the menu bar at the top-left. */
export function gridMetrics(iconSize: number, viewport: Size, insets: Insets): GridMetrics {
  const size = clamp(Math.round(Number.isFinite(iconSize) && iconSize > 0 ? iconSize : 64), MIN_ICON, MAX_ICON);
  const cellW = size + 36;
  const cellH = size + 46;
  const originX = insets.left + GRID_PAD;
  const originY = insets.top + GRID_PAD;
  const availW = viewport.width - originX - insets.right - GRID_PAD;
  const availH = viewport.height - originY - insets.bottom - GRID_PAD;
  return {
    iconSize: size, cellW, cellH, originX, originY,
    cols: Math.max(1, Math.floor(availW / cellW)),
    rows: Math.max(1, Math.floor(availH / cellH)),
  };
}

export function cellBox(m: GridMetrics, c: Cell): Box {
  return { x: m.originX + c.x * m.cellW, y: m.originY + c.y * m.cellH, width: m.cellW, height: m.cellH };
}

export function isInsideGrid(m: GridMetrics, c: Cell): boolean {
  return c.x >= 0 && c.y >= 0 && c.x < m.cols && c.y < m.rows;
}

export function clampCell(m: GridMetrics, c: Cell): Cell {
  return { x: clamp(Math.round(c.x), 0, m.cols - 1), y: clamp(Math.round(c.y), 0, m.rows - 1) };
}

/** The cell under a point (always inside the grid). */
export function cellFromPoint(m: GridMetrics, p: Point): Cell {
  return clampCell(m, { x: Math.floor((p.x - m.originX) / m.cellW), y: Math.floor((p.y - m.originY) / m.cellH) });
}

/** The nearest unoccupied cell to `want` (ring search, ties: smaller row first, then smaller column). Grows extra columns when the grid is full. */
export function nearestFreeCell(m: GridMetrics, want: Cell, occupied: ReadonlySet<string>): Cell {
  const start = clampCell(m, want);
  if (!occupied.has(cellKey(start))) return start;
  const maxR = m.cols + m.rows;
  for (let r = 1; r <= maxR; r++) {
    const ring: Cell[] = [];
    for (let dy = -r; dy <= r; dy++) {
      for (let dx = -r; dx <= r; dx++) {
        if (Math.max(Math.abs(dx), Math.abs(dy)) !== r) continue;
        const c = { x: start.x + dx, y: start.y + dy };
        if (isInsideGrid(m, c) && !occupied.has(cellKey(c))) ring.push(c);
      }
    }
    if (ring.length) {
      ring.sort((a, b) => dist2(a, start) - dist2(b, start) || a.y - b.y || a.x - b.x);
      return ring[0];
    }
  }
  // Grid is full: continue in extra (off-screen) columns so nothing ever overlaps.
  for (let x = m.cols; ; x++) for (let y = 0; y < m.rows; y++) if (!occupied.has(`${x},${y}`)) return { x, y };
}

function dist2(a: Cell, b: Cell) { return (a.x - b.x) ** 2 + (a.y - b.y) ** 2; }

/**
 * The host stores one cell per icon but knows nothing about the visible grid (another resolution, a bigger icon size). Give every icon a
 * unique, visible cell: first come first served, icons whose cell is outside the grid or taken go to the nearest free one.
 */
export function resolvePlacements(m: GridMetrics, icons: ReadonlyArray<{ id: string; x: number; y: number }>): Map<string, Cell> {
  const out = new Map<string, Cell>();
  const taken = new Set<string>();
  const rest: typeof icons[number][] = [];
  for (const i of icons) {
    const c = { x: Math.round(i.x), y: Math.round(i.y) };
    if (isInsideGrid(m, c) && !taken.has(cellKey(c))) { out.set(i.id, c); taken.add(cellKey(c)); } else rest.push(i);
  }
  for (const i of rest) {
    const c = nearestFreeCell(m, { x: i.x, y: i.y }, taken);
    out.set(i.id, c);
    taken.add(cellKey(c));
  }
  return out;
}

/** Where a dragged group lands: the anchor icon goes to `target`, the others keep their offset; conflicts with resting icons shift to a free cell. */
export function planMove(m: GridMetrics, placements: ReadonlyMap<string, Cell>, moving: readonly string[], anchorId: string, target: Cell): Map<string, Cell> {
  const out = new Map<string, Cell>();
  const anchor = placements.get(anchorId);
  const group = moving.filter((id) => placements.has(id));
  if (!anchor || group.length === 0) return out;
  let dx = target.x - anchor.x;
  let dy = target.y - anchor.y;
  // keep the whole group inside the grid
  let minDx = -Infinity, maxDx = Infinity, minDy = -Infinity, maxDy = Infinity;
  for (const id of group) {
    const c = placements.get(id)!;
    minDx = Math.max(minDx, -c.x); maxDx = Math.min(maxDx, m.cols - 1 - c.x);
    minDy = Math.max(minDy, -c.y); maxDy = Math.min(maxDy, m.rows - 1 - c.y);
  }
  dx = minDx <= maxDx ? clamp(dx, minDx, maxDx) : 0;
  dy = minDy <= maxDy ? clamp(dy, minDy, maxDy) : 0;
  const movingSet = new Set(group);
  const occupied = new Set<string>();
  for (const [id, c] of placements) if (!movingSet.has(id)) occupied.add(cellKey(c));
  // The anchor first, so it gets its exact cell whenever that is free.
  const ordered = [anchorId, ...group.filter((id) => id !== anchorId)];
  for (const id of ordered) {
    const c = placements.get(id)!;
    const want = { x: c.x + dx, y: c.y + dy };
    const got = occupied.has(cellKey(want)) ? nearestFreeCell(m, want, occupied) : want;
    out.set(id, got);
    occupied.add(cellKey(got));
  }
  return out;
}

/** Reading order (column by column, top to bottom) for shift-range selection and Home/End. */
export function readingOrder(placements: ReadonlyMap<string, Cell>): string[] {
  return [...placements.entries()].sort(([, a], [, b]) => a.x - b.x || a.y - b.y).map(([id]) => id);
}

export type Direction = 'left' | 'right' | 'up' | 'down';

/** Arrow-key navigation: the nearest icon in that direction (the primary axis counts once, the cross axis three times). */
export function neighbour(placements: ReadonlyMap<string, Cell>, fromId: string, dir: Direction): string | null {
  const from = placements.get(fromId);
  if (!from) return null;
  let best: { id: string; score: number; c: Cell } | null = null;
  for (const [id, c] of placements) {
    if (id === fromId) continue;
    const dx = c.x - from.x, dy = c.y - from.y;
    const [main, cross] = dir === 'left' ? [-dx, Math.abs(dy)] : dir === 'right' ? [dx, Math.abs(dy)] : dir === 'up' ? [-dy, Math.abs(dx)] : [dy, Math.abs(dx)];
    if (main <= 0) continue;
    const score = main + cross * 3;
    if (!best || score < best.score || (score === best.score && (c.x < best.c.x || (c.x === best.c.x && c.y < best.c.y)))) best = { id, score, c };
  }
  return best ? best.id : null;
}

// ---------------------------------------------------------------- marquee

export function rectFromPoints(a: Point, b: Point): Box {
  return { x: Math.min(a.x, b.x), y: Math.min(a.y, b.y), width: Math.abs(a.x - b.x), height: Math.abs(a.y - b.y) };
}

export function boxesIntersect(a: Box, b: Box): boolean {
  return a.x < b.x + b.width && a.x + a.width > b.x && a.y < b.y + b.height && a.y + a.height > b.y;
}

/** The part of a cell that is "the icon" for marquee hit testing (icon + label, without the empty margins). */
export function iconHitBox(m: GridMetrics, c: Cell): Box {
  const b = cellBox(m, c);
  return { x: b.x + 4, y: b.y + 2, width: b.width - 8, height: b.height - 6 };
}

export function marqueeHits(placements: ReadonlyMap<string, Cell>, m: GridMetrics, marquee: Box): string[] {
  const out: string[] = [];
  for (const [id, c] of placements) if (boxesIntersect(marquee, iconHitBox(m, c))) out.push(id);
  return out;
}

// ---------------------------------------------------------------- selection

export interface Selection { ids: ReadonlySet<string>; anchor: string | null }
export const emptySelection = (): Selection => ({ ids: new Set(), anchor: null });
export const selectOnly = (id: string): Selection => ({ ids: new Set([id]), anchor: id });

/** Click semantics of a file manager: plain = replace, Ctrl = toggle, Shift = range from the anchor (Ctrl+Shift adds the range). */
export function applyClick(order: readonly string[], sel: Selection, id: string, mods: { ctrl: boolean; shift: boolean }): Selection {
  if (mods.shift && sel.anchor && order.includes(sel.anchor) && order.includes(id)) {
    const a = order.indexOf(sel.anchor), b = order.indexOf(id);
    const range = order.slice(Math.min(a, b), Math.max(a, b) + 1);
    return { ids: new Set(mods.ctrl ? [...sel.ids, ...range] : range), anchor: sel.anchor };
  }
  if (mods.ctrl) {
    const ids = new Set(sel.ids);
    if (ids.has(id)) ids.delete(id); else ids.add(id);
    return { ids, anchor: id };
  }
  return selectOnly(id);
}

/** Rubber-band selection: `base` is the selection when the band started (kept when additive). */
export function applyMarquee(base: Selection, hits: readonly string[], additive: boolean): Selection {
  const ids = new Set(additive ? [...base.ids, ...hits] : hits);
  return { ids, anchor: hits.length ? hits[0] : additive ? base.anchor : null };
}

// ---------------------------------------------------------------- widgets

export const WIDGET_UNIT = 86;
export const WIDGET_GAP = 12;
export const WIDGET_SNAP = 8;
export const WIDGET_CELLS: Record<WidgetSize, { cols: number; rows: number }> = {
  small: { cols: 2, rows: 2 },
  medium: { cols: 4, rows: 2 },
  large: { cols: 4, rows: 4 },
};

/** Pixel size of a widget: n cells of 86 DIP with 12 DIP gaps between them (small 184x184, medium 380x184, large 380x380). */
export function widgetBox(size: WidgetSize): Size {
  const { cols, rows } = WIDGET_CELLS[size] ?? WIDGET_CELLS.medium;
  return { width: cols * WIDGET_UNIT + (cols - 1) * WIDGET_GAP, height: rows * WIDGET_UNIT + (rows - 1) * WIDGET_GAP };
}

export const snapTo = (v: number, step: number) => (step > 0 ? Math.round(v / step) * step : v);

/** Keep a widget inside the usable area of the monitor (a widget bigger than the area is pinned to its top-left corner). */
export function clampWidget(p: Point, size: WidgetSize, viewport: Size, insets: Insets): Point {
  const w = widgetBox(size);
  return {
    x: clamp(p.x, insets.left, viewport.width - insets.right - w.width),
    y: clamp(p.y, insets.top, viewport.height - insets.bottom - w.height),
  };
}

/** Snap to the 8 DIP grid, then keep inside (the clamp wins over the snap at the edges). */
export function snapWidget(p: Point, size: WidgetSize, viewport: Size, insets: Insets, step = WIDGET_SNAP): Point {
  return clampWidget({ x: snapTo(p.x, step), y: snapTo(p.y, step) }, size, viewport, insets);
}
