import { afterEach, describe, expect, it } from 'vitest';
import { computeDockState, defaultPins, DockWorld, fixtureWindows, type WorldHooks } from '../../mock/domains/dock';
import { MockBridge } from '../../mock/mockBridge';
import { getMockHost, resetMockHost } from '../../mock/host';
import { appByName } from '../../mock/fixtures';
import type { DockBounce, DockState, PinnedItem } from '$lib/protocol/generated';

const edge = appByName('Microsoft Edge').id;
const notepad = appByName('Notepad').id;
const spotify = appByName('Spotify').id;
const terminal = appByName('Terminal').id;
const calc = appByName('Calculator').id;

function makeWorld() {
  let pins: PinnedItem[] = defaultPins();
  const bounces: DockBounce[] = [];
  const active: Array<string | null> = [];
  let changes = 0;
  const hooks: WorldHooks = {
    getPins: () => pins, setPins: (p) => { pins = p; changes++; }, changed: () => { changes++; },
    bounce: (b) => bounces.push(b), setActiveApp: (id) => active.push(id), launchDelayMs: 0,
  };
  const world = new DockWorld(hooks, fixtureWindows());
  return { world, bounces, active, get pins() { return pins; }, get changes() { return changes; } };
}
const item = (s: DockState, id: string) => s.items.find((i) => i.id === id)!;
const code = (fn: () => void) => { try { fn(); } catch (e: any) { return e.code as string; } return 'no error'; };

describe('computeDockState', () => {
  const s = makeWorld().world.state();
  it('orders pinned apps, running-unpinned apps, folders with separators, then minimised windows apart', () => {
    const kinds = s.items.map((i) => i.kind);
    expect(kinds.filter((k) => k === 'separator')).toHaveLength(2);
    const ids = s.items.map((i) => i.id);
    expect(ids.slice(0, 9)).toEqual(defaultPins().filter((p) => p.kind === 'app').map((p) => p.id));
    expect(ids[9]).toBe('separator:1');
    expect(ids[10]).toBe(notepad);
    expect(ids[11]).toBe('separator:2');
    expect(s.items[12]).toMatchObject({ kind: 'folder', pinned: true, name: 'Downloads' });
    expect(s.minimized.map((w) => w.appId)).toEqual([spotify]);
  });
  it('marks running, windows (most recent first), active and the active item id', () => {
    expect(item(s, edge)).toMatchObject({ running: true, pinned: true, active: true });
    expect(item(s, edge).windows).toHaveLength(3);
    expect(item(s, terminal).windows).toHaveLength(1);
    expect(item(s, appByName('Mail').id).running).toBe(false);
    expect(item(s, notepad)).toMatchObject({ pinned: false, running: true });
    expect(s.activeItemId).toBe(edge);
    expect(s.items.every((i) => i.kind !== 'app' || i.badge === undefined)).toBe(true);
  });
  it('keeps minimised windows in their app (the window list shows them too)', () => {
    expect(item(s, spotify).windows.map((w) => w.minimized)).toEqual([true]);
  });
  it('omits the separators of empty groups', () => {
    const only = computeDockState({ pins: [], windows: [] });
    expect(only.items).toEqual([]);
    const pinnedOnly = computeDockState({ pins: defaultPins().slice(0, 2), windows: [] });
    expect(pinnedOnly.items.some((i) => i.kind === 'separator')).toBe(false);
  });
  it('shows launching apps (also unpinned ones) as items', () => {
    const st = computeDockState({ pins: [], windows: [], launching: new Set([calc]) });
    expect(st.items).toHaveLength(1);
    expect(st.items[0]).toMatchObject({ id: calc, launching: true, running: false, pinned: false });
  });
});

