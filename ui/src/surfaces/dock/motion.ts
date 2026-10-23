import type { MagnifySpec, Metrics, Slot } from './layout';
import { mapPoint } from './magnify';
import { isSettled, snap, spring, stepSpring, type SpringConfig, type SpringState } from './spring';

/**
 * The dock's animation engine: pointer-anchored magnification (see magnify.ts), drag-gap shifts and pill growth,
 * all smoothed with springs. DOM-free: `step(dt)` advances the state, `frame()` reads the resulting geometry.
 * The Surface applies the frame with transforms only (one translate + one scale per slot).
 */
export interface FrameSlot {
  /** Translation of the slot centre from its rest position along the main axis. */
  offset: number;
  /** Uniform magnification of the slot (main and cross axis). */
  scale: number;
}

export interface Frame {
  slots: FrameSlot[];
  /** Pill extents on the main axis in strip coordinates (padding included). */
  pillStart: number;
  pillEnd: number;
  /** Cross-axis growth of the pill needed to contain the magnified icons (0 at rest). */
  grow: number;
  /** Largest slot scale. */
  peak: number;
  /** Slot under the pointer, or -1. */
  hover: number;
}

const ANCHOR: SpringConfig = { omega: 60, zeta: 1 };
const AMOUNT: SpringConfig = { omega: 24, zeta: 1 };
const SHIFT: SpringConfig = { omega: 26, zeta: 0.8 };
const LENGTH: SpringConfig = { omega: 26, zeta: 0.9 };
const BOUNCE: SpringConfig = { omega: 22, zeta: 0.9 };

export class DockMotion {
  private slots: Slot[] = [];
  private length = 0;
  private m!: Metrics;
  private mag!: MagnifySpec;
  private reduce = false;

  private pointer: number | null = null;
  private anchor = spring(0);
  private amount = spring(0);
  private lenDelta = spring(0);
  private lenDeltaTarget = 0;
  private bounce = spring(0);
  private bounceTarget = 0;
  private shifts = new Map<string, SpringState>();
  private shiftTargets = new Map<string, number>();
  private dragging = false;

  configure(slots: Slot[], length: number, m: Metrics, mag: MagnifySpec, reduceMotion: boolean): void {
    this.slots = slots;
    this.length = length;
    this.m = m;
    this.mag = mag;
    this.reduce = reduceMotion;
    const keys = new Set(slots.map((s) => s.key));
    for (const k of [...this.shifts.keys()]) if (!keys.has(k)) { this.shifts.delete(k); this.shiftTargets.delete(k); }
    if (reduceMotion) this.snapAll();
  }

  /** Pointer position on the main axis (strip coordinates) or null when it left the dock. */
  setPointer(main: number | null): void {
    if (main !== null && this.pointer === null) snap(this.anchor, main); // entering: no slide from a stale anchor
    this.pointer = main;
  }

  get pointerMain(): number | null { return this.pointer; }

  /** Starts / updates a drag preview: shifts per slot index (same order as the slots) or null to end it. */
  setDrag(shifts: number[] | null, lengthDelta = 0): void {
    this.dragging = shifts !== null;
    this.shiftTargets.clear();
    if (shifts) this.slots.forEach((s, i) => this.shiftTargets.set(s.key, shifts[i] ?? 0));
    this.lenDeltaTarget = shifts ? lengthDelta : 0;
    if (this.reduce) this.snapAll();
  }

  /** Extra cross-axis room the pill needs for hopping icons (px), 0 when nothing bounces. */
  setBounce(px: number): void {
    this.bounceTarget = px;
    if (this.reduce) snap(this.bounce, px);
  }

  /** Forget all drag offsets at once (the DOM order changed under them: positions are now authoritative). */
  resetShifts(): void {
    this.shifts.clear();
    this.shiftTargets.clear();
    snap(this.lenDelta, 0);
    this.lenDeltaTarget = 0;
  }

