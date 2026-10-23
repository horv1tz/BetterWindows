import { describe, expect, it } from 'vitest';
import { createFisheye, falloff, falloffIntegral, influenceRadius, intervalScale, mapPoint, maxSideShift, peakScale } from './magnify';

const SIZE = 56, GAP = 5, MAG = 88;
const R = influenceRadius(SIZE, GAP);
const f = createFisheye(SIZE, MAG, R);
const PITCH = SIZE + GAP;

/** Rest layout of n icons starting at 0. */
const icons = (n: number) => Array.from({ length: n }, (_, i) => ({ l: i * PITCH, r: i * PITCH + SIZE, c: i * PITCH + SIZE / 2 }));

describe('falloff', () => {
  it('is 1 at the centre, 0 at and beyond the radius, symmetric and monotonic', () => {
    expect(falloff(0)).toBe(1);
    expect(falloff(1)).toBe(0);
    expect(falloff(-3)).toBe(0);
    expect(falloff(0.3)).toBeCloseTo(falloff(-0.3), 12);
    for (let t = 0; t < 1; t += 0.05) expect(falloff(t + 0.05)).toBeLessThanOrEqual(falloff(t) + 1e-12);
  });
  it('integral matches numeric integration and saturates at 1/2', () => {
    let sum = 0;
    const n = 20000;
    for (let i = 0; i < n; i++) sum += falloff((i + 0.5) / n) / n;
    expect(falloffIntegral(1)).toBeCloseTo(sum, 6);
    expect(falloffIntegral(1)).toBeCloseTo(0.5, 12);
    expect(falloffIntegral(5)).toBeCloseTo(0.5, 12);
    expect(falloffIntegral(-0.4)).toBeCloseTo(-falloffIntegral(0.4), 12);
  });
});

describe('createFisheye', () => {
  it('lets the icon centred under the anchor reach exactly magnifiedSize', () => {
    expect(peakScale(f, SIZE) * SIZE).toBeCloseTo(MAG, 6);
  });
  it('is the identity when magnification would not enlarge anything', () => {
    const none = createFisheye(SIZE, SIZE, R);
    expect(none.amp).toBe(0);
    expect(mapPoint(123, 50, none)).toBe(123);
    expect(createFisheye(SIZE, 40, R).amp).toBe(0);
  });
  it('survives degenerate input', () => {
    expect(createFisheye(0, 10, R).amp).toBe(0);
    expect(Number.isFinite(createFisheye(56, 88, 0).amp)).toBe(true);
  });
});

describe('mapPoint', () => {
  it('keeps the anchor fixed and is strictly increasing', () => {
    expect(mapPoint(100, 100, f)).toBeCloseTo(100, 9);
    let prev = -Infinity;
    for (let x = -200; x <= 600; x += 3.7) {
      const y = mapPoint(x, 211, f);
      expect(y).toBeGreaterThan(prev);
      prev = y;
    }
  });
  it('is point-symmetric around the anchor', () => {
    for (const d of [3, 20, 61, 120, 200, 500]) {
      expect(mapPoint(300 + d, 300, f) - 300).toBeCloseTo(-(mapPoint(300 - d, 300, f) - 300), 9);
    }
  });
  it('does not move the strip locally beyond the radius, only translates it by a constant', () => {
    const shiftA = mapPoint(1000, 100, f) - 1000;
    const shiftB = mapPoint(1500, 100, f) - 1500;
    expect(shiftA).toBeCloseTo(shiftB, 9);
    expect(shiftA).toBeCloseTo(maxSideShift(f), 9);
  });
  it('amount blends between rest and full magnification', () => {
    expect(mapPoint(180, 100, f, 0)).toBe(180);
    const half = mapPoint(180, 100, f, 0.5) - 180;
    const full = mapPoint(180, 100, f, 1) - 180;
    expect(half).toBeCloseTo(full / 2, 9);
  });
});

describe('icon scales along a row', () => {
  const row = icons(15);
  const scalesAt = (anchor: number) => row.map((i) => intervalScale(i.l, i.r, anchor, f));

  it('the icon under the pointer is the biggest and neighbours fall off smoothly to 1', () => {
    const k = scalesAt(row[7].c);
    expect(k[7] * SIZE).toBeCloseTo(MAG, 5);
    expect(k[7]).toBeGreaterThan(k[6]);
    expect(k[6]).toBeGreaterThan(k[5]);
    expect(k[5]).toBeGreaterThan(k[4]);
    expect(k[4]).toBeGreaterThanOrEqual(1);
    expect(k[0]).toBeCloseTo(1, 9);
    expect(k[14]).toBeCloseTo(1, 9);
  });
  it('is mirror symmetric when the pointer is at the centre of the row', () => {
    const k = scalesAt(row[7].c);
    for (let i = 1; i <= 7; i++) expect(k[7 - i]).toBeCloseTo(k[7 + i], 9);
  });
  it('never shrinks an icon and never exceeds the magnified size', () => {
    for (let a = -50; a < 1000; a += 7.3) {
      for (const k of scalesAt(a)) { expect(k).toBeGreaterThanOrEqual(1 - 1e-12); expect(k).toBeLessThanOrEqual(MAG / SIZE + 1e-9); }
    }
  });
  it('icons never overlap after mapping (gaps stay positive)', () => {
    for (let a = 0; a < 900; a += 11) {
      for (let i = 0; i < row.length - 1; i++) {
        const gap = mapPoint(row[i + 1].l, a, f) - mapPoint(row[i].r, a, f);
        expect(gap).toBeGreaterThanOrEqual(GAP - 1e-9);
      }
    }
  });
  it('keeps the icon under the pointer under the pointer', () => {
    const a = row[4].c + 9;
    const L = mapPoint(row[4].l, a, f), R = mapPoint(row[4].r, a, f);
    expect(a).toBeGreaterThan(L);
    expect(a).toBeLessThan(R);
  });
  it('without a pointer in range everything is at rest', () => {
    const k = scalesAt(-10_000);
    for (const v of k) expect(v).toBeCloseTo(1, 9);
  });
  it('bounds the movement of the row ends by maxSideShift', () => {
    for (let a = -100; a < 1000; a += 13) {
      expect(Math.abs(mapPoint(row[0].l, a, f) - row[0].l)).toBeLessThanOrEqual(maxSideShift(f) + 1e-9);
      expect(Math.abs(mapPoint(row[14].r, a, f) - row[14].r)).toBeLessThanOrEqual(maxSideShift(f) + 1e-9);
    }
  });
});
