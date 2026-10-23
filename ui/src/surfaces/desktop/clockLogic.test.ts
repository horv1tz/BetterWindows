import { describe, expect, it } from 'vitest';
import { clockParts, formatDigital, handAngles, isValidTimeZone, msToNextTick, zoneCity } from './clockLogic';

describe('clockParts', () => {
  it('reads wall-clock parts in a named zone', () => {
    const d = new Date(Date.UTC(2026, 0, 15, 12, 30, 45, 250)); // winter: Moscow UTC+3, New York UTC-5
    const msk = clockParts(d, 'Europe/Moscow');
    expect([msk.hours, msk.minutes, msk.seconds, msk.ms, msk.day, msk.month, msk.year]).toEqual([15, 30, 45, 250, 15, 0, 2026]);
    expect(msk.weekday).toBe(4); // Thursday
    const ny = clockParts(d, 'America/New_York');
    expect(ny.hours).toBe(7);
  });
  it('rolls the date over in zones ahead of UTC', () => {
    const d = new Date(Date.UTC(2026, 11, 31, 22, 0, 0));
    const tokyo = clockParts(d, 'Asia/Tokyo');
    expect([tokyo.year, tokyo.month, tokyo.day, tokyo.hours]).toEqual([2027, 0, 1, 7]);
  });
  it('midnight is hour 0, not 24', () => {
    expect(clockParts(new Date(Date.UTC(2026, 5, 1, 0, 0, 0)), 'UTC').hours).toBe(0);
  });
  it('falls back to the system zone for missing/invalid zones', () => {
    const d = new Date(2026, 5, 1, 8, 15, 9, 500);
    expect(clockParts(d).hours).toBe(8);
    expect(clockParts(d, 'Not/AZone').minutes).toBe(15);
    expect(isValidTimeZone('Europe/Berlin')).toBe(true);
    expect(isValidTimeZone('nope')).toBe(false);
    expect(isValidTimeZone(undefined)).toBe(false);
  });
});

describe('handAngles', () => {
  const at = (h: number, m: number, s: number, ms = 0) => ({ hours: h, minutes: m, seconds: s, ms, weekday: 0, day: 1, month: 0, year: 2026 });
  it('points straight up at 12:00:00', () => {
    expect(handAngles(at(12, 0, 0), true)).toEqual({ hour: 0, minute: 0, second: 0 });
  });
  it('3:00 and 6:30 land on the right angles', () => {
    expect(handAngles(at(15, 0, 0), true).hour).toBe(90);
    const a = handAngles(at(6, 30, 0), true);
    expect(a.minute).toBe(180);
    expect(a.hour).toBe(195); // 6.5 h * 30
  });
  it('smooth mode sweeps with milliseconds, tick mode does not', () => {
    expect(handAngles(at(0, 0, 10, 500), true).second).toBeCloseTo(63);
    expect(handAngles(at(0, 0, 10, 500), false).second).toBe(60);
  });
});

describe('digital formatting', () => {
  const at = (h: number, m: number, s = 0) => ({ hours: h, minutes: m, seconds: s, ms: 0, weekday: 0, day: 1, month: 0, year: 2026 });
  it('24 h pads the hour', () => {
    expect(formatDigital(at(9, 5), true, false)).toEqual({ time: '09:05', suffix: '' });
    expect(formatDigital(at(9, 5, 7), true, true).time).toBe('09:05:07');
  });
  it('12 h uses 12 for midnight and noon', () => {
    expect(formatDigital(at(0, 7), false, false)).toEqual({ time: '12:07', suffix: 'AM' });
    expect(formatDigital(at(12, 0), false, false)).toEqual({ time: '12:00', suffix: 'PM' });
    expect(formatDigital(at(23, 59), false, false)).toEqual({ time: '11:59', suffix: 'PM' });
  });
});

describe('misc', () => {
  it('schedules the next tick just after the boundary', () => {
    expect(msToNextTick(1000, true)).toBe(1005);
    expect(msToNextTick(1999, true)).toBe(6);
    expect(msToNextTick(59000, false)).toBe(1005);
  });
  it('extracts a readable city', () => {
    expect(zoneCity('America/New_York')).toBe('New York');
    expect(zoneCity('UTC')).toBe('UTC');
    expect(zoneCity(undefined)).toBe('');
  });
});