describe('DockWorld activation', () => {
  it('launching a pinned, not running app: launching placeholder + bounce, then a window and focus', () => {
    const t = makeWorld();
    const mail = appByName('Mail').id;
    t.world.activate(mail); // launchDelay 0 -> synchronous
    expect(t.bounces).toEqual([{ itemId: mail, kind: 'launch' }]);
    const s = t.world.state();
    expect(item(s, mail)).toMatchObject({ running: true, active: true, launching: false });
    expect(t.active.at(-1)).toBe(mail);
  });
  it('keeps the launching flag until the window opens when the launch is delayed', async () => {
    let n = 0;
    const hooks: WorldHooks = { getPins: () => defaultPins(), setPins() {}, changed: () => n++, bounce() {}, setActiveApp() {}, launchDelayMs: 20 };
    const world = new DockWorld(hooks, []);
    const mail = appByName('Mail').id;
    world.activate(mail);
    expect(item(world.state(), mail).launching).toBe(true);
    await new Promise((r) => setTimeout(r, 60));
    expect(item(world.state(), mail)).toMatchObject({ launching: false, running: true });
    world.dispose();
  });
  it('activating a running app focuses its most recent window and restores a minimised one', () => {
    const t = makeWorld();
    t.world.activate(spotify);
    const s = t.world.state();
    expect(item(s, spotify)).toMatchObject({ active: true });
    expect(s.minimized).toHaveLength(0);
    expect(s.activeItemId).toBe(spotify);
    expect(item(s, edge).active).toBe(false);
    expect(t.active.at(-1)).toBe(spotify);
  });
  it('activating the active single-window app minimises it (taskbar toggle)', () => {
    const t = makeWorld();
    t.world.activate(terminal); // focus
    t.world.activate(terminal); // toggle
    const s = t.world.state();
    expect(s.minimized.map((w) => w.appId)).toContain(terminal);
    expect(s.activeItemId).not.toBe(terminal);
  });
  it('folders do nothing to windows; unknown ids are notFound', () => {
    const t = makeWorld();
    const before = JSON.stringify(t.world.state());
    t.world.activate(t.world.state().items.find((i) => i.kind === 'folder')!.id);
    expect(JSON.stringify(t.world.state())).toBe(before);
    expect(code(() => t.world.activate('aumid:nope'))).toBe('notFound');
    expect(code(() => t.world.activate(''))).toBe('badRequest');
    expect(code(() => t.world.activate(42))).toBe('badRequest');
  });
});

describe('DockWorld windows', () => {
  it('activateWindow focuses (and restores) exactly that window', () => {
    const t = makeWorld();
    const second = t.world.state().items.find((i) => i.id === edge)!.windows[1];
    t.world.activateWindow(second.handle);
    const w = item(t.world.state(), edge).windows;
    expect(w[0].handle).toBe(second.handle);
    expect(w[0].active).toBe(true);
    expect(w.filter((x) => x.active)).toHaveLength(1);
    expect(code(() => t.world.activateWindow('0'))).toBe('notFound');
  });
  it('closeWindow removes it, hands the focus on, and drops the app when the last one closes', () => {
    const t = makeWorld();
    const wins = item(t.world.state(), terminal).windows;
    t.world.closeWindow(wins[0].handle);
    expect(item(t.world.state(), terminal).running).toBe(false);
    const activeEdge = item(t.world.state(), edge).windows[0];
    t.world.closeWindow(activeEdge.handle);
    expect(item(t.world.state(), edge).windows).toHaveLength(2);
    expect(t.world.state().activeItemId).toBeDefined();
  });
  it('unpinned apps disappear from the dock when their last window closes', () => {
    const t = makeWorld();
    t.world.quit(notepad);
    expect(t.world.state().items.some((i) => i.id === notepad)).toBe(false);
    expect(t.world.state().items.filter((i) => i.kind === 'separator')).toHaveLength(1);
  });
  it('quit closes all windows of ANY running app id and rejects apps that are not running', () => {
    const t = makeWorld();
    t.world.quit(edge);
    expect(item(t.world.state(), edge).running).toBe(false);
    expect(code(() => t.world.quit(edge))).toBe('notFound');
    expect(code(() => t.world.quit(calc))).toBe('notFound');
  });
  it('hide minimises all windows of the app', () => {
    const t = makeWorld();
    t.world.hide(edge);
    const s = t.world.state();
    expect(s.minimized.filter((w) => w.appId === edge)).toHaveLength(3);
    expect(item(s, edge).active).toBe(false);
    expect(s.activeItemId).not.toBe(edge);
  });
  it('newWindow adds and focuses a window, or launches a closed app', () => {
    const t = makeWorld();
    t.world.newWindow(edge);
    expect(item(t.world.state(), edge).windows).toHaveLength(4);
    t.world.newWindow(appByName('Photos').id);
    expect(item(t.world.state(), appByName('Photos').id).running).toBe(true);
    expect(code(() => t.world.newWindow(t.world.state().items.find((i) => i.kind === 'folder')!.id))).toBe('unsupported');
  });
  it('elevated windows cannot be controlled', () => {
    const t = makeWorld();
    const w = item(t.world.state(), terminal).windows[0];
    t.world.windows.find((x) => x.handle === w.handle)!.elevated = true;
    expect(code(() => t.world.activateWindow(w.handle))).toBe('failed');
    expect(code(() => t.world.closeWindow(w.handle))).toBe('failed');
  });
});

