import type { BatteryState } from '$lib/protocol/generated';

/** How the battery glyph looks for a given BatteryState. Pure so it can be unit tested without a DOM. */
export type BatteryTone = 'normal' | 'low' | 'critical' | 'charging';

export interface BatteryVisual {
  /** False when there is no battery (desktop PC): the item is not rendered at all. */
  visible: boolean;
  /** Integer 0..100, clamped. */
  percent: number;
  /** Portion of the cell that is filled, 0..1 (never invisible for a non-empty battery). */
  fill: number;
  tone: BatteryTone;
  /** Lightning bolt on top of the cell (actively charging). */
  bolt: boolean;
  /** Mains connected but not charging (full): shows a plug mark instead of the bolt. */
  plugged: boolean;
}

export const LOW_PERCENT = 20;
export const CRITICAL_PERCENT = 10;
/** Smallest fill (of the cell) drawn for a non-empty battery so 1..5 % still reads as "almost empty", not "no battery". */
const MIN_FILL = 0.08;

export function batteryVisual(b: BatteryState | null | undefined): BatteryVisual {
  if (!b || !b.present) return { visible: false, percent: 0, fill: 0, tone: 'normal', bolt: false, plugged: false };
  const raw = Number.isFinite(b.percent) ? b.percent : 0;
  const percent = Math.min(100, Math.max(0, Math.round(raw)));
  const bolt = b.charging;
  const plugged = b.acOnline && !b.charging;
  // Charging wins over "low": a battery on its way up is not alarming. Plugged-in-and-full is plain.
  const tone: BatteryTone = bolt
    ? 'charging'
    : plugged ? 'normal'
    : percent <= CRITICAL_PERCENT ? 'critical'
    : percent <= LOW_PERCENT ? 'low'
    : 'normal';
  const fill = percent === 0 ? 0 : Math.max(MIN_FILL, percent / 100);
  return { visible: true, percent, fill, tone, bolt, plugged };
}
