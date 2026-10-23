// Pure helpers for the calendar widget (month grid, ISO week numbers, locale week start).
export interface CalDay { day: number; inMonth: boolean; today: boolean; weekend: boolean; key: string }
export interface MonthGrid { weeks: CalDay[][]; weekNumbers: number[] }

const pad = (n: number) => String(n).padStart(2, '0');
export const dayKey = (y: number, m: number, d: number) => `${y}-${pad(m + 1)}-${pad(d)}`;

/** First day of the week for a locale: 0 = Sunday, 1 = Monday. Uses Intl week info where the engine has it, else Monday for everything but English. */
export function defaultWeekStart(locale: string): number {
  try {
    const info = (new Intl.Locale(locale) as unknown as { getWeekInfo?: () => { firstDay: number }; weekInfo?: { firstDay: number } });
    const wi = info.getWeekInfo?.() ?? info.weekInfo;
    if (wi) return wi.firstDay % 7; // Intl: 1 = Monday ... 7 = Sunday
  } catch { /* fall through */ }
  return locale.toLowerCase().startsWith('en') ? 0 : 1;
}

/** ISO-8601 week number (weeks start on Monday, week 1 contains the year's first Thursday). */
export function isoWeekNumber(y: number, m: number, d: number): number {
  const t = new Date(Date.UTC(y, m, d));
  const dow = t.getUTCDay() || 7;
  t.setUTCDate(t.getUTCDate() + 4 - dow);
  const yearStart = Date.UTC(t.getUTCFullYear(), 0, 1);
  return Math.ceil(((t.getTime() - yearStart) / 86400000 + 1) / 7);
}

/** All weeks that touch the month (4-6 rows), padded with the neighbouring months' days. `weekStart`: 0 = Sunday .. 6 = Saturday. */
export function monthGrid(year: number, month: number, weekStart: number, today: { year: number; month: number; day: number }): MonthGrid {
  const first = new Date(year, month, 1, 12);
  const lead = (first.getDay() - weekStart + 7) % 7;
  const daysInMonth = new Date(year, month + 1, 0).getDate();
  const rows = Math.ceil((lead + daysInMonth) / 7);
  const weeks: CalDay[][] = [];
  const weekNumbers: number[] = [];
  for (let r = 0; r < rows; r++) {
    const week: CalDay[] = [];
    for (let c = 0; c < 7; c++) {
      const d = new Date(year, month, 1 - lead + r * 7 + c, 12);
      const y = d.getFullYear(), mo = d.getMonth(), da = d.getDate();
      week.push({
        day: da, inMonth: mo === month && y === year,
        today: y === today.year && mo === today.month && da === today.day,
        weekend: d.getDay() === 0 || d.getDay() === 6,
        key: dayKey(y, mo, da),
      });
    }
    weeks.push(week);
    const mid = new Date(year, month, 1 - lead + r * 7 + 3, 12); // Thursday-ish day of the row decides the ISO week
    weekNumbers.push(isoWeekNumber(mid.getFullYear(), mid.getMonth(), mid.getDate()));
  }
  return { weeks, weekNumbers };
}

/** Narrow weekday names in display order, e.g. ['M','T','W',...] or ['П','В','С',...]. */
export function weekdayHeaders(locale: string, weekStart: number, style: 'narrow' | 'short' = 'narrow'): string[] {
  const f = new Intl.DateTimeFormat(locale, { weekday: style });
  // 2023-01-01 was a Sunday
  return Array.from({ length: 7 }, (_, i) => f.format(new Date(2023, 0, 1 + ((weekStart + i) % 7), 12)));
}

/** Milliseconds until the next local midnight (+1s), to roll "today" over. */
export function msUntilMidnight(now: Date): number {
  const next = new Date(now.getFullYear(), now.getMonth(), now.getDate() + 1, 0, 0, 1);
  return Math.max(1000, next.getTime() - now.getTime());
}
