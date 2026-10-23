// Pure helpers for the reminders widget.
import type { Reminder } from '$lib/protocol/generated';
import { parseLocalIso, sameDay } from './weatherLogic';

export const MAX_TITLE = 200;

export function dueDate(r: Pick<Reminder, 'due'>): Date | null {
  return parseLocalIso(r.due);
}

/** Not done and the due date-time has passed. */
export function isOverdue(r: Pick<Reminder, 'due' | 'done'>, now: Date): boolean {
  if (r.done) return false;
  const d = dueDate(r);
  return !!d && d.getTime() < now.getTime();
}

/** Open reminders first (overdue, then by due time, then undated in the given order), completed ones last. Stable. */
export function sortReminders(items: readonly Reminder[]): Reminder[] {
  const rank = (r: Reminder) => (r.done ? 2 : r.due ? 0 : 1);
  return items
    .map((r, i) => ({ r, i }))
    .sort((a, b) => {
      const ra = rank(a.r), rb = rank(b.r);
      if (ra !== rb) return ra - rb;
      const da = dueDate(a.r)?.getTime() ?? 0, db = dueDate(b.r)?.getTime() ?? 0;
      return da - db || a.i - b.i;
    })
    .map((x) => x.r);
}

/** Only the reminders of one list (config.list); no list = all lists. */
export function filterByList(items: readonly Reminder[], list: string | undefined): Reminder[] {
  const l = (list ?? '').trim();
  return l ? items.filter((r) => (r.list ?? '') === l) : [...items];
}

export interface DueLabels { today: string; tomorrow: string; yesterday: string }

/** "Today, 17:00" / "Tomorrow" / "Yesterday, 9:00" / "Mon, 14:30" / "12 Oct"; the time is omitted for midnight (date-only reminders). */
export function formatDue(due: string | undefined, now: Date, locale: string, use24h: boolean, labels: DueLabels): string {
  const d = parseLocalIso(due);
  if (!d) return '';
  const hasTime = d.getHours() !== 0 || d.getMinutes() !== 0;
  const time = hasTime ? new Intl.DateTimeFormat(locale, { hour: 'numeric', minute: '2-digit', hour12: !use24h }).format(d) : '';
  const startOf = (x: Date) => new Date(x.getFullYear(), x.getMonth(), x.getDate()).getTime();
  const diffDays = Math.round((startOf(d) - startOf(now)) / 86400000);
  let day: string;
  if (sameDay(d, now)) day = labels.today;
  else if (diffDays === 1) day = labels.tomorrow;
  else if (diffDays === -1) day = labels.yesterday;
  else if (diffDays > 1 && diffDays < 7) day = new Intl.DateTimeFormat(locale, { weekday: 'short' }).format(d);
  else day = new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'short', ...(d.getFullYear() !== now.getFullYear() ? { year: 'numeric' as const } : {}) }).format(d);
  return time ? `${day}, ${time}` : day;
}

/** Validates the text typed into the add field: trimmed, non-empty, capped. */
export function cleanTitle(raw: string): string | null {
  const t = raw.replace(/\s+/g, ' ').trim().slice(0, MAX_TITLE);
  return t.length ? t : null;
}
