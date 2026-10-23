import { describe, expect, it } from 'vitest';
import { barThickness, COLLAPSED_SIZE, DRAG_HEADROOM, fitIconSize, layoutStrip, magnifySpec, metricsFor, pillThickness, restPillLength, slotExtent, windowSizing, type SlotSpec } from './layout';

const specs = (n: number): SlotSpec[] => Array.from({ length: n }, (_, i) => ({ key: `a${i}`, kind: 'item' as const }));

describe('metricsFor', () => {
  it('scales paddings with the icon size and keeps sane minimums', () => {
    const m = metricsFor(56);
    expect(m.size).toBe(56);
    expect(pillThickness(m)).toBe(56 + 2 * m.pad);
    expect(barThickness(m)).toBe(m.edgeGap + pillThickness(m));
    const tiny = metricsFor(32);
    expect(tiny.gap).toBeGreaterThanOrEqual(2);
    expect(tiny.pad).toBeGreaterThanOrEqual(5);
    expect(metricsFor(128).radius).toBeLessThanOrEqual(26);
  });
});

describe('layoutStrip', () => {
  it('places slots one after another with the gap in between and no trailing gap', () => {
    const m = metricsFor(56);
    const l = layoutStrip(specs(3), m, 'bottom');
    expect(l.slots.map((s) => s.start)).toEqual([0, 56 + m.gap, 2 * (56 + m.gap)]);
    expect(l.length).toBe(3 * 56 + 2 * m.gap);
  });
  it('sizes separators and tiles by kind and swaps tile extents for vertical docks', () => {
    const m = metricsFor(56);
    expect(slotExtent('sep', m, 'bottom')).toEqual({ len: m.sepLen, cross: m.sepCross });
    expect(slotExtent('tile', m, 'bottom')).toEqual({ len: m.tileW, cross: m.tileH });
    expect(slotExtent('tile', m, 'left')).toEqual({ len: m.tileH, cross: m.tileW });
  });
  it('handles an empty strip', () => {
    const m = metricsFor(56);
    const l = layoutStrip([], m, 'bottom');
    expect(l.length).toBe(0);
    expect(restPillLength(l, m)).toBeGreaterThan(2 * m.pad);
  });
});

describe('windowSizing', () => {
  const m = metricsFor(56);
  const strip = layoutStrip(specs(10), m, 'bottom');
  const mag = magnifySpec(m, true, 56, 88);
  const base = { pos: 'bottom' as const, strip, m, mag, hop: 0, dragExtra: 0, monitorMain: 1920, collapsed: false };

  it('idle = bar only (+ a few px of margin)', () => {
    const s = windowSizing({ ...base, mode: 'idle' });
    expect(s.height).toBe(barThickness(m) + m.margin);
    expect(s.width).toBe(restPillLength(strip, m) + 2 * m.margin);
    expect(s.barThickness).toBe(barThickness(m));
  });
  it('hover adds magnification headroom, tooltip room and side room; barThickness stays the bar', () => {
    const idle = windowSizing({ ...base, mode: 'idle' });
    const hover = windowSizing({ ...base, mode: 'hover' });
    expect(hover.height).toBe(idle.height + (mag.maxIcon - m.size) + m.tipRoom);
    expect(hover.width).toBeGreaterThan(idle.width + 60);
    expect(hover.barThickness).toBe(idle.barThickness);
  });
  it('without magnification hover only adds the tooltip room', () => {
    const off = magnifySpec(m, false, 56, 88);
    const idle = windowSizing({ ...base, mag: off, mode: 'idle' });
    const hover = windowSizing({ ...base, mag: off, mode: 'hover' });
    expect(hover.width).toBe(idle.width);
    expect(hover.height).toBe(idle.height + m.tipRoom);
  });
  it('drag guarantees headroom for the unpin zone and room for an inserted slot', () => {
    const s = windowSizing({ ...base, mode: 'drag', dragExtra: 61 });
    expect(s.height).toBeGreaterThanOrEqual(barThickness(m) + DRAG_HEADROOM);
    expect(s.width).toBeGreaterThan(windowSizing({ ...base, mode: 'hover' }).width);
  });
  it('swaps width and height for left/right docks', () => {
    const v = layoutStrip(specs(10), m, 'left');
    const s = windowSizing({ ...base, pos: 'left', strip: v, mode: 'idle' });
    expect(s.width).toBe(barThickness(m) + m.margin);
    expect(s.height).toBe(restPillLength(v, m) + 2 * m.margin);
  });
  it('collapses to a tiny window when auto-hidden', () => {
    const s = windowSizing({ ...base, mode: 'idle', collapsed: true });
    expect(s).toEqual({ width: COLLAPSED_SIZE, height: COLLAPSED_SIZE, barThickness: COLLAPSED_SIZE });
  });
  it('never asks for more than the monitor', () => {
    const wide = layoutStrip(specs(80), m, 'bottom');
    expect(windowSizing({ ...base, strip: wide, mode: 'hover', monitorMain: 1440 }).width).toBeLessThanOrEqual(1440);
  });
  it('reserves hop room while an icon bounces even when idle', () => {
    const calm = windowSizing({ ...base, mode: 'idle' });
    expect(windowSizing({ ...base, mode: 'idle', hop: 20 }).height).toBe(calm.height + 20);
  });
});

describe('fitIconSize', () => {
  it('keeps the requested size when everything fits', () => {
    expect(fitIconSize(56, 1920, specs(12), 'bottom', 88, true)).toBe(56);
  });
  it('shrinks icons until the (magnified) dock fits the monitor, but not below 24', () => {
    const size = fitIconSize(56, 1280, specs(30), 'bottom', 88, true);
    expect(size).toBeLessThan(56);
    expect(size).toBeGreaterThanOrEqual(24);
    const m = metricsFor(size);
    const mag = magnifySpec(m, true, 56, 88);
    const w = windowSizing({ pos: 'bottom', strip: layoutStrip(specs(30), m, 'bottom'), m, mag, mode: 'hover', hop: 0, dragExtra: 0, monitorMain: 99999, collapsed: false }).width;
    expect(w).toBeLessThanOrEqual(1280);
    expect(fitIconSize(56, 100, specs(30), 'bottom', 88, true)).toBe(24);
  });
});
