import { describe, expect, it } from 'vitest';
import { type ZOrder, clampWindow, defaultWindows, dockFrame, monitorIndex, raise, screenFrames, topWindow } from './layout';
import { overlayAction, shortcutOf } from './shortcuts';

describe('screenFrames', () => {
  it('one monitor fills the viewport', () => {
    expect(screenFrames({ width: 1440, height: 900 }, 1)).toEqual([{ left: 0, top: 0, width: 1440, height: 900 }]);
  });
  it('two monitors split the width with a bezel and cover everything', () => {
    const [a, b] = screenFrames({ width: 1442, height: 900 }, 2);
    expect(a).toEqual({ left: 0, top: 0, width: 720, height: 900 });
    expect(b).toEqual({ left: 722, top: 0, width: 720, height: 900 });
    expect(b.left + b.width).toBe(1442);
  });
  it('clamps silly counts', () => {
    expect(screenFrames({ width: 1000, height: 500 }, 0)).toHaveLength(1);
    expect(screenFrames({ width: 1000, height: 500 }, 99)).toHaveLength(4);
  });
  it('maps monitor ids to indexes with a primary fallback', () => {
    expect(monitorIndex('1', 2)).toBe(0);
    expect(monitorIndex('2', 2)).toBe(1);
    expect(monitorIndex('3', 2)).toBe(0);
    expect(monitorIndex(undefined, 2)).toBe(0);
    expect(monitorIndex('x', 2)).toBe(0);
    expect(monitorIndex('2', 1)).toBe(0);
  });
});

describe('dockFrame', () => {
  const screen = { width: 1440, height: 900 };
  it('centres a bottom dock on the edge', () => {
    expect(dockFrame(screen, { width: 600, height: 110 }, 'bottom')).toEqual({ left: 420, top: 790, width: 600, height: 110 });
  });
  it('left and right docks are centred vertically below the menu bar', () => {
    expect(dockFrame(screen, { width: 100, height: 400 }, 'left')).toEqual({ left: 0, top: 28 + 236, width: 100, height: 400 });
    expect(dockFrame(screen, { width: 100, height: 400 }, 'right').left).toBe(1340);
  });
  it('never exceeds the screen', () => {
    const f = dockFrame(screen, { width: 9999, height: 9999 }, 'bottom');
    expect(f.width).toBe(1440);
    expect(f.height).toBe(900 - 28);
    expect(dockFrame(screen, { width: -5, height: -5 }, 'bottom')).toMatchObject({ width: 0, height: 0 });
  });
});

describe('faux windows', () => {
  const bounds = { width: 1000, height: 700 };
  it('keeps the title bar below the menu bar and part of the window on screen', () => {
    const f = { left: 0, top: 0, width: 400, height: 300 };
    expect(clampWindow({ ...f, top: -50 }, bounds).top).toBe(28);
    expect(clampWindow({ ...f, top: 5000 }, bounds).top).toBe(660);
    expect(clampWindow({ ...f, left: -900 }, bounds).left).toBe(96 - 400);
    expect(clampWindow({ ...f, left: 5000 }, bounds).left).toBe(1000 - 96);
    expect(clampWindow({ ...f, left: 100, top: 100 }, bounds)).toEqual({ ...f, left: 100, top: 100 });
  });
  it('raising puts a window on top and keeps the others ordered', () => {
    let z: ZOrder = { a: 1, b: 2, c: 3 };
    z = raise(z, 'a');
    expect(topWindow(z)).toBe('a');
    expect(z.b).toBeLessThan(z.c);
    expect(raise(z, 'a')).toBe(z); // already on top: unchanged
    expect(topWindow({})).toBeNull();
    expect(raise({}, 'x')).toEqual({ x: 1 });
  });
  it('default windows fit the screen and have unique ids', () => {
    for (const s of [{ width: 1440, height: 900 }, { width: 720, height: 900 }, { width: 1920, height: 1080 }]) {
      const w = defaultWindows(s);
      expect(new Set(w.map((x) => x.id)).size).toBe(w.length);
      for (const x of w) {
        expect(x.frame.left).toBeGreaterThanOrEqual(0);
        expect(x.frame.top).toBeGreaterThanOrEqual(28);
        expect(x.frame.top + x.frame.height).toBeLessThanOrEqual(s.height);
      }
    }
  });
});

describe('shortcuts', () => {
  const key = (o: Partial<Parameters<typeof shortcutOf>[0]>) => ({ key: '', code: '', altKey: false, ctrlKey: false, shiftKey: false, metaKey: false, ...o });
  it('recognises the three hotkeys by physical key', () => {
    expect(shortcutOf(key({ key: ' ', code: 'Space', altKey: true }))).toBe('spotlight');
    expect(shortcutOf(key({ key: 'l', code: 'KeyL', altKey: true, ctrlKey: true }))).toBe('launchpad');
    expect(shortcutOf(key({ key: 'л', code: 'KeyL', altKey: true, ctrlKey: true }))).toBe('launchpad'); // Cyrillic layout
    expect(shortcutOf(key({ key: 'M', code: 'KeyM', altKey: true, ctrlKey: true }))).toBe('missionControl');
    expect(shortcutOf(key({ key: 'Escape', code: 'Escape' }))).toBe('escape');
  });
  it('requires the exact modifiers', () => {
    expect(shortcutOf(key({ key: ' ', code: 'Space' }))).toBeNull();
    expect(shortcutOf(key({ key: ' ', code: 'Space', altKey: true, shiftKey: true }))).toBeNull();
    expect(shortcutOf(key({ key: ' ', code: 'Space', altKey: true, ctrlKey: true }))).toBeNull();
    expect(shortcutOf(key({ key: 'l', code: 'KeyL', ctrlKey: true }))).toBeNull();
    expect(shortcutOf(key({ key: 'Escape', code: 'Escape', ctrlKey: true }))).toBeNull();
    expect(shortcutOf(key({ key: 'a', code: 'KeyA' }))).toBeNull();
  });
  it('a hotkey toggles its own overlay and replaces another', () => {
    expect(overlayAction(null, 'spotlight')).toBe('show');
    expect(overlayAction('spotlight', 'spotlight')).toBe('hide');
    expect(overlayAction('launchpad', 'spotlight')).toBe('show');
    expect(overlayAction('popover', 'missionControl')).toBe('show');
  });
});
