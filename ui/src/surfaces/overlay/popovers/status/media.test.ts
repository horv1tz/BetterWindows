import { describe, expect, it } from 'vitest';
import { extrapolatePosition, formatClock } from './media';

describe('extrapolatePosition', () => {
  it('advances while playing and stays put while paused', () => {
    expect(extrapolatePosition({ positionSec: 74, durationSec: 200, playing: true }, 1500)).toBeCloseTo(75.5);
    expect(extrapolatePosition({ positionSec: 74, durationSec: 200, playing: false }, 1500)).toBe(74);
  });

  it('never runs past the duration and never goes negative', () => {
    expect(extrapolatePosition({ positionSec: 199, durationSec: 200, playing: true }, 60_000)).toBe(200);
    expect(extrapolatePosition({ positionSec: -4, durationSec: 200, playing: false }, 0)).toBe(0);
  });

  it('has no upper bound when the duration is unknown (0 = live or unknown)', () => {
    expect(extrapolatePosition({ positionSec: 10, durationSec: 0, playing: true }, 5000)).toBe(15);
  });

  it('ignores NaN and negative elapsed times', () => {
    expect(extrapolatePosition({ positionSec: 10, durationSec: 100, playing: true }, Number.NaN)).toBe(10);
    expect(extrapolatePosition({ positionSec: 10, durationSec: 100, playing: true }, -2000)).toBe(10);
    expect(extrapolatePosition({ positionSec: Number.NaN, durationSec: 100, playing: false }, 0)).toBe(0);
  });
});

describe('formatClock', () => {
  it('formats m:ss and h:mm:ss', () => {
    expect(formatClock(0)).toBe('0:00');
    expect(formatClock(74)).toBe('1:14');
    expect(formatClock(9.99)).toBe('0:09');
    expect(formatClock(3599)).toBe('59:59');
    expect(formatClock(3600)).toBe('1:00:00');
    expect(formatClock(3725)).toBe('1:02:05');
  });

  it('renders invalid input as zero', () => {
    expect(formatClock(-3)).toBe('0:00');
    expect(formatClock(Number.NaN)).toBe('0:00');
    expect(formatClock(Number.POSITIVE_INFINITY)).toBe('0:00');
  });
});
