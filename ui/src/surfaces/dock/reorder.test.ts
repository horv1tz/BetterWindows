import { describe, expect, it } from 'vitest';
import { exceedsDragThreshold, insertionIndex, planDrag, reorderedPinned, sameOrder, unpinDistance, type DragGeometry } from './reorder';

const SLOT = 62;
// entries: 0..3 pinned apps, 4 separator, 5..6 running apps, 7 separator, 8 folder
const centers = [28, 90, 152, 214, 247, 280, 342, 375, 408];
const reorderGeo = (dragged: number): DragGeometry => ({ kind: 'reorder', dragged, group: [0, 1, 2, 3], centers, slot: SLOT });
const pinGeo: DragGeometry = { kind: 'pin', dragged: 5, group: [0, 1, 2, 3], centers, slot: SLOT };
const inside = (main: number) => ({ main, outside: -20 });

describe('insertionIndex', () => {
  it('counts the slots strictly before the pointer', () => {
    expect(insertionIndex([10, 20, 30], 5)).toBe(0);
    expect(insertionIndex([10, 20, 30], 25)).toBe(2);
    expect(insertionIndex([10, 20, 30], 99)).toBe(3);
    expect(insertionIndex([], 5)).toBe(0);
  });
});

describe('planDrag reorder', () => {
  it('dragging an item over its own slot changes nothing', () => {
    const p = planDrag(reorderGeo(1), inside(90), 60);
    expect(p.verdict).toBe('reorder');
    expect(p.insertIndex).toBe(1);
    expect(p.shifts.every((s) => s === 0)).toBe(true);
  });
  it('moving item 0 to the right shifts the passed neighbours one slot left and records the landing slot', () => {
    const p = planDrag(reorderGeo(0), inside(160), 60);
    expect(p.insertIndex).toBe(2);
    expect(p.shifts[1]).toBe(centers[0] - centers[1]);
    expect(p.shifts[2]).toBe(centers[1] - centers[2]);
    expect(p.shifts[3]).toBe(0);
    expect(p.shifts[0]).toBe(centers[2] - centers[0]);
  });
  it('moving the last item to the front shifts everything else one slot right', () => {
    const p = planDrag(reorderGeo(3), inside(-40), 60);
    expect(p.insertIndex).toBe(0);
    expect(p.shifts.slice(0, 3)).toEqual([centers[1] - centers[0], centers[2] - centers[1], centers[3] - centers[2]]);
    expect(p.shifts[3]).toBe(centers[0] - centers[3]);
  });
  it('clamps beyond the end of the group', () => {
    const p = planDrag(reorderGeo(0), inside(9999), 60);
    expect(p.insertIndex).toBe(3);
  });
  it('does not move separators, running apps or folders while reordering pinned apps', () => {
    const p = planDrag(reorderGeo(0), inside(200), 60);
    for (const e of [4, 5, 6, 7, 8]) expect(p.shifts[e]).toBe(0);
    expect(p.lengthDelta).toBe(0);
  });
  it('far outside the dock the verdict is unpin and the gap collapses', () => {
    const p = planDrag(reorderGeo(1), { main: 100, outside: 80 }, 60);
    expect(p.verdict).toBe('unpin');
    expect(p.lengthDelta).toBe(-SLOT);
    expect(p.shifts).toEqual([0, 0, -SLOT, -SLOT, -SLOT, -SLOT, -SLOT, -SLOT, -SLOT]);
  });
  it('the unpin threshold is inclusive', () => {
    expect(planDrag(reorderGeo(1), { main: 100, outside: 60 }, 60).verdict).toBe('unpin');
    expect(planDrag(reorderGeo(1), { main: 100, outside: 59.9 }, 60).verdict).toBe('reorder');
  });
  it('ignores a dragged slot that is not in its group', () => {
    expect(planDrag({ ...reorderGeo(0), group: [1, 2] }, inside(0), 60).verdict).toBe('cancel');
  });
});

