import { describe, expect, it } from 'vitest';
import { clamp, fraction, snap, valueFromKey, valueFromPointer } from './slider';

describe('clamp / snap / fraction', () => {
  it('clamps and treats NaN as min', () => {
    expect(clamp(5, 0, 1)).toBe(1);
    expect(clamp(-5, 0, 1)).toBe(0);
    expect(clamp(Number.NaN, 0.2, 1)).toBe(0.2);
  });

  it('snaps to the step grid without float noise', () => {
    expect(snap(0.304, 0, 1, 0.01)).toBe(0.3);
    expect(snap(0.1 + 0.2, 0, 1, 0.01)).toBe(0.3);
    expect(snap(0.756, 0, 1, 0.05)).toBe(0.75);
    expect(snap(7.4, 0, 100, 1)).toBe(7);
    expect(snap(7.5, 0, 100, 1)).toBe(8);
  });

  it('snaps relative to min and clamps to max', () => {
    expect(snap(3.2, 1, 10, 2)).toBe(3);
    expect(snap(99, 1, 10, 2)).toBe(9); // max is not on the grid: stay clamped, never exceed
    expect(snap(2, 0, 1, 0)).toBe(1); // zero step = no snapping, just clamp
  });

  it('fraction is 0..1 and safe for degenerate ranges', () => {
    expect(fraction(0.5, 0, 1)).toBe(0.5);
    expect(fraction(2, 0, 1)).toBe(1);
    expect(fraction(-1, 0, 1)).toBe(0);
    expect(fraction(5, 5, 5)).toBe(0);
    expect(fraction(50, 0, 200)).toBe(0.25);
  });
});

describe('valueFromPointer', () => {
  const rect = { left: 100, width: 200 };
  it('maps the track edges and middle', () => {
    expect(valueFromPointer(100, rect, 0, 1, 0.01)).toBe(0);
    expect(valueFromPointer(300, rect, 0, 1, 0.01)).toBe(1);
    expect(valueFromPointer(200, rect, 0, 1, 0.01)).toBe(0.5);
  });
  it('clamps pointers outside of the track', () => {
    expect(valueFromPointer(-50, rect, 0, 1, 0.01)).toBe(0);
    expect(valueFromPointer(900, rect, 0, 1, 0.01)).toBe(1);
  });
  it('honours the thumb inset so the thumb centre follows the pointer', () => {
    // 200 wide, inset 10 -> usable 180 starting at x=110
    expect(valueFromPointer(110, rect, 0, 1, 0.01, 10)).toBe(0);
    expect(valueFromPointer(290, rect, 0, 1, 0.01, 10)).toBe(1);
    expect(valueFromPointer(200, rect, 0, 1, 0.01, 10)).toBe(0.5);
  });
  it('snaps to integer steps for seek bars', () => {
    expect(valueFromPointer(250, rect, 0, 200, 1)).toBe(150);
  });
  it('a collapsed track returns min instead of NaN', () => {
    expect(valueFromPointer(5, { left: 0, width: 0 }, 0.1, 1, 0.01)).toBe(0.1);
  });
});

describe('valueFromKey', () => {
  it('arrows move by one step in both directions', () => {
    expect(valueFromKey('ArrowRight', 0.5, 0, 1, 0.05)).toBe(0.55);
    expect(valueFromKey('ArrowUp', 0.5, 0, 1, 0.05)).toBe(0.55);
    expect(valueFromKey('ArrowLeft', 0.5, 0, 1, 0.05)).toBe(0.45);
    expect(valueFromKey('ArrowDown', 0.5, 0, 1, 0.05)).toBe(0.45);
  });
  it('page keys move by the page step (default 10 steps)', () => {
    expect(valueFromKey('PageUp', 0.2, 0, 1, 0.01)).toBe(0.3);
    expect(valueFromKey('PageDown', 0.2, 0, 1, 0.01)).toBe(0.1);
    expect(valueFromKey('PageUp', 0.2, 0, 1, 0.01, 0.25)).toBe(0.45);
  });
  it('home/end jump to the bounds and values never leave the range', () => {
    expect(valueFromKey('Home', 0.6, 0, 1, 0.01)).toBe(0);
    expect(valueFromKey('End', 0.6, 0, 1, 0.01)).toBe(1);
    expect(valueFromKey('ArrowRight', 1, 0, 1, 0.05)).toBe(1);
    expect(valueFromKey('ArrowLeft', 0, 0, 1, 0.05)).toBe(0);
  });
  it('ignores unrelated keys', () => {
    expect(valueFromKey('a', 0.5, 0, 1, 0.05)).toBeNull();
    expect(valueFromKey('Enter', 0.5, 0, 1, 0.05)).toBeNull();
  });
});
