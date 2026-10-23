/**
 * Dock magnification as a "fisheye" mapping of the strip axis. Pure and DOM-free (unit tested).
 *
 * The strip (all dock items laid out along the main axis at rest) is stretched around the pointer: a piece of the strip at
 * distance u from the anchor is stretched by s(u) = 1 + amp * falloff(u / radius), with a cosine falloff that reaches 0 at
 * `radius`. A point x of the rest layout therefore moves to  M(x) = anchor + integral_0^(x-anchor) s(u) du.  Properties:
 *  - M(anchor) = anchor: the item under the pointer stays under the pointer (no feedback jitter between pointer and layout);
 *  - M is strictly increasing: items never overlap or swap order, gaps stretch together with the icons;
 *  - an item occupying [l, r] at rest occupies [M(l), M(r)] afterwards, so its scale is (M(r) - M(l)) / (r - l);
 *  - symmetric around the anchor, and the strip ends move outward by at most amp * radius / 2 (see maxSideShift).
 */
export interface Fisheye {
  /** Peak stretch above 1, corrected so that an icon centred exactly under the anchor reaches `magnifiedSize`. */
  amp: number;
  /** Influence radius in px; the strip is not stretched further than this from the anchor. */
  radius: number;
}

/** Cosine bell: 1 at t = 0, 0 at |t| >= 1, smooth (zero slope at both ends). */
export function falloff(t: number): number {
  const a = Math.abs(t);
  return a >= 1 ? 0 : 0.5 * (1 + Math.cos(Math.PI * a));
}

/** Integral of falloff from 0 to t (odd function, saturates at +-0.5). */
export function falloffIntegral(t: number): number {
  const a = Math.min(Math.abs(t), 1);
  const v = (a + Math.sin(Math.PI * a) / Math.PI) / 2;
  return t < 0 ? -v : v;
}

/** Influence radius used by the dock: about 2.6 icon pitches, i.e. three icons on each side react. */
export function influenceRadius(size: number, gap: number): number {
  return 2.6 * (size + gap);
}

/** Builds the mapping for icons of `size` px that should reach `magnifiedSize` px under the pointer. */
export function createFisheye(size: number, magnifiedSize: number, radius: number): Fisheye {
  const safeRadius = Math.max(radius, 1);
  const scale = magnifiedSize / size;
  if (!(size > 0) || !(scale > 1)) return { amp: 0, radius: safeRadius };
  // The icon centred under the anchor spans +-size/2, so it gets the MEAN stretch over that span (< the peak): boost the peak to compensate.
  const t0 = Math.min(size / 2 / safeRadius, 1);
  return { amp: ((scale - 1) * t0) / falloffIntegral(t0), radius: safeRadius };
}

/** Where rest coordinate `x` ends up when the strip is stretched around `anchor`; `amount` (0..1) blends from rest to full magnification. */
export function mapPoint(x: number, anchor: number, f: Fisheye, amount = 1): number {
  const amp = f.amp * amount;
  if (amp === 0) return x;
  const u = x - anchor;
  return anchor + u + amp * f.radius * falloffIntegral(u / f.radius);
}

/** Scale factor of an item spanning [l, r] at rest. */
export function intervalScale(l: number, r: number, anchor: number, f: Fisheye, amount = 1): number {
  const w = r - l;
  if (w <= 0) return 1;
  return (mapPoint(r, anchor, f, amount) - mapPoint(l, anchor, f, amount)) / w;
}

/** Upper bound of how far any point of the strip can move (the far ends): the window needs this much room on each side. */
export function maxSideShift(f: Fisheye): number {
  return (f.amp * f.radius) / 2;
}

/** Largest scale an icon can reach (the one centred under the anchor). */
export function peakScale(f: Fisheye, size: number): number {
  return intervalScale(-size / 2, size / 2, 0, f, 1);
}
