// Pure helpers for the clock widget.
export interface ClockParts {
  hours: number; minutes: number; seconds: number; ms: number;
  /** 0 = Sunday */
  weekday: number;
  day: number;
  /** 0-based */
  month: number;
  year: number;
}

const formatters = new Map<string, Intl.DateTimeFormat>();

export function isValidTimeZone(tz: string | undefined | null): tz is string {
  if (!tz) return false;
  try { new Intl.DateTimeFormat('en-US', { timeZone: tz }); return true; } catch { return false; }
}

function zoneFormatter(tz: string): Intl.DateTimeFormat {
  let f = formatters.get(tz);
  if (!f) {
    f = new Intl.DateTimeFormat('en-US', { timeZone: tz, hourCycle: 'h23', weekday: 'short', year: 'numeric', month: 'numeric', day: 'numeric', hour: 'numeric', minute: 'numeric', second: 'numeric' });
    formatters.set(tz, f);
  }
  return f;
}

const WEEKDAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

/** Wall-clock parts of `date` in `timeZone` (IANA name; invalid or absent = the system zone). */
export function clockParts(date: Date, timeZone?: string): ClockParts {
  if (!isValidTimeZone(timeZone)) {
    return { hours: date.getHours(), minutes: date.getMinutes(), seconds: date.getSeconds(), ms: date.getMilliseconds(), weekday: date.getDay(), day: date.getDate(), month: date.getMonth(), year: date.getFullYear() };
  }
  const get: Record<string, string> = {};
  for (const p of zoneFormatter(timeZone).formatToParts(date)) get[p.type] = p.value;
  return {
    hours: Number(get.hour) % 24, minutes: Number(get.minute), seconds: Number(get.second), ms: date.getMilliseconds(),
    weekday: Math.max(0, WEEKDAYS.indexOf(get.weekday)), day: Number(get.day), month: Number(get.month) - 1, year: Number(get.year),
  };
}

export interface HandAngles { hour: number; minute: number; second: number }

/** Degrees clockwise from 12 o'clock. `smooth` includes the sub-unit fraction (sweeping hands), otherwise hands tick in whole units. */
export function handAngles(p: ClockParts, smooth: boolean): HandAngles {
  const sec = smooth ? p.seconds + p.ms / 1000 : p.seconds;
  const min = smooth ? p.minutes + sec / 60 : p.minutes + Math.floor(sec) / 60;
  const hour = (p.hours % 12) + min / 60;
  return { hour: hour * 30, minute: min * 6, second: sec * 6 };
}

/** "09:05" (24 h) or "9:05" + "AM" (12 h). */
export function formatDigital(p: ClockParts, use24h: boolean, withSeconds: boolean): { time: string; suffix: string } {
  const two = (n: number) => String(n).padStart(2, '0');
  const sec = withSeconds ? `:${two(p.seconds)}` : '';
  if (use24h) return { time: `${two(p.hours)}:${two(p.minutes)}${sec}`, suffix: '' };
  const h = p.hours % 12 === 0 ? 12 : p.hours % 12;
  return { time: `${h}:${two(p.minutes)}${sec}`, suffix: p.hours < 12 ? 'AM' : 'PM' };
}

/** Milliseconds until the next whole second / minute boundary (+ a few ms so the tick lands after it). */
export function msToNextTick(now: number, withSeconds: boolean): number {
  const unit = withSeconds ? 1000 : 60000;
  return unit - (now % unit) + 5;
}

/** The city part of an IANA zone, "Europe/Moscow" -> "Moscow", "America/New_York" -> "New York". */
export function zoneCity(tz: string | undefined): string {
  if (!tz) return '';
  return (tz.split('/').pop() ?? tz).replace(/_/g, ' ');
}
