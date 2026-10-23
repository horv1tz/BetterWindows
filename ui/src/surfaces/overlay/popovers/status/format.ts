/**
 * Pure formatters and classifiers for the status popovers. They take the translator and the locale explicitly so unit tests
 * can run every language without touching the global UI locale.
 */
import type { BatteryState, NetworkState } from '$lib/protocol/generated';
import { plural } from '$lib/i18n.svelte';

export type Translate = (key: string, params?: Record<string, string | number>) => string;

const clamp01 = (v: number) => (Number.isFinite(v) ? Math.min(1, Math.max(0, v)) : 0);

/** Whole percent of a 0..1 level (NaN and out-of-range values are clamped). */
export function percentOf(level: number): number {
  return Math.round(clamp01(level) * 100);
}

/** Localised count of a unit: "1 hour", "2 hours", "5 часов"... (CLDR plural rules of `locale`). */
function countOf(n: number, unit: 'hour' | 'minute', t: Translate, locale: string): string {
  const k = (form: 'one' | 'few' | 'many' | 'other') => t(`dur.${unit}.${form}`, { n });
  return plural(n, { one: k('one'), few: k('few'), many: k('many'), other: k('other') }, locale);
}

/**
 * Remaining time as a human string: "3 hours 34 minutes", "45 minutes", "less than a minute".
 * Non-finite or negative input yields the 'bat.calculating' text, which is what the host reports when it has no estimate.
 */
export function formatRemaining(minutes: number | undefined, t: Translate, locale: string): string {
  if (minutes === undefined || !Number.isFinite(minutes) || minutes < 0) return t('bat.calculating');
  const total = Math.round(minutes);
  if (total < 1) return t('dur.lessThanMinute');
  const h = Math.floor(total / 60);
  const m = total % 60;
  if (h === 0) return countOf(m, 'minute', t, locale);
  if (m === 0) return countOf(h, 'hour', t, locale);
  return `${countOf(h, 'hour', t, locale)} ${countOf(m, 'minute', t, locale)}`;
}

export type BatteryStatus = 'none' | 'charging' | 'full' | 'pluggedIn' | 'onBattery';

/** Which headline the battery popover shows. `full` = on AC power at 100 %. */
export function batteryStatus(b: Pick<BatteryState, 'present' | 'charging' | 'acOnline' | 'percent'>): BatteryStatus {
  if (!b.present) return 'none';
  if (b.charging) return 'charging';
  if (b.acOnline && b.percent >= 100) return 'full';
  if (b.acOnline) return 'pluggedIn';
  return 'onBattery';
}

/** 'low' when discharging at 20 % or below: the popover then shows the warning colour. */
export function batteryTier(percent: number, charging: boolean): 'low' | 'normal' {
  return !charging && Number.isFinite(percent) && percent <= 20 ? 'low' : 'normal';
}

/** Battery popover sub-line: "3 hours 34 minutes remaining" / "... until full" / "" when nothing applies. */
export function batteryTimeLine(b: Pick<BatteryState, 'present' | 'charging' | 'acOnline' | 'percent' | 'minutesRemaining'>, t: Translate, locale: string): string {
  const status = batteryStatus(b);
  if (status === 'none' || status === 'full' || status === 'pluggedIn') return '';
  if (b.minutesRemaining === undefined) return t('bat.calculating');
  const time = formatRemaining(b.minutesRemaining, t, locale);
  return status === 'charging' ? t('bat.untilFull', { time }) : t('bat.remaining', { time });
}

export type VolumeTier = 'muted' | 'low' | 'medium' | 'high';

/** Speaker glyph thresholds: muted (or silent) < 1/3 <= low < 2/3 <= medium <= high. */
export function volumeTier(volume: number, muted: boolean): VolumeTier {
  if (muted || !(volume > 0)) return 'muted';
  const v = clamp01(volume);
  if (v < 1 / 3) return 'low';
  if (v < 2 / 3) return 'medium';
  return 'high';
}

export type NetworkLabel = 'wifi' | 'ethernet' | 'cellular' | 'other' | 'offline';

/** Which connection name the network popover shows. */
export function networkLabel(n: Pick<NetworkState, 'kind' | 'connected'>): NetworkLabel {
  if (n.kind === 'none' || !n.connected) return 'offline';
  return n.kind === 'wifi' || n.kind === 'ethernet' || n.kind === 'cellular' ? n.kind : 'other';
}

/** Signal bars 0..4 (fractional or out-of-range input is clamped). */
export function signalBars(bars: number): number {
  return Number.isFinite(bars) ? Math.min(4, Math.max(0, Math.round(bars))) : 0;
}

/** Bluetooth battery chip text (the translation carries the % sign); null when the device reports no battery. */
export function batteryChip(percent: number | undefined, t: Translate): string | null {
  if (percent === undefined || !Number.isFinite(percent)) return null;
  return t('bt.battery', { n: Math.round(Math.min(100, Math.max(0, percent))) });
}
