import { describe, expect, it } from 'vitest';
import { defaultSettings, type Settings } from '$lib/protocol/generated';
import { applyPatch, clearPlacementsPatch, clone, deepEqual, diffPatch, moveItem, patchAt, resetAllPatch, toggleProvider, wallpaperEntryPatch } from './patch';
import { OptimisticSettings } from './optimistic';

const base = (): Settings => defaultSettings();

describe('patchAt', () => {
  it('builds nested minimal patches', () => {
    expect(patchAt('dock.iconSize', 72)).toEqual({ dock: { iconSize: 72 } });
    expect(patchAt('general.features.trayHosting', true)).toEqual({ general: { features: { trayHosting: true } } });
    expect(patchAt('x', 1)).toEqual({ x: 1 });
  });
});

describe('applyPatch (RFC 7386 as the host applies it)', () => {
  it('merges objects, replaces arrays and primitives', () => {
    const s = applyPatch(base(), { dock: { iconSize: 80 }, spotlight: { providers: ['apps'] } });
    expect(s.dock.iconSize).toBe(80);
    expect(s.dock.magnification).toBe(true); // untouched sibling
    expect(s.spotlight.providers).toEqual(['apps']);
  });

  it('does not mutate its input', () => {
    const s = base();
    applyPatch(s, { dock: { iconSize: 99 } });
    expect(s.dock.iconSize).toBe(56);
  });

  it('null restores the default of a fixed field', () => {
    const cur = applyPatch(base(), { dock: { iconSize: 100 } });
    expect(applyPatch(cur, { dock: { iconSize: null } }).dock.iconSize).toBe(56);
  });

  it('null on a map entry removes it, adding an entry works', () => {
    let s = applyPatch(base(), wallpaperEntryPatch('m1', 'C:\\a.jpg'));
    expect(s.desktop.wallpapers).toEqual({ m1: 'C:\\a.jpg' });
    s = applyPatch(s, wallpaperEntryPatch('m2', 'C:\\b.jpg'));
    expect(Object.keys(s.desktop.wallpapers)).toEqual(['m1', 'm2']);
    s = applyPatch(s, wallpaperEntryPatch('m1', null));
    expect(s.desktop.wallpapers).toEqual({ m2: 'C:\\b.jpg' });
  });

  it('null on the placements map resets it to {}', () => {
    let s = base();
    s.desktop.placements = { 'fs:c:\\x': { x: 1, y: 2 } };
    s = applyPatch(s, clearPlacementsPatch());
    expect(s.desktop.placements).toEqual({});
  });
});

describe('diffPatch', () => {
  it('is undefined for equal values and minimal otherwise', () => {
    expect(diffPatch(base(), base())).toBeUndefined();
    const b = base();
    b.dock.iconSize = 90;
    b.hotCorners.topRight = 'launchpad';
    expect(diffPatch(base(), b)).toEqual({ dock: { iconSize: 90 }, hotCorners: { topRight: 'launchpad' } });
  });

  it('arrays replace as a whole', () => {
    const b = base();
    b.spotlight.providers = ['apps', 'calc'];
    expect(diffPatch(base(), b)).toEqual({ spotlight: { providers: ['apps', 'calc'] } });
  });

  it('removed map keys become null', () => {
    const a = base(); a.desktop.wallpapers = { m1: 'x', m2: 'y' };
    const b = base(); b.desktop.wallpapers = { m2: 'y' };
    expect(diffPatch(a, b)).toEqual({ desktop: { wallpapers: { m1: null } } });
  });

  it('round-trips: applying the diff yields the target', () => {
    const a = base();
    a.dock.position = 'left'; a.desktop.wallpapers = { m1: 'x' }; a.desktop.shortcuts = [{ id: 's', name: 'n', target: 't', x: 0, y: 0 }];
    const b = base();
    expect(deepEqual(applyPatch(a, diffPatch(a, b)), b)).toBe(true);
  });
});

describe('resetAllPatch', () => {
  it('is empty for default settings', () => {
    expect(resetAllPatch(base())).toEqual({});
  });
  it('contains only what differs and never touches version or firstRunDone', () => {
    const cur = base();
    cur.version = 7;
    cur.general.firstRunDone = true;
    cur.general.language = 'ru';
    cur.dock.iconSize = 100;
    const p = resetAllPatch(cur) as any;
    expect(p).toEqual({ general: { language: 'auto' }, dock: { iconSize: 56 } });
    expect(p.version).toBeUndefined();
    expect(p.general.firstRunDone).toBeUndefined();
    const after = applyPatch(cur, p);
    expect(after.dock.iconSize).toBe(56);
    expect(after.general.firstRunDone).toBe(true);
  });
  it('resets lists and maps too', () => {
    const cur = base();
    cur.desktop.wallpapers = { m1: 'a' };
    cur.desktop.shortcuts = [{ id: 's', name: 'n', target: 't', x: 0, y: 0 }];
    const p = resetAllPatch(cur) as any;
    expect(p.desktop.wallpapers).toEqual({ m1: null });
    expect(p.desktop.shortcuts).toEqual([]);
  });
});