describe('DockWorld pins', () => {
  it('pin inserts at the index among the apps, keeps folders last, and is idempotent', () => {
    const t = makeWorld();
    t.world.pin(notepad, 1);
    const ids = t.pins.map((p) => p.id);
    expect(ids[1]).toBe(notepad);
    expect(t.pins.at(-1)!.kind).toBe('folder');
    const n = t.pins.length;
    t.world.pin(notepad);
    expect(t.pins).toHaveLength(n);
    t.world.pin(calc, 999);
    expect(t.pins.filter((p) => p.kind === 'app').at(-1)!.id).toBe(calc);
    expect(t.world.state().items.find((i) => i.id === notepad)).toMatchObject({ pinned: true });
  });
  it('pin validates the app and the index', () => {
    const t = makeWorld();
    expect(code(() => t.world.pin('aumid:nope'))).toBe('notFound');
    expect(code(() => t.world.pin(''))).toBe('badRequest');
    expect(code(() => t.world.pin(calc, -1))).toBe('badRequest');
    expect(code(() => t.world.pin(calc, 1.5))).toBe('badRequest');
  });
  it('unpin removes a pin (a running app stays as unpinned item) and rejects unknown ids', () => {
    const t = makeWorld();
    t.world.unpin(terminal);
    expect(t.pins.some((p) => p.id === terminal)).toBe(false);
    const it = item(t.world.state(), terminal);
    expect(it).toMatchObject({ pinned: false, running: true });
    expect(code(() => t.world.unpin(terminal))).toBe('notFound');
  });
  it('reorder needs a full permutation of the pinned ids', () => {
    const t = makeWorld();
    const ids = t.pins.map((p) => p.id);
    const next = [ids[2], ...ids.filter((_, i) => i !== 2)];
    t.world.reorder(next);
    expect(t.pins.map((p) => p.id)).toEqual(next);
    expect(code(() => t.world.reorder(ids.slice(1)))).toBe('badRequest');
    expect(code(() => t.world.reorder([...ids.slice(1), ids[1]]))).toBe('badRequest');
    expect(code(() => t.world.reorder('x'))).toBe('badRequest');
    expect(code(() => t.world.reorder([...ids.slice(0, -1), 'nope']))).toBe('badRequest');
  });
  it('reorder keeps folders behind the apps', () => {
    const t = makeWorld();
    const ids = t.pins.map((p) => p.id);
    t.world.reorder([ids.at(-1)!, ...ids.slice(0, -1)]);
    expect(t.pins.at(-1)!.kind).toBe('folder');
  });
  it('pinPath pins folders and exe/lnk files, validates the path, and ignores duplicates', () => {
    const t = makeWorld();
    t.world.pinPath('C:\\Tools\\Foo.EXE', 0);
    expect(t.pins[0]).toMatchObject({ id: 'exe:c:\\tools\\foo.exe', kind: 'app', name: 'Foo' });
    t.world.pinPath('D:\\Projects');
    expect(t.pins.at(-1)).toMatchObject({ id: 'dir:D:\\Projects', kind: 'folder', name: 'Projects' });
    const n = t.pins.length;
    t.world.pinPath('D:\\Projects');
    expect(t.pins).toHaveLength(n);
    expect(code(() => t.world.pinPath('relative\\dir'))).toBe('badRequest');
    expect(code(() => t.world.pinPath(''))).toBe('badRequest');
    expect(code(() => t.world.pinPath('C:\\' + 'x'.repeat(2000)))).toBe('badRequest');
    expect(t.world.state().items.some((i) => i.id === 'exe:c:\\tools\\foo.exe')).toBe(true);
  });
  it('flash sets attention and emits an attention bounce', () => {
    const t = makeWorld();
    t.world.flash(edge);
    expect(item(t.world.state(), edge).attention).toBe(true);
    expect(t.bounces.at(-1)).toEqual({ itemId: edge, kind: 'attention' });
    t.world.activateWindow(item(t.world.state(), edge).windows[0].handle);
    expect(item(t.world.state(), edge).attention).toBe(false);
  });
});

