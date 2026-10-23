import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { setBridgeForTests, setInit } from '$lib/ipc';
import { i18n } from '$lib/i18n.svelte';
import { MockBridge } from '$mock/mockBridge';
import { getMockHost, resetMockHost } from '$mock/host';
import { appByName } from '$mock/fixtures';
import type { OverlayRequest } from '$lib/protocol/generated';
import { flushSync, mountComponent, tick, type Mounted } from './testing/mount';
import Surface from './Surface.svelte';

globalThis.ResizeObserver ??= class { observe() {} unobserve() {} disconnect() {} } as unknown as typeof ResizeObserver;

type MockStatus = Record<string, any>;
const host = () => getMockHost() as unknown as ReturnType<typeof getMockHost> & { status: MockStatus; pushStatus: () => void; setActiveApp: (id: string | null) => void };

let bridge: MockBridge;
let mounted: Mounted;
const opened: OverlayRequest[] = [];

/** Boots a menu bar surface against a fresh mock host. `prepare` can change mock state before the first state push. */
async function boot(prepare?: () => void, route: { monitorId?: string } = { monitorId: '1' }) {
  prepare?.();
  bridge = new MockBridge({ surface: 'menubar', ...route });
  setBridgeForTests(bridge);
  setInit(await bridge.call('surface.ready', { surface: 'menubar', ...route }));
  mounted = mountComponent(Surface);
  await settle();
}
/** Lets queued state events (microtasks) reach the component and flushes its effects. */
async function settle() {
  if (vi.isFakeTimers()) await vi.advanceTimersByTimeAsync(1);
  else await new Promise((r) => setTimeout(r, 0));
  flushSync();
  await tick();
}
const items = () => [...document.querySelectorAll<HTMLButtonElement>('[data-bar-item]')];
const labels = () => items().map((i) => i.getAttribute('aria-label'));
const item = (startsWith: string) => {
  const el = items().find((i) => i.getAttribute('aria-label')?.startsWith(startsWith));
  if (!el) throw new Error(`no bar item '${startsWith}' in ${JSON.stringify(labels())}`);
  return el;
};
const has = (startsWith: string) => items().some((i) => i.getAttribute('aria-label')?.startsWith(startsWith));
const click = async (el: HTMLElement) => { el.click(); flushSync(); await tick(); };
const push = async () => { host().pushStatus(); await settle(); };

beforeEach(() => {
  resetMockHost();
  i18n.set('en');
  opened.length = 0;
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
    const n = this.hasAttribute('data-bar-item') ? 1 : 0;
    return { x: 100 * n, y: 3 * n, width: 30 * n, height: 22 * n, left: 100 * n, top: 3 * n, right: 130 * n, bottom: 25 * n, toJSON: () => ({}) } as DOMRect;
  });
  // overlay.open is recorded instead of driving the overlay mock
  getMockHost().handle('overlay.open', ((p: OverlayRequest) => { opened.push(p); }) as never);
});
afterEach(() => {
  vi.restoreAllMocks();
  vi.useRealTimers();
  mounted?.unmount();
  setBridgeForTests(null);
  setInit(null);
});

