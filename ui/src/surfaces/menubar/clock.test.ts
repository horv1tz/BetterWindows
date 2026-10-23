import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TICK_PAD_MS, createClockTicker, formatClock, formatClockLabel, formatShortDate, formatTime, msUntilNextTick } from './clock';

// Fri 23 Oct 2026 15:42:07 UTC. All formatting tests pin the time zone so they do not depend on the machine.
const D = new Date(Date.UTC(2026, 9, 23, 15, 42, 7));
const base = { use24Hour: true, showSeconds: false, showDate: true };

describe('formatClock', () => {
  it('24h with date, English', () => {
    expect(formatClock(D, 'en', base, 'UTC')).toBe('Fri 23 Oct 15:42');
  });
  it('24h with date, Russian: no dots after the abbreviations', () => {
    expect(formatClock(D, 'ru', base, 'UTC')).toBe('пт 23 окт 15:42');
  });
  it('12h shows AM/PM and no zero padding of the hour', () => {
    expect(formatClock(D, 'en', { ...base, use24Hour: false }, 'UTC')).toBe('Fri 23 Oct 3:42 PM');
    expect(formatClock(new Date(Date.UTC(2026, 9, 23, 9, 5)), 'en', { ...base, use24Hour: false }, 'UTC')).toBe('Fri 23 Oct 9:05 AM');
  });
  it('seconds are optional', () => {
    expect(formatClock(D, 'en', { ...base, showSeconds: true }, 'UTC')).toBe('Fri 23 Oct 15:42:07');
    expect(formatClock(D, 'en', { ...base, showSeconds: true, use24Hour: false }, 'UTC')).toBe('Fri 23 Oct 3:42:07 PM');
  });
  it('date is optional', () => {
    expect(formatClock(D, 'en', { ...base, showDate: false }, 'UTC')).toBe('15:42');
    expect(formatClock(D, 'ru', { ...base, showDate: false, showSeconds: true }, 'UTC')).toBe('15:42:07');
  });
  it('midnight is 00:xx in 24h mode and 12:xx AM in 12h mode', () => {
    const midnight = new Date(Date.UTC(2026, 9, 24, 0, 5));
    expect(formatTime(midnight, 'en', { use24Hour: true, showSeconds: false }, 'UTC')).toBe('00:05');
    expect(formatTime(midnight, 'en', { use24Hour: false, showSeconds: false }, 'UTC')).toBe('12:05 AM');
  });
  it('uses the time zone of the machine when none is pinned (no throw)', () => {
    expect(formatClock(D, 'en', base)).toMatch(/^\w{3} \d{1,2} \w+ \d{2}:\d{2}$/);
  });
  it('an unknown locale falls back to English instead of throwing', () => {
    expect(formatClock(D, 'xx-invalid-locale-tag', base, 'UTC')).toBe('Fri 23 Oct 15:42');
  });
  it('the date part uses the genitive abbreviation in Russian', () => {
    expect(formatShortDate(new Date(Date.UTC(2026, 2, 5, 12)), 'ru', 'UTC')).toBe('чт 5 мар');
    expect(formatShortDate(new Date(Date.UTC(2026, 8, 1, 12)), 'ru', 'UTC')).toMatch(/^вт 1 сен/);
  });
});

describe('formatClockLabel', () => {
  it('spells the full date for screen readers', () => {
    expect(formatClockLabel(D, 'en', base, 'UTC')).toBe('Friday, 23 October 2026, 15:42');
    expect(formatClockLabel(D, 'ru', base, 'UTC')).toBe('пятница, 23 октября 2026, 15:42');
  });
});

describe('msUntilNextTick', () => {
  it('aligns to the next minute boundary', () => {
    expect(msUntilNextTick(Date.UTC(2026, 0, 1, 10, 0, 7, 250), false)).toBe(52_750 + TICK_PAD_MS);
  });
  it('aligns to the next second boundary when seconds are shown', () => {
    expect(msUntilNextTick(Date.UTC(2026, 0, 1, 10, 0, 7, 250), true)).toBe(750 + TICK_PAD_MS);
  });
  it('exactly on a boundary waits one full step (the tick that just happened is not repeated)', () => {
    expect(msUntilNextTick(Date.UTC(2026, 0, 1, 10, 0, 0, 0), false)).toBe(60_000 + TICK_PAD_MS);
    expect(msUntilNextTick(Date.UTC(2026, 0, 1, 10, 0, 5, 0), true)).toBe(1000 + TICK_PAD_MS);
  });
  it('handles negative timestamps', () => {
    expect(msUntilNextTick(-1, true)).toBe(1 + TICK_PAD_MS);
  });
});

describe('createClockTicker', () => {
  beforeEach(() => { vi.useFakeTimers(); vi.setSystemTime(Date.UTC(2026, 9, 23, 15, 42, 50, 0)); });
  afterEach(() => vi.useRealTimers());

  it('ticks immediately and then on each minute boundary, not every second', () => {
    const ticks: number[] = [];
    const t = createClockTicker((d) => ticks.push(d.getUTCMinutes() * 100 + d.getUTCSeconds()), () => false);
    expect(ticks).toEqual([4250]);
    vi.advanceTimersByTime(9_000);
    expect(ticks).toEqual([4250]);
    vi.advanceTimersByTime(2_000);
    expect(ticks).toHaveLength(2);
    expect(ticks[1]).toBe(4300);
    vi.advanceTimersByTime(60_000);
    expect(ticks).toHaveLength(3);
    t.stop();
  });

  it('ticks every second when seconds are shown and picks the cadence up on refresh', () => {
    let secs = false;
    const ticks: number[] = [];
    const t = createClockTicker(() => ticks.push(Date.now()), () => secs);
    secs = true;
    t.refresh();
    const before = ticks.length;
    vi.advanceTimersByTime(3_050);
    expect(ticks.length - before).toBe(3);
    t.stop();
  });

  it('schedules nothing while paused and ticks right away on resume', () => {
    const onTick = vi.fn();
    const t = createClockTicker(onTick, () => false);
    expect(onTick).toHaveBeenCalledTimes(1);
    t.setPaused('hidden', true);
    vi.advanceTimersByTime(180_000);
    expect(onTick).toHaveBeenCalledTimes(1);
    expect(vi.getTimerCount()).toBe(0);
    t.setPaused('hidden', false);
    expect(onTick).toHaveBeenCalledTimes(2);
    expect(vi.getTimerCount()).toBe(1);
    t.stop();
  });

  it('stays paused until every reason is cleared', () => {
    const onTick = vi.fn();
    const t = createClockTicker(onTick, () => false);
    t.setPaused('document', true);
    t.setPaused('surface', true);
    t.setPaused('document', false);
    vi.advanceTimersByTime(120_000);
    expect(onTick).toHaveBeenCalledTimes(1);
    t.setPaused('surface', false);
    expect(onTick).toHaveBeenCalledTimes(2);
    t.stop();
  });

  it('stop() clears the pending timer and ignores later calls', () => {
    const onTick = vi.fn();
    const t = createClockTicker(onTick, () => false);
    t.stop();
    expect(vi.getTimerCount()).toBe(0);
    t.refresh();
    t.setPaused('x', true);
    t.setPaused('x', false);
    vi.advanceTimersByTime(120_000);
    expect(onTick).toHaveBeenCalledTimes(1);
  });
});