describe('mock host integration', () => {
  afterEach(() => resetMockHost());

  it('pushes dock.state to the dock on ready and after every mutation, and seeds settings.dock.pins', async () => {
    const dock = new MockBridge({ surface: 'dock' });
    const states: DockState[] = [];
    dock.on('dock.state', (s) => states.push(s));
    const init = await dock.call('surface.ready', { surface: 'dock' });
    expect(init.settings.dock.pins.length).toBeGreaterThan(8);
    await new Promise((r) => setTimeout(r, 0));
    expect(states.length).toBeGreaterThan(0);
    await dock.call('dock.unpin', { itemId: terminal });
    await new Promise((r) => setTimeout(r, 0));
    expect(states.at(-1)!.items.find((i) => i.id === terminal)!.pinned).toBe(false);
    expect(getMockHost().settings.dock.pins.some((p) => p.id === terminal)).toBe(false);
  });

  it('broadcasts settings.changed for pin changes and rejects bad payloads with BridgeError codes', async () => {
    const dock = new MockBridge({ surface: 'dock' });
    const settings = new MockBridge({ surface: 'settings' });
    await dock.call('surface.ready', { surface: 'dock' });
    const pins: number[] = [];
    settings.on('settings.changed', (s) => pins.push(s.dock.pins.length));
    await dock.call('dock.pinPath', { path: 'C:\\Games' });
    await new Promise((r) => setTimeout(r, 0));
    expect(pins.at(-1)).toBeGreaterThan(9);
    await expect(dock.call('dock.activate', { itemId: 'nope' })).rejects.toMatchObject({ code: 'notFound' });
    await expect(dock.call('dock.pin', { appId: '' })).rejects.toMatchObject({ code: 'badRequest' });
  });

  it('settings.patch of the pins (settings window) refreshes the dock', async () => {
    const dock = new MockBridge({ surface: 'dock' });
    const settings = new MockBridge({ surface: 'settings' });
    await dock.call('surface.ready', { surface: 'dock' });
    const seen: number[] = [];
    dock.on('dock.state', (s: DockState) => seen.push(s.items.filter((i) => i.pinned && i.kind === 'app').length));
    await settings.call('settings.patch', { patch: { dock: { pins: [] } } });
    await new Promise((r) => setTimeout(r, 0));
    expect(seen.at(-1)).toBe(0);
  });

  it('emits dock.bounce to the dock surface when launching', async () => {
    const dock = new MockBridge({ surface: 'dock' });
    await dock.call('surface.ready', { surface: 'dock' });
    const bounces: DockBounce[] = [];
    dock.on('dock.bounce', (b) => bounces.push(b));
    const mail = appByName('Mail').id;
    await dock.call('dock.activate', { itemId: mail });
    await new Promise((r) => setTimeout(r, 0));
    expect(bounces).toEqual([{ itemId: mail, kind: 'launch' }]);
  });
});
