import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { setBridgeForTests, setInit } from '$lib/ipc';
import { i18n } from '$lib/i18n.svelte';
import { BridgeError } from '$lib/ipc/bridge';
import { MockBridge } from '$mock/mockBridge';
import { getMockHost, resetMockHost } from '$mock/host';
import { appByName } from '$mock/fixtures';
import { flushSync, mountComponent, tick, type Mounted } from '../../../menubar/testing/mount';
import AppMenu from '../AppMenu.svelte';

globalThis.ResizeObserver ??= class { observe() {} unobserve() {} disconnect() {} } as unknown as typeof ResizeObserver;

const edge = appByName('Microsoft Edge');
const anchor = { x: 44, y: 3, width: 100, height: 22 };

let mounted: Mounted;
let onclose: ReturnType<typeof vi.fn>;
const calls: Array<{ type: string; params: unknown }> = [];

async function open(data: unknown, active: string | null = edge.id) {
  const bridge = new MockBridge({ surface: 'overlay' });
  setBridgeForTests(bridge);
  setInit(await bridge.call('surface.ready', { surface: 'overlay' }));
  (getMockHost() as any).setActiveApp(active);
  await tick(); await new Promise((r) => setTimeout(r, 0)); // activeApp.changed is delivered in a microtask
  onclose = vi.fn();
  mounted = mountComponent(AppMenu, { request: { kind: 'appMenu', anchor, data }, onclose });
  await tick();
}
const rows = () => [...document.querySelectorAll<HTMLElement>('[role="menuitem"]')];
const click = async (el: HTMLElement) => { el.click(); flushSync(); await tick(); };

beforeEach(() => {
  resetMockHost();
  i18n.set('en');
  calls.length = 0;
  vi.spyOn(console, 'warn').mockImplementation(() => {});
  for (const type of ['dock.hide', 'dock.quit']) getMockHost().handle(type as never, ((p: unknown) => { calls.push({ type, params: p }); }) as never);
});
afterEach(() => { vi.restoreAllMocks(); mounted?.unmount(); setBridgeForTests(null); setInit(null); });

describe('AppMenu', () => {
  it('shows the app name with Hide and Quit and their shortcut hints', async () => {
    await open({ appId: edge.id });
    expect(document.querySelector('.name')?.textContent).toBe('Microsoft Edge');
    expect(rows().map((r) => r.textContent?.replace(/\s+/g, ' ').trim())).toEqual(['Hide Microsoft Edge Ctrl+H', 'Quit Microsoft Edge Ctrl+Q']);
  });

  it('does not register the Ctrl+H / Ctrl+Q hints as hotkeys', async () => {
    await open({ appId: edge.id });
    const seen = vi.fn();
    window.addEventListener('keydown', seen);
    document.activeElement?.dispatchEvent(new KeyboardEvent('keydown', { key: 'q', ctrlKey: true, bubbles: true }));
    document.activeElement?.dispatchEvent(new KeyboardEvent('keydown', { key: 'h', ctrlKey: true, bubbles: true }));
    window.removeEventListener('keydown', seen);
    await tick();
    expect(calls).toEqual([]);
    expect(onclose).not.toHaveBeenCalled();
  });

  it('Hide calls dock.hide with the app id and closes', async () => {
    await open({ appId: edge.id });
    await click(rows()[0]);
    expect(onclose).toHaveBeenCalledTimes(1);
    await vi.waitFor(() => expect(calls).toEqual([{ type: 'dock.hide', params: { itemId: edge.id } }]));
  });

  it('Quit calls dock.quit with the app id and closes', async () => {
    await open({ appId: edge.id });
    await click(rows()[1]);
    await vi.waitFor(() => expect(calls).toEqual([{ type: 'dock.quit', params: { itemId: edge.id } }]));
    expect(onclose).toHaveBeenCalledTimes(1);
  });

  it('a failing call is swallowed (logged), the popover still closes', async () => {
    getMockHost().handle('dock.quit', (() => { throw new BridgeError('failed', 'nope'); }) as never);
    await open({ appId: edge.id });
    await click(rows()[1]);
    expect(onclose).toHaveBeenCalledTimes(1);
    await tick();
  });

  it('ignores a malformed appId from the untrusted menu bar and uses the active app instead', async () => {
    await open({ appId: { evil: true } });
    await click(rows()[1]);
    await vi.waitFor(() => expect(calls).toEqual([{ type: 'dock.quit', params: { itemId: edge.id } }]));
  });

  it('is disabled when neither the request nor the active app names an app', async () => {
    await open(undefined, null);
    expect(rows().every((r) => r.getAttribute('aria-disabled') === 'true')).toBe(true);
    await click(rows()[0]);
    await tick();
    expect(calls).toEqual([]);
    expect(onclose).not.toHaveBeenCalled();
  });

  it('derives a name from the id when the foreground app is a different one', async () => {
    await open({ appId: 'exe:c:\\tools\\Putty.exe' });
    expect(document.querySelector('.name')?.textContent).toBe('Putty');
    expect(rows()[1].textContent).toContain('Quit Putty');
  });

  it('is localised', async () => {
    i18n.set('ru');
    await open({ appId: edge.id });
    expect(rows()[0].textContent).toContain('Скрыть «Microsoft Edge»');
    expect(rows()[1].textContent).toContain('Завершить «Microsoft Edge»');
  });
});
