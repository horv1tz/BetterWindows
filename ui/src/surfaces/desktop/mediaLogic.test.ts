import { describe, expect, it } from 'vitest';
import { batteryTone, deviceKind, devicesWithBattery, estimatePosition, formatClock, progressFraction, splitMinutes } from './mediaLogic';

describe('formatClock', () => {
  it('formats m:ss and h:mm:ss, tolerating bad input', () => {
    expect(formatClock(0)).toBe('0:00');
    expect(formatClock(74.9)).toBe('1:14');
    expect(formatClock(3725)).toBe('1:02:05');
    expect(formatClock(-3)).toBe('0:00');
    expect(formatClock(NaN)).toBe('0:00');
  });
});

describe('estimatePosition', () => {
  const s = { playing: true, positionSec: 10, durationSec: 100 };
  it('advances while playing and holds while paused', () => {
    expect(estimatePosition(s, 1000, 4000)).toBe(13);
    expect(estimatePosition({ ...s, playing: false }, 1000, 4000)).toBe(10);
  });
  it('never exceeds the duration and ignores clock skew', () => {
    expect(estimatePosition(s, 0, 500_000)).toBe(100);
    expect(estimatePosition(s, 5000, 1000)).toBe(10);
    expect(estimatePosition({ playing: true, positionSec: 5, durationSec: 0 }, 0, 2000)).toBe(7);
  });
  it('progress is clamped to 0..1', () => {
    expect(progressFraction(25, 100)).toBe(0.25);
    expect(progressFraction(500, 100)).toBe(1);
    expect(progressFraction(5, 0)).toBe(0);
  });
});

describe('battery helpers', () => {
  it('classifies device names', () => {
    expect(deviceKind('WH-1000XM5')).toBe('headphones');
    expect(deviceKind("Anna's AirPods Pro")).toBe('headphones');
    expect(deviceKind('MX Master 3S')).toBe('mouse');
    expect(deviceKind('Keychron K2')).toBe('keyboard');
    expect(deviceKind('Galaxy S24')).toBe('phone');
    expect(deviceKind('Xbox Wireless Controller')).toBe('gamepad');
    expect(deviceKind('Thing 3000')).toBe('other');
  });
  it('tones', () => {
    expect(batteryTone(80, false)).toBe('good');
    expect(batteryTone(20, false)).toBe('low');
    expect(batteryTone(10, false)).toBe('critical');
    expect(batteryTone(5, true)).toBe('charging');
  });
  it('lists connected devices with a level, lowest first', () => {
    const list = devicesWithBattery([
      { id: 'a', connected: true, batteryPercent: 82 }, { id: 'b', connected: true, batteryPercent: 45 },
      { id: 'c', connected: false, batteryPercent: 10 }, { id: 'd', connected: true },
    ]);
    expect(list.map((d) => d.id)).toEqual(['b', 'a']);
  });
  it('splits minutes', () => {
    expect(splitMinutes(214)).toEqual({ h: 3, m: 34 });
    expect(splitMinutes(-5)).toEqual({ h: 0, m: 0 });
  });
});
