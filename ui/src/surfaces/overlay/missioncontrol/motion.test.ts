import { describe, expect, it } from 'vitest';
import { flipDelta, flipKeyframes, flipTransform, isNoopFlip, offsetBox, realBoxInViewport } from './motion';

describe('realBoxInViewport', () => {
  it('maps monitor DIP bounds into the viewport per axis', () => {
    // a 1920x1080 monitor shown in a 960x540 viewport: everything halves
    expect(realBoxInViewport({ x: 100, y: 200, width: 400, height: 300 }, { w: 1920, h: 1080 }, { w: 960, h: 540 }))
      .toEqual({ x: 50, y: 100, w: 200, h: 150 });
  });

  it('is the identity when the viewport is the monitor', () => {
    const b = realBoxInViewport({ x: 7, y: 9, width: 30, height: 40 }, { w: 1440, h: 900 }, { w: 1440, h: 900 });
    expect(b).toEqual({ x: 7, y: 9, w: 30, h: 40 });
  });

  it('handles a non-uniform viewport scale without distorting the mapping', () => {
    const b = realBoxInViewport({ x: 100, y: 100, width: 100, height: 100 }, { w: 1000, h: 500 }, { w: 500, h: 500 });
    expect(b).toEqual({ x: 50, y: 100, w: 50, h: 100 });
  });

  it('falls back to scale 1 for a degenerate monitor size', () => {
    const b = realBoxInViewport({ x: 10, y: 10, width: 20, height: 20 }, { w: 0, h: 0 }, { w: 500, h: 500 });
    expect(b).toEqual({ x: 10, y: 10, w: 20, h: 20 });
  });
});

describe('flipDelta and flipTransform', () => {
  it('a card that did not move or resize needs no animation', () => {
    const box = { x: 40, y: 50, w: 200, h: 120 };
    const d = flipDelta(box, box);
    expect(d).toEqual({ dx: 0, dy: 0, sx: 1, sy: 1 });
    expect(isNoopFlip(d)).toBe(true);
  });

  it('translation is the difference of the top-left corners', () => {
    expect(flipDelta({ x: 0, y: 0, w: 100, h: 100 }, { x: 300, y: 120, w: 100, h: 100 })).toEqual({ dx: -300, dy: -120, sx: 1, sy: 1 });
  });

  it('scale maps the target size back to the real window size', () => {
    // real window is 800x500 at the origin, its slot is 400x250: the start frame is twice as large
    const d = flipDelta({ x: 0, y: 0, w: 800, h: 500 }, { x: 0, y: 0, w: 400, h: 250 });
    expect(d.sx).toBeCloseTo(2, 9);
    expect(d.sy).toBeCloseTo(2, 9);
  });

  it('applying the delta to the slot reproduces the real box exactly (transform-origin 0 0)', () => {
    const from = { x: 120, y: 60, w: 640, h: 400 };
    const to = { x: 300, y: 210, w: 320, h: 200 };
    const d = flipDelta(from, to);
    // the element's top-left and bottom-right corners under: translate(dx,dy) scale(sx,sy)
    const mapX = (px: number) => d.dx + px * d.sx;
    const mapY = (py: number) => d.dy + py * d.sy;
    expect(to.x + mapX(0)).toBeCloseTo(from.x, 9);
    expect(to.y + mapY(0)).toBeCloseTo(from.y, 9);
    expect(to.x + mapX(to.w)).toBeCloseTo(from.x + from.w, 9);
    expect(to.y + mapY(to.h)).toBeCloseTo(from.y + from.h, 9);
  });

  it('degenerate target boxes never produce Infinity or NaN', () => {
    const d = flipDelta({ x: 0, y: 0, w: 100, h: 100 }, { x: 0, y: 0, w: 0, h: 0 });
    expect(Number.isFinite(d.sx) && Number.isFinite(d.sy)).toBe(true);
    const zeroFrom = flipDelta({ x: 0, y: 0, w: 0, h: 0 }, { x: 0, y: 0, w: 100, h: 100 });
    expect(zeroFrom.sx).toBe(1);
  });

  it('rounds the CSS string to sub-pixel precision', () => {
    expect(flipTransform({ dx: 12.3456, dy: -7.001, sx: 0.5, sy: 1.25 })).toBe('translate(12.35px, -7px) scale(0.5, 1.25)');
  });

  it('keyframes go from the old transform to identity (only transform is animated)', () => {
    const frames = flipKeyframes({ x: 0, y: 0, w: 200, h: 100 }, { x: 50, y: 0, w: 100, h: 50 });
    expect(frames).toHaveLength(2);
    expect(frames[0]).toEqual({ transform: 'translate(-50px, 0px) scale(2, 2)' });
    expect(frames[1]).toEqual({ transform: 'translate(0px, 0px) scale(1, 1)' });
    for (const f of frames) expect(Object.keys(f)).toEqual(['transform']);
  });
});

describe('offsetBox', () => {
  it('moves a slot by the grid origin and keeps its size', () => {
    expect(offsetBox({ x: 10, y: 20, w: 30, h: 40 }, { x: 100, y: 200 })).toEqual({ x: 110, y: 220, w: 30, h: 40 });
  });
});
