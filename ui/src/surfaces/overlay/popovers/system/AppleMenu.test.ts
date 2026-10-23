import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { setBridgeForTests, setInit } from '$lib/ipc';
import { i18n } from '$lib/i18n.svelte';
import { BridgeError } from '$lib/ipc/bridge';
import { MockBridge } from '$mock/mockBridge';
import { getMockHost, resetMockHost } from '$mock/host';
import { flushSync, mountComponent, tick, type Mounted } from '../../../menubar/testing/mount';
import { resetTaskbarForTests } from './taskbar.svelte';
import AppleMenu from '../AppleMenu.svelte';

// jsdom has no ResizeObserver (use:glassShape in PopoverFrame observes its panel).
globalThis.ResizeObserver ??= class { observe() {} unobserve() {} disconnect() {} } as unknown as typeof ResizeObserver;

const request = { kind: 'apple' as const, anchor: { x: 8, y: 3, width: 34, height: 22 } };

let mounted: Mounted;
let onclose: ReturnType<typeof vi.fn>;
const calls: Array<{ type: string; params: unknown }> = [];

/** Registers a spy handler on the mock host that records the call; `fail` makes it reject. */
function spy(type: string, fail?: () => unknown) {
  getMockHost().handle(type as never, ((p: unknown) => {
    calls.push({ type, params: p });
    const e = fail?.();
    if (e) throw e;
  }) as never);
}
const called = (type: string) => calls.filter((c) => c.type === type);

async function open() {
  const bridge = new MockBridge({ surface: 'overlay' });
  setBridgeForTests(bridge);
  setInit(await bridge.call('surface.ready', { surface: 'overlay' }));
  onclose = vi.fn();
  mounted = mountComponent(AppleMenu, { request, onclose });
  await tick();
}
const rows = () => [...document.querySelectorAll<HTMLElement>('[role^="menuitem"]')];
const row = (label: string) => {
  const el = rows().find((r) => r.textContent?.trim().startsWith(label));
  if (!el) throw new Error(`no menu row '${label}' in: ${rows().map((r) => r.textContent?.trim()).join(' | ')}`);
  return el;
};
const click = async (el: HTMLElement) => { el.click(); flushSync(); await tick(); };

beforeEach(() => {
  resetMockHost();
  resetTaskbarForTests();
  i18n.set('en');
  calls.length = 0;
  vi.spyOn(console, 'warn').mockImplementation(() => {}); // the mock host prints surface.log lines; failures are expected in some tests
  for (const type of ['system.open', 'system.power', 'system.quit', 'system.showTaskbar', 'settings.openWindow', 'apps.launch']) spy(type);
});
afterEach(() => { vi.restoreAllMocks(); mounted?.unmount(); setBridgeForTests(null); setInit(null); });

describe('AppleMenu: content', () => {
  it('lists the system actions in order and has no Force Quit entry', async () => {
    await open();
    expect(rows().map((r) => r.textContent?.trim())).toEqual([
      'About This PC', 'System Settings…', 'Microsoft Store', 'Sleep', 'Restart…', 'Shut Down…',
      'Lock Screen', 'Log Out…', 'Show Windows Taskbar', 'Quit BetterWindows',
    ]);
  });

  it('is localised', async () => {
    i18n.set('ru');
    await open();
    expect(rows().map((r) => r.textContent?.trim())).toContain('Перезагрузить…');
    expect(rows().map((r) => r.textContent?.trim())).toContain('Выйти из BetterWindows');
    expect(document.querySelector('[role="menu"]')?.getAttribute('aria-label')).toBe('Системное меню');
  });

  it('focuses the menu itself on open (so Escape/arrow keys work) without highlighting a row', async () => {
    await open();
    expect(document.activeElement?.getAttribute('role')).toBe('menu');
  });
});

