import { describe, expect, it } from 'vitest';
import { decide, edgeTurn, initialDnd, nextDecisionIn, resolveDrop, slotAtPoint, EDGE_DWELL, MERGE_DWELL, REORDER_DWELL, type DndContext, type HitGrid } from './dnd';
import { computeGrid } from './layout';

const g1 = computeGrid(1920, 1080);
const grid: HitGrid = { cols: g1.cols, rows: g1.rows, cellW: g1.cellW, cellH: g1.cellH, originX: g1.originX, originY: g1.originY, iconSize: g1.iconSize, contentH: g1.contentH, perPage: g1.perPage };
const center = (col: number, row: number) => ({
  x: grid.originX + col * grid.cellW + grid.cellW / 2,
  y: grid.originY + row * grid.cellH + (grid.cellH - grid.contentH) / 2 + grid.iconSize / 2,
});
const rim = (col: number, row: number) => ({ x: grid.originX + col * grid.cellW + 4, y: grid.originY + row * grid.cellH + grid.cellH - 4 });

describe('slotAtPoint', () => {
  it('finds the cell and the zone', () => {
    const c = center(2, 1);
    expect(slotAtPoint(c.x, c.y, grid, 0, 30)).toEqual({ index: 1 * 7 + 2, zone: 'inner' });
    const r = rim(2, 1);
    expect(slotAtPoint(r.x, r.y, grid, 0, 30)).toEqual({ index: 1 * 7 + 2, zone: 'rim' });
  });
  it('adds the page offset', () => {
    const c = center(0, 0);
    expect(slotAtPoint(c.x, c.y, grid, 2, 100)?.index).toBe(2 * 35);
  });
  it('clamps pointers outside the area to the nearest cell', () => {
    expect(slotAtPoint(-50, 0, grid, 0, 30)?.index).toBe(0);
    expect(slotAtPoint(5000, 5000, grid, 0, 30)?.index).toBe(Math.min(29, 4 * 7 + 6));
  });
  it('maps empty trailing cells to the last item with the rim zone (drop at the end)', () => {
    const c = center(6, 4);
    expect(slotAtPoint(c.x, c.y, grid, 0, 10)).toEqual({ index: 9, zone: 'rim' });
  });
  it('returns null for an empty list', () => {
    expect(slotAtPoint(10, 10, grid, 0, 0)).toBeNull();
  });
  it('works for a single page grid (folder panel)', () => {
    const f: HitGrid = { cols: 3, rows: 2, cellW: 128, cellH: 126, originX: 100, originY: 50, iconSize: 72, contentH: 116, perPage: Infinity };
    expect(slotAtPoint(100 + 128 + 64, 50 + 126 + 40, f, 0, 5)).toMatchObject({ index: 4 });
    expect(slotAtPoint(100 + 128 * 2 + 64, 50 + 126 + 40, f, 7, 5)).toMatchObject({ index: 4 }); // page ignored, clamped to last
  });
});

