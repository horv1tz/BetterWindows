import { afterEach, describe, expect, it } from 'vitest';
import { i18n } from '$lib/i18n.svelte';
import { t } from './strings';
import {
  batteryStatus, batteryTier, batteryTimeLine, batteryChip, formatRemaining, networkLabel, percentOf, signalBars, volumeTier,
} from './format';

/** Switch the UI language for the duration of one test (the translator reads the global locale). */
function inLocale(locale: 'en' | 'ru') {
  i18n.set(locale);
  return { t: (key: string, params?: Record<string, string | number>) => t(key, params), locale };
}
afterEach(() => i18n.set('en'));

describe('formatRemaining', () => {
  it('formats hours and minutes in English with singular and plural forms', () => {
    const en = inLocale('en');
    expect(formatRemaining(214, en.t, 'en')).toBe('3 hours 34 minutes');
    expect(formatRemaining(60, en.t, 'en')).toBe('1 hour');
    expect(formatRemaining(61, en.t, 'en')).toBe('1 hour 1 minute');
    expect(formatRemaining(45, en.t, 'en')).toBe('45 minutes');
    expect(formatRemaining(1, en.t, 'en')).toBe('1 minute');
  });

  it('uses the Russian plural categories (1 / 2-4 / 5+ / 11-14 exceptions)', () => {
    const ru = inLocale('ru');
    expect(formatRemaining(60, ru.t, 'ru')).toBe('1 час');
    expect(formatRemaining(120, ru.t, 'ru')).toBe('2 часа');
    expect(formatRemaining(300, ru.t, 'ru')).toBe('5 часов');
    expect(formatRemaining(11 * 60, ru.t, 'ru')).toBe('11 часов'); // 11..14 are "many", not "few"
    expect(formatRemaining(21 * 60 + 22, ru.t, 'ru')).toBe('21 час 22 минуты');
    expect(formatRemaining(5, ru.t, 'ru')).toBe('5 минут');
    expect(formatRemaining(2, ru.t, 'ru')).toBe('2 минуты');
  });

  it('rounds to whole minutes and treats sub-minute values as less than a minute', () => {
    const en = inLocale('en');
    expect(formatRemaining(0.2, en.t, 'en')).toBe('less than a minute');
    expect(formatRemaining(0, en.t, 'en')).toBe('less than a minute');
    expect(formatRemaining(59.6, en.t, 'en')).toBe('1 hour');
  });

  it('falls back to the estimating text for missing or invalid estimates', () => {
    const en = inLocale('en');
    expect(formatRemaining(undefined, en.t, 'en')).toBe('Estimating time…');
    expect(formatRemaining(Number.NaN, en.t, 'en')).toBe('Estimating time…');
    expect(formatRemaining(-5, en.t, 'en')).toBe('Estimating time…');
    expect(formatRemaining(Number.POSITIVE_INFINITY, en.t, 'en')).toBe('Estimating time…');
  });
});

describe('battery', () => {
  const base = { present: true, charging: false, acOnline: false, percent: 76, minutesRemaining: 214 };

  it('classifies the battery headline', () => {
    expect(batteryStatus({ ...base, present: false })).toBe('none');
    expect(batteryStatus({ ...base, charging: true, acOnline: true })).toBe('charging');
    expect(batteryStatus({ ...base, acOnline: true, percent: 100 })).toBe('full');
    expect(batteryStatus({ ...base, acOnline: true, percent: 80 })).toBe('pluggedIn');
    expect(batteryStatus(base)).toBe('onBattery');
  });

  it('flags low battery only while discharging at 20 % or below', () => {
    expect(batteryTier(20, false)).toBe('low');
    expect(batteryTier(21, false)).toBe('normal');
    expect(batteryTier(5, true)).toBe('normal');
    expect(batteryTier(Number.NaN, false)).toBe('normal');
  });

  it('writes the time line per state', () => {
    const en = inLocale('en');
    expect(batteryTimeLine(base, en.t, 'en')).toBe('3 hours 34 minutes remaining');
    expect(batteryTimeLine({ ...base, charging: true, acOnline: true, minutesRemaining: 40 }, en.t, 'en')).toBe('40 minutes until full');
    expect(batteryTimeLine({ ...base, acOnline: true }, en.t, 'en')).toBe(''); // plugged in: no estimate line
    expect(batteryTimeLine({ ...base, present: false }, en.t, 'en')).toBe('');
    expect(batteryTimeLine({ ...base, minutesRemaining: undefined }, en.t, 'en')).toBe('Estimating time…');
    const ru = inLocale('ru');
    expect(batteryTimeLine(base, ru.t, 'ru')).toBe('Осталось: 3 часа 34 минуты');
  });
});

describe('volume, network, signal and percent helpers', () => {
  it('maps volume to the speaker glyph tiers', () => {
    expect(volumeTier(0.5, true)).toBe('muted');
    expect(volumeTier(0, false)).toBe('muted');
    expect(volumeTier(0.2, false)).toBe('low');
    expect(volumeTier(1 / 3, false)).toBe('medium'); // boundary belongs to the upper tier
    expect(volumeTier(0.5, false)).toBe('medium');
    expect(volumeTier(2 / 3, false)).toBe('high');
    expect(volumeTier(7, false)).toBe('high'); // out of range clamps
    expect(volumeTier(Number.NaN, false)).toBe('muted');
  });

  it('names the connection', () => {
    expect(networkLabel({ kind: 'wifi', connected: true })).toBe('wifi');
    expect(networkLabel({ kind: 'wifi', connected: false })).toBe('offline');
    expect(networkLabel({ kind: 'none', connected: true })).toBe('offline');
    expect(networkLabel({ kind: 'ethernet', connected: true })).toBe('ethernet');
    expect(networkLabel({ kind: 'other', connected: true })).toBe('other');
  });

  it('clamps signal bars and percentages', () => {
    expect(signalBars(3.6)).toBe(4);
    expect(signalBars(-1)).toBe(0);
    expect(signalBars(Number.NaN)).toBe(0);
    expect(percentOf(0.755)).toBe(76);
    expect(percentOf(2)).toBe(100);
    expect(percentOf(-1)).toBe(0);
  });

  it('builds the Bluetooth battery chip only when a value exists', () => {
    const en = inLocale('en');
    expect(batteryChip(82, en.t)).toBe('Battery 82%');
    expect(batteryChip(150, en.t)).toBe('Battery 100%');
    expect(batteryChip(undefined, en.t)).toBeNull();
    expect(batteryChip(Number.NaN, en.t)).toBeNull();
  });
});
