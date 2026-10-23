import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
vi.mock('svelte', () => import('./spotlight/svelteClient')); // see spotlight/svelteClient.ts
import { fireEvent } from '@testing-library/svelte';
import { setBridgeForTests, setInit } from '$lib/ipc';
import { MockBridge } from '$mock/mockBridge';
import { getMockHost, resetMockHost } from '$mock/host';
import { i18n } from '$lib/i18n.svelte';
import { cleanup, render } from './spotlight/testing';
import Surface from './Surface.svelte';

let bridge: MockBridge;
let calls: string[];
let token = 0;

async function boot() {
  vi.useFakeTimers();
  vi.stubGlobal('ResizeObserver', class { observe() {} unobserve() {} disconnect() {} });
  Element.prototype.scrollIntoView = vi.fn();
  HTMLElement.prototype.scrollTo = vi.fn() as any;
  i18n.set('en');
  document.documentElement.dataset.reduceMotion = 'false';
  bridge = new MockBridge({ surface: 'overlay' });
  calls = [];
  const original = bridge.call.bind(bridge);
  (bridge as any).call = (type: string, params?: unknown) => { calls.push(type); return original(type as any, params as any); };
  setBridgeForTests(bridge);
  setInit(await original('surface.ready', { surface: 'overlay' }));
  render(Surface, {} as any);
  await vi.advanceTimersByTimeAsync(20);
}
const show = async (mode: string, popover?: unknown) => {
  getMockHost().emit('overlay.show', { mode, popover, monitorId: '1', token: ++token } as any, (c) => c.surface === 'overlay');
  await vi.advanceTimersByTimeAsync(20);
};
const key = (k: string) => fireEvent.keyDown(window, { key: k });

beforeEach(boot);
afterEach(() => { cleanup(); setBridgeForTests(null); setInit(null); resetMockHost(); vi.useRealTimers(); vi.unstubAllGlobals(); });

describe('overlay Surface router', () => {
  it('renders nothing until overlay.show, then the mode component', async () => {
    expect(document.querySelector('.stage')).toBeNull();
    await show('spotlight');
    expect(document.querySelector('.stage[data-mode="spotlight"]')).toBeTruthy();
    expect(document.querySelector('[role="dialog"]')).toBeTruthy();
  });

  it('overlay.hide drops the content immediately (the host already hid the window) without calling overlay.close', async () => {
    await show('launchpad');
    expect(document.querySelector('.stage[data-mode="launchpad"]')).toBeTruthy();
    getMockHost().emit('overlay.hide', undefined as never, (c) => c.surface === 'overlay');
    await vi.advanceTimersByTimeAsync(5);
    expect(document.querySelector('.stage')).toBeNull();
    expect(calls).not.toContain('overlay.close');
  });

  it('Spotlight closes with a short out-animation and only then calls overlay.close', async () => {
    await show('spotlight');
    await key('Escape'); // empty query -> Spotlight asks to close
    expect(document.querySelector('.stage.leaving')).toBeTruthy();
    expect(calls).not.toContain('overlay.close');
    await vi.advanceTimersByTimeAsync(200);
    expect(document.querySelector('.stage')).toBeNull();
    expect(calls).toContain('overlay.close');
  });

  it('skips the out-animation with reduced motion', async () => {
    document.documentElement.dataset.reduceMotion = 'true';
    await show('spotlight');
    await key('Escape');
    await vi.advanceTimersByTimeAsync(5);
    expect(document.querySelector('.stage')).toBeNull();
    expect(calls).toContain('overlay.close');
  });

  it('a new overlay.show during the out-animation cancels it', async () => {
    await show('spotlight');
    await key('Escape');
    await show('launchpad');
    await vi.advanceTimersByTimeAsync(300);
    expect(document.querySelector('.stage[data-mode="launchpad"]')).toBeTruthy();
    expect(document.querySelector('.stage.leaving')).toBeNull();
  });

  it('Esc closes popovers directly (they do not handle it themselves)', async () => {
    await show('popover', { kind: 'calendar', anchor: { x: 100, y: 0, width: 40, height: 28 } });
    await key('Escape');
    await vi.advanceTimersByTimeAsync(5);
    expect(calls).toContain('overlay.close');
  });

  it('Spotlight and Launchpad keep ownership of Esc: the router does not close them on its own', async () => {
    await show('launchpad');
    await fireEvent.keyDown(window, { key: 'Escape', cancelable: true });
    // Launchpad closes via its own onclose (animated), proving the router did not short-circuit
    expect(document.querySelector('.stage.leaving')).toBeTruthy();
  });

  it('remounts the mode component on every show (token key), resetting its state', async () => {
    await show('spotlight');
    const first = document.querySelector('input[role="combobox"]') as HTMLInputElement;
    await fireEvent.input(first, { target: { value: 'abc' } });
    await show('spotlight');
    const second = document.querySelector('input[role="combobox"]') as HTMLInputElement;
    expect(second).not.toBe(first);
    expect(second.value).toBe('');
  });
});
