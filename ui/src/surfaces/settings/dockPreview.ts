/** Layout math for the live dock preview on the Dock pane (pure, unit-tested). Sizes are real DIPs; the component scales them. */

/** How far (in icon slots) the magnification reaches from the pointer. */
export const MAGNIFY_SPREAD = 2.4;

/**
 * Size of every icon when the pointer is at `focus` (a fractional icon index). Cosine-squared falloff, 1 at the pointer, 0 at `spread` slots away.
 * Without magnification (or when the magnified size is not larger) every icon has the base size.
 */
export function magnifiedSizes(count: number, base: number, peak: number, focus: number, enabled = true, spread = MAGNIFY_SPREAD): number[] {
  const n = Math.max(0, Math.floor(count));
  if (!enabled || peak <= base) return Array.from({ length: n }, () => base);
  return Array.from({ length: n }, (_, i) => {
    const d = Math.abs(i - focus);
    const k = d >= spread ? 0 : Math.cos((Math.PI * d) / (2 * spread)) ** 2;
    return base + (peak - base) * k;
  });
}

export interface DockPreviewGeometry {
  sizes: number[];
  gap: number;
  padding: number;
  /** length of the bar along the docked edge */
  length: number;
  /** thickness of the bar (resting; magnified icons grow out of it) */
  thickness: number;
  /** extra room the biggest icon needs beyond the bar */
  overflow: number;
}

export function dockPreviewGeometry(o: { count: number; iconSize: number; magnifiedSize: number; magnification: boolean; focus: number; scale: number }): DockPreviewGeometry {
  const { scale } = o;
  const base = o.iconSize * scale;
  const sizes = magnifiedSizes(o.count, base, o.magnifiedSize * scale, o.focus, o.magnification);
  const gap = base * 0.14;
  const padding = base * 0.2;
  const length = sizes.reduce((a, b) => a + b, 0) + gap * Math.max(0, sizes.length - 1) + padding * 2;
  const thickness = base + padding * 2;
  const biggest = sizes.length ? Math.max(...sizes) : base;
  return { sizes, gap, padding, length, thickness, overflow: Math.max(0, biggest - base) };
}

/** Largest scale (<= max) that keeps a magnified dock of `count` icons inside `available` px. */
export function fitScale(o: { count: number; iconSize: number; magnifiedSize: number; magnification: boolean; available: number; max?: number }): number {
  const max = o.max ?? 0.6;
  if (o.available <= 0) return max;
  const at1 = dockPreviewGeometry({ ...o, focus: (o.count - 1) / 2, scale: 1 }).length;
  return Math.min(max, o.available / Math.max(1, at1));
}
