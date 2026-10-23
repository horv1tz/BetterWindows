import { describe, expect, it } from 'vitest';
import type { Reminder } from '$lib/protocol/generated';
import { cleanTitle, filterByList, formatDue, isOverdue, sortReminders } from './remindersLogic';

const r = (id: string, o: Partial<Reminder> = {}): Reminder => ({ id, title: id, done: false, ...o });
const now = new Date(2026, 9, 23, 12, 0);
const labels = { today: 'Today', tomorrow: 'Tomorrow', yesterday: 'Yesterday' };

describe('isOverdue', () => {
  it('only open reminders with a past due time are overdue', () => {
    expect(isOverdue(r('a', { due: '2026-10-23T11:59' }), now)).toBe(true);
    expect(isOverdue(r('a', { due: '2026-10-23T12:01' }), now)).toBe(false);
    expect(isOverdue(r('a', { due: '2026-10-22T08:00', done: true }), now)).toBe(false);
    expect(isOverdue(r('a'), now)).toBe(false);
    expect(isOverdue(r('a', { due: 'garbage' }), now)).toBe(false);
  });
});

describe('sortReminders', () => {
  it('orders open by due, undated after dated, completed last, stable', () => {
    const items = [
      r('undated1'),
      r('later', { due: '2026-10-25T10:00' }),
      r('done', { done: true, due: '2026-10-20T10:00' }),
      r('overdue', { due: '2026-10-21T09:00' }),
      r('undated2'),
      r('soon', { due: '2026-10-23T15:00' }),
    ];
    expect(sortReminders(items).map((x) => x.id)).toEqual(['overdue', 'soon', 'later', 'undated1', 'undated2', 'done']);
  });
  it('does not mutate the input', () => {
    const items = [r('b', { due: '2026-10-25T10:00' }), r('a', { due: '2026-10-24T10:00' })];
    sortReminders(items);
    expect(items.map((x) => x.id)).toEqual(['b', 'a']);
  });
});

describe('filterByList', () => {
  const items = [r('a', { list: 'Work' }), r('b', { list: 'Home' }), r('c')];
  it('filters by list name or keeps all', () => {
    expect(filterByList(items, 'Work').map((x) => x.id)).toEqual(['a']);
    expect(filterByList(items, undefined)).toHaveLength(3);
    expect(filterByList(items, '  ')).toHaveLength(3);
  });
});

describe('formatDue', () => {
  it('uses relative day names with the time', () => {
    expect(formatDue('2026-10-23T17:00', now, 'en', true, labels)).toBe('Today, 17:00');
    expect(formatDue('2026-10-24T09:30', now, 'en', true, labels)).toBe('Tomorrow, 09:30');
    expect(formatDue('2026-10-22T09:00', now, 'en', true, labels)).toBe('Yesterday, 09:00');
  });
  it('omits midnight times (date-only reminders)', () => {
    expect(formatDue('2026-10-24T00:00', now, 'en', true, labels)).toBe('Tomorrow');
    expect(formatDue('2026-10-24', now, 'en', true, labels)).toBe('Tomorrow');
  });
  it('shows weekday within a week, short date beyond', () => {
    expect(formatDue('2026-10-27T10:00', now, 'en', true, labels)).toMatch(/^Tue, 10:00$/);
    expect(formatDue('2026-11-12T10:00', now, 'en', true, labels)).toMatch(/Nov/);
    expect(formatDue('2027-01-05T10:00', now, 'en', true, labels)).toMatch(/2027/);
  });
  it('12 h mode', () => {
    expect(formatDue('2026-10-23T17:05', now, 'en', false, labels)).toMatch(/^Today, 5:05\s?PM$/);
  });
  it('empty for missing/invalid', () => {
    expect(formatDue(undefined, now, 'en', true, labels)).toBe('');
    expect(formatDue('x', now, 'en', true, labels)).toBe('');
  });
});

describe('cleanTitle', () => {
  it('trims, collapses whitespace and caps the length', () => {
    expect(cleanTitle('  buy   milk \n')).toBe('buy milk');
    expect(cleanTitle('   ')).toBeNull();
    expect(cleanTitle('x'.repeat(500))).toHaveLength(200);
  });
});
