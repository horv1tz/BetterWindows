import { describe, expect, it } from 'vitest';
import {
  applyClick, applyMarquee, boxesIntersect, cellBox, cellFromPoint, cellKey, clampWidget, emptySelection, gridMetrics, iconHitBox, marqueeHits,
  nearestFreeCell, neighbour, planMove, readingOrder, rectFromPoints, resolvePlacements, selectOnly, snapTo, snapWidget, surfaceInsets, widgetBox,
  type Cell, type GridMetrics,
} from './layout';
import { defaultSettings } from '$lib/protocol/generated';

const noInsets = { top: 0, right: 0, bottom: 0, left: 0 };
// 64px icons -> cell 100x110, origin (8,8). 1000x500 -> 9 cols x 4 rows
const m: GridMetrics = gridMetrics(64, { width: 1000, height: 500 }, noInsets);
const P = (entries: Array<[string, number, number]>) => new Map<string, Cell>(entries.map(([id, x, y]) => [id, { x, y }]));

describe('gridMetrics', () => {
  it('derives the cell size from the icon size and counts whole cells', () => {
    expect(m).toMatchObject({ iconSize: 64, cellW: 100, cellH: 110, originX: 8, originY: 8, cols: 9, rows: 4 });
  });
  it('leaves room for the menu bar and a reserved dock', () => {
    const s = defaultSettings();
    s.dock.position = 'bottom';
    const ins = surfaceInsets(s);
    expect(ins.top).toBe(28);
    expect(ins.bottom).toBe(s.dock.iconSize + 28);
    const g = gridMetrics(64, { width: 1440, height: 900 }, ins);
    expect(g.originY).toBe(36);
    expect(g.rows).toBe(Math.floor((900 - 36 - ins.bottom - 8) / 110));
  });
  it('reserves the dock on the left/right edge and nothing when it auto-hides', () => {
    const s = defaultSettings();
    s.dock.position = 'left';
    expect(surfaceInsets(s).left).toBeGreaterThan(0);
    expect(surfaceInsets(s).bottom).toBe(0);
    s.dock.autoHide = true;
    expect(surfaceInsets(s).left).toBe(0);
    s.menuBar.enabled = false;
    expect(surfaceInsets(s).top).toBe(0);
  });
  it('clamps absurd icon sizes and never returns an empty grid', () => {
    expect(gridMetrics(0, { width: 1000, height: 500 }, noInsets).iconSize).toBe(64);
    expect(gridMetrics(5, { width: 1000, height: 500 }, noInsets).iconSize).toBe(32);
    expect(gridMetrics(999, { width: 1000, height: 500 }, noInsets).iconSize).toBe(128);
    const tiny = gridMetrics(64, { width: 10, height: 10 }, noInsets);
    expect(tiny.cols).toBe(1);
    expect(tiny.rows).toBe(1);
  });
});

describe('cells', () => {
  it('maps points to cells, clamped to the grid', () => {
    expect(cellFromPoint(m, { x: 8, y: 8 })).toEqual({ x: 0, y: 0 });
    expect(cellFromPoint(m, { x: 107, y: 117 })).toEqual({ x: 0, y: 0 });
    expect(cellFromPoint(m, { x: 108, y: 118 })).toEqual({ x: 1, y: 1 });
    expect(cellFromPoint(m, { x: -50, y: -50 })).toEqual({ x: 0, y: 0 });
    expect(cellFromPoint(m, { x: 99999, y: 99999 })).toEqual({ x: 8, y: 3 });
  });
  it('cellBox is the inverse of cellFromPoint for cell centres', () => {
    for (const c of [{ x: 0, y: 0 }, { x: 3, y: 2 }, { x: 8, y: 3 }]) {
      const b = cellBox(m, c);
      expect(cellFromPoint(m, { x: b.x + b.width / 2, y: b.y + b.height / 2 })).toEqual(c);
    }
  });
});

describe('nearestFreeCell', () => {
  it('returns the wanted cell when free (clamped into the grid)', () => {
    expect(nearestFreeCell(m, { x: 2, y: 1 }, new Set())).toEqual({ x: 2, y: 1 });
    expect(nearestFreeCell(m, { x: 50, y: -3 }, new Set())).toEqual({ x: 8, y: 0 });
  });
  it('prefers the closest free cell, ties broken by row then column', () => {
    const occ = new Set([cellKey({ x: 2, y: 1 })]);
    expect(nearestFreeCell(m, { x: 2, y: 1 }, occ)).toEqual({ x: 2, y: 0 });
    occ.add(cellKey({ x: 2, y: 0 }));
    expect(nearestFreeCell(m, { x: 2, y: 1 }, occ)).toEqual({ x: 1, y: 1 });
  });
  it('spills into extra columns when the visible grid is full', () => {
    const occ = new Set<string>();
    for (let x = 0; x < m.cols; x++) for (let y = 0; y < m.rows; y++) occ.add(cellKey({ x, y }));
    expect(nearestFreeCell(m, { x: 0, y: 0 }, occ)).toEqual({ x: m.cols, y: 0 });
  });
});

