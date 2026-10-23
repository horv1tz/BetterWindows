/**
 * Pure geometry for Mission Control motion: FLIP deltas and the WAAPI keyframes that animate a card from its old box to its
 * new one. Only transform is animated (the fade-out of removed cards uses opacity and transform). Boxes are CSS px in the
 * overlay viewport, which covers its monitor, so real window bounds (DIPs) are already in the same units.
 */

export interface Box {
  x: number;
  y: number;
  w: number;
  h: number;
}

export interface FlipDelta {
  dx: number;
  dy: number;
  sx: number;
  sy: number;
}

/**
 * The transform that takes an element laid out at `to` (transform-origin: 0 0) to the box `from`.
 * Degenerate boxes (zero size) fall back to scale 1 so a bad input never produces Infinity or NaN.
 */
export function flipDelta(from: Box, to: Box): FlipDelta {
  const sx = to.w > 0 && from.w > 0 ? from.w / to.w : 1;
  const sy = to.h > 0 && from.h > 0 ? from.h / to.h : 1;
  return { dx: from.x - to.x, dy: from.y - to.y, sx, sy };
}

/** True when a FLIP would be invisible (sub-pixel), so it can be skipped. */
export function isNoopFlip(d: FlipDelta, epsilon = 0.5): boolean {
  return Math.abs(d.dx) < epsilon && Math.abs(d.dy) < epsilon && Math.abs(d.sx - 1) < 1e-3 && Math.abs(d.sy - 1) < 1e-3;
}

/** CSS transform string for a FLIP start frame. */
export function flipTransform(d: FlipDelta): string {
  return `translate(${round(d.dx)}px, ${round(d.dy)}px) scale(${round(d.sx, 5)}, ${round(d.sy, 5)})`;
}

/** Keyframes for element.animate(): from the old box to the element's laid-out box (identity). */
export function flipKeyframes(from: Box, to: Box): Keyframe[] {
  const d = flipDelta(from, to);
  return [{ transform: flipTransform(d) }, { transform: 'translate(0px, 0px) scale(1, 1)' }];
}

function round(v: number, digits = 2): number {
  const f = 10 ** digits;
  return Math.round(v * f) / f;
}
