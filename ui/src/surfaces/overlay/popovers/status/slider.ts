/**
 * Pure slider math (no DOM): clamping, step snapping, pointer <-> value mapping and keyboard handling.
 * Kept separate from Slider.svelte so every edge case is unit-tested without a layout engine.
 */

/** Clamp `v` into [min, max]; NaN collapses to `min`. */
export function clamp(v: number, min: number, max: number): number {
  if (Number.isNaN(v)) return min;
  return Math.min(max, Math.max(min, v));
}

/** Number of decimals needed to represent `step` exactly (0.01 -> 2, 1 -> 0, 0.25 -> 2). */
function decimalsOf(step: number): number {
  if (!Number.isFinite(step) || step <= 0) return 0;
  let d = 0;
  let s = step;
  while (d < 8 && Math.abs(Math.round(s) - s) > 1e-9) { s *= 10; d++; }
  return d;
}

/**
 * Snap to the nearest multiple of `step` counted from `min` and clamp. Removes binary floating-point noise
 * (0.1 + 0.2 style) so aria-valuenow and the host never see 0.30000000000000004.
 */
export function snap(v: number, min: number, max: number, step: number): number {
  const c = clamp(v, min, max);
  if (!(step > 0)) return c;
  const steps = Math.round((c - min) / step);
  let snapped = min + steps * step;
  // The grid may not contain max: take the largest grid value that does not exceed it instead of an off-grid max.
  if (snapped > max + 1e-9) snapped -= step;
  const decimals = Math.max(decimalsOf(step), decimalsOf(min));
  const rounded = Number(snapped.toFixed(decimals));
  return clamp(rounded, min, max);
}

/** 0..1 position of `v` inside [min, max]; a degenerate range (max <= min) is 0. */
export function fraction(v: number, min: number, max: number): number {
  if (!(max > min)) return 0;
  return clamp((v - min) / (max - min), 0, 1);
}

/**
 * Map a pointer x position to a value. `rect` is the track's bounding box; `inset` is half of the thumb size so the
 * thumb centre (not the track edge) follows the pointer, exactly like the rendered geometry.
 */
export function valueFromPointer(
  clientX: number,
  rect: { left: number; width: number },
  min: number,
  max: number,
  step: number,
  inset = 0,
): number {
  const usable = rect.width - inset * 2;
  if (!(usable > 0)) return snap(min, min, max, step);
  const f = clamp((clientX - rect.left - inset) / usable, 0, 1);
  return snap(min + f * (max - min), min, max, step);
}

/** Keyboard behaviour of a horizontal slider (WAI-ARIA slider pattern). Returns null when the key is not handled. */
export function valueFromKey(
  key: string,
  value: number,
  min: number,
  max: number,
  step: number,
  pageStep: number = step * 10,
): number | null {
  switch (key) {
    case 'ArrowRight':
    case 'ArrowUp': return snap(value + step, min, max, step);
    case 'ArrowLeft':
    case 'ArrowDown': return snap(value - step, min, max, step);
    case 'PageUp': return snap(value + pageStep, min, max, step);
    case 'PageDown': return snap(value - pageStep, min, max, step);
    case 'Home': return snap(min, min, max, step);
    case 'End': return snap(max, min, max, step);
    default: return null;
  }
}
