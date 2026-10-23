/** Tiny deterministic spring integrator for the dock animations (fixed 1/240 s sub-steps, so the result does not depend on frame rate hiccups). */
export interface SpringState { x: number; v: number }
export interface SpringConfig {
  /** Natural angular frequency in rad/s: higher = faster. */
  omega: number;
  /** Damping ratio: 1 = critically damped (no overshoot), < 1 = bouncy. */
  zeta: number;
}

const STEP = 1 / 240;

export function spring(x = 0): SpringState { return { x, v: 0 }; }

/** Advances `s` towards `target` by `dt` seconds. */
export function stepSpring(s: SpringState, target: number, dt: number, cfg: SpringConfig): void {
  if (!(dt > 0)) return;
  const k = cfg.omega * cfg.omega;
  const c = 2 * cfg.zeta * cfg.omega;
  let left = Math.min(dt, 0.1);
  while (left > 1e-9) {
    const h = Math.min(STEP, left);
    // semi-implicit Euler: stable for omega * h << 1 (here <= 0.2)
    s.v += (k * (target - s.x) - c * s.v) * h;
    s.x += s.v * h;
    left -= h;
  }
}

export function isSettled(s: SpringState, target: number, eps = 0.02): boolean {
  return Math.abs(s.x - target) <= eps && Math.abs(s.v) <= eps * 4;
}

export function snap(s: SpringState, target: number): void { s.x = target; s.v = 0; }