describe('resolvePlacements', () => {
  it('keeps unique in-grid cells untouched', () => {
    const r = resolvePlacements(m, [{ id: 'a', x: 0, y: 0 }, { id: 'b', x: 1, y: 2 }]);
    expect(r.get('a')).toEqual({ x: 0, y: 0 });
    expect(r.get('b')).toEqual({ x: 1, y: 2 });
  });
  it('moves duplicates and off-screen icons to free cells without overlapping', () => {
    const icons = [{ id: 'a', x: 0, y: 0 }, { id: 'b', x: 0, y: 0 }, { id: 'c', x: 40, y: 1 }, { id: 'd', x: -2, y: -2 }];
    const r = resolvePlacements(m, icons);
    expect(r.get('a')).toEqual({ x: 0, y: 0 });
    const keys = new Set([...r.values()].map(cellKey));
    expect(keys.size).toBe(4);
    for (const c of r.values()) expect(c.x >= 0 && c.y >= 0 && c.x < m.cols && c.y < m.rows).toBe(true);
  });
  it('is deterministic', () => {
    const icons = Array.from({ length: 12 }, (_, i) => ({ id: `i${i}`, x: 0, y: 0 }));
    expect([...resolvePlacements(m, icons)]).toEqual([...resolvePlacements(m, icons)]);
    expect(new Set([...resolvePlacements(m, icons).values()].map(cellKey)).size).toBe(12);
  });
});

describe('planMove', () => {
  const placements = P([['a', 0, 0], ['b', 0, 1], ['c', 3, 3]]);
  it('moves a single icon to the target cell', () => {
    const r = planMove(m, placements, ['a'], 'a', { x: 4, y: 2 });
    expect(r.get('a')).toEqual({ x: 4, y: 2 });
  });
  it('keeps the offsets of a group and clamps the group inside the grid', () => {
    const r = planMove(m, placements, ['a', 'b'], 'a', { x: 8, y: 3 });
    expect(r.get('a')).toEqual({ x: 8, y: 2 });
    expect(r.get('b')).toEqual({ x: 8, y: 3 });
    const left = planMove(m, placements, ['a', 'b'], 'b', { x: -4, y: 1 });
    expect(left.get('a')).toEqual({ x: 0, y: 0 });
    expect(left.get('b')).toEqual({ x: 0, y: 1 });
  });
  it('dropping on a resting icon takes the nearest free cell instead of overlapping', () => {
    const r = planMove(m, placements, ['a'], 'a', { x: 3, y: 3 });
    expect(r.get('a')).not.toEqual({ x: 3, y: 3 });
    expect(cellKey(r.get('a')!)).not.toBe(cellKey(placements.get('c')!));
  });
  it('a group may land on cells it just vacated', () => {
    const r = planMove(m, placements, ['a', 'b'], 'a', { x: 0, y: 1 });
    expect(r.get('a')).toEqual({ x: 0, y: 1 });
    expect(r.get('b')).toEqual({ x: 0, y: 2 });
  });
  it('returns nothing for unknown ids', () => {
    expect(planMove(m, placements, ['zzz'], 'zzz', { x: 1, y: 1 }).size).toBe(0);
  });
});

describe('navigation order', () => {
  const placements = P([['a', 0, 0], ['b', 0, 1], ['c', 1, 0], ['d', 2, 2]]);
  it('reading order is column by column', () => {
    expect(readingOrder(placements)).toEqual(['a', 'b', 'c', 'd']);
  });
  it('arrow keys pick the nearest icon in that direction', () => {
    expect(neighbour(placements, 'a', 'down')).toBe('b');
    expect(neighbour(placements, 'a', 'right')).toBe('c');
    expect(neighbour(placements, 'b', 'up')).toBe('a');
    expect(neighbour(placements, 'c', 'left')).toBe('a');
    expect(neighbour(placements, 'a', 'up')).toBeNull();
    expect(neighbour(placements, 'd', 'right')).toBeNull();
    expect(neighbour(placements, 'b', 'right')).toBe('c'); // c is one column right, one row up
  });
  it('unknown source yields null', () => { expect(neighbour(placements, 'x', 'down')).toBeNull(); });
});

