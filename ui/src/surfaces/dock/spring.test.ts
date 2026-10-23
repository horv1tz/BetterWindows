import { describe, expect, it } from 'vitest';
import { isSettled, snap, spring, stepSpring } from './spring';

describe('spring', () => {
  it('converges to the target without overshoot when critically damped', () => {
    const s = spring(0);
    let max = 0;
    for (let i = 0; i < 120; i++) { stepSpring(s, 1, 1 / 60, { omega: 24, zeta: 1 }); max = Math.max(max, s.x); }
    expect(max).toBeLessThanOrEqual(1.0001);
    expect(isSettled(s, 1)).toBe(true);
  });
  it('overshoots when under-damped but still settles', () => {
    const s = spring(0);
    let max = 0;
    for (let i = 0; i < 240; i++) { stepSpring(s, 1, 1 / 60, { omega: 24, zeta: 0.4 }); max = Math.max(max, s.x); }
    expect(max).toBeGreaterThan(1.05);
    expect(isSettled(s, 1)).toBe(true);
  });
  it('gives the same result for one big step and many small steps (fixed sub-steps)', () => {
    const a = spring(0), b = spring(0);
    stepSpring(a, 10, 0.1, { omega: 30, zeta: 0.8 });
    for (let i = 0; i < 10; i++) stepSpring(b, 10, 0.01, { omega: 30, zeta: 0.8 });
    expect(a.x).toBeCloseTo(b.x, 1);
  });
  it('ignores non-positive dt and survives a huge dt', () => {
    const s = spring(5);
    stepSpring(s, 0, 0, { omega: 20, zeta: 1 });
    stepSpring(s, 0, -1, { omega: 20, zeta: 1 });
    expect(s.x).toBe(5);
    stepSpring(s, 0, 100, { omega: 20, zeta: 1 });
    expect(Number.isFinite(s.x)).toBe(true);
  });
  it('snap sets value and kills velocity', () => {
    const s = { x: 1, v: 3 };
    snap(s, 7);
    expect(s).toEqual({ x: 7, v: 0 });
  });
});