describe('planDrag pin', () => {
  it('opens a gap in the pinned area, shifting only what lies between the gap and the dragged slot', () => {
    const p = planDrag(pinGeo, inside(150), 60); // between slot 1 and 2 -> index 2
    expect(p.verdict).toBe('pin');
    expect(p.insertIndex).toBe(2);
    expect(p.shifts).toEqual([0, 0, SLOT, SLOT, SLOT, 0, 0, 0, 0].map((v, i) => (i === 5 ? centers[2] - centers[5] : v)));
    expect(p.lengthDelta).toBe(0);
  });
  it('appending after the last pinned app lands right behind it', () => {
    const p = planDrag(pinGeo, inside(240), 60);
    expect(p.verdict).toBe('pin');
    expect(p.insertIndex).toBe(4);
    expect(p.shifts[4]).toBe(SLOT); // the separator moves
    expect(p.shifts[5]).toBe(centers[3] + SLOT - centers[5]);
  });
  it('inserting at the very front works', () => {
    const p = planDrag(pinGeo, inside(-30), 60);
    expect(p.insertIndex).toBe(0);
    expect(p.shifts.slice(0, 4)).toEqual([SLOT, SLOT, SLOT, SLOT]);
  });
  it('cancels when the pointer is among the running apps or too far from the dock', () => {
    expect(planDrag(pinGeo, inside(345), 60).verdict).toBe('cancel');
    expect(planDrag(pinGeo, { main: 100, outside: 70 }, 60).verdict).toBe('cancel');
    expect(planDrag(pinGeo, inside(345), 60).shifts.every((s) => s === 0)).toBe(true);
  });
  it('works with an empty pinned area', () => {
    const geo: DragGeometry = { kind: 'pin', dragged: 1, group: [], centers: [28, 90, 152], slot: SLOT };
    const p = planDrag(geo, inside(10), 60);
    expect(p.verdict).toBe('pin');
    expect(p.insertIndex).toBe(0);
    expect(p.shifts[0]).toBe(SLOT);
    expect(p.shifts[1]).toBe(28 - 90);
    expect(planDrag(geo, inside(200), 60).verdict).toBe('cancel');
  });
});

describe('reorderedPinned', () => {
  const all = ['a', 'b', 'c', 'd', 'f1', 'f2'];
  it('moves an id inside its group and keeps the other group untouched', () => {
    expect(reorderedPinned(all, ['a', 'b', 'c', 'd'], 'a', 2)).toEqual(['b', 'c', 'a', 'd', 'f1', 'f2']);
    expect(reorderedPinned(all, ['a', 'b', 'c', 'd'], 'd', 0)).toEqual(['d', 'a', 'b', 'c', 'f1', 'f2']);
    expect(reorderedPinned(all, ['f1', 'f2'], 'f1', 1)).toEqual(['a', 'b', 'c', 'd', 'f2', 'f1']);
  });
  it('clamps the index and keeps the multiset of ids', () => {
    const out = reorderedPinned(all, ['a', 'b', 'c', 'd'], 'b', 99);
    expect(out).toEqual(['a', 'c', 'd', 'b', 'f1', 'f2']);
    expect([...out].sort()).toEqual([...all].sort());
    expect(reorderedPinned(all, ['a', 'b', 'c', 'd'], 'b', -5)[0]).toBe('b');
  });
  it('sameOrder detects no-ops', () => {
    expect(sameOrder(all, [...all])).toBe(true);
    expect(sameOrder(all, reorderedPinned(all, ['a', 'b', 'c', 'd'], 'a', 0))).toBe(true);
    expect(sameOrder(all, reorderedPinned(all, ['a', 'b', 'c', 'd'], 'a', 1))).toBe(false);
  });
});

describe('thresholds', () => {
  it('drag starts only after a few pixels of movement', () => {
    expect(exceedsDragThreshold(2, 2)).toBe(false);
    expect(exceedsDragThreshold(0, 7)).toBe(true);
  });
  it('unpin distance scales with the icon size but has a floor', () => {
    expect(unpinDistance(32)).toBe(56);
    expect(unpinDistance(128)).toBeGreaterThan(unpinDistance(56));
  });
});