describe('menu bar: layout and visibility', () => {
  it('renders the default items left to right: system, app name, then the right cluster ending with the clock', async () => {
    await boot();
    expect(labels().map((l) => l!.replace(/[0-9]+/g, 'N'))).toEqual([
      'System menu', 'File Explorer', 'Now playing: Blinding Lights by The Weeknd', 'Bluetooth, N connected', 'Wi-Fi Home-NG, signal N of N',
      'Sound, volume N%', 'Battery N%', 'Spotlight search', 'Control Center', expect.stringMatching(/^Date and time, /),
    ]);
  });

  it('names the toolbar after the monitor', async () => {
    await boot();
    expect(document.querySelector('[role="toolbar"]')?.getAttribute('aria-label')).toBe('Menu bar, Mock display');
  });

  it('hides the battery item on a machine without a battery', async () => {
    await boot(() => { host().status.battery = { present: false, percent: 0, charging: false, acOnline: true }; });
    expect(has('Battery')).toBe(false);
  });

  it('shows battery percent text according to settings.menuBar.showBatteryPercent', async () => {
    await boot();
    expect(document.querySelector('.pct')?.textContent).toBe('76%');
    await bridge.call('settings.patch', { patch: { menuBar: { showBatteryPercent: false } } });
    await settle();
    expect(document.querySelector('.pct')).toBeNull();
    expect(has('Battery')).toBe(true);
  });

  it('describes the battery state in its accessible name', async () => {
    await boot();
    host().status.battery = { present: true, percent: 80, charging: true, acOnline: true };
    await push();
    expect(item('Battery').getAttribute('aria-label')).toBe('Battery 80%, charging');
    host().status.battery = { present: true, percent: 15, charging: false, acOnline: false };
    await push();
    expect(item('Battery').getAttribute('aria-label')).toBe('Battery 15%, low');
  });

  it('reflects muted sound and a missing connection', async () => {
    await boot();
    host().status.audio = { ...host().status.audio, muted: true };
    host().status.network = { kind: 'none', connected: false, signalBars: 0 };
    await push();
    expect(item('Sound').getAttribute('aria-label')).toBe('Sound, muted');
    expect(has('Network: no connection')).toBe(true);
  });

  it('only shows Bluetooth when supported, dimmed when off', async () => {
    await boot();
    host().status.bluetooth = { supported: true, enabled: false, devices: [] };
    await push();
    expect(item('Bluetooth, off').classList.contains('dim')).toBe(true);
    host().status.bluetooth = { supported: false, enabled: false, devices: [] };
    await push();
    expect(has('Bluetooth')).toBe(false);
  });

  it('shows now playing only with an available session that has a title (playing or paused)', async () => {
    await boot();
    expect(item('Now playing').classList.contains('dim')).toBe(false);
    host().status.media = { ...host().status.media, playing: false };
    await push();
    expect(item('Paused:').classList.contains('dim')).toBe(true);
    host().status.media = { ...host().status.media, title: '' };
    await push();
    expect(has('Paused') || has('Now playing')).toBe(false);
    host().status.media = { ...host().status.media, title: 'Back', available: false };
    await push();
    expect(has('Paused') || has('Now playing')).toBe(false);
  });

  it('shows tray icons only when the host can host a tray', async () => {
    const icon = { id: 't1', tooltip: 'Dropbox', iconUrl: 'x.png', hidden: false };
    await boot(() => { host().status.tray = { supported: false, icons: [icon] }; });
    expect(has('Dropbox')).toBe(false);
    host().status.tray = { supported: true, icons: [icon, { ...icon, id: 't2', tooltip: 'Parked', hidden: true }] };
    await push();
    expect(has('Dropbox')).toBe(true);
    expect(has('Parked')).toBe(false);
  });

  it('waits for the first state before showing a status item (no wrong default flash)', async () => {
    bridge = new MockBridge({ surface: 'menubar', monitorId: '1' });
    setBridgeForTests(bridge);
    setInit(await bridge.call('surface.ready', { surface: 'menubar', monitorId: '1' }));
    // mount synchronously, before the queued status events are delivered
    mounted = mountComponent(Surface);
    expect(labels()).toEqual(['System menu', 'Desktop', 'Spotlight search', 'Control Center', expect.stringMatching(/^Date and time/)]);
    await settle();
    expect(has('Battery')).toBe(true);
  });

  it('uses the solid style when translucency is off', async () => {
    await boot();
    expect(document.querySelector('.bar')?.classList.contains('solid')).toBe(false);
    await bridge.call('settings.patch', { patch: { menuBar: { translucent: false } } });
    await settle();
    expect(document.querySelector('.bar')?.classList.contains('solid')).toBe(true);
  });
});

