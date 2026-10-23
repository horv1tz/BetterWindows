import { describe, expect, it } from 'vitest';
import { feedWheel, initialWheel } from './pager';

describe('feedWheel', () => {
  it('turns one page once the accumulated delta passes the threshold', () => {
    let s = initialWheel();
    let r = feedWheel(s, 0, 20, 1000);
    expect(r.turn).toBe(0);
    r = feedWheel(r.state, 0, 20, 1016);
    expect(r.turn).toBe(0);
    r = feedWheel(r.state, 0, 20, 1032);
    expect(r.turn).toBe(1);
    s = r.state;
    expect(s.lockedUntil).toBeGreaterThan(1032);
  });

  it('a long inertia tail turns exactly one page', () => {
    let s = initialWheel();
    let turns = 0;
    for (let t = 0; t < 900; t += 16) {
      const r = feedWheel(s, -Math.max(1, 80 - t / 10), 0, 1000 + t); // decaying swipe to the left
      s = r.state;
      turns += Math.abs(r.turn);
    }
    expect(turns).toBe(1);
  });

  it('negative deltas go back, trackpad (deltaX) and wheel (deltaY) both work', () => {
    expect(feedWheel(initialWheel(), -100, 0, 0).turn).toBe(-1);
    expect(feedWheel(initialWheel(), 0, 100, 0).turn).toBe(1);
    expect(feedWheel(initialWheel(), 10, -100, 0).turn).toBe(-1); // dominant axis
  });

  it('a pause starts a new gesture, which can turn again', () => {
    let r = feedWheel(initialWheel(), 0, 100, 0);
    expect(r.turn).toBe(1);
    r = feedWheel(r.state, 0, 100, 2000);
    expect(r.turn).toBe(1);
  });

  it('small slow nudges separated by pauses never accumulate', () => {
    let s = initialWheel();
    for (let i = 0; i < 20; i++) {
      const r = feedWheel(s, 0, 10, i * 400);
      expect(r.turn).toBe(0);
      s = r.state;
    }
  });

  it('a reversal inside a gesture resets the accumulator', () => {
    let r = feedWheel(initialWheel(), 0, 30, 0);
    r = feedWheel(r.state, 0, -30, 10);
    expect(r.turn).toBe(0);
    expect(r.state.acc).toBe(-30);
  });

  it('ignores zero deltas', () => {
    const s = initialWheel();
    expect(feedWheel(s, 0, 0, 5)).toEqual({ state: s, turn: 0 });
  });
});
