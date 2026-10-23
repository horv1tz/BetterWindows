import type { DockPos } from './layout';

/**
 * Keyframes for the dock "hop" (launch bounce = 3 decaying hops; attention = the same burst repeated with a pause).
 * Used with the Web Animations API on the inner `.bounce` element, so it composes with the magnification transforms on its parents.
 */
export type HopKind = 'launch' | 'launching' | 'attention';

export interface HopPlan {
  keyframes: Keyframe[];
  options: KeyframeAnimationOptions;
}

/** Direction of a hop: away from the docked screen edge. */
export function hopDirection(pos: DockPos): { x: number; y: number } {
  return pos === 'bottom' ? { x: 0, y: -1 } : pos === 'left' ? { x: 1, y: 0 } : { x: -1, y: 0 };
}

// [offset within the burst, height as a fraction of the amplitude]
const BURST: Array<[number, number]> = [[0, 0], [0.16, 1], [0.32, 0], [0.54, 0.62], [0.7, 0], [0.88, 0.3], [1, 0]];

export function hopPlan(kind: HopKind, pos: DockPos, amplitude: number): HopPlan {
  const d = hopDirection(pos);
  // fraction of the whole cycle taken by the burst; the rest of a looping cycle is calm
  const share = kind === 'launch' ? 1 : kind === 'launching' ? 0.62 : 0.5;
  const at = (h: number) => `translate3d(${d.x * h * amplitude}px, ${d.y * h * amplitude}px, 0)`;
  const keyframes: Keyframe[] = BURST.map(([o, h], i) => ({
    offset: o * share,
    transform: at(h),
    // rising segments decelerate, falling segments accelerate: a ball under gravity
    easing: i < BURST.length - 1 ? (BURST[i + 1][1] > h ? 'cubic-bezier(0.2, 0.7, 0.4, 1)' : 'cubic-bezier(0.6, 0, 0.8, 0.4)') : 'linear',
  }));
  if (share < 1) keyframes.push({ offset: 1, transform: at(0), easing: 'linear' });
  const duration = kind === 'launch' ? 950 : kind === 'launching' ? 1500 : 2300;
  return { keyframes, options: { duration, iterations: kind === 'launch' ? 1 : Infinity } };
}

export function hopAmplitude(size: number): number { return Math.round(size * 0.34); }
