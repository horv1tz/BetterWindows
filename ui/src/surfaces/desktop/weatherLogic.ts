// Pure helpers for the weather widget: WMO weather-code table, temperature conversion, hour/day formatting.
import type { TemperatureUnit, WeatherDay, WeatherHour } from '$lib/protocol/generated';

export type WeatherIconKey =
  | 'sun' | 'moon' | 'cloud-sun' | 'cloud-moon' | 'cloud' | 'cloud-fog' | 'cloud-drizzle' | 'cloud-rain' | 'cloud-rain-wind'
  | 'cloud-sun-rain' | 'cloud-moon-rain' | 'cloud-snow' | 'snowflake' | 'cloud-lightning' | 'cloud-hail';

/** Visual mood of the card: picks the gradient. */
export type WeatherMood = 'clear' | 'night' | 'cloudy' | 'rain' | 'snow' | 'storm' | 'fog';

export interface WeatherDescription {
  /** i18n key (strings.ts) of the condition text, e.g. 'wx.61' -> "Light rain". */
  labelKey: string;
  icon: WeatherIconKey;
  mood: WeatherMood;
}

interface Entry { icon: WeatherIconKey; night?: WeatherIconKey; mood: WeatherMood }

/** WMO "weather interpretation codes" as served by Open-Meteo. */
const TABLE: Record<number, Entry> = {
  0: { icon: 'sun', night: 'moon', mood: 'clear' },
  1: { icon: 'sun', night: 'moon', mood: 'clear' },
  2: { icon: 'cloud-sun', night: 'cloud-moon', mood: 'cloudy' },
  3: { icon: 'cloud', mood: 'cloudy' },
  45: { icon: 'cloud-fog', mood: 'fog' },
  48: { icon: 'cloud-fog', mood: 'fog' },
  51: { icon: 'cloud-drizzle', mood: 'rain' },
  53: { icon: 'cloud-drizzle', mood: 'rain' },
  55: { icon: 'cloud-drizzle', mood: 'rain' },
  56: { icon: 'cloud-drizzle', mood: 'rain' },
  57: { icon: 'cloud-drizzle', mood: 'rain' },
  61: { icon: 'cloud-rain', mood: 'rain' },
  63: { icon: 'cloud-rain', mood: 'rain' },
  65: { icon: 'cloud-rain-wind', mood: 'rain' },
  66: { icon: 'cloud-rain', mood: 'rain' },
  67: { icon: 'cloud-rain-wind', mood: 'rain' },
  71: { icon: 'cloud-snow', mood: 'snow' },
  73: { icon: 'cloud-snow', mood: 'snow' },
  75: { icon: 'cloud-snow', mood: 'snow' },
  77: { icon: 'snowflake', mood: 'snow' },
  80: { icon: 'cloud-sun-rain', night: 'cloud-moon-rain', mood: 'rain' },
  81: { icon: 'cloud-sun-rain', night: 'cloud-moon-rain', mood: 'rain' },
  82: { icon: 'cloud-rain-wind', mood: 'rain' },
  85: { icon: 'cloud-snow', mood: 'snow' },
  86: { icon: 'cloud-snow', mood: 'snow' },
  95: { icon: 'cloud-lightning', mood: 'storm' },
  96: { icon: 'cloud-hail', mood: 'storm' },
  99: { icon: 'cloud-hail', mood: 'storm' },
};

/** Every WMO code the table knows (the strings must provide 'wx.<code>' for each). */
export const KNOWN_WEATHER_CODES: readonly number[] = Object.keys(TABLE).map(Number);

export function describeWeather(code: number, isDay: boolean): WeatherDescription {
  const e = TABLE[Math.trunc(code)];
  if (!e) return { labelKey: 'wx.unknown', icon: 'cloud', mood: isDay ? 'cloudy' : 'night' };
  const icon = !isDay && e.night ? e.night : e.icon;
  const mood: WeatherMood = !isDay && (e.mood === 'clear' || e.mood === 'cloudy') ? 'night' : e.mood;
  return { labelKey: `wx.${Math.trunc(code)}`, icon, mood };
}

// ---------------------------------------------------------------- units

export function convertTemp(c: number, unit: TemperatureUnit): number {
  return unit === 'f' ? (c * 9) / 5 + 32 : c;
}

/** "21°" (rounded, no unit letter, never "-0°"). */
export function formatTemp(c: number | undefined | null, unit: TemperatureUnit): string {
  if (c === undefined || c === null || !Number.isFinite(c)) return '–°';
  const v = Math.round(convertTemp(c, unit));
  return `${v === 0 ? 0 : v}°`;
}

