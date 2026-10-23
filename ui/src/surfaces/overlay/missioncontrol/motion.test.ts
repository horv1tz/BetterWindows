import { describe, expect, it } from 'vitest';
import { flipDelta, flipKeyframes, flipTransform, isNoopFlip } from './motion';

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

  it('a sub-pixel change is a no-op, a visible one is not', () => {
    expect(isNoopFlip(flipDelta({ x: 0.2, y: 0, w: 10, h: 10 }, { x: 0, y: 0, w: 10, h: 10 }))).toBe(true);
    expect(isNoopFlip(flipDelta({ x: 3, y: 0, w: 10, h: 10 }, { x: 0, y: 0, w: 10, h: 10 }))).toBe(false);
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

  it('degenerate boxes never produce Infinity or NaN', () => {
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
