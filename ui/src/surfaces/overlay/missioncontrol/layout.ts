/**
 * Pure window-grid layout for Mission Control.
 *
 * Problem: place N windows (in a given order) in a rectangle without overlap, maximising their size while keeping each
 * window's aspect ratio, never enlarging a window beyond its real size, and staying deterministic.
 *
 * Method: justified rows. Windows are kept in order and split into k contiguous rows. In a row every window gets the same
 * height H (so widths follow from the aspect ratios), and the row is filled edge to edge; H is additionally capped by the
 * smallest real height in the row so that nothing is scaled up. A dynamic programme finds, for every row count k, the
 * partition with the smallest total height (O(n^3), fine for the few dozen windows a desktop holds). If the stack does
 * not fit vertically every row is shrunk by the same factor, which keeps aspect ratios. The row count with the largest
 * total on-screen area wins (ties: fewer rows). The block is centred in the area.
 */

export interface LayoutItem {
  id: string;
  /** Real size (any consistent unit; the caller passes the viewport-scaled size so the 1.0 cap means "real size"). */
  w: number;
  h: number;
}

export interface LayoutArea {
  w: number;
  h: number;
}

export interface Slot {
  id: string;
  x: number;
  y: number;
  w: number;
  h: number;
}

/** Height of the title row under each thumbnail; it lives inside the layout gap, so it never overlaps a neighbour. */
export const CARD_LABEL_H = 26;

const sanitize = (v: number): number => (Number.isFinite(v) && v > 0 ? v : 1);

/**
 * Lays out `items` (order is preserved) inside `area` with `gap` between windows (and rows).
 * Returns one slot per item, in the same order, relative to the area's top-left corner.
 */
export function layoutWindows(items: readonly LayoutItem[], area: LayoutArea, gap = 16): Slot[] {
  const n = items.length;
  if (n === 0) return [];
  const W = area.w;
  const H = area.h;
  if (!(Number.isFinite(W) && Number.isFinite(H) && W > 0 && H > 0)) return items.map((it) => ({ id: it.id, x: 0, y: 0, w: 0, h: 0 }));
  const g = Number.isFinite(gap) && gap > 0 ? gap : 0;

  const ws = items.map((it) => sanitize(it.w));
  const hs = items.map((it) => sanitize(it.h));
  const as = ws.map((w, i) => w / hs[i]!);

  // rowH[j][i]: the justified height of the row holding items j..i-1 (Infinity when the gaps alone exceed the width).
  const rowH: number[][] = Array.from({ length: n + 1 }, () => new Array<number>(n + 1).fill(Infinity));
  for (let j = 0; j < n; j++) {
    let sumA = 0;
    let minH = Infinity;
    for (let i = j + 1; i <= n; i++) {
      sumA += as[i - 1]!;
      minH = Math.min(minH, hs[i - 1]!);
      const avail = W - g * (i - j - 1);
      rowH[j]![i] = avail > 0 ? Math.min(avail / sumA, minH) : Infinity;
    }
  }

  // dp[k][i]: minimal sum of row heights for the first i items in exactly k rows; par for reconstruction.
  const dp: number[][] = Array.from({ length: n + 1 }, () => new Array<number>(n + 1).fill(Infinity));
  const par: number[][] = Array.from({ length: n + 1 }, () => new Array<number>(n + 1).fill(-1));
  dp[0]![0] = 0;
  for (let k = 1; k <= n; k++) {
    for (let i = k; i <= n; i++) {
      for (let j = k - 1; j < i; j++) {
        const prev = dp[k - 1]![j]!;
        const cost = rowH[j]![i]!;
        if (!Number.isFinite(prev) || !Number.isFinite(cost)) continue;
        if (prev + cost < dp[k]![i]!) {
          dp[k]![i] = prev + cost;
          par[k]![i] = j;
        }
      }
    }
  }

  let best: { score: number; k: number; scale: number } | null = null;
  for (let k = 1; k <= n; k++) {
    const total = dp[k]![n]!;
    if (!Number.isFinite(total)) continue;
    const gapsV = g * (k - 1);
    const availH = H - gapsV;
    if (availH <= 0) continue;
    const scale = total > availH ? availH / total : 1; // uniform vertical shrink keeps aspect ratios
    // Score: total on-screen area of the windows (sum of w*h = a*H^2 per window).
    let score = 0;
    let i = n;
    for (let r = k; r >= 1; r--) {
      const j = par[r]![i]!;
      const rowHeight = rowH[j]![i]! * scale;
      for (let m = j; m < i; m++) score += as[m]! * rowHeight * rowHeight;
      i = j;
    }
    if (best === null || score > best.score * (1 + 1e-12)) best = { score, k, scale };
  }

  if (best === null) {
    // The gaps do not fit the area: drop them rather than overlap or return NaN.
    return g > 0 ? layoutWindows(items, area, 0) : items.map((it) => ({ id: it.id, x: 0, y: 0, w: 0, h: 0 }));
  }

  // Reconstruct the rows of the best k (top to bottom).
  const rows: Array<{ from: number; to: number; h: number }> = [];
  let i = n;
  for (let r = best.k; r >= 1; r--) {
    const j = par[r]![i]!;
    rows.push({ from: j, to: i, h: rowH[j]![i]! * best.scale });
    i = j;
  }
  rows.reverse();

  const blockH = rows.reduce((s, r) => s + r.h, 0) + g * (rows.length - 1);
  let y = (H - blockH) / 2;
  const slots: Slot[] = new Array(n);
  for (const row of rows) {
    const count = row.to - row.from;
    let rowA = 0;
    for (let m = row.from; m < row.to; m++) rowA += as[m]!;
    const rowW = rowA * row.h + g * (count - 1);
    let x = (W - rowW) / 2;
    for (let m = row.from; m < row.to; m++) {
      const w = as[m]! * row.h;
      slots[m] = { id: items[m]!.id, x, y, w, h: row.h };
      x += w + g;
    }
    y += row.h + g;
  }
  return slots;
}
