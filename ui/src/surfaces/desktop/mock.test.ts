// Behaviour of the ui-desktop mock domains (desktop / reminders / weather) through the same bridge API the UI uses.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MockBridge } from '$mock/mockBridge';
import { resetMockHost } from '$mock/host';
import { settingsBridge } from './testSupport';
import type { DesktopState, ReminderList } from '$lib/protocol/generated';
import { makeWeather, searchCities } from '$mock/domains/weather';
import { applyReminderUpdate, isoLocal, seedReminders } from '$mock/domains/reminders';

const tick = () => new Promise((r) => setTimeout(r, 0));

function setQuery(q: string) { window.history.replaceState(null, '', `/${q}`); }

async function desktop(monitorId = '1') {
  const b = new MockBridge({ surface: 'desktop', monitorId });
  let state!: DesktopState;
  b.on('desktop.state', (s) => (state = s));
  await b.call('surface.ready', { surface: 'desktop', monitorId });
  await tick(); await tick();
  return { b, get state() { return state; } };
}

beforeEach(() => { setQuery(''); vi.spyOn(console, 'info').mockImplementation(() => {}); });
afterEach(() => { resetMockHost(); vi.useRealTimers(); vi.restoreAllMocks(); setQuery(''); });

describe('desktop mock: state', () => {
  it('pushes an enabled desktop with icons, widgets and a wallpaper to the primary monitor after surface.ready', async () => {
    const d = await desktop();
    expect(d.state.enabled).toBe(true);
    expect(d.state.monitorId).toBe('1');
    expect(d.state.iconSize).toBe(64);
    expect(d.state.wallpaperUrl).toMatch(/^data:image\/svg\+xml/);
    expect(d.state.icons.map((i) => i.kind)).toEqual(expect.arrayContaining(['special', 'shortcut', 'folder', 'file']));
    expect(d.state.icons.find((i) => i.id === 'special:thisPc')).toBeTruthy();
    expect(d.state.widgets.map((w) => w.type).sort()).toEqual(['calendar', 'clock', 'notes', 'reminders', 'weather']);
    // every icon has a unique cell
    expect(new Set(d.state.icons.map((i) => `${i.x},${i.y}`)).size).toBe(d.state.icons.length);
  });

  it('the second monitor gets its own wallpaper and widgets but no icons', async () => {
    const one = await desktop('1');
    const two = await desktop('2');
    expect(two.state.monitorId).toBe('2');
    expect(two.state.icons).toEqual([]);
    expect(two.state.wallpaperUrl).not.toBe(one.state.wallpaperUrl);
    expect(two.state.widgets.length).toBeGreaterThan(0);
    expect(two.state.widgets.every((w) => w.monitorId === '2')).toBe(true);
    expect(one.state.widgets.every((w) => !w.monitorId)).toBe(true);
  });

  it('?widgets=none seeds no widgets, ?widgets=all seeds media and battery too', async () => {
    setQuery('?widgets=none');
    expect((await desktop()).state.widgets).toEqual([]);
    resetMockHost();
    setQuery('?widgets=all');
    const types = (await desktop()).state.widgets.map((w) => w.type);
    expect(types).toEqual(expect.arrayContaining(['media', 'battery']));
  });

  it('showDesktopFolder=false leaves only user shortcuts', async () => {
    const d = await desktop();
    await settingsBridge().call('settings.patch', { patch: { desktop: { showDesktopFolder: false } } });
    await tick(); await tick();
    expect(d.state.icons.every((i) => i.kind === 'shortcut')).toBe(true);
  });
});