describe('AppleMenu: immediate actions', () => {
  it('About This PC opens ms-settings:about and closes', async () => {
    await open();
    await click(row('About This PC'));
    expect(onclose).toHaveBeenCalled();
    await vi.waitFor(() => expect(called('system.open')).toEqual([{ type: 'system.open', params: { target: 'ms-settings:about' } }]));
  });

  it('System Settings… opens the settings window', async () => {
    await open();
    await click(row('System Settings'));
    await vi.waitFor(() => expect(called('settings.openWindow')).toHaveLength(1));
    expect(onclose).toHaveBeenCalled();
  });

  it('Sleep and Lock Screen act immediately, without a confirmation sheet', async () => {
    await open();
    await click(row('Sleep'));
    await vi.waitFor(() => expect(called('system.power')).toEqual([{ type: 'system.power', params: { action: 'sleep' } }]));
    expect(document.querySelector('[role="alertdialog"]')).toBeNull();
    expect(onclose).toHaveBeenCalledTimes(1);
    await click(row('Lock Screen'));
    await vi.waitFor(() => expect(called('system.power').map((c) => (c.params as { action: string }).action)).toEqual(['sleep', 'lock']));
  });

  it('Microsoft Store launches the Store app through the catalog', async () => {
    await open();
    await click(row('Microsoft Store'));
    await vi.waitFor(() => expect(called('apps.launch')).toEqual([{ type: 'apps.launch', params: { appId: 'aumid:Microsoft.WindowsStore_8wekyb3d8bbwe!App' } }]));
    expect(called('system.open')).toHaveLength(0);
  });

  it('Microsoft Store falls back to the https page when the app is not in the catalog', async () => {
    spy('apps.launch', () => new BridgeError('notFound', 'no such app'));
    await open();
    await click(row('Microsoft Store'));
    await vi.waitFor(() => expect(called('system.open')).toEqual([{ type: 'system.open', params: { target: 'https://apps.microsoft.com/' } }]));
  });

  it('Quit BetterWindows restores the taskbar on exit', async () => {
    await open();
    await click(row('Quit BetterWindows'));
    await vi.waitFor(() => expect(called('system.quit')).toEqual([{ type: 'system.quit', params: undefined }]));
  });
});

describe('AppleMenu: confirmation sheet', () => {
  const sheet = () => document.querySelector<HTMLElement>('[role="alertdialog"]');
  const button = (label: string) => [...document.querySelectorAll<HTMLButtonElement>('.sheet .btn')].find((b) => b.textContent?.trim() === label)!;

  it.each([
    ['Restart…', 'restart', 'Restart'],
    ['Shut Down…', 'shutdown', 'Shut Down'],
    ['Log Out…', 'logout', 'Log Out'],
  ])('%s asks first, focuses Cancel and only then calls system.power(%s)', async (item, action, confirmLabel) => {
    await open();
    await click(row(item));
    expect(sheet()).not.toBeNull();
    expect(sheet()!.textContent).toContain('Are you sure');
    expect(document.activeElement?.textContent?.trim()).toBe('Cancel');
    expect(called('system.power')).toHaveLength(0);
    await click(button(confirmLabel));
    await vi.waitFor(() => expect(called('system.power')).toEqual([{ type: 'system.power', params: { action } }]));
    await vi.waitFor(() => expect(onclose).toHaveBeenCalled());
  });

  it('Cancel returns to the menu without calling the host', async () => {
    await open();
    await click(row('Restart…'));
    await click(button('Cancel'));
    expect(sheet()).toBeNull();
    expect(rows().length).toBeGreaterThan(5);
    expect(called('system.power')).toHaveLength(0);
    expect(onclose).not.toHaveBeenCalled();
  });

  it('Escape dismisses the sheet only and does not reach the overlay', async () => {
    await open();
    await click(row('Shut Down…'));
    const reached = vi.fn();
    window.addEventListener('keydown', reached);
    button('Cancel').dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    flushSync(); await tick();
    window.removeEventListener('keydown', reached);
    expect(sheet()).toBeNull();
    expect(reached).not.toHaveBeenCalled();
  });

  it('Tab and arrow keys keep the focus inside the sheet', async () => {
    await open();
    await click(row('Restart…'));
    const cancel = button('Cancel');
    cancel.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true }));
    expect(document.activeElement).toBe(button('Restart'));
    button('Restart').dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true }));
    expect(document.activeElement).toBe(cancel);
  });

  it('a failing power call keeps the sheet open with an alert and lets the user retry', async () => {
    spy('system.power', () => new BridgeError('failed', 'nope'));
    await open();
    await click(row('Restart…'));
    await click(button('Restart'));
    await vi.waitFor(() => expect(document.querySelector('[role="alert"]')?.textContent).toContain("didn't work"));
    expect(onclose).not.toHaveBeenCalled();
    expect(button('Restart').getAttribute('aria-disabled')).toBe('false');
    expect(document.activeElement).toBe(button('Cancel')); // focus returns to the safe button after a failed attempt
    spy('system.power');
    await click(button('Restart'));
    await vi.waitFor(() => expect(onclose).toHaveBeenCalled());
  });

  it('an unsupported action explains that it is unavailable', async () => {
    spy('system.power', () => new BridgeError('unsupported', 'n/a'));
    await open();
    await click(row('Log Out…'));
    await click(button('Log Out'));
    await vi.waitFor(() => expect(document.querySelector('[role="alert"]')?.textContent).toContain('isn’t available'));
  });

  it('a double click on the confirm button sends one request', async () => {
    let release!: () => void;
    getMockHost().handle('system.power', (async (p: unknown) => { calls.push({ type: 'system.power', params: p }); await new Promise<void>((r) => (release = r)); }) as never);
    await open();
    await click(row('Restart…'));
    const confirm = button('Restart');
    confirm.click(); confirm.click();
    flushSync(); await tick();
    await vi.waitFor(() => expect(called('system.power')).toHaveLength(1));
    expect(button('Restarting…').getAttribute('aria-disabled')).toBe('true');
    release();
    await vi.waitFor(() => expect(onclose).toHaveBeenCalled());
    expect(called('system.power')).toHaveLength(1);
  });
});

