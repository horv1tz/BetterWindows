import { describe, expect, it } from 'vitest';
import type { DesktopInfo, McSnapshot, McWindow } from '$lib/protocol/generated';
import {
  applySnapshot,
  hueFor,
  pickSelectedDesktop,
  previewRects,
  visibleWindows,
  withWindowOnDesktop,
  withoutWindow,
} from './model';

const desktops: DesktopInfo[] = [
  { id: 'd1', name: 'Desktop 1', index: 0, current: true },
  { id: 'd2', name: 'Desktop 2', index: 1, current: false },
  { id: 'd3', name: 'Desktop 3', index: 2, current: false },
];

function win(handle: string, desktopId: string | undefined, bounds = { x: 0, y: 0, width: 100, height: 100 }, minimized = false): McWindow {
  return { handle, title: `t${handle}`, appId: 'a', appName: 'App', iconUrl: '', bounds, minimized, active: false, desktopId };
}

function snap(windows: McWindow[], rev = 1, supported = true): McSnapshot {
  return { desktops, desktopsSupported: supported, windows, rev };
}

describe('applySnapshot (rev guard)', () => {
  it('accepts the first snapshot and newer revs', () => {
    const a = snap([], 1);
    expect(applySnapshot(null, a)).toBe(a);
    const b = snap([win('1', 'd1')], 2);
    expect(applySnapshot(a, b)).toBe(b);
  });

  it('drops a snapshot whose rev is lower than the applied one (mc.changed overtaking the reply)', () => {
    const current = snap([win('1', 'd1')], 5);
    const stale = snap([], 4);
    expect(applySnapshot(current, stale)).toBe(current);
  });

  it('accepts an equal rev (idempotent re-delivery)', () => {
    const current = snap([], 3);
    const same = snap([win('9', 'd1')], 3);
    expect(applySnapshot(current, same)).toBe(same);
  });
});

describe('pickSelectedDesktop', () => {
  it('keeps the previous selection while it exists', () => {
    expect(pickSelectedDesktop(desktops, 'd3')).toBe('d3');
  });

  it('falls back to the current desktop, then the first one', () => {
    expect(pickSelectedDesktop(desktops, null)).toBe('d1');
    expect(pickSelectedDesktop(desktops, 'gone')).toBe('d1');
    expect(pickSelectedDesktop([{ id: 'x', name: 'x', index: 0, current: false }, { id: 'y', name: 'y', index: 1, current: false }], null)).toBe('x');
  });

  it('is null without desktops', () => {
    expect(pickSelectedDesktop([], 'd1')).toBeNull();
  });
});

describe('visibleWindows', () => {
  const s = snap([win('1', 'd1'), win('2', 'd2'), win('3', undefined), win('4', 'd2')]);

  it('shows only the selected desktop, plus windows the host could not place', () => {
    expect(visibleWindows(s, 'd2').map((w) => w.handle)).toEqual(['2', '3', '4']);
    expect(visibleWindows(s, 'd1').map((w) => w.handle)).toEqual(['1', '3']);
  });

  it('shows every window when desktops are not supported', () => {
    const unsupported = snap(s.windows, 1, false);
    expect(visibleWindows(unsupported, 'd2')).toHaveLength(4);
    expect(visibleWindows(unsupported, null)).toHaveLength(4);
  });

  it('shows every window when there is no selection', () => {
    expect(visibleWindows(s, null)).toHaveLength(4);
  });

  it('keeps the host order (most recently active first)', () => {
    expect(visibleWindows(snap([win('b', 'd1'), win('a', 'd1')]), 'd1').map((w) => w.handle)).toEqual(['b', 'a']);
  });
});

describe('previewRects', () => {
  const monitor = { w: 1000, h: 500 };

  it('scales real bounds to fractions of the monitor', () => {
    const [r] = previewRects([win('1', 'd1', { x: 100, y: 50, width: 400, height: 250 })], monitor);
    expect(r).toEqual({ x: 0.1, y: 0.1, w: 0.4, h: 0.5 });
  });

  it('leaves minimised windows out', () => {
    expect(previewRects([win('1', 'd1', undefined, true)], monitor)).toEqual([]);
  });

  it('clamps windows that hang off the monitor and drops the empty ones', () => {
    const rects = previewRects([
      win('off', 'd1', { x: 900, y: -200, width: 400, height: 300 }),
      win('gone', 'd1', { x: 2000, y: 0, width: 100, height: 100 }),
    ], monitor);
    expect(rects).toHaveLength(1);
    expect(rects[0]!.x).toBeCloseTo(0.9, 9);
    expect(rects[0]!.y).toBe(0);
    expect(rects[0]!.x + rects[0]!.w).toBeCloseTo(1, 9);
  });

  it('is empty for a degenerate monitor', () => {
    expect(previewRects([win('1', 'd1')], { w: 0, h: 0 })).toEqual([]);
  });
});

describe('optimistic local updates', () => {
  it('withoutWindow removes exactly that window', () => {
    const next = withoutWindow(snap([win('1', 'd1'), win('2', 'd1')]), '1');
    expect(next.windows.map((w) => w.handle)).toEqual(['2']);
  });

  it('withWindowOnDesktop reassigns exactly that window and does not mutate the input', () => {
    const original = snap([win('1', 'd1'), win('2', 'd1')]);
    const next = withWindowOnDesktop(original, '2', 'd3');
    expect(next.windows.map((w) => w.desktopId)).toEqual(['d1', 'd3']);
    expect(original.windows[1]!.desktopId).toBe('d1');
  });
});

describe('hueFor', () => {
  it('is stable, in range and varies between keys', () => {
    expect(hueFor('aumid:x')).toBe(hueFor('aumid:x'));
    expect(hueFor('aumid:x')).toBeGreaterThanOrEqual(0);
    expect(hueFor('aumid:x')).toBeLessThan(360);
    expect(hueFor('a')).not.toBe(hueFor('b'));
  });
});
