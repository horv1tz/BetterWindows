import { describe, expect, it } from 'vitest';
import {
  KNOWN_WEATHER_CODES, convertTemp, convertWind, describeWeather, forecastRows, formatHourLabel, formatTemp, formatWeekdayShort, isCurrentHour,
  parseLocalIso, rangeBar, upcomingHours, weatherView,
} from './weatherLogic';

describe('WMO code table', () => {
  it('maps clear sky to sun by day and moon by night', () => {
    expect(describeWeather(0, true)).toMatchObject({ icon: 'sun', mood: 'clear', labelKey: 'wx.0' });
    expect(describeWeather(0, false)).toMatchObject({ icon: 'moon', mood: 'night' });
  });
  it('partly cloudy and showers have night variants', () => {
    expect(describeWeather(2, true).icon).toBe('cloud-sun');
    expect(describeWeather(2, false).icon).toBe('cloud-moon');
    expect(describeWeather(81, true).icon).toBe('cloud-sun-rain');
    expect(describeWeather(81, false).icon).toBe('cloud-moon-rain');
  });
  it('groups precipitation and storms', () => {
    expect(describeWeather(63, true)).toMatchObject({ icon: 'cloud-rain', mood: 'rain' });
    expect(describeWeather(65, false).icon).toBe('cloud-rain-wind');
    expect(describeWeather(73, true)).toMatchObject({ icon: 'cloud-snow', mood: 'snow' });
    expect(describeWeather(77, true).icon).toBe('snowflake');
    expect(describeWeather(95, true)).toMatchObject({ icon: 'cloud-lightning', mood: 'storm' });
    expect(describeWeather(99, false).icon).toBe('cloud-hail');
    expect(describeWeather(45, true)).toMatchObject({ icon: 'cloud-fog', mood: 'fog' });
    expect(describeWeather(53, true).icon).toBe('cloud-drizzle');
  });
  it('falls back to a cloud for unknown codes (and truncates fractional codes)', () => {
    expect(describeWeather(1234, true)).toMatchObject({ icon: 'cloud', labelKey: 'wx.unknown' });
    expect(describeWeather(-1, false).labelKey).toBe('wx.unknown');
    expect(describeWeather(61.9, true).labelKey).toBe('wx.61');
  });
  it('every known code resolves to a label key of its own', () => {
    expect(KNOWN_WEATHER_CODES.length).toBeGreaterThan(25);
    for (const c of KNOWN_WEATHER_CODES) {
      expect(describeWeather(c, true).labelKey).toBe(`wx.${c}`);
      expect(describeWeather(c, false).icon).toBeTruthy();
    }
  });
});

describe('units', () => {
  it('converts Celsius to Fahrenheit', () => {
    expect(convertTemp(0, 'f')).toBe(32);
    expect(convertTemp(100, 'f')).toBe(212);
    expect(convertTemp(-40, 'f')).toBe(-40);
    expect(convertTemp(21.5, 'c')).toBe(21.5);
  });
  it('formats rounded temperatures with a degree sign and never -0', () => {
    expect(formatTemp(21.4, 'c')).toBe('21°');
    expect(formatTemp(21.6, 'c')).toBe('22°');
    expect(formatTemp(-0.2, 'c')).toBe('0°');
    expect(formatTemp(20, 'f')).toBe('68°');
    expect(formatTemp(undefined, 'c')).toBe('–°');
    expect(formatTemp(NaN, 'f')).toBe('–°');
  });
  it('wind follows the unit', () => {
    expect(convertWind(10, 'c')).toEqual({ value: 10, unit: 'km/h' });
    expect(convertWind(16.09, 'f')).toEqual({ value: 10, unit: 'mph' });
  });
});

