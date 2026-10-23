import { describe, expect, it } from 'vitest';
import { dayKey, defaultWeekStart, isoWeekNumber, monthGrid, msUntilMidnight, weekdayHeaders } from './calendarLogic';

describe('monthGrid', () => {
  it('Oct 2026 with Monday start: Oct 1 is a Thursday, 5 rows', () => {
    const g = monthGrid(2026, 9, 1, { year: 2026, month: 9, day: 23 });
    expect(g.weeks).toHaveLength(5);
    expect(g.weeks[0].map((d) => d.day)).toEqual([28, 29, 30, 1, 2, 3, 4]);
    expect(g.weeks[0].map((d) => d.inMonth)).toEqual([false, false, false, true, true, true, true]);
    expect(g.weeks[4].map((d) => d.day)).toEqual([26, 27, 28, 29, 30, 31, 1]);
    const today = g.weeks.flat().filter((d) => d.today);
    expect(today).toHaveLength(1);
    expect(today[0].key).toBe('2026-10-23');
  });
  it('Sunday start shifts the first row', () => {
    const g = monthGrid(2026, 9, 0, { year: 2020, month: 0, day: 1 });
    expect(g.weeks[0].map((d) => d.day)).toEqual([27, 28, 29, 30, 1, 2, 3]);
    expect(g.weeks.flat().some((d) => d.today)).toBe(false);
  });
  it('a month that fills exactly four rows (Feb 2027, Monday start) has four rows', () => {
    const g = monthGrid(2027, 1, 1, { year: 2027, month: 1, day: 1 });
    expect(g.weeks).toHaveLength(4);
    expect(g.weeks.flat().every((d) => d.inMonth)).toBe(true);
  });
  it('a month can need six rows', () => {
    // Aug 2026 starts on Saturday; with a Monday start: 5 lead + 31 days = 36 -> 6 rows
    expect(monthGrid(2026, 7, 1, { year: 0, month: 0, day: 0 }).weeks).toHaveLength(6);
  });
  it('marks weekends and spans the year boundary', () => {
    const g = monthGrid(2026, 11, 1, { year: 2026, month: 11, day: 31 });
    expect(g.weeks[0].map((d) => d.weekend)).toEqual([false, false, false, false, false, true, true]);
    const last = g.weeks[g.weeks.length - 1];
    expect(last.some((d) => d.key === '2027-01-03')).toBe(true);
  });
  it('week numbers are ISO weeks', () => {
    const g = monthGrid(2026, 0, 1, { year: 2026, month: 0, day: 1 });
    expect(g.weekNumbers[0]).toBe(1); // Jan 1 2026 is a Thursday -> ISO week 1
    expect(g.weekNumbers).toEqual([1, 2, 3, 4, 5]);
  });
});

describe('isoWeekNumber', () => {
  it('handles year edges', () => {
    expect(isoWeekNumber(2026, 0, 1)).toBe(1);
    expect(isoWeekNumber(2021, 0, 1)).toBe(53); // belongs to the previous year's last week
    expect(isoWeekNumber(2024, 11, 30)).toBe(1); // belongs to the next year's week 1
    expect(isoWeekNumber(2026, 9, 23)).toBe(43);
  });
});

describe('locale helpers', () => {
  it('week start: Monday for Russian, Sunday for US English', () => {
    expect(defaultWeekStart('ru')).toBe(1);
    expect(defaultWeekStart('en')).toBe(0);
  });
  it('weekday headers are rotated', () => {
    expect(weekdayHeaders('en', 1)).toEqual(['M', 'T', 'W', 'T', 'F', 'S', 'S']);
    expect(weekdayHeaders('en', 0)[0]).toBe('S');
    expect(weekdayHeaders('ru', 1)[0].toUpperCase()).toBe('П');
  });
  it('dayKey pads', () => { expect(dayKey(2026, 0, 5)).toBe('2026-01-05'); });
  it('computes the delay to midnight', () => {
    expect(msUntilMidnight(new Date(2026, 5, 1, 23, 59, 0))).toBe(61000);
    expect(msUntilMidnight(new Date(2026, 5, 1, 0, 0, 0))).toBe(86401000);
  });
});
