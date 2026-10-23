// Launchpad grid math: pure functions, no DOM. All numbers are DIPs.

export interface GridSpec {
  cols: number;
  rows: number;
  perPage: number;
  /** Cell size (icon + label + gaps). */
  cellW: number;
  cellH: number;
  iconSize: number;
  /** Top-left of the grid area inside one page (page width == viewport width). */
  originX: number;
  originY: number;
  /** Height of icon + label block, centred vertically inside a cell. */
  contentH: number;
}

export const ICON_MAX = 96;
const ICON_MIN = 48;
const LABEL_GAP = 10;
const LABEL_H = 34; // two lines at 13px

/** Space reserved for the search field (top) and the page dots (bottom). */
export const TOP_RESERVE = 112;
export const BOTTOM_RESERVE = 88;

const clamp = (v: number, lo: number, hi: number) => Math.min(Math.max(v, lo), hi);

/** Fit columns/rows (target 7x5 at 1920x1080) and the icon size to the viewport. */
export function computeGrid(vw: number, vh: number): GridSpec {
  const areaW = clamp(vw * 0.8, Math.min(320, vw - 24), Math.max(vw - 48, 0));
  const areaH = Math.max(vh - TOP_RESERVE - BOTTOM_RESERVE, 140);
  const cols = clamp(Math.floor(areaW / 200), 3, 9);
  const rows = clamp(Math.floor(areaH / 150), 2, 6);
  const cellW = areaW / cols;
  const cellH = areaH / rows;
  const iconSize = Math.round(clamp(Math.min(cellW - 64, cellH - 56, ICON_MAX), ICON_MIN, ICON_MAX));
  return {
    cols, rows, perPage: cols * rows, cellW, cellH, iconSize,
    originX: (vw - areaW) / 2,
    originY: TOP_RESERVE,
    contentH: iconSize + LABEL_GAP + LABEL_H,
  };
}

export const pageCount = (items: number, perPage: number): number => Math.max(1, Math.ceil(items / Math.max(1, perPage)));
export const clampPage = (page: number, count: number): number => clamp(Math.round(page), 0, Math.max(0, count - 1));

/** Top-left of slot `index` in track coordinates (pages laid out side by side, each `vw` wide). */
export function slotPosition(index: number, g: GridSpec, vw: number): { x: number; y: number; page: number } {
  const page = Math.floor(index / g.perPage);
  const i = index % g.perPage;
  return { x: page * vw + g.originX + (i % g.cols) * g.cellW, y: g.originY + Math.floor(i / g.cols) * g.cellH, page };
}

/** Where the page ends up after a swipe: far enough or fast enough turns the page (velocity in px/ms, negative = towards the next page). */
export function pageAfterSwipe(page: number, dx: number, velocity: number, vw: number, count: number): number {
  let target = page;
  if (dx < -vw * 0.16 || velocity < -0.45) target = page + 1;
  else if (dx > vw * 0.16 || velocity > 0.45) target = page - 1;
  return clampPage(target, count);
}

/** Rubber-band resistance when pulling the first/last page past its end. */
export function rubberBand(dx: number, page: number, count: number): number {
  const pastStart = page === 0 && dx > 0;
  const pastEnd = page === count - 1 && dx < 0;
  return pastStart || pastEnd ? dx * 0.35 : dx;
}

// ---- folder panel ----

export interface FolderGeometry {
  cols: number;
  rows: number;
  cellW: number;
  cellH: number;
  iconSize: number;
  contentH: number;
  width: number;
  height: number;
}

/** Grid of the expanded folder: wider folders get more columns; the panel scrolls when rows do not fit. */
export function folderGeometry(count: number, vw: number, vh: number): FolderGeometry {
  const n = Math.max(1, count);
  const maxCols = vw < 700 ? 3 : 5;
  const cols = clamp(Math.ceil(Math.sqrt(n * 1.7)), Math.min(3, maxCols), maxCols);
  const rows = Math.ceil(n / cols);
  const iconSize = vw < 700 || vh < 640 ? 56 : 72;
  const cellW = iconSize + 56;
  const cellH = iconSize + 54;
  const contentH = iconSize + LABEL_GAP + LABEL_H;
  const maxHeight = Math.max(cellH, vh - 260);
  return { cols, rows, cellW, cellH, iconSize, contentH, width: cols * cellW, height: Math.min(rows * cellH, maxHeight) };
}

/** Which apps the 3x3 preview of a folder tile shows (first nine). */
export const MINI_COUNT = 9;
