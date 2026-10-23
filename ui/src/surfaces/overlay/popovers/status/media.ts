/**
 * Now Playing position logic: the host sends positionSec on every media.state, the popover extrapolates it locally while
 * playing (so the scrubber moves smoothly without a host event per frame) and resyncs on every new state.
 */

export interface PlaybackSnapshot {
  positionSec: number;
  durationSec: number;
  playing: boolean;
}

/** Position estimate `elapsedMs` after the snapshot was received. Clamped to [0, duration] (duration 0 = unknown: no upper bound). */
export function extrapolatePosition(s: PlaybackSnapshot, elapsedMs: number): number {
  const base = Number.isFinite(s.positionSec) ? Math.max(0, s.positionSec) : 0;
  const elapsed = s.playing && Number.isFinite(elapsedMs) && elapsedMs > 0 ? elapsedMs / 1000 : 0;
  const pos = base + elapsed;
  if (!(s.durationSec > 0)) return pos;
  return Math.min(pos, s.durationSec);
}

/** m:ss, or h:mm:ss from one hour on. Negative, NaN and infinite inputs render as 0:00. */
export function formatClock(seconds: number): string {
  const total = Number.isFinite(seconds) && seconds > 0 ? Math.floor(seconds) : 0;
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  const ss = String(s).padStart(2, '0');
  return h > 0 ? `${h}:${String(m).padStart(2, '0')}:${ss}` : `${m}:${ss}`;
}
