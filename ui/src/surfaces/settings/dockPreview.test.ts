import { describe, expect, it } from 'vitest';
import { dockPreviewGeometry, fitScale, magnifiedSizes } from './dockPreview';

describe('magnifiedSizes', () => {
  it('is flat without magnification or when the peak is not larger', () => {
    expect(magnifiedSizes(4, 40, 80, 1, false)).toEqual([40, 40, 40, 40]);
    expect(magnifiedSizes(3, 40, 40, 1)).toEqual([40, 40, 40]);
    expect(magnifiedSizes(3, 40, 30, 1)).toEqual([40, 40, 40]);
  });
  it('peaks exactly at the focused icon and falls off symmetrically', () => {
    const s = magnifiedSizes(7, 40, 100, 3);
    expect(s[3]).toBeCloseTo(100, 6);
    expect(s[2]).toBeCloseTo(s[4], 6);
    expect(s[1]).toBeCloseTo(s[5], 6);
    expect(s[3]).toBeGreaterThan(s[2]);
    expect(s[2]).toBeGreaterThan(s[1]);
    expect(s[0]).toBe(40); // beyond the spread
    expect(s[6]).toBe(40);
  });
  it('a fractional focus blends neighbours', () => {
    const s = magnifiedSizes(5, 40, 100, 1.5);
    expect(s[1]).toBeCloseTo(s[2], 6);
  });
  it('handles empty and negative counts', () => {
    expect(magnifiedSizes(0, 40, 80, 0)).toEqual([]);
    expect(magnifiedSizes(-3, 40, 80, 0)).toEqual([]);
  });
});

describe('dockPreviewGeometry', () => {
  it('scales everything and reports overflow of magnified icons', () => {
    const g = dockPreviewGeometry({ count: 5, iconSize: 64, magnifiedSize: 128, magnification: true, focus: 2, scale: 0.5 });
    expect(g.sizes[2]).toBeCloseTo(64, 6);
    expect(g.sizes[0]).toBeGreaterThan(32); // still inside the magnification spread
    expect(g.sizes[0]).toBeLessThan(g.sizes[1]);
    expect(g.thickness).toBeCloseTo(32 + 2 * 6.4, 6);
    expect(g.overflow).toBeCloseTo(32, 6);
    expect(g.length).toBeGreaterThan(g.thickness);
  });
  it('no overflow without magnification', () => {
    const g = dockPreviewGeometry({ count: 5, iconSize: 64, magnifiedSize: 128, magnification: false, focus: 2, scale: 1 });
    expect(g.overflow).toBe(0);
    expect(g.length).toBeCloseTo(5 * 64 + 4 * 64 * 0.14 + 2 * 64 * 0.2, 6);
  });
});

describe('fitScale', () => {
  it('never exceeds max and shrinks to fit', () => {
    expect(fitScale({ count: 3, iconSize: 32, magnifiedSize: 32, magnification: false, available: 10_000 })).toBe(0.6);
    const s = fitScale({ count: 9, iconSize: 128, magnifiedSize: 192, magnification: true, available: 300 });
    expect(s).toBeLessThan(0.6);
    expect(dockPreviewGeometry({ count: 9, iconSize: 128, magnifiedSize: 192, magnification: true, focus: 4, scale: s }).length).toBeLessThanOrEqual(300.0001);
  });
  it('tolerates zero width', () => {
    expect(fitScale({ count: 3, iconSize: 32, magnifiedSize: 32, magnification: false, available: 0 })).toBe(0.6);
  });
});
