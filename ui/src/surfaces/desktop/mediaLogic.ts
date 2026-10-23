// Pure helpers for the media and battery widgets.
import type { MediaState } from '$lib/protocol/generated';

/** m:ss (or h:mm:ss) */
export function formatClock(sec: number): string {
  if (!Number.isFinite(sec) || sec < 0) return '0:00';
  const s = Math.floor(sec);
  const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), r = s % 60;
  return h > 0 ? `${h}:${String(m).padStart(2, '0')}:${String(r).padStart(2, '0')}` : `${m}:${String(r).padStart(2, '0')}`;
}

/** The host sends the position only on change; while playing, extrapolate from the moment the state arrived. */
export function estimatePosition(state: Pick<MediaState, 'playing' | 'positionSec' | 'durationSec'>, receivedAtMs: number, nowMs: number): number {
  const base = Math.max(0, state.positionSec || 0);
  const pos = state.playing ? base + Math.max(0, nowMs - receivedAtMs) / 1000 : base;
  return state.durationSec > 0 ? Math.min(pos, state.durationSec) : pos;
}

export function progressFraction(pos: number, duration: number): number {
  return duration > 0 ? Math.min(1, Math.max(0, pos / duration)) : 0;
}

// ------------------------------------------------------------------ battery

export type DeviceKind = 'headphones' | 'mouse' | 'keyboard' | 'phone' | 'watch' | 'gamepad' | 'speaker' | 'other';

export function deviceKind(name: string): DeviceKind {
  const n = name.toLowerCase();
  if (/head(phone|set)|buds|airpods|\bwh-|\bwf-|earbud|earphone/.test(n)) return 'headphones';
  if (/mouse|\bmx\b|trackpad|trackball/.test(n)) return 'mouse';
  if (/keyboard|keychron|\bk\d/.test(n)) return 'keyboard';
  if (/phone|galaxy|pixel|iphone/.test(n)) return 'phone';
  if (/watch|band/.test(n)) return 'watch';
  if (/controller|gamepad|dualsense|xbox|joy-?con/.test(n)) return 'gamepad';
  if (/speaker|soundbar|boombox|jbl|\bsrs-/.test(n)) return 'speaker';
  return 'other';
}

export type BatteryTone = 'good' | 'low' | 'critical' | 'charging';

export function batteryTone(percent: number, charging: boolean): BatteryTone {
  if (charging) return 'charging';
  if (percent <= 10) return 'critical';
  if (percent <= 20) return 'low';
  return 'good';
}

/** Connected Bluetooth devices that report a battery level, highest-priority (lowest charge) first. */
export function devicesWithBattery<T extends { connected: boolean; batteryPercent?: number }>(devices: readonly T[]): T[] {
  return devices
    .filter((d) => d.connected && typeof d.batteryPercent === 'number' && Number.isFinite(d.batteryPercent))
    .sort((a, b) => (a.batteryPercent as number) - (b.batteryPercent as number));
}

/** "3 h 34 min" parts for the remaining-time line. */
export function splitMinutes(min: number): { h: number; m: number } {
  const t = Math.max(0, Math.round(min));
  return { h: Math.floor(t / 60), m: t % 60 };
}
