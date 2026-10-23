/** Spatial keyboard navigation between Mission Control cards. Pure: works on the laid-out slots. */

export type Direction = 'left' | 'right' | 'up' | 'down';

export interface NavSlot {
  id: string;
  x: number;
  y: number;
  w: number;
  h: number;
}

/**
 * The card to focus when the user presses an arrow key on `fromId`, or null when there is none in that direction.
 * A candidate must lie in the half-plane of the direction. Among candidates the nearest wins, where moving across the
 * direction costs twice as much as moving along it, so a row is preferred over a diagonal neighbour.
 * Ties resolve by slot order, which keeps the result deterministic.
 */
export function spatialNeighbor(slots: readonly NavSlot[], fromId: string, dir: Direction): string | null {
  const from = slots.find((s) => s.id === fromId);
  if (!from) return slots[0]?.id ?? null;
  const cx = from.x + from.w / 2;
  const cy = from.y + from.h / 2;
  let best: { id: string; score: number } | null = null;
  for (const s of slots) {
    if (s.id === fromId) continue;
    const dx = s.x + s.w / 2 - cx;
    const dy = s.y + s.h / 2 - cy;
    const along = dir === 'left' ? -dx : dir === 'right' ? dx : dir === 'up' ? -dy : dy;
    const across = dir === 'left' || dir === 'right' ? Math.abs(dy) : Math.abs(dx);
    if (along <= 0) continue;
    const score = along + 2 * across;
    if (best === null || score < best.score) best = { id: s.id, score };
  }
  return best?.id ?? null;
}

/** The first slot in reading order (top, then left): the default focus target. */
export function firstInReadingOrder(slots: readonly NavSlot[]): string | null {
  let best: NavSlot | null = null;
  for (const s of slots) {
    if (!best || s.y < best.y - 0.5 || (Math.abs(s.y - best.y) <= 0.5 && s.x < best.x)) best = s;
  }
  return best?.id ?? null;
}
