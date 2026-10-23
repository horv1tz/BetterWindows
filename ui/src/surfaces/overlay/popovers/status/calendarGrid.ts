/**
 * Pure month-grid generator for the Calendar popover (no DOM, no Date.now): all arithmetic is done on UTC calendar days so
 * the result never depends on the machine's time zone or DST. Weekdays are 0..6 with Sunday = 0 (the JS convention).
 */

export interface DayCell {
  year: number;
  /** 0-based month. */
  month: number;
  day: number;
  /** false for the leading/trailing days that belong to a neighbouring month. */
  inMonth: boolean;
  isToday: boolean;
  /** 0 = Sunday .. 6 = Saturday. */
  weekday: number;
}

export interface MonthGrid {
  year: number;
  month: number;
  /** 4..6 rows of 7 cells, the first cell is `weekStart`. */
  rows: DayCell[][];
  /** ISO-8601 week number for each row (weeks are Monday-based, the week of the row's Thursday). */
  weekNumbers: number[];
}

export interface CalendarDate { year: number; month: number; day: number }

const MS_DAY = 86_400_000;

function utc(year: number, month: number, day: number): Date {
  const d = new Date(0);
  d.setUTCFullYear(year, month, day);
  return d;
}

export function isLeapYear(year: number): boolean {
  return (year % 4 === 0 && year % 100 !== 0) || year % 400 === 0;
}

/** Number of days in a 0-based month (handles leap years and out-of-range months by normalising through Date). */
export function daysInMonth(year: number, month: number): number {
  return utc(year, month + 1, 0).getUTCDate();
}

/** 0 = Sunday .. 6 = Saturday. */
export function weekdayOf(year: number, month: number, day: number): number {
  return utc(year, month, day).getUTCDay();
}

/**
 * First day of the week for a locale as 0 = Sunday .. 6 = Saturday. Uses Intl.Locale week info where the engine has it,
 * otherwise a small table of the regions that start the week on Sunday (the rest of the world starts on Monday).
 */
export function weekStartFor(locale: string): number {
  try {
    const loc = new Intl.Locale(locale) as Intl.Locale & { weekInfo?: { firstDay: number }; getWeekInfo?: () => { firstDay: number } };
    const info = loc.getWeekInfo?.() ?? loc.weekInfo;
    if (info && info.firstDay >= 1 && info.firstDay <= 7) return info.firstDay % 7; // Intl: 1 = Monday .. 7 = Sunday
  } catch { /* invalid tag: fall through to the table */ }
  const region = (() => {
    try { return new Intl.Locale(locale).maximize().region ?? ''; } catch { return ''; }
  })();
  const sundayRegions = ['US', 'CA', 'JP', 'IL', 'BR', 'IN', 'KR', 'TW', 'HK', 'MX', 'PH', 'ZA'];
  if (region && sundayRegions.includes(region)) return 0;
  if (!region && /^en$/i.test(locale)) return 0;
  return 1; // ru, de, fr, en-GB, ...
}

/** ISO-8601 week number (1..53) of a calendar day. */
export function isoWeekNumber(year: number, month: number, day: number): number {
  const date = utc(year, month, day);
  const dayNum = (date.getUTCDay() + 6) % 7; // Monday = 0
  // Move to the Thursday of this ISO week; its year decides the week-year.
  const thursday = new Date(date.getTime() + (3 - dayNum) * MS_DAY);
  const jan4 = utc(thursday.getUTCFullYear(), 0, 4);
  const jan4Mon = (jan4.getUTCDay() + 6) % 7;
  const week1Monday = jan4.getTime() - jan4Mon * MS_DAY;
  return Math.round((thursday.getTime() - week1Monday) / (7 * MS_DAY)) + 1;
}

/**
 * Builds the rows of a month view. The grid starts on the `weekStart` weekday on or before the 1st of the month and has
 * just enough rows (4, 5 or 6) to cover the month. `today` only sets the isToday flag.
 */
export function buildMonthGrid(year: number, month: number, weekStart: number, today: CalendarDate): MonthGrid {
  const first = utc(year, month, 1);
  const normalizedMonth = first.getUTCMonth();
  const normalizedYear = first.getUTCFullYear();
  const start = ((first.getUTCDay() - weekStart) % 7 + 7) % 7; // leading days from the previous month
  const count = daysInMonth(normalizedYear, normalizedMonth);
  const rowCount = Math.ceil((start + count) / 7);
  const gridStart = new Date(first.getTime() - start * MS_DAY);

  const rows: DayCell[][] = [];
  const weekNumbers: number[] = [];
  for (let r = 0; r < rowCount; r++) {
    const row: DayCell[] = [];
    for (let c = 0; c < 7; c++) {
      const d = new Date(gridStart.getTime() + (r * 7 + c) * MS_DAY);
      const y = d.getUTCFullYear(), m = d.getUTCMonth(), day = d.getUTCDate();
      row.push({
        year: y, month: m, day,
        inMonth: y === normalizedYear && m === normalizedMonth,
        isToday: y === today.year && m === today.month && day === today.day,
        weekday: d.getUTCDay(),
      });
    }
    // Any seven consecutive days contain a Thursday; its ISO week is the row's week number.
    const thursday = row.find((cell) => cell.weekday === 4)!;
    weekNumbers.push(isoWeekNumber(thursday.year, thursday.month, thursday.day));
    rows.push(row);
  }
  return { year: normalizedYear, month: normalizedMonth, rows, weekNumbers };
}

/** The weekday headers for a grid, starting at `weekStart`: 0..6 values in display order. */
export function weekdayOrder(weekStart: number): number[] {
  return Array.from({ length: 7 }, (_, i) => (weekStart + i) % 7);
}

/** 'YYYY-MM-DD' key of a calendar day (zero padded), used to match reminder due dates without parsing time zones. */
export function dateKey(year: number, month: number, day: number): string {
  return `${year}-${String(month + 1).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
}