describe('menu bar: active app', () => {
  it('shows the name of the active app and updates it', async () => {
    await boot();
    expect(item('File Explorer').textContent?.trim()).toBe('File Explorer');
    host().setActiveApp(appByName('Terminal').id);
    await settle();
    expect(item('Terminal').textContent?.trim()).toBe('Terminal');
  });

  it('falls back to Desktop (Рабочий стол) and disables the name button without an active app', async () => {
    await boot();
    host().setActiveApp(null);
    await settle();
    const desktop = item('Desktop');
    expect(desktop.disabled).toBe(true);
    await click(desktop);
    expect(opened).toEqual([]);
    i18n.set('ru');
    await settle();
    expect(item('Рабочий стол').disabled).toBe(true);
  });

  it('opens the appMenu popover with the app id as data and the name button as anchor', async () => {
    await boot();
    await click(item('File Explorer'));
    expect(opened).toEqual([{
      mode: 'popover', monitorId: '1',
      popover: { kind: 'appMenu', anchor: { x: 100, y: 3, width: 30, height: 22 }, data: { appId: appByName('File Explorer').id } },
    }]);
  });
});

describe('menu bar: opening panels', () => {
  const cases: Array<[string, string]> = [
    ['System menu', 'apple'], ['Bluetooth', 'bluetooth'], ['Wi-Fi', 'network'], ['Sound', 'sound'], ['Battery', 'battery'],
    ['Control Center', 'controlCenter'], ['Date and time', 'calendar'], ['Now playing', 'media'],
  ];
  it.each(cases)('%s opens the %s popover anchored at its own bounding rect', async (label, kind) => {
    await boot();
    await click(item(label));
    expect(opened).toEqual([{ mode: 'popover', monitorId: '1', popover: { kind, anchor: { x: 100, y: 3, width: 30, height: 22 } } }]);
  });

  it('Spotlight opens the spotlight mode, not a popover', async () => {
    await boot();
    await click(item('Spotlight'));
    expect(opened).toEqual([{ mode: 'spotlight', monitorId: '1' }]);
  });

  it('tray icons forward clicks to tray.click with their position', async () => {
    const clicks: unknown[] = [];
    getMockHost().handle('tray.click', ((p: unknown) => { clicks.push(p); }) as never);
    const icon = { id: 't1', tooltip: 'Dropbox', iconUrl: 'x.png', hidden: false };
    await boot(() => { host().status.tray = { supported: true, icons: [icon] }; });
    await click(item('Dropbox'));
    item('Dropbox').dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true }));
    await vi.waitFor(() => expect(clicks).toEqual([
      { id: 't1', button: 'left', anchor: { x: 115, y: 25 } },
      { id: 't1', button: 'right', anchor: { x: 115, y: 25 } },
    ]));
    expect(opened).toEqual([]);
  });
});