describe('AppleMenu: Show Windows Taskbar', () => {
  const taskbarRow = () => row('Show Windows Taskbar');

  it('starts from settings.taskbar.mode (visible by default), flips on click and remembers the state when reopened', async () => {
    await open();
    expect(taskbarRow().getAttribute('aria-checked')).toBe('true');
    await click(taskbarRow());
    await vi.waitFor(() => expect(called('system.showTaskbar')).toEqual([{ type: 'system.showTaskbar', params: { show: false } }]));
    await vi.waitFor(() => expect(onclose).toHaveBeenCalled());
    mounted.unmount();
    mounted = mountComponent(AppleMenu, { request, onclose });
    await tick();
    expect(taskbarRow().getAttribute('aria-checked')).toBe('false');
    await click(taskbarRow());
    await vi.waitFor(() => expect(called('system.showTaskbar').at(-1)).toEqual({ type: 'system.showTaskbar', params: { show: true } }));
  });

  it('rolls the check back and shows an error when the host refuses', async () => {
    spy('system.showTaskbar', () => new BridgeError('failed', 'nope'));
    await open();
    await click(taskbarRow());
    await vi.waitFor(() => expect(document.querySelector('[role="alert"]')).not.toBeNull());
    expect(taskbarRow().getAttribute('aria-checked')).toBe('true');
    expect(onclose).not.toHaveBeenCalled();
  });
});

describe('AppleMenu: keyboard', () => {
  const key = async (k: string) => {
    (document.activeElement ?? document.body).dispatchEvent(new KeyboardEvent('keydown', { key: k, bubbles: true, cancelable: true }));
    flushSync(); await tick();
  };

  it('ArrowDown / ArrowUp / Home / End move the focus through the items, wrapping around', async () => {
    await open();
    await key('ArrowDown');
    expect(document.activeElement).toBe(row('About This PC'));
    await key('ArrowDown');
    expect(document.activeElement).toBe(row('System Settings'));
    await key('End');
    expect(document.activeElement).toBe(row('Quit BetterWindows'));
    await key('ArrowDown');
    expect(document.activeElement).toBe(row('About This PC'));
    await key('ArrowUp');
    expect(document.activeElement).toBe(row('Quit BetterWindows'));
    await key('Home');
    expect(document.activeElement).toBe(row('About This PC'));
  });

  it('type-ahead jumps to the next matching item', async () => {
    await open();
    await key('l');
    expect(document.activeElement).toBe(row('Lock Screen'));
    await key('l');
    // 'l' typed twice quickly = repeated letter: cycles to the next item starting with L
    expect(document.activeElement).toBe(row('Log Out'));
  });
});
