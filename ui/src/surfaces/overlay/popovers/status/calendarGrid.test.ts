import { describe, expect, it } from 'vitest';
import { buildMonthGrid, dateKey, daysInMonth, isLeapYear, isoWeekNumber, weekStartFor, weekdayOf, weekdayOrder } from './calendarGrid';

const NO_TODAY = { year: 1900, month: 0, day: 1 };

describe('calendar arithmetic', () => {
  it('knows leap years (century rule and 400-year rule)', () => {
    expect(isLeapYear(2024)).toBe(true);
    expect(isLeapYear(2023)).toBe(false);
    expect(isLeapYear(2100)).toBe(false);
    expect(isLeapYear(2000)).toBe(true);
  });

  it('gives month lengths including February of leap and common years', () => {
    expect(daysInMonth(2028, 1)).toBe(29);
    expect(daysInMonth(2027, 1)).toBe(28);
    expect(daysInMonth(2100, 1)).toBe(28);
    expect(daysInMonth(2026, 0)).toBe(31);
    expect(daysInMonth(2026, 3)).toBe(30);
    expect(daysInMonth(2026, 11)).toBe(31);
  });

  it('computes weekdays with Sunday = 0', () => {
    expect(weekdayOf(2026, 0, 1)).toBe(4); // Thursday
    expect(weekdayOf(2026, 9, 23)).toBe(5); // Friday
    expect(weekdayOf(2000, 1, 29)).toBe(2); // leap day, Tuesday
  });

  it('computes ISO-8601 week numbers including year boundaries', () => {
    expect(isoWeekNumber(2026, 0, 1)).toBe(1);
    expect(isoWeekNumber(2020, 11, 31)).toBe(53); // belongs to 2020's week 53
    expect(isoWeekNumber(2021, 0, 3)).toBe(53);
    expect(isoWeekNumber(2024, 11, 30)).toBe(1); // 2025-W01
    expect(isoWeekNumber(2026, 9, 23)).toBe(43);
    expect(isoWeekNumber(2028, 1, 29)).toBe(9);
  });

  it('chooses the first weekday per locale', () => {
    expect(weekStartFor('en-US')).toBe(0);
    expect(weekStartFor('ru')).toBe(1);
    expect(weekStartFor('ru-RU')).toBe(1);
    expect(weekStartFor('en-GB')).toBe(1);
    expect(weekStartFor('ja')).toBe(0);
    expect(weekStartFor('de-DE')).toBe(1);
  });

  it('orders weekday headers from the week start', () => {
    expect(weekdayOrder(0)).toEqual([0, 1, 2, 3, 4, 5, 6]);
    expect(weekdayOrder(1)).toEqual([1, 2, 3, 4, 5, 6, 0]);
  });

  it('formats date keys with zero padding', () => {
    expect(dateKey(2026, 0, 5)).toBe('2026-01-05');
    expect(dateKey(2026, 10, 23)).toBe('2026-11-23');
  });
});

describe('buildMonthGrid', () => {
  it('starts on the week start and pads with neighbouring days', () => {
    // October 2026 starts on a Thursday; a Monday-based week shows Mon 28 Sep first.
    const g = buildMonthGrid(2026, 9, 1, NO_TODAY);
    expect(g.rows[0][0]).toMatchObject({ year: 2026, month: 8, day: 28, inMonth: false });
    expect(g.rows[0][3]).toMatchObject({ month: 9, day: 1, inMonth: true, weekday: 4 });
    expect(g.rows[0].every((c) => c.weekday !== 9)).toBe(true);
    expect(g.rows[0].map((c) => c.weekday)).toEqual([1, 2, 3, 4, 5, 6, 0]);
  });

  it('uses 4 rows for February 2026 with a Sunday start (Feb 1 is a Sunday)', () => {
    const g = buildMonthGrid(2026, 1, 0, NO_TODAY);
    expect(g.rows).toHaveLength(4);
    expect(g.rows[0][0]).toMatchObject({ month: 1, day: 1, inMonth: true });
    expect(g.rows[3][6]).toMatchObject({ month: 1, day: 28, inMonth: true }); // 28 days: no trailing days
  });

  it('uses 5 rows for March 2026 (starts Sunday, 31 days)', () => {
    const g = buildMonthGrid(2026, 2, 0, NO_TODAY);
    expect(g.rows).toHaveLength(5);
    expect(g.rows[4][2]).toMatchObject({ month: 2, day: 31, inMonth: true });
    expect(g.rows[4][3]).toMatchObject({ month: 3, day: 1, inMonth: false });
  });

  it('uses 6 rows when the month spans six weeks (August 2026 with a Sunday start)', () => {
    // Aug 1 2026 is a Saturday: 6 leading days + 31 days = 37 cells, so 6 rows.
    const g = buildMonthGrid(2026, 7, 0, NO_TODAY);
    expect(g.rows).toHaveLength(6);
    expect(g.rows[5][0]).toMatchObject({ month: 7, day: 30, inMonth: true });
    expect(g.rows[5][1]).toMatchObject({ month: 7, day: 31, inMonth: true });
    expect(g.rows[5][2]).toMatchObject({ month: 8, day: 1, inMonth: false });
  });

  it('shows a leap day in a 2028 February grid that starts on a Tuesday (Monday start)', () => {
    const g = buildMonthGrid(2028, 1, 1, NO_TODAY);
    const days = g.rows.flat().filter((c) => c.inMonth).map((c) => c.day);
    expect(days).toHaveLength(29);
    expect(days[28]).toBe(29);
  });

  it('keeps every row exactly 7 cells and contiguous days', () => {
    for (let m = 0; m < 12; m++) for (const ws of [0, 1]) {
      const g = buildMonthGrid(2027, m, ws, NO_TODAY);
      expect(g.rows.length).toBeGreaterThanOrEqual(4);
      expect(g.rows.length).toBeLessThanOrEqual(6);
      const flat = g.rows.flat();
      expect(flat.every((c, i) => c.weekday === (ws + i) % 7)).toBe(true);
      for (let i = 1; i < flat.length; i++) {
        const prev = new Date(Date.UTC(flat[i - 1].year, flat[i - 1].month, flat[i - 1].day)).getTime();
        const cur = new Date(Date.UTC(flat[i].year, flat[i].month, flat[i].day)).getTime();
        expect(cur - prev).toBe(86_400_000);
      }
      expect(g.rows.every((r) => r.length === 7)).toBe(true);
      expect(g.rows[0][0].weekday).toBe(ws);
    }
  });

  it('marks exactly one today cell and carries ISO week numbers per row', () => {
    const g = buildMonthGrid(2026, 9, 1, { year: 2026, month: 9, day: 23 });
    const todays = g.rows.flat().filter((c) => c.isToday);
    expect(todays).toEqual([expect.objectContaining({ month: 9, day: 23 })]);
    expect(g.weekNumbers).toEqual([40, 41, 42, 43, 44]);
  });

  it('keeps the month when given out-of-range input', () => {
    const g = buildMonthGrid(2026, 12, 0, NO_TODAY); // month 12 = January 2027
    expect(g.year).toBe(2027);
    expect(g.month).toBe(0);
  });
});