describe('decide', () => {
  const order = ['a', 'b', 'c', 'd', 'e'];
  const ctx = (over: Partial<DndContext> = {}): DndContext => ({ order, dragId: 'a', mergeable: (id) => id !== 'folder', ...over });

  it('does nothing while the pointer is over the dragged item itself', () => {
    const s = decide(initialDnd(0), { index: 0, zone: 'inner' }, ctx(), 1000);
    expect(s).toEqual(initialDnd(0));
  });

  it('resting on the icon of another app merges after the merge dwell', () => {
    let s = decide(initialDnd(0), { index: 2, zone: 'inner' }, ctx(), 1000);
    expect(s.mergeTarget).toBeNull();
    expect(nextDecisionIn(s, 1000)).toBe(MERGE_DWELL);
    s = decide(s, { index: 2, zone: 'inner' }, ctx(), 1000 + MERGE_DWELL - 1);
    expect(s.mergeTarget).toBeNull();
    s = decide(s, { index: 2, zone: 'inner' }, ctx(), 1000 + MERGE_DWELL);
    expect(s.mergeTarget).toBe('c');
    expect(s.hoverIndex).toBe(0); // no reorder happened
  });

  it('resting in the rim reorders after the (shorter) reorder dwell', () => {
    let s = decide(initialDnd(0), { index: 3, zone: 'rim' }, ctx(), 500);
    expect(s.hoverIndex).toBe(0);
    s = decide(s, { index: 3, zone: 'rim' }, ctx(), 500 + REORDER_DWELL);
    expect(s.hoverIndex).toBe(3);
    expect(s.mergeTarget).toBeNull();
  });

  it('passing quickly through a cell decides nothing', () => {
    let s = decide(initialDnd(0), { index: 1, zone: 'rim' }, ctx(), 0);
    s = decide(s, { index: 2, zone: 'inner' }, ctx(), 40);
    s = decide(s, { index: 3, zone: 'rim' }, ctx(), 80);
    expect(s.hoverIndex).toBe(0);
    expect(s.mergeTarget).toBeNull();
  });

  it('leaving the merge target clears it', () => {
    let s = decide(initialDnd(0), { index: 2, zone: 'inner' }, ctx(), 0);
    s = decide(s, { index: 2, zone: 'inner' }, ctx(), MERGE_DWELL);
    expect(s.mergeTarget).toBe('c');
    s = decide(s, { index: 2, zone: 'rim' }, ctx(), MERGE_DWELL + 10);
    expect(s.mergeTarget).toBeNull();
  });

  it('an unmergeable target (a folder dragged onto an app) falls back to reordering', () => {
    const c = ctx({ dragId: 'folder', order: ['folder', 'b', 'c'], mergeable: () => false });
    let s = decide(initialDnd(0), { index: 2, zone: 'inner' }, c, 0);
    s = decide(s, { index: 2, zone: 'inner' }, c, REORDER_DWELL);
    expect(s.hoverIndex).toBe(2);
    expect(s.mergeTarget).toBeNull();
  });

  it('pointer outside any cell clears pending decisions', () => {
    let s = decide(initialDnd(0), { index: 2, zone: 'inner' }, ctx(), 0);
    s = decide(s, null, ctx(), 10);
    expect(s.candidate).toBeNull();
  });

  it('returns the same object when nothing changed (cheap for the pointer-move handler)', () => {
    const a = decide(initialDnd(0), { index: 2, zone: 'rim' }, ctx(), 0);
    const b = decide(a, { index: 2, zone: 'rim' }, ctx(), 10);
    expect(b).toBe(a);
  });
});

describe('edgeTurn', () => {
  it('turns the page after the dwell at an edge and only when a page exists', () => {
    let st = { edge: 0 as -1 | 0 | 1, since: 0 };
    let r = edgeTurn(st, 1915, 1920, 0, 3, 0);
    expect(r.turn).toBe(0);
    r = edgeTurn(r.state, 1915, 1920, 0, 3, EDGE_DWELL - 1);
    expect(r.turn).toBe(0);
    r = edgeTurn(r.state, 1915, 1920, 0, 3, EDGE_DWELL);
    expect(r.turn).toBe(1);
    // last page: no further turn
    st = { edge: 1, since: 0 };
    expect(edgeTurn(st, 1915, 1920, 2, 3, EDGE_DWELL * 3).turn).toBe(0);
    // left edge on the first page: nothing
    expect(edgeTurn({ edge: -1, since: 0 }, 3, 1920, 0, 3, EDGE_DWELL * 3).turn).toBe(0);
  });
  it('resets when the pointer leaves the edge', () => {
    const r = edgeTurn({ edge: 1, since: 0 }, 900, 1920, 0, 3, 100);
    expect(r.state.edge).toBe(0);
    expect(r.turn).toBe(0);
  });
});

describe('resolveDrop', () => {
  const order = ['a', 'b', 'c', 'd'];
  it('a quick drop onto another cell moves the item there', () => {
    const s = decide(initialDnd(0), { index: 2, zone: 'inner' }, { order, dragId: 'a', mergeable: () => true }, 0);
    expect(resolveDrop(s, { index: 2, zone: 'inner' }, { order, dragId: 'a' }).hoverIndex).toBe(2);
  });
  it('keeps an armed merge', () => {
    let s = decide(initialDnd(0), { index: 2, zone: 'inner' }, { order, dragId: 'a', mergeable: () => true }, 0);
    s = decide(s, { index: 2, zone: 'inner' }, { order, dragId: 'a', mergeable: () => true }, MERGE_DWELL);
    expect(resolveDrop(s, { index: 2, zone: 'inner' }, { order, dragId: 'a' })).toBe(s);
  });
  it('does nothing over the dragged item itself or outside any cell', () => {
    const s = initialDnd(1);
    expect(resolveDrop(s, { index: 1, zone: 'inner' }, { order, dragId: 'b' })).toBe(s);
    expect(resolveDrop(s, null, { order, dragId: 'b' })).toBe(s);
  });
});