describe('desktop mock: icons', () => {
  it('moves shortcuts and desktop-folder items and re-emits the state', async () => {
    const d = await desktop();
    await d.b.call('desktop.moveIcon', { id: 'sc:documents', x: 5, y: 1 });
    await tick(); await tick();
    expect(d.state.icons.find((i) => i.id === 'sc:documents')).toMatchObject({ x: 5, y: 1 });
    await d.b.call('desktop.moveIcon', { id: 'special:recycleBin', x: 6, y: 2 });
    await tick(); await tick();
    expect(d.state.icons.find((i) => i.id === 'special:recycleBin')).toMatchObject({ x: 6, y: 2 });
  });
  it('rejects unknown ids and bad cells', async () => {
    const d = await desktop();
    await expect(d.b.call('desktop.moveIcon', { id: 'nope', x: 1, y: 1 })).rejects.toMatchObject({ code: 'notFound' });
    await expect(d.b.call('desktop.moveIcon', { id: 'sc:documents', x: -1, y: 1 })).rejects.toMatchObject({ code: 'badRequest' });
    await expect(d.b.call('desktop.moveIcon', { id: 'sc:documents', x: 1.5, y: 1 })).rejects.toMatchObject({ code: 'badRequest' });
    await expect(d.b.call('desktop.open', { id: 'nope' })).rejects.toMatchObject({ code: 'notFound' });
  });
  it('open works for every kind', async () => {
    const d = await desktop();
    for (const i of d.state.icons) await expect(d.b.call('desktop.open', { id: i.id })).resolves.toBeUndefined();
  });
  it('adds and removes shortcuts with path validation', async () => {
    const d = await desktop();
    await expect(d.b.call('desktop.addShortcut', { path: 'relative\\x.exe' })).rejects.toMatchObject({ code: 'badRequest' });
    const added = await d.b.call('desktop.addShortcut', { path: 'C:\\Tools\\Cool App.exe' });
    expect(added).toMatchObject({ name: 'Cool App', kind: 'shortcut' });
    await tick(); await tick();
    expect(d.state.icons.some((i) => i.id === added.id)).toBe(true);
    await d.b.call('desktop.removeShortcut', { id: added.id });
    await tick(); await tick();
    expect(d.state.icons.some((i) => i.id === added.id)).toBe(false);
    await expect(d.b.call('desktop.removeShortcut', { id: added.id })).rejects.toMatchObject({ code: 'notFound' });
  });
  it('setWallpaper changes the URL for one monitor or for all', async () => {
    const one = await desktop('1');
    const two = await desktop('2');
    const before1 = one.state.wallpaperUrl, before2 = two.state.wallpaperUrl;
    await one.b.call('desktop.setWallpaper', { path: 'C:\\Pictures\\a.jpg', monitorId: '2' });
    await tick(); await tick();
    expect(one.state.wallpaperUrl).toBe(before1);
    expect(two.state.wallpaperUrl).not.toBe(before2);
    await one.b.call('desktop.setWallpaper', { path: 'C:\\Pictures\\b.jpg' });
    await tick(); await tick();
    expect(one.state.wallpaperUrl).not.toBe(before1);
    await expect(one.b.call('desktop.setWallpaper', { path: 'b.jpg' })).rejects.toMatchObject({ code: 'badRequest' });
  });
});

describe('desktop mock: widgets', () => {
  it('addWidget cascades at (48+24n, 80+24n) with default config', async () => {
    const d = await desktop();
    const n = d.state.widgets.length;
    const w = await d.b.call('desktop.addWidget', { type: 'battery' });
    expect(w).toMatchObject({ type: 'battery', size: 'medium', x: 48 + 24 * n, y: 80 + 24 * n });
    const notes = await d.b.call('desktop.addWidget', { type: 'notes', size: 'large', x: 10, y: 20 });
    expect(notes).toMatchObject({ size: 'large', x: 10, y: 20, config: { text: '' } });
    await tick(); await tick();
    expect(d.state.widgets.some((x) => x.id === w.id)).toBe(true);
    await expect(d.b.call('desktop.addWidget', { type: 'bogus' as any })).rejects.toMatchObject({ code: 'badRequest' });
  });
  it('moveWidget / updateWidget / removeWidget mutate and re-emit; config is REPLACED', async () => {
    const d = await desktop();
    const note = d.state.widgets.find((w) => w.type === 'notes')!;
    await d.b.call('desktop.moveWidget', { id: note.id, x: 123.5, y: 77 });
    const upd = await d.b.call('desktop.updateWidget', { id: note.id, size: 'large', config: { text: 'hello' } });
    expect(upd).toMatchObject({ size: 'large', config: { text: 'hello' } });
    await tick(); await tick();
    expect(d.state.widgets.find((w) => w.id === note.id)).toMatchObject({ x: 123.5, y: 77, size: 'large', config: { text: 'hello' } });
    // replacing: keys not sent are gone
    await d.b.call('desktop.updateWidget', { id: note.id, config: { other: 1 } });
    await tick(); await tick();
    expect(d.state.widgets.find((w) => w.id === note.id)!.config).toEqual({ other: 1 });
    // absent config = unchanged
    await d.b.call('desktop.updateWidget', { id: note.id, size: 'small' });
    await tick(); await tick();
    expect(d.state.widgets.find((w) => w.id === note.id)).toMatchObject({ size: 'small', config: { other: 1 } });
    await d.b.call('desktop.removeWidget', { id: note.id });
    await tick(); await tick();
    expect(d.state.widgets.some((w) => w.id === note.id)).toBe(false);
    await expect(d.b.call('desktop.removeWidget', { id: note.id })).rejects.toMatchObject({ code: 'notFound' });
    await expect(d.b.call('desktop.updateWidget', { id: 'zzz', size: 'small' })).rejects.toMatchObject({ code: 'notFound' });
    await expect(d.b.call('desktop.updateWidget', { id: 'w-clock', config: 'x' as any })).rejects.toMatchObject({ code: 'badRequest' });
    await expect(d.b.call('desktop.moveWidget', { id: 'w-clock', x: NaN, y: 0 })).rejects.toMatchObject({ code: 'badRequest' });
  });
  it('changes made on one monitor never leak into the other', async () => {
    const one = await desktop('1');
    const two = await desktop('2');
    const w = await two.b.call('desktop.addWidget', { type: 'clock', monitorId: '2' });
    await tick(); await tick();
    expect(two.state.widgets.some((x) => x.id === w.id)).toBe(true);
    expect(one.state.widgets.some((x) => x.id === w.id)).toBe(false);
  });
});