/** Wind speed in the unit that goes with the temperature unit (F -> mph). */
export function convertWind(kph: number, unit: TemperatureUnit): { value: number; unit: 'km/h' | 'mph' } {
  return unit === 'f' ? { value: Math.round(kph * 0.621371), unit: 'mph' } : { value: Math.round(kph), unit: 'km/h' };
}

// ---------------------------------------------------------------- dates (ISO-8601 local date-times from the host)

/** Parses "2026-10-23T14:00[:00]" or "2026-10-23" as LOCAL time (a bare date string would otherwise be read as UTC). */
export function parseLocalIso(s: string | undefined | null): Date | null {
  if (!s) return null;
  const m = /^(\d{4})-(\d{2})-(\d{2})(?:[T ](\d{2}):(\d{2})(?::(\d{2}))?)?/.exec(s);
  if (!m) { const d = new Date(s); return Number.isNaN(d.getTime()) ? null : d; }
  const d = new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]), Number(m[4] ?? 0), Number(m[5] ?? 0), Number(m[6] ?? 0));
  return Number.isNaN(d.getTime()) ? null : d;
}

const startOfHour = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate(), d.getHours());

/** The next `count` hourly entries starting with the current hour (the host may send the whole day, starting at midnight). */
export function upcomingHours(hourly: readonly WeatherHour[], now: Date, count: number): WeatherHour[] {
  const from = startOfHour(now).getTime();
  const out: WeatherHour[] = [];
  for (const h of hourly) {
    const t = parseLocalIso(h.time)?.getTime();
    if (t !== undefined && t >= from) out.push(h);
    if (out.length >= count) break;
  }
  return out;
}

export function isCurrentHour(time: string, now: Date): boolean {
  return parseLocalIso(time)?.getTime() === startOfHour(now).getTime();
}

/** "14:00" in 24 h mode, "2 PM" otherwise; locale decides the exact form. */
export function formatHourLabel(time: string, locale: string, use24h: boolean): string {
  const d = parseLocalIso(time);
  if (!d) return '';
  if (use24h) return `${String(d.getHours()).padStart(2, '0')}`;
  return new Intl.DateTimeFormat(locale, { hour: 'numeric', hour12: true }).format(d).replace(/\s/g, ' ');
}

export function sameDay(a: Date, b: Date): boolean {
  return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();
}

export function formatWeekdayShort(date: string, locale: string): string {
  const d = parseLocalIso(date);
  return d ? new Intl.DateTimeFormat(locale, { weekday: 'short' }).format(d) : '';
}

export interface DayRow { label: string; today: boolean; highC: number; lowC: number; code: number }

/** Forecast rows starting at today (past days dropped); the first row is labelled with `todayLabel`. */
export function forecastRows(daily: readonly WeatherDay[], now: Date, locale: string, todayLabel: string, count: number): DayRow[] {
  const rows: DayRow[] = [];
  for (const d of daily) {
    const dt = parseLocalIso(d.date);
    if (!dt) continue;
    const startToday = new Date(now.getFullYear(), now.getMonth(), now.getDate());
    if (dt.getTime() < startToday.getTime()) continue;
    const today = sameDay(dt, now);
    rows.push({ label: today ? todayLabel : formatWeekdayShort(d.date, locale), today, highC: d.highC, lowC: d.lowC, code: d.code });
    if (rows.length >= count) break;
  }
  return rows;
}

/** Position of a day's low..high bar inside the overall min..max span, in percent. */
export function rangeBar(low: number, high: number, min: number, max: number): { left: number; width: number } {
  const span = Math.max(1e-6, max - min);
  const left = ((low - min) / span) * 100;
  const width = Math.max(6, ((high - low) / span) * 100);
  return { left: Math.min(left, 100 - width), width };
}

export type WeatherView = 'loading' | 'ok' | 'noLocation' | 'error';

/**
 * Which of the widget's states a weather.get outcome maps to. A missing/empty error on an unavailable snapshot means "no location configured".
 */
export function weatherView(input: { loading: boolean; snapshot: { available: boolean; error?: string } | null; failed: boolean; hasLocation: boolean }): WeatherView {
  if (input.failed) return 'error';
  if (!input.snapshot) return input.loading ? 'loading' : input.hasLocation ? 'loading' : 'noLocation';
  if (input.snapshot.available) return 'ok';
  const err = (input.snapshot.error ?? '').trim();
  if (!err || err === 'noLocation' || !input.hasLocation) return 'noLocation';
  return 'error';
}