describe('list helpers', () => {
  it('moveItem clamps and keeps the rest in order', () => {
    expect(moveItem(['a', 'b', 'c'], 0, 1)).toEqual(['b', 'a', 'c']);
    expect(moveItem(['a', 'b', 'c'], 2, 0)).toEqual(['c', 'a', 'b']);
    expect(moveItem(['a', 'b', 'c'], 1, 99)).toEqual(['a', 'c', 'b']);
    expect(moveItem(['a', 'b', 'c'], 1, -5)).toEqual(['b', 'a', 'c']);
    expect(moveItem(['a', 'b', 'c'], 5, 0)).toEqual(['a', 'b', 'c']);
    expect(moveItem(['a'], 0, 0)).toEqual(['a']);
  });
  it('toggleProvider keeps order and avoids duplicates', () => {
    expect(toggleProvider(['apps', 'calc'], 'web', true)).toEqual(['apps', 'calc', 'web']);
    expect(toggleProvider(['apps', 'calc'], 'calc', true)).toEqual(['apps', 'calc']);
    expect(toggleProvider(['apps', 'calc'], 'apps', false)).toEqual(['calc']);
    expect(toggleProvider(['apps'], 'zzz', false)).toEqual(['apps']);
  });
  it('clone/deepEqual basics', () => {
    const o = { a: [1, { b: 2 }] };
    expect(deepEqual(o, clone(o))).toBe(true);
    expect(deepEqual({ a: 1 }, { a: 1, b: undefined })).toBe(false);
    expect(deepEqual([1], [1, 2])).toBe(false);
  });
});

describe('OptimisticSettings', () => {
  it('shows a patch immediately and keeps it after the ack', () => {
    const o = new OptimisticSettings(base());
    const seq = o.begin({ dock: { autoHide: true } });
    expect(o.view.dock.autoHide).toBe(true);
    const stored = applyPatch(base(), { dock: { autoHide: true } });
    o.ack(seq, stored);
    expect(o.view.dock.autoHide).toBe(true);
    expect(o.pendingCount).toBe(0);
  });

  it('adopts the host-clamped value from the ack', () => {
    const o = new OptimisticSettings(base());
    const seq = o.begin({ dock: { iconSize: 999 } });
    expect(o.view.dock.iconSize).toBe(999);
    o.ack(seq, applyPatch(base(), { dock: { iconSize: 128 } }));
    expect(o.view.dock.iconSize).toBe(128);
  });

  it('rolls back only the failed patch', () => {
    const o = new OptimisticSettings(base());
    const a = o.begin({ dock: { autoHide: true } });
    const b = o.begin({ menuBar: { showSeconds: true } });
    o.fail(a);
    expect(o.view.dock.autoHide).toBe(false);
    expect(o.view.menuBar.showSeconds).toBe(true);
    o.ack(b, applyPatch(base(), { menuBar: { showSeconds: true } }));
    expect(o.view.menuBar.showSeconds).toBe(true);
  });

  it('later pending patches stay on top of an earlier ack', () => {
    const o = new OptimisticSettings(base());
    const a = o.begin({ dock: { iconSize: 60 } });
    o.begin({ dock: { iconSize: 70 } });
    o.ack(a, applyPatch(base(), { dock: { iconSize: 60 } }));
    expect(o.view.dock.iconSize).toBe(70);
    expect(o.pendingCount).toBe(1);
  });

  it('ignores the result of an older patch that arrives after a newer ack', () => {
    const o = new OptimisticSettings(base());
    const a = o.begin({ dock: { iconSize: 60 } });
    const b = o.begin({ dock: { iconSize: 70 } });
    o.ack(b, applyPatch(base(), { dock: { iconSize: 70 } }));
    o.ack(a, applyPatch(base(), { dock: { iconSize: 60 } })); // stale
    expect(o.view.dock.iconSize).toBe(70);
  });

  it('settings.changed replaces the confirmed state but keeps pending optimism', () => {
    const o = new OptimisticSettings(base());
    o.begin({ dock: { autoHide: true } });
    o.external(applyPatch(base(), { menuBar: { use24Hour: true } }));
    expect(o.view.menuBar.use24Hour).toBe(true);
    expect(o.view.dock.autoHide).toBe(true);
  });

  it('view is stable between mutations (same object) and fresh after one', () => {
    const o = new OptimisticSettings(base());
    const v1 = o.view;
    expect(o.view).toBe(v1);
    o.begin({ dock: { autoHide: true } });
    expect(o.view).not.toBe(v1);
  });

  it('null patches restore defaults optimistically', () => {
    const o = new OptimisticSettings(applyPatch(base(), { dock: { iconSize: 100 } }));
    o.begin({ dock: { iconSize: null } });
    expect(o.view.dock.iconSize).toBe(56);
  });
});