describe('reminders mock', () => {
  async function connect() {
    const b = new MockBridge({ surface: 'desktop', monitorId: '1' });
    let list!: ReminderList;
    b.on('reminders.changed', (l) => (list = l));
    await b.call('surface.ready', { surface: 'desktop', monitorId: '1' });
    await tick(); await tick();
    return { b, get list() { return list; } };
  }
  it('pushes the list on ready and supports add/update/remove with live reminders.changed', async () => {
    const r = await connect();
    expect(r.list.items.length).toBeGreaterThan(3);
    expect(r.list.items.some((i) => i.done)).toBe(true);
    const added = await r.b.call('reminders.add', { title: '  Water the plants ', list: 'Home' });
    expect(added).toMatchObject({ title: 'Water the plants', done: false, list: 'Home' });
    await tick(); await tick();
    expect(r.list.items.at(-1)!.id).toBe(added.id);
    const done = await r.b.call('reminders.update', { id: added.id, done: true });
    expect(done.done).toBe(true);
    await r.b.call('reminders.remove', { id: added.id });
    await tick(); await tick();
    expect(r.list.items.some((i) => i.id === added.id)).toBe(false);
  });
  it('validates input', async () => {
    const r = await connect();
    await expect(r.b.call('reminders.add', { title: '   ' })).rejects.toMatchObject({ code: 'badRequest' });
    await expect(r.b.call('reminders.add', { title: 'x', due: 'not a date' })).rejects.toMatchObject({ code: 'badRequest' });
    await expect(r.b.call('reminders.update', { id: 'nope', done: true })).rejects.toMatchObject({ code: 'notFound' });
    await expect(r.b.call('reminders.remove', { id: 'nope' })).rejects.toMatchObject({ code: 'notFound' });
    await expect(r.b.call('reminders.update', { id: 'r1', title: '' })).rejects.toMatchObject({ code: 'badRequest' });
  });
  it('empty string clears due / notes / list, absent leaves them', () => {
    const base = { id: 'x', title: 't', done: false, due: '2026-01-01T10:00:00', notes: 'n', list: 'L' };
    expect(applyReminderUpdate(base, { due: '' })).not.toHaveProperty('due');
    expect(applyReminderUpdate(base, { notes: '' })).not.toHaveProperty('notes');
    expect(applyReminderUpdate(base, { list: '' })).not.toHaveProperty('list');
    expect(applyReminderUpdate(base, { done: true })).toMatchObject({ due: base.due, notes: 'n', list: 'L', done: true });
    expect(applyReminderUpdate(base, { due: '2026-02-02T09:00:00' }).due).toBe('2026-02-02T09:00:00');
  });
  it('seed data contains an overdue, a today, a tomorrow, an undated and a completed reminder', () => {
    const now = new Date(2026, 9, 23, 10, 0);
    const items = seedReminders(now);
    expect(items.some((i) => !i.done && i.due && new Date(i.due) < now)).toBe(true);
    expect(items.some((i) => i.due?.startsWith('2026-10-23'))).toBe(true);
    expect(items.some((i) => i.due?.startsWith('2026-10-24'))).toBe(true);
    expect(items.some((i) => !i.due && !i.done)).toBe(true);
    expect(items.some((i) => i.done)).toBe(true);
    expect(isoLocal(new Date(2026, 0, 2, 3, 4))).toBe('2026-01-02T03:04:00');
  });
  it('fires reminders.due once when a reminder becomes due', async () => {
    vi.useFakeTimers();
    setQuery('?reminders=soon');
    const b = new MockBridge({ surface: 'desktop', monitorId: '1' });
    const due: string[] = [];
    b.on('reminders.due', (r) => due.push(r.id));
    await b.call('surface.ready', { surface: 'desktop', monitorId: '1' });
    await vi.advanceTimersByTimeAsync(50);
    expect(due).toEqual([]);
    await vi.advanceTimersByTimeAsync(3 * 60_000);
    expect(due).toEqual(['soon']);
    await vi.advanceTimersByTimeAsync(5 * 60_000);
    expect(due).toEqual(['soon']);
  });
});

