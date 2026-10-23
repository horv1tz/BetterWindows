// Drag & drop decision logic for the Launchpad grid and the folder panel (pure, no DOM).
//
// The pointer moves over cells of a grid. What the drop will do is decided by dwelling:
//  - pointer rests on the ICON of another app (inner zone) for MERGE_DWELL ms  -> merge (create a folder / add to a folder)
//  - pointer rests on another cell (rim, or icon of a non-mergeable item) for REORDER_DWELL ms -> the dragged item takes that slot and the others reflow
//  - pointer passing through quickly decides nothing, so reaching the centre of a tile never reorders it away first.

export const REORDER_DWELL = 220;
export const MERGE_DWELL = 450;
export const EDGE_DWELL = 650;
export const EDGE_ZONE = 56;

/** A rectangular grid of equal cells; `perPage` cells per page, pages `pageWidth` apart (single page: perPage = Infinity). */
export interface HitGrid {
  cols: number;
  cellW: number;
  cellH: number;
  originX: number;
  originY: number;
  iconSize: number;
  contentH: number;
  /** Cells per page (Infinity for the folder panel). */
  perPage: number;
  /** Number of cell rows on a page (folder panel: needed rows). */
  rows: number;
}

export interface Slot {
  /** Index in the (preview) order. */
  index: number;
  zone: 'inner' | 'rim';
}

/**
 * Cell under a pointer. `px/py` are in the grid's own space (page 0 origin = originX/originY already applied by the caller when
 * the grid is scrolled/translated: pass viewport coordinates minus the grid container offset). Coordinates outside the grid
 * clamp to the nearest cell; cells past the last item resolve to the last item (drop at the end).
 */
export function slotAtPoint(px: number, py: number, g: HitGrid, page: number, count: number): Slot | null {
  if (count <= 0) return null;
  const col = clamp(Math.floor((px - g.originX) / g.cellW), 0, g.cols - 1);
  const row = clamp(Math.floor((py - g.originY) / g.cellH), 0, g.rows - 1);
  const perPage = Number.isFinite(g.perPage) ? g.perPage : g.cols * g.rows;
  const raw = (Number.isFinite(g.perPage) ? page * perPage : 0) + row * g.cols + col;
  const index = Math.min(raw, count - 1);
  // inner zone = the icon rectangle (inflated a little) of that cell
  const cellLeft = g.originX + col * g.cellW;
  const cellTop = g.originY + row * g.cellH;
  const iconLeft = cellLeft + (g.cellW - g.iconSize) / 2;
  const iconTop = cellTop + (g.cellH - g.contentH) / 2;
  const pad = 20; // generous: reaching the icon of another app must not trip a reorder on the way
  const inside = px >= iconLeft - pad && px <= iconLeft + g.iconSize + pad && py >= iconTop - pad && py <= iconTop + g.iconSize + pad;
  return { index, zone: raw <= count - 1 && inside ? 'inner' : 'rim' };
}

const clamp = (v: number, lo: number, hi: number) => Math.min(Math.max(v, lo), hi);

export interface DndState {
  /** Slot the dragged item currently occupies in the preview order. */
  hoverIndex: number;
  /** Id of the item the drop would merge into (folder creation / add to folder). */
  mergeTarget: string | null;
  /** What the pointer has been resting on and since when (for dwell detection). */
  candidate: { key: string; since: number } | null;
}

export const initialDnd = (hoverIndex: number): DndState => ({ hoverIndex, mergeTarget: null, candidate: null });

export interface DndContext {
  /** Preview order (includes the dragged id at hoverIndex). */
  order: readonly string[];
  dragId: string;
  /** May the dragged item merge into `targetId`? */
  mergeable: (targetId: string) => boolean;
}

/** Feed the pointer position (null = outside any cell) and the clock; returns the next state (same object when nothing changed). */
export function decide(prev: DndState, slot: Slot | null, ctx: DndContext, now: number): DndState {
  if (!slot) return prev.candidate || prev.mergeTarget ? { ...prev, candidate: null, mergeTarget: null } : prev;
  const cellId = ctx.order[slot.index];
  if (cellId === undefined || cellId === ctx.dragId) {
    return prev.candidate || prev.mergeTarget ? { ...prev, candidate: null, mergeTarget: null } : prev;
  }
  const canMerge = slot.zone === 'inner' && ctx.mergeable(cellId);
  const key = `${cellId}|${canMerge ? 'merge' : 'move'}`;
  const candidate = prev.candidate?.key === key ? prev.candidate : { key, since: now };
  const waited = now - candidate.since;
  if (canMerge) {
    const mergeTarget = waited >= MERGE_DWELL ? cellId : null;
    if (mergeTarget === prev.mergeTarget && candidate === prev.candidate) return prev;
    return { ...prev, candidate, mergeTarget };
  }
  if (waited >= REORDER_DWELL) return { hoverIndex: slot.index, mergeTarget: null, candidate: null };
  if (candidate === prev.candidate && prev.mergeTarget === null) return prev;
  return { ...prev, candidate, mergeTarget: null };
}

/**
 * The pointer was released: a quick drop onto another cell (before any dwell elapsed) still means "put it there",
 * unless a merge target was already armed.
 */
export function resolveDrop(s: DndState, slot: Slot | null, ctx: Pick<DndContext, 'order' | 'dragId'>): DndState {
  if (s.mergeTarget || !slot) return s;
  const cellId = ctx.order[slot.index];
  if (cellId === undefined || cellId === ctx.dragId) return s;
  return { hoverIndex: slot.index, mergeTarget: null, candidate: null };
}

/** Milliseconds until `decide` could change its answer while the pointer stays still (null = nothing pending). */
export function nextDecisionIn(s: DndState, now: number): number | null {
  if (!s.candidate) return null;
  const need = s.candidate.key.endsWith('|merge') ? MERGE_DWELL : REORDER_DWELL;
  if (s.candidate.key.endsWith('|merge') && s.mergeTarget) return null;
  return Math.max(0, need - (now - s.candidate.since));
}

/** Dragging near the left/right viewport edge turns the page after a dwell. Returns -1 / 0 / +1 and the edge-dwell bookkeeping. */
export interface EdgeState { edge: -1 | 0 | 1; since: number }
export function edgeTurn(prev: EdgeState, x: number, vw: number, page: number, pages: number, now: number): { state: EdgeState; turn: -1 | 0 | 1 } {
  const edge: -1 | 0 | 1 = x <= EDGE_ZONE ? -1 : x >= vw - EDGE_ZONE ? 1 : 0;
  if (edge === 0) return { state: prev.edge === 0 ? prev : { edge: 0, since: now }, turn: 0 };
  const available = edge < 0 ? page > 0 : page < pages - 1;
  if (!available) return { state: { edge, since: now }, turn: 0 };
  if (prev.edge !== edge) return { state: { edge, since: now }, turn: 0 };
  if (now - prev.since >= EDGE_DWELL) return { state: { edge, since: now }, turn: edge };
  return { state: prev, turn: 0 };
}
