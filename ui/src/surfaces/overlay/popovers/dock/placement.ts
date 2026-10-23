import type { Rect } from '$lib/protocol/generated';

/** Where a dock popover goes relative to the dock icon it belongs to. Pure (unit tested). */
export type DockSide = 'bottom' | 'left' | 'right';

export interface PlaceInput {
  /** The dock icon (overlay client coordinates). */
  anchor: Rect;
  /** Measured panel size. */
  w: number;
  h: number;
  /** Overlay viewport. */
  vw: number;
  vh: number;
  /** Which screen edge the dock sits at: the panel opens away from it (above a bottom dock, beside a side dock). */
  dock: DockSide;
  gap?: number;
  margin?: number;
}

export interface Placed {
  x: number;
  y: number;
  /** CSS transform-origin in px inside the panel, pointing at the icon so the panel "grows out of" it. */
  originX: number;
  originY: number;
}

const clamp = (v: number, lo: number, hi: number) => Math.min(Math.max(v, lo), Math.max(lo, hi));

export function placePanel(i: PlaceInput): Placed {
  const gap = i.gap ?? 10;
  const margin = i.margin ?? 8;
  const cx = i.anchor.x + i.anchor.width / 2;
  const cy = i.anchor.y + i.anchor.height / 2;
  if (i.dock === 'bottom') {
    let y = i.anchor.y - gap - i.h;
    let below = false;
    if (y < margin) {
      const alt = i.anchor.y + i.anchor.height + gap;
      if (alt + i.h <= i.vh - margin) { y = alt; below = true; }
    }
    y = clamp(y, margin, i.vh - i.h - margin);
    const x = clamp(cx - i.w / 2, margin, i.vw - i.w - margin);
    return { x, y, originX: clamp(cx - x, 0, i.w), originY: below ? 0 : i.h };
  }
  const sideRight = i.dock === 'left';
  let x = sideRight ? i.anchor.x + i.anchor.width + gap : i.anchor.x - gap - i.w;
  let flipped = false;
  if (sideRight ? x + i.w > i.vw - margin : x < margin) {
    const alt = sideRight ? i.anchor.x - gap - i.w : i.anchor.x + i.anchor.width + gap;
    if (alt >= margin && alt + i.w <= i.vw - margin) { x = alt; flipped = true; }
  }
  x = clamp(x, margin, i.vw - i.w - margin);
  const y = clamp(cy - i.h / 2, margin, i.vh - i.h - margin);
  const fromLeft = sideRight !== flipped;
  return { x, y, originX: fromLeft ? 0 : i.w, originY: clamp(cy - y, 0, i.h) };
}
