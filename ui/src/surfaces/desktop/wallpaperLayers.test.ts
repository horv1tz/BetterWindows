import { describe, expect, it } from 'vitest';
import { fitStyle, markFailed, markReady, pushLayer, settle } from './wallpaperLayers';

describe('wallpaper layers', () => {
  it('stacks a new url on top and ignores a repeat of the newest', () => {
    let l = pushLayer([], 'a.png', 1);
    expect(l).toEqual([{ id: 1, url: 'a.png', ready: false }]);
    l = pushLayer(l, 'a.png', 2);
    expect(l).toHaveLength(1);
    l = pushLayer(l, 'b.png', 3);
    expect(l.map((x) => x.url)).toEqual(['a.png', 'b.png']);
  });
  it('fades in on ready; older layers are dropped only after the top one is ready', () => {
    let l = markReady(pushLayer([], 'a', 1), 1);
    l = pushLayer(l, 'b', 2);
    expect(settle(l, 2).map((x) => x.id)).toEqual([1, 2]); // b not ready yet: keep a underneath
    l = markReady(l, 2);
    expect(settle(l, 2).map((x) => x.id)).toEqual([2]);
  });
  it('a failed layer disappears and the previous wallpaper stays', () => {
    let l = markReady(pushLayer([], 'a', 1), 1);
    l = markFailed(pushLayer(l, 'bad', 2), 2);
    expect(l.map((x) => x.url)).toEqual(['a']);
  });
  it('rapid changes keep a consistent stack', () => {
    let l = pushLayer([], 'a', 1);
    l = pushLayer(l, 'b', 2);
    l = pushLayer(l, 'c', 3);
    l = markReady(l, 3);
    expect(settle(l, 3).map((x) => x.url)).toEqual(['c']);
  });
});

describe('fitStyle', () => {
  it('maps the five fit modes', () => {
    expect(fitStyle('fill')).toEqual({ objectFit: 'cover', tile: false });
    expect(fitStyle('fit')).toEqual({ objectFit: 'contain', tile: false });
    expect(fitStyle('stretch')).toEqual({ objectFit: 'fill', tile: false });
    expect(fitStyle('center')).toEqual({ objectFit: 'none', tile: false });
    expect(fitStyle('tile').tile).toBe(true);
  });
});