describe('menu bar: pressed state', () => {
  const pressed = (l: string) => item(l).getAttribute('aria-expanded') === 'true';
  const show = async (payload: object) => { host().emitTo(bridge.ctx, 'overlay.show', { monitorId: '1', token: 1, ...payload } as never); await settle(); };

  it('presses the clicked item immediately and releases it on overlay.hide', async () => {
    await boot();
    await click(item('Sound'));
    expect(pressed('Sound')).toBe(true);
    expect(pressed('Battery')).toBe(false);
    host().emitTo(bridge.ctx, 'overlay.hide', undefined as never);
    await settle();
    expect(pressed('Sound')).toBe(false);
  });

  it('follows overlay.show: a panel opened elsewhere (hotkey, other item) moves the pressed state', async () => {
    await boot();
    await click(item('Sound'));
    await show({ mode: 'popover', popover: { kind: 'battery', anchor: { x: 0, y: 0, width: 1, height: 1 } } });
    expect(pressed('Sound')).toBe(false);
    expect(pressed('Battery')).toBe(true);
    await show({ mode: 'spotlight' });
    expect(pressed('Spotlight')).toBe(true);
    await show({ mode: 'launchpad' });
    expect(items().some((i) => i.getAttribute('aria-expanded') === 'true')).toBe(false);
  });

  it('ignores overlay.show of another monitor', async () => {
    await boot();
    await show({ monitorId: '2', mode: 'popover', popover: { kind: 'sound', anchor: { x: 0, y: 0, width: 1, height: 1 } } });
    expect(pressed('Sound')).toBe(false);
  });

  it('releases the item when overlay.open fails', async () => {
    getMockHost().handle('overlay.open', (() => { throw new Error('boom'); }) as never);
    vi.spyOn(console, 'warn').mockImplementation(() => {});
    await boot();
    await click(item('Sound'));
    await vi.waitFor(() => expect(pressed('Sound')).toBe(false));
  });

  describe('without overlay events from the host (fallback heuristics)', () => {
    it('the pointer leaving the bar and coming back means the panel closed', async () => {
      await boot();
      await click(item('Sound'));
      window.dispatchEvent(new Event('pointermove'));
      flushSync(); await tick();
      expect(pressed('Sound')).toBe(true); // moving inside the bar right after the click is not a release
      document.documentElement.dispatchEvent(new Event('pointerleave'));
      window.dispatchEvent(new Event('pointermove'));
      flushSync(); await tick();
      expect(pressed('Sound')).toBe(false);
    });

    it('a generous timeout releases an item whose panel was closed while the pointer stayed away', async () => {
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
      await boot();
      await click(item('Sound'));
      vi.advanceTimersByTime(19_000);
      flushSync();
      expect(pressed('Sound')).toBe(true);
      vi.advanceTimersByTime(2_000);
      flushSync();
      expect(pressed('Sound')).toBe(false);
    });
  });

  describe('once the host reports overlay events, the heuristics step aside', () => {
    it('pointer movement and the timeout no longer release the item', async () => {
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
      await boot();
      await show({ mode: 'popover', popover: { kind: 'sound', anchor: { x: 0, y: 0, width: 1, height: 1 } } });
      expect(pressed('Sound')).toBe(true);
      document.documentElement.dispatchEvent(new Event('pointerleave'));
      window.dispatchEvent(new Event('pointermove'));
      vi.advanceTimersByTime(60_000);
      flushSync(); await tick();
      expect(pressed('Sound')).toBe(true);
      host().emitTo(bridge.ctx, 'overlay.hide', undefined as never);
      await settle();
      expect(pressed('Sound')).toBe(false);
    });
  });
});

describe('menu bar: clock', () => {
  const clockText = () => document.querySelector('.clock')?.textContent;

  it('shows weekday, day, month and time in the UI language', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 9, 23, 15, 42, 7));
    await boot();
    expect(clockText()).toBe('Fri 23 Oct 15:42');
    i18n.set('ru');
    await settle();
    expect(clockText()).toBe('пт 23 окт 15:42');
  });

  it('follows 12h / seconds / date settings', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 9, 23, 15, 42, 7));
    await boot();
    await bridge.call('settings.patch', { patch: { menuBar: { use24Hour: false, showSeconds: true, showDate: false } } });
    await settle();
    expect(clockText()).toBe('3:42:07 PM');
  });
});

describe('menu bar: keyboard', () => {
  it('arrow keys move focus between the items, Home/End jump, and wrap around', async () => {
    await boot();
    const all = items().filter((i) => !i.disabled);
    all[0].focus();
    const press = (key: string) => { (document.activeElement as HTMLElement).dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true })); };
    press('ArrowRight');
    expect(document.activeElement).toBe(all[1]);
    press('End');
    expect(document.activeElement).toBe(all.at(-1));
    press('ArrowRight');
    expect(document.activeElement).toBe(all[0]);
    press('ArrowLeft');
    expect(document.activeElement).toBe(all.at(-1));
    press('Home');
    expect(document.activeElement).toBe(all[0]);
  });
});