describe('marquee', () => {
  it('normalises a rectangle drawn in any direction', () => {
    expect(rectFromPoints({ x: 50, y: 40 }, { x: 10, y: 90 })).toEqual({ x: 10, y: 40, width: 40, height: 50 });
  });
  it('detects intersections, touching edges do not count', () => {
    expect(boxesIntersect({ x: 0, y: 0, width: 10, height: 10 }, { x: 9, y: 9, width: 5, height: 5 })).toBe(true);
    expect(boxesIntersect({ x: 0, y: 0, width: 10, height: 10 }, { x: 10, y: 0, width: 5, height: 5 })).toBe(false);
  });
  it('selects exactly the icons the band touches', () => {
    const placements = P([['a', 0, 0], ['b', 1, 0], ['c', 0, 1], ['d', 2, 2]]);
    const band = rectFromPoints({ x: 60, y: 20 }, { x: 150, y: 60 }); // spans the right edge of a and the left of b
    expect(marqueeHits(placements, m, band).sort()).toEqual(['a', 'b']);
    const miss = rectFromPoints({ x: 1, y: 1 }, { x: 5, y: 5 }); // only the cell margin
    expect(marqueeHits(placements, m, miss)).toEqual([]);
    const all = rectFromPoints({ x: 0, y: 0 }, { x: 999, y: 499 });
    expect(marqueeHits(placements, m, all).sort()).toEqual(['a', 'b', 'c', 'd']);
  });
  it('icon hit box sits inside its cell', () => {
    const c = cellBox(m, { x: 1, y: 1 });
    const h = iconHitBox(m, { x: 1, y: 1 });
    expect(h.x).toBeGreaterThan(c.x);
    expect(h.x + h.width).toBeLessThan(c.x + c.width);
  });
});

describe('selection reducer', () => {
  const order = ['a', 'b', 'c', 'd', 'e'];
  const none = { ctrl: false, shift: false };
  it('plain click replaces the selection', () => {
    const s = applyClick(order, selectOnly('a'), 'c', none);
    expect([...s.ids]).toEqual(['c']);
    expect(s.anchor).toBe('c');
  });
  it('ctrl toggles', () => {
    let s = applyClick(order, selectOnly('a'), 'c', { ctrl: true, shift: false });
    expect([...s.ids].sort()).toEqual(['a', 'c']);
    s = applyClick(order, s, 'a', { ctrl: true, shift: false });
    expect([...s.ids]).toEqual(['c']);
  });
  it('shift selects the range from the anchor, in both directions, keeping the anchor', () => {
    const s = applyClick(order, selectOnly('b'), 'd', { ctrl: false, shift: true });
    expect([...s.ids]).toEqual(['b', 'c', 'd']);
    expect(s.anchor).toBe('b');
    const back = applyClick(order, s, 'a', { ctrl: false, shift: true });
    expect([...back.ids]).toEqual(['a', 'b']);
  });
  it('ctrl+shift adds the range', () => {
    const s = applyClick(order, { ids: new Set(['a', 'e']), anchor: 'b' }, 'c', { ctrl: true, shift: true });
    expect([...s.ids].sort()).toEqual(['a', 'b', 'c', 'e']);
  });
  it('shift without an anchor behaves like a plain click', () => {
    const s = applyClick(order, emptySelection(), 'c', { ctrl: false, shift: true });
    expect([...s.ids]).toEqual(['c']);
  });
  it('marquee replaces or extends the base selection', () => {
    const base = selectOnly('a');
    expect([...applyMarquee(base, ['b', 'c'], false).ids]).toEqual(['b', 'c']);
    expect([...applyMarquee(base, ['b'], true).ids].sort()).toEqual(['a', 'b']);
    expect(applyMarquee(base, [], false).ids.size).toBe(0);
    expect(applyMarquee(base, [], true).ids.size).toBe(1);
  });
});

describe('widget grid', () => {
  it('maps sizes to 2x2 / 4x2 / 4x4 cells of 86 DIP with 12 DIP gaps', () => {
    expect(widgetBox('small')).toEqual({ width: 184, height: 184 });
    expect(widgetBox('medium')).toEqual({ width: 380, height: 184 });
    expect(widgetBox('large')).toEqual({ width: 380, height: 380 });
  });
  it('snaps to 8 DIP', () => {
    expect(snapTo(13, 8)).toBe(16);
    expect(snapTo(11, 8)).toBe(8);
    expect(snapTo(-5, 8)).toBe(-8);
    expect(snapTo(7.3, 0)).toBe(7.3);
  });
  const vp = { width: 1000, height: 700 };
  const ins = { top: 28, right: 0, bottom: 84, left: 0 };
  it('keeps widgets inside the usable area', () => {
    expect(clampWidget({ x: -50, y: -50 }, 'small', vp, ins)).toEqual({ x: 0, y: 28 });
    expect(clampWidget({ x: 5000, y: 5000 }, 'medium', vp, ins)).toEqual({ x: 620, y: 700 - 84 - 184 });
  });
  it('snaps first, then clamps (edges win over the snap)', () => {
    expect(snapWidget({ x: 101, y: 203 }, 'small', vp, ins)).toEqual({ x: 104, y: 200 });
    expect(snapWidget({ x: 810, y: 10 }, 'small', vp, ins)).toEqual({ x: 808, y: 28 });
    expect(snapWidget({ x: 990, y: 600 }, 'small', vp, ins)).toEqual({ x: 816, y: 432 });
  });
  it('pins an oversized widget to the top-left of the area', () => {
    expect(clampWidget({ x: 300, y: 300 }, 'large', { width: 200, height: 200 }, noInsets)).toEqual({ x: 0, y: 0 });
  });
});