describe('dates', () => {
  it('parses date-times and bare dates as local time', () => {
    const d = parseLocalIso('2026-10-23T14:30:00')!;
    expect([d.getFullYear(), d.getMonth(), d.getDate(), d.getHours(), d.getMinutes()]).toEqual([2026, 9, 23, 14, 30]);
    const bare = parseLocalIso('2026-10-23')!;
    expect([bare.getDate(), bare.getHours()]).toEqual([23, 0]);
    expect(parseLocalIso('')).toBeNull();
    expect(parseLocalIso(undefined)).toBeNull();
    expect(parseLocalIso('not a date')).toBeNull();
  });
  const hourly = Array.from({ length: 30 }, (_, i) => ({ time: `2026-10-23T${String(i % 24).padStart(2, '0')}:00`, tempC: i, code: 0 })).slice(0, 24);
  it('picks the hours from the current hour on', () => {
    const now = new Date(2026, 9, 23, 14, 40);
    const up = upcomingHours(hourly, now, 6);
    expect(up.map((h) => h.time.slice(11, 13))).toEqual(['14', '15', '16', '17', '18', '19']);
    expect(isCurrentHour(up[0].time, now)).toBe(true);
    expect(isCurrentHour(up[1].time, now)).toBe(false);
  });
  it('returns fewer hours near the end of the data and none after it', () => {
    expect(upcomingHours(hourly, new Date(2026, 9, 23, 22, 5), 6)).toHaveLength(2);
    expect(upcomingHours(hourly, new Date(2026, 9, 24, 3), 6)).toHaveLength(0);
  });
  it('formats hour labels per locale and clock mode', () => {
    expect(formatHourLabel('2026-10-23T05:00', 'en', true)).toBe('05');
    expect(formatHourLabel('2026-10-23T15:00', 'en', false)).toMatch(/^3\s?PM$/i);
    expect(formatHourLabel('bad', 'en', true)).toBe('');
  });
  it('labels forecast rows: today first, past days dropped', () => {
    const daily = ['2026-10-22', '2026-10-23', '2026-10-24', '2026-10-25'].map((date, i) => ({ date, highC: 10 + i, lowC: i, code: 3 }));
    const rows = forecastRows(daily, new Date(2026, 9, 23, 9), 'en', 'Today', 3);
    expect(rows.map((r) => r.label)).toEqual(['Today', formatWeekdayShort('2026-10-24', 'en'), formatWeekdayShort('2026-10-25', 'en')]);
    expect(rows[0].today).toBe(true);
    expect(rows[1].today).toBe(false);
    expect(formatWeekdayShort('2026-10-24', 'en')).toBe('Sat');
    expect(formatWeekdayShort('2026-10-24', 'ru').toLowerCase()).toBe('сб');
  });
  it('range bars stay inside 0..100', () => {
    const b = rangeBar(2, 8, 0, 10);
    expect(b.left).toBeCloseTo(20);
    expect(b.width).toBeCloseTo(60);
    const full = rangeBar(0, 10, 0, 10);
    expect(full).toEqual({ left: 0, width: 100 });
    const flat = rangeBar(5, 5, 5, 5);
    expect(flat.left + flat.width).toBeLessThanOrEqual(100);
    expect(flat.width).toBeGreaterThan(0);
  });
});

describe('weatherView state machine', () => {
  const ok = { available: true };
  it('maps outcomes to the widget state', () => {
    expect(weatherView({ loading: true, snapshot: null, failed: false, hasLocation: true })).toBe('loading');
    expect(weatherView({ loading: false, snapshot: ok, failed: false, hasLocation: true })).toBe('ok');
    expect(weatherView({ loading: false, snapshot: null, failed: true, hasLocation: true })).toBe('error');
    expect(weatherView({ loading: false, snapshot: { available: false }, failed: false, hasLocation: true })).toBe('noLocation');
    expect(weatherView({ loading: false, snapshot: { available: false, error: 'noLocation' }, failed: false, hasLocation: true })).toBe('noLocation');
    expect(weatherView({ loading: false, snapshot: { available: false, error: 'timeout' }, failed: false, hasLocation: true })).toBe('error');
    expect(weatherView({ loading: false, snapshot: { available: false, error: 'timeout' }, failed: false, hasLocation: false })).toBe('noLocation');
    expect(weatherView({ loading: false, snapshot: null, failed: false, hasLocation: false })).toBe('noLocation');
  });
  it('keeps showing stale data while a refresh is running', () => {
    expect(weatherView({ loading: true, snapshot: ok, failed: false, hasLocation: true })).toBe('ok');
  });
});
