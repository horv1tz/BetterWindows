import { describe, expect, it } from 'vitest';
import { layoutWindows, type LayoutItem, type Slot } from './layout';

/** Deterministic PRNG (mulberry32) so property tests are reproducible. */
function rng(seed: number) {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

function items(n: number, w = 1280, h = 800): LayoutItem[] {
  return Array.from({ length: n }, (_, i) => ({ id: `w${i}`, w, h }));
}

function randomItems(n: number, seed: number): LayoutItem[] {
  const r = rng(seed);
  return Array.from({ length: n }, (_, i) => ({
    id: `r${i}`,
    w: 120 + Math.floor(r() * 1400),
    h: 90 + Math.floor(r() * 900),
  }));
}

const EPS = 1e-6;

function expectInside(slots: Slot[], area: { w: number; h: number }) {
  for (const s of slots) {
    expect(s.x).toBeGreaterThanOrEqual(-EPS);
    expect(s.y).toBeGreaterThanOrEqual(-EPS);
    expect(s.x + s.w).toBeLessThanOrEqual(area.w + 1e-4);
    expect(s.y + s.h).toBeLessThanOrEqual(area.h + 1e-4);
  }
}

function expectNoOverlap(slots: Slot[]) {
  for (let i = 0; i < slots.length; i++) {
    for (let j = i + 1; j < slots.length; j++) {
      const a = slots[i]!;
      const b = slots[j]!;
      const overlapX = Math.min(a.x + a.w, b.x + b.w) - Math.max(a.x, b.x);
      const overlapY = Math.min(a.y + a.h, b.y + b.h) - Math.max(a.y, b.y);
      expect(overlapX > EPS && overlapY > EPS, `${a.id} overlaps ${b.id}`).toBe(false);
    }
  }
}

function expectAspect(slots: Slot[], input: LayoutItem[]) {
  for (const s of slots) {
    const src = input.find((i) => i.id === s.id)!;
    expect(s.w / s.h).toBeCloseTo(src.w / src.h, 6);
  }
}

function expectCapped(slots: Slot[], input: LayoutItem[]) {
  for (const s of slots) {
    const src = input.find((i) => i.id === s.id)!;
    expect(s.w).toBeLessThanOrEqual(src.w + 1e-6);
    expect(s.h).toBeLessThanOrEqual(src.h + 1e-6);
  }
}

describe('layoutWindows: basics', () => {
  it('returns nothing for no windows', () => {
    expect(layoutWindows([], { w: 1000, h: 600 }, 16)).toEqual([]);
  });

  it('keeps a single window at its real size (never upscaled) and centres it', () => {
    const [s] = layoutWindows([{ id: 'a', w: 800, h: 500 }], { w: 1920, h: 1000 }, 16);
    expect(s).toEqual({ id: 'a', x: (1920 - 800) / 2, y: (1000 - 500) / 2, w: 800, h: 500 });
  });

  it('shrinks a single window that does not fit and keeps its aspect ratio', () => {
    const [s] = layoutWindows([{ id: 'a', w: 1600, h: 900 }], { w: 300, h: 200 }, 16);
    expect(s!.w).toBeCloseTo(300, 6);
    expect(s!.h).toBeCloseTo(300 * 9 / 16, 6);
    expect(s!.x).toBeCloseTo(0, 6);
  });

  it('preserves order and ids', () => {
    const input = randomItems(17, 3);
    const slots = layoutWindows(input, { w: 1400, h: 800 }, 16);
    expect(slots.map((s) => s.id)).toEqual(input.map((i) => i.id));
  });

  it('packs five identical windows into two rows on a wide screen (split 2 + 3)', () => {
    const slots = layoutWindows(items(5), { w: 1920, h: 1000 }, 16);
    const rowsOf = new Map<number, number>();
    for (const s of slots) rowsOf.set(Math.round(s.y), (rowsOf.get(Math.round(s.y)) ?? 0) + 1);
    expect([...rowsOf.values()].sort()).toEqual([2, 3]);
  });

  it('uses a single row for two windows on a wide area', () => {
    const slots = layoutWindows(items(2), { w: 2000, h: 900 }, 16);
    expect(new Set(slots.map((s) => Math.round(s.y))).size).toBe(1);
  });

  it('stacks rows when windows are wide and the area is tall', () => {
    const slots = layoutWindows(items(3, 1600, 900), { w: 900, h: 1500 }, 16);
    expect(new Set(slots.map((s) => Math.round(s.y))).size).toBe(3);
  });

  it('never makes a window bigger because more windows were added (no upscaling beyond the real size)', () => {
    const area = { w: 1600, h: 900 };
    const single = layoutWindows(items(1), area, 16)[0]!;
    for (const n of [2, 3, 5]) {
      for (const s of layoutWindows(items(n), area, 16)) expect(s.h).toBeLessThanOrEqual(single.h + 1e-6);
    }
  });
});

describe('layoutWindows: invariants', () => {
  const cases: Array<{ name: string; input: LayoutItem[]; area: { w: number; h: number }; gap: number }> = [
    { name: '1 window', input: items(1), area: { w: 1440, h: 800 }, gap: 16 },
    { name: '2 windows', input: items(2), area: { w: 1440, h: 800 }, gap: 16 },
    { name: '5 windows', input: items(5), area: { w: 1440, h: 800 }, gap: 16 },
    { name: '12 windows', input: randomItems(12, 11), area: { w: 1440, h: 800 }, gap: 16 },
    { name: '24 windows', input: randomItems(24, 21), area: { w: 1920, h: 1000 }, gap: 16 },
    { name: '40 windows', input: randomItems(40, 31), area: { w: 1920, h: 1000 }, gap: 16 },
    { name: 'tall and wide mix', input: [{ id: 'a', w: 40, h: 2000 }, { id: 'b', w: 3000, h: 60 }, { id: 'c', w: 500, h: 500 }], area: { w: 1200, h: 700 }, gap: 12 },
    { name: 'gap 0', input: randomItems(9, 5), area: { w: 1000, h: 700 }, gap: 0 },
  ];

  for (const c of cases) {
    it(`${c.name}: inside the area, no overlap, aspect kept, never upscaled`, () => {
      const slots = layoutWindows(c.input, c.area, c.gap);
      expect(slots).toHaveLength(c.input.length);
      expectInside(slots, c.area);
      expectNoOverlap(slots);
      expectAspect(slots, c.input);
      expectCapped(slots, c.input);
    });
  }

  it('keeps the gap between neighbouring windows (same row or same column)', () => {
    const area = { w: 1440, h: 800 };
    const slots = layoutWindows(randomItems(14, 8), area, 16);
    for (let i = 0; i < slots.length; i++) {
      for (let j = i + 1; j < slots.length; j++) {
        const a = slots[i]!;
        const b = slots[j]!;
        const gapX = Math.max(b.x - (a.x + a.w), a.x - (b.x + b.w));
        const gapY = Math.max(b.y - (a.y + a.h), a.y - (b.y + b.h));
        expect(Math.max(gapX, gapY)).toBeGreaterThanOrEqual(16 - 1e-6);
      }
    }
  });

  it('property: random sets stay valid across many seeds and sizes', () => {
    for (let seed = 1; seed <= 60; seed++) {
      const r = rng(seed * 97);
      const n = 1 + Math.floor(r() * 18);
      const area = { w: 300 + Math.floor(r() * 1800), h: 200 + Math.floor(r() * 1000) };
      const input = randomItems(n, seed);
      const slots = layoutWindows(input, area, 14);
      expectInside(slots, area);
      expectNoOverlap(slots);
      expectAspect(slots, input);
      expectCapped(slots, input);
    }
  });

  it('is deterministic for the same input', () => {
    const input = randomItems(20, 99);
    const a = layoutWindows(input, { w: 1500, h: 900 }, 16);
    const b = layoutWindows(input, { w: 1500, h: 900 }, 16);
    expect(b).toEqual(a);
  });

  it('handles 40 windows quickly', () => {
    const input = randomItems(40, 4);
    const t0 = performance.now();
    const slots = layoutWindows(input, { w: 1920, h: 1000 }, 16);
    expect(performance.now() - t0).toBeLessThan(500);
    expectNoOverlap(slots);
  });
});

describe('layoutWindows: edge cases', () => {
  it('extreme aspect ratios stay valid', () => {
    const input: LayoutItem[] = [
      { id: 'tall', w: 20, h: 4000 },
      { id: 'wide', w: 4000, h: 20 },
      { id: 'square', w: 500, h: 500 },
    ];
    const area = { w: 900, h: 600 };
    const slots = layoutWindows(input, area, 10);
    expectInside(slots, area);
    expectNoOverlap(slots);
    expectAspect(slots, input);
  });

  it('tiny areas do not throw, produce NaN or leave the area', () => {
    const input = randomItems(6, 2);
    for (const area of [{ w: 1, h: 1 }, { w: 20, h: 10 }, { w: 60, h: 40 }]) {
      const slots = layoutWindows(input, area, 16);
      for (const s of slots) {
        expect(Number.isFinite(s.x) && Number.isFinite(s.y) && Number.isFinite(s.w) && Number.isFinite(s.h)).toBe(true);
      }
      expectInside(slots, area);
    }
  });

  it('a zero or negative area yields empty boxes instead of NaN', () => {
    const slots = layoutWindows(items(3), { w: 0, h: 500 }, 16);
    expect(slots.every((s) => s.w === 0 && s.h === 0)).toBe(true);
  });

  it('sanitises invalid window sizes (0, negative, NaN) instead of propagating them', () => {
    const slots = layoutWindows([{ id: 'a', w: 0, h: 0 }, { id: 'b', w: Number.NaN, h: -4 }], { w: 500, h: 400 }, 8);
    for (const s of slots) {
      expect(Number.isFinite(s.w) && s.w > 0 && Number.isFinite(s.h) && s.h > 0).toBe(true);
    }
    expectNoOverlap(slots);
  });

  it('a gap larger than the area drops the gap rather than overlapping', () => {
    const slots = layoutWindows(items(4), { w: 300, h: 200 }, 500);
    expectInside(slots, { w: 300, h: 200 });
    expectNoOverlap(slots);
  });
});