  private snapAll(): void {
    snap(this.amount, this.magTarget());
    if (this.pointer !== null) snap(this.anchor, this.pointer);
    for (const [k, v] of this.shiftTargets) { const s = this.shifts.get(k) ?? spring(0); snap(s, v); this.shifts.set(k, s); }
    snap(this.lenDelta, this.lenDeltaTarget);
    snap(this.bounce, this.bounceTarget);
  }

  private magTarget(): number {
    return this.pointer !== null && this.mag.enabled && !this.dragging && !this.reduce ? 1 : 0;
  }

  /** Advances the springs; returns true while anything is still moving (the caller keeps its rAF loop alive). */
  step(dt: number): boolean {
    if (this.reduce) { this.snapAll(); return false; }
    if (this.pointer !== null) stepSpring(this.anchor, this.pointer, dt, ANCHOR);
    const amountTarget = this.magTarget();
    stepSpring(this.amount, amountTarget, dt, AMOUNT);
    stepSpring(this.lenDelta, this.lenDeltaTarget, dt, LENGTH);
    stepSpring(this.bounce, this.bounceTarget, dt, BOUNCE);
    // springs that came to rest are snapped onto their target: no residual 1.002x scale / sub-pixel offsets stay behind in the DOM
    let busy = false;
    for (const [s, target] of [[this.amount, amountTarget], [this.lenDelta, this.lenDeltaTarget], [this.bounce, this.bounceTarget]] as const) {
      if (isSettled(s, target)) snap(s, target); else busy = true;
    }
    if (this.pointer !== null && this.mag.enabled) busy = true;
    for (const s of this.slots) {
      const target = this.shiftTargets.get(s.key) ?? 0;
      let st = this.shifts.get(s.key);
      if (!st) { if (target === 0) continue; st = spring(0); this.shifts.set(s.key, st); }
      stepSpring(st, target, dt, SHIFT);
      if (!isSettled(st, target)) busy = true;
      else if (target === 0) this.shifts.delete(s.key);
      else snap(st, target);
    }
    return busy;
  }

  frame(): Frame {
    const a = this.anchor.x;
    const amt = Math.max(0, this.amount.x);
    const f = this.mag.fisheye;
    let peak = 1;
    const slots: FrameSlot[] = this.slots.map((s) => {
      const l = s.start + (this.shifts.get(s.key)?.x ?? 0);
      const r = l + s.len;
      const L = mapPoint(l, a, f, amt);
      const R = mapPoint(r, a, f, amt);
      const scale = s.len > 0 ? (R - L) / s.len : 1;
      if (s.kind !== 'sep' && scale > peak) peak = scale;
      return { offset: (L + R) / 2 - (s.start + s.len / 2), scale };
    });
    const pillStart = mapPoint(0, a, f, amt) - this.m.pad;
    const pillEnd = mapPoint(Math.max(this.length, this.m.size * 0.5) + this.lenDelta.x, a, f, amt) + this.m.pad;
    // a hopping icon can be the magnified one: both amounts add up
    const grow = (peak - 1) * this.m.size + this.bounce.x;
    return { slots, pillStart, pillEnd, grow, peak, hover: this.hoverIndex(a, amt, pillStart, pillEnd) };
  }

  /** The slot under the pointer (separators and the pill's padding resolve to the nearest neighbour / none). */
  private hoverIndex(a: number, amt: number, pillStart: number, pillEnd: number): number {
    const p = this.pointer;
    if (p === null || this.slots.length === 0) return -1;
    if (p < pillStart || p > pillEnd) return -1;
    const f = this.mag.fisheye;
    const half = this.m.gap / 2;
    let found = -1;
    for (let i = 0; i < this.slots.length; i++) {
      const s = this.slots[i];
      const sh = this.shifts.get(s.key)?.x ?? 0;
      const L = mapPoint(s.start + sh, a, f, amt) - half;
      const R = mapPoint(s.start + sh + s.len, a, f, amt) + half;
      if (p >= L && p <= R) { found = i; break; }
    }
    if (found < 0) found = p < mapPoint(this.slots[0].start, a, f, amt) ? 0 : this.slots.length - 1;
    return this.slots[found].kind === 'sep' ? -1 : found;
  }
}
