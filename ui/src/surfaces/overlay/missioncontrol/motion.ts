/**
 * Pure geometry for Mission Control motion: mapping real window bounds into the overlay viewport, FLIP deltas, and the
 * WAAPI keyframes that animate a card from its old box to its new one. Only transform and opacity are animated.
 */

export interface Box {
  x: number;
  y: number;
  w: number;
  h: number;
}

export interface Bounds {
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface Size {
  w: number;
  h: number;
}

/**
 * Maps a window rectangle (DIPs, relative to its monitor) into viewport CSS pixels. The overlay covers its whole monitor,
 * so the scale is viewport / monitor per axis (1 when they match).
 */
export function realBoxInViewport(b: Bounds, monitorDip: Size, viewport: Size): Box {
  const sx = monitorDip.w > 0 ? viewport.w / monitorDip.w : 1;
  const sy = monitorDip.h > 0 ? viewport.h / monitorDip.h : 1;
  return { x: b.x * sx, y: b.y * sy, w: b.width * sx, h: b.height * sy };
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

/** Position of a card on the overlay: the layout slot offset by the grid's origin in the viewport. */
export function offsetBox(b: Box, origin: { x: number; y: number }): Box {
  return { x: b.x + origin.x, y: b.y + origin.y, w: b.w, h: b.h };
}

/** Minimal Keyframe shape so this file does not depend on DOM lib typings at runtime. */
interface Keyframe {
  transform?: string;
  opacity?: number;
}

function round(v: number, digits = 2): number {
  const f = 10 ** digits;
  return Math.round(v * f) / f;
}