describe('weather mock', () => {
  const bridge = () => new MockBridge({ surface: 'desktop', monitorId: '1' });
  it('searches cities by English or Russian name', () => {
    expect(searchCities('mos')[0]).toMatchObject({ name: 'Moscow' });
    expect(searchCities('Москва')[0]).toMatchObject({ name: 'Moscow' });
    expect(searchCities('lond').map((c) => c.country)).toEqual(['United Kingdom', 'Canada']);
    expect(searchCities('  ')).toEqual([]);
    expect(searchCities('zzzzzz')).toEqual([]);
  });
  it('fixture is deterministic, complete and consistent', () => {
    const now = new Date(2026, 9, 23, 15, 20);
    const a = makeWeather({ name: 'Berlin', latitude: 52.52, longitude: 13.4 }, now);
    const b = makeWeather({ name: 'Berlin', latitude: 52.52, longitude: 13.4 }, now);
    expect(a).toEqual(b);
    expect(a.available).toBe(true);
    expect(a.hourly).toHaveLength(24);
    expect(a.daily).toHaveLength(7);
    expect(a.hourly[0].time).toBe('2026-10-23T15:00');
    expect(a.daily[0].date).toBe('2026-10-23');
    expect(a.daily[0].highC).toBeGreaterThanOrEqual(a.tempC);
    expect(a.daily[0].lowC).toBeLessThanOrEqual(a.tempC);
    for (const d of a.daily) expect(d.highC).toBeGreaterThan(d.lowC);
    expect(a.isDay).toBe(true);
    expect(makeWeather({ name: 'Berlin', latitude: 52.52, longitude: 13.4 }, new Date(2026, 9, 23, 2, 0)).isDay).toBe(false);
    // the southern hemisphere is warmer in January, the northern colder
    const jan = new Date(2026, 0, 15, 12);
    expect(makeWeather({ name: 'Sydney', latitude: -33.9, longitude: 151 }, jan).tempC).toBeGreaterThan(makeWeather({ name: 'Moscow', latitude: 55.8, longitude: 37.6 }, jan).tempC + 10);
  });
  it('weather.get returns a snapshot for the configured location, setLocation switches city and broadcasts settings', async () => {
    const b = bridge();
    await b.call('surface.ready', { surface: 'desktop', monitorId: '1' });
    const snap = await b.call('weather.get', { force: false });
    expect(snap.available).toBe(true);
    expect(snap.location).toBe('Moscow');
    const seen: string[] = [];
    b.on('settings.changed', (s) => seen.push(s.weather.locationName));
    await settingsBridge().call('weather.setLocation', { name: 'Sydney', latitude: -33.87, longitude: 151.2 });
    await tick(); await tick();
    expect(seen.at(-1)).toBe('Sydney');
    expect((await b.call('weather.get', { force: false })).location).toBe('Sydney');
    const settings = settingsBridge();
    await expect(settings.call('weather.setLocation', { name: '', latitude: 0, longitude: 0 })).rejects.toMatchObject({ code: 'badRequest' });
    await expect(settings.call('weather.setLocation', { name: 'X', latitude: 99, longitude: 0 })).rejects.toMatchObject({ code: 'badRequest' });
    const found = await settings.call('weather.search', { query: 'Pari' });
    expect(found.locations[0].name).toBe('Paris');
  });
  it('?weather=none gives an unavailable snapshot without error, ?weather=error rejects, error-once recovers', async () => {
    setQuery('?weather=none');
    let b = bridge();
    await b.call('surface.ready', { surface: 'desktop', monitorId: '1' });
    expect(await b.call('weather.get', { force: true })).toMatchObject({ available: false });
    resetMockHost();
    setQuery('?weather=error');
    b = bridge();
    await b.call('surface.ready', { surface: 'desktop', monitorId: '1' });
    await expect(b.call('weather.get', { force: true })).rejects.toMatchObject({ code: 'failed' });
    resetMockHost();
    setQuery('?weather=error-once');
    b = bridge();
    await b.call('surface.ready', { surface: 'desktop', monitorId: '1' });
    await expect(b.call('weather.get', { force: true })).rejects.toMatchObject({ code: 'failed' });
    expect((await b.call('weather.get', { force: true })).available).toBe(true);
  });
});
