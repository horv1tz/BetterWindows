// Wheel / trackpad paging for the Launchpad (pure). A physical gesture produces a burst of wheel events (inertia included):
// it must turn exactly ONE page.

export interface WheelState {
  /** Accumulated delta in the current gesture. */
  acc: number;
  lastEventAt: number;
  /** No further page turn until this time. */
  lockedUntil: number;
}

export const initialWheel = (): WheelState => ({ acc: 0, lastEventAt: 0, lockedUntil: 0 });

const THRESHOLD = 48; // px of accumulated wheel delta that turns the page
const LOCK_MS = 480; // after a turn: ignore the rest of the gesture
const GAP_MS = 140; // a pause this long starts a new gesture

/**
 * Feed one wheel event (deltaX/deltaY in px, deltaMode 0 assumed; line mode is scaled by the caller).
 * The dominant axis wins (trackpad swipes are deltaX, mouse wheels deltaY). Returns the next state and -1/0/+1 (previous/none/next page).
 */
export function feedWheel(s: WheelState, dx: number, dy: number, now: number): { state: WheelState; turn: -1 | 0 | 1 } {
  const delta = Math.abs(dx) >= Math.abs(dy) ? dx : dy;
  if (delta === 0) return { state: s, turn: 0 };
  // while locked, a continuing stream (inertia) keeps extending the lock so the tail cannot start a second turn
  if (now < s.lockedUntil) return { state: { ...s, lastEventAt: now, lockedUntil: now - s.lastEventAt < GAP_MS ? Math.max(s.lockedUntil, now + 160) : s.lockedUntil }, turn: 0 };
  const fresh = now - s.lastEventAt > GAP_MS || Math.sign(delta) !== Math.sign(s.acc);
  const acc = (fresh ? 0 : s.acc) + delta;
  if (Math.abs(acc) >= THRESHOLD) return { state: { acc: 0, lastEventAt: now, lockedUntil: now + LOCK_MS }, turn: acc > 0 ? 1 : -1 };
  return { state: { acc, lastEventAt: now, lockedUntil: s.lockedUntil }, turn: 0 };
}
