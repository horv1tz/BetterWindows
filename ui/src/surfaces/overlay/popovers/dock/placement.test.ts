import { describe, expect, it } from 'vitest';
import { placePanel } from './placement';

const vw = 1440, vh = 900;
const icon = (x: number, y: number) => ({ x, y, width: 56, height: 72 });

describe('placePanel', () => {
  it('opens above a bottom dock, centred on the icon, growing out of it', () => {
    const p = placePanel({ anchor: icon(700, 820), w: 260, h: 300, vw, vh, dock: 'bottom' });
    expect(p.x).toBe(728 - 130);
    expect(p.y).toBe(820 - 10 - 300);
    expect(p.originX).toBe(130);
    expect(p.originY).toBe(300);
  });
  it('clamps horizontally into the viewport and keeps the origin on the icon', () => {
    const left = placePanel({ anchor: icon(2, 820), w: 260, h: 300, vw, vh, dock: 'bottom' });
    expect(left.x).toBe(8);
    expect(left.originX).toBeCloseTo(30 - 8, 5);
    const right = placePanel({ anchor: icon(1400, 820), w: 260, h: 300, vw, vh, dock: 'bottom' });
    expect(right.x).toBe(vw - 260 - 8);
  });
  it('falls below the icon (or clamps) when there is no room above', () => {
    const p = placePanel({ anchor: { x: 700, y: 20, width: 56, height: 72 }, w: 260, h: 300, vw, vh, dock: 'bottom' });
    expect(p.y).toBe(20 + 72 + 10);
    expect(p.originY).toBe(0);
    const tall = placePanel({ anchor: icon(700, 820), w: 260, h: 880, vw, vh, dock: 'bottom' });
    expect(tall.y).toBe(8);
  });
  it('opens beside a left dock, vertically centred on the icon, and flips if it does not fit', () => {
    const p = placePanel({ anchor: { x: 8, y: 400, width: 72, height: 56 }, w: 260, h: 200, vw, vh, dock: 'left' });
    expect(p.x).toBe(8 + 72 + 10);
    expect(p.y).toBe(428 - 100);
    expect(p.originX).toBe(0);
    expect(p.originY).toBe(100);
    const flip = placePanel({ anchor: { x: 1300, y: 400, width: 72, height: 56 }, w: 260, h: 200, vw, vh, dock: 'left' });
    expect(flip.x).toBe(1300 - 10 - 260);
    expect(flip.originX).toBe(260);
  });
  it('opens to the left of a right dock and keeps itself inside vertically', () => {
    const p = placePanel({ anchor: { x: 1360, y: 20, width: 72, height: 56 }, w: 260, h: 200, vw, vh, dock: 'right' });
    expect(p.x).toBe(1360 - 10 - 260);
    expect(p.y).toBe(8);
    expect(p.originX).toBe(260);
  });
  it('never returns NaN for a degenerate (not yet measured) panel', () => {
    const p = placePanel({ anchor: icon(0, 0), w: 0, h: 0, vw, vh, dock: 'bottom' });
    expect(Number.isFinite(p.x) && Number.isFinite(p.y)).toBe(true);
  });
});
