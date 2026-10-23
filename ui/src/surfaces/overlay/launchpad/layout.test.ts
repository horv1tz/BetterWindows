import { describe, expect, it } from 'vitest';
import { clampPage, computeGrid, folderGeometry, pageAfterSwipe, pageCount, rubberBand, slotPosition } from './layout';

describe('computeGrid', () => {
  it('targets 7x5 with 96 DIP icons at 1920x1080', () => {
    const g = computeGrid(1920, 1080);
    expect([g.cols, g.rows, g.perPage, g.iconSize]).toEqual([7, 5, 35, 96]);
    expect(g.originX).toBeCloseTo(192, 5);
  });
  it('shrinks the grid on smaller viewports and never goes below the minimums', () => {
    const g = computeGrid(1440, 900);
    expect(g.cols).toBe(5);
    expect(g.rows).toBe(4);
    const tiny = computeGrid(400, 400);
    expect(tiny.cols).toBe(3);
    expect(tiny.rows).toBe(2);
    expect(tiny.iconSize).toBeGreaterThanOrEqual(48);
    expect(tiny.iconSize).toBeLessThanOrEqual(96);
  });
  it('keeps the whole grid inside the viewport', () => {
    for (const [w, h] of [[1920, 1080], [1366, 768], [1280, 720], [800, 600], [2560, 1440], [3840, 2160], [360, 640]]) {
      const g = computeGrid(w, h);
      expect(g.originX).toBeGreaterThanOrEqual(0);
      expect(g.originX + g.cols * g.cellW).toBeLessThanOrEqual(w + 0.01);
      expect(g.originY + g.rows * g.cellH).toBeLessThanOrEqual(Math.max(h, 140 + 200) + 0.01);
      expect(g.contentH).toBeLessThanOrEqual(g.cellH + 0.01);
    }
  });
});

describe('pages', () => {
  it('counts at least one page', () => {
    expect(pageCount(0, 35)).toBe(1);
    expect(pageCount(35, 35)).toBe(1);
    expect(pageCount(36, 35)).toBe(2);
  });
  it('clamps', () => {
    expect(clampPage(-3, 4)).toBe(0);
    expect(clampPage(9, 4)).toBe(3);
    expect(clampPage(1.6, 4)).toBe(2);
  });
});

describe('slotPosition', () => {
  const g = computeGrid(1920, 1080);
  it('lays out row by row and page by page', () => {
    expect(slotPosition(0, g, 1920)).toMatchObject({ x: g.originX, y: g.originY, page: 0 });
    expect(slotPosition(1, g, 1920).x).toBeCloseTo(g.originX + g.cellW, 5);
    const second = slotPosition(g.cols, g, 1920);
    expect(second.x).toBeCloseTo(g.originX, 5);
    expect(second.y).toBeCloseTo(g.originY + g.cellH, 5);
    const next = slotPosition(g.perPage, g, 1920);
    expect(next.page).toBe(1);
    expect(next.x).toBeCloseTo(1920 + g.originX, 5);
    expect(next.y).toBe(g.originY);
  });
});

describe('pageAfterSwipe', () => {
  it('turns the page on a long drag or a fast flick', () => {
    expect(pageAfterSwipe(1, -400, 0, 1920, 4)).toBe(2);
    expect(pageAfterSwipe(1, 400, 0, 1920, 4)).toBe(0);
    expect(pageAfterSwipe(1, -40, -0.8, 1920, 4)).toBe(2);
    expect(pageAfterSwipe(1, 40, 0.8, 1920, 4)).toBe(0);
  });
  it('snaps back on a short slow drag and clamps at the ends', () => {
    expect(pageAfterSwipe(1, -100, -0.1, 1920, 4)).toBe(1);
    expect(pageAfterSwipe(0, 900, 0, 1920, 4)).toBe(0);
    expect(pageAfterSwipe(3, -900, 0, 1920, 4)).toBe(3);
  });
});

describe('rubberBand', () => {
  it('resists only when pulling past an end', () => {
    expect(rubberBand(100, 0, 3)).toBeCloseTo(35, 5);
    expect(rubberBand(-100, 2, 3)).toBeCloseTo(-35, 5);
    expect(rubberBand(100, 1, 3)).toBe(100);
    expect(rubberBand(-100, 0, 3)).toBe(-100);
  });
});

describe('folderGeometry', () => {
  it('uses 3..5 columns and grows rows', () => {
    expect(folderGeometry(2, 1920, 1080).cols).toBe(3);
    expect(folderGeometry(9, 1920, 1080).cols).toBe(4);
    expect(folderGeometry(30, 1920, 1080).cols).toBe(5);
    expect(folderGeometry(30, 1920, 1080).rows).toBe(6);
  });
  it('limits the panel height so it can scroll', () => {
    const g = folderGeometry(40, 1280, 720);
    expect(g.height).toBeLessThanOrEqual(720 - 260 + 1);
    expect(g.rows * g.cellH).toBeGreaterThan(g.height);
  });
  it('uses fewer columns on narrow viewports', () => {
    expect(folderGeometry(30, 600, 800).cols).toBe(3);
  });
});
