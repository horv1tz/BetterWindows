import { describe, expect, it } from 'vitest';
import { firstInReadingOrder, spatialNeighbor, type NavSlot } from './navigate';

/** A 3 x 2 grid of 100x80 cards with 10px gaps: a0 a1 a2 / b0 b1 b2. */
const grid: NavSlot[] = [
  ...[0, 1, 2].map((c) => ({ id: `a${c}`, x: c * 110, y: 0, w: 100, h: 80 })),
  ...[0, 1, 2].map((c) => ({ id: `b${c}`, x: c * 110, y: 90, w: 100, h: 80 })),
];

describe('spatialNeighbor', () => {
  it('moves right and left within a row', () => {
    expect(spatialNeighbor(grid, 'a0', 'right')).toBe('a1');
    expect(spatialNeighbor(grid, 'a1', 'left')).toBe('a0');
  });

  it('moves down and up to the card directly below or above', () => {
    expect(spatialNeighbor(grid, 'a1', 'down')).toBe('b1');
    expect(spatialNeighbor(grid, 'b2', 'up')).toBe('a2');
  });

  it('returns null at the edges (focus stays put)', () => {
    expect(spatialNeighbor(grid, 'a2', 'right')).toBeNull();
    expect(spatialNeighbor(grid, 'a0', 'left')).toBeNull();
    expect(spatialNeighbor(grid, 'a0', 'up')).toBeNull();
    expect(spatialNeighbor(grid, 'b1', 'down')).toBeNull();
  });

  it('prefers the card in the same row over a nearer diagonal one when moving across', () => {
    // from a0 going down: b0 is straight below; a shifted card far right must not win
    const slots: NavSlot[] = [
      { id: 'from', x: 0, y: 0, w: 100, h: 100 },
      { id: 'diag', x: 60, y: 150, w: 100, h: 100 },
      { id: 'below', x: 0, y: 150, w: 100, h: 100 },
    ];
    expect(spatialNeighbor(slots, 'from', 'down')).toBe('below');
  });

  it('chooses the nearest candidate in a ragged layout', () => {
    const slots: NavSlot[] = [
      { id: 'a', x: 0, y: 0, w: 200, h: 100 },
      { id: 'b', x: 220, y: 0, w: 100, h: 100 },
      { id: 'c', x: 220, y: 120, w: 100, h: 100 },
    ];
    expect(spatialNeighbor(slots, 'a', 'right')).toBe('b');
    expect(spatialNeighbor(slots, 'b', 'down')).toBe('c');
  });

  it('is deterministic and ignores the source card itself', () => {
    expect(spatialNeighbor(grid, 'b1', 'left')).toBe('b0');
    expect(spatialNeighbor(grid, 'b1', 'left')).toBe('b0');
  });

  it('falls back to the first slot when the source is unknown, and is null for no slots', () => {
    expect(spatialNeighbor(grid, 'missing', 'right')).toBe('a0');
    expect(spatialNeighbor([], 'x', 'right')).toBeNull();
  });
});

describe('firstInReadingOrder', () => {
  it('returns the top-left card', () => {
    expect(firstInReadingOrder(grid)).toBe('a0');
  });

  it('breaks a row tie by x and tolerates sub-pixel differences', () => {
    const slots: NavSlot[] = [
      { id: 'right', x: 300, y: 10.2, w: 10, h: 10 },
      { id: 'left', x: 20, y: 10, w: 10, h: 10 },
    ];
    expect(firstInReadingOrder(slots)).toBe('left');
  });

  it('is null for no cards', () => {
    expect(firstInReadingOrder([])).toBeNull();
  });
});
