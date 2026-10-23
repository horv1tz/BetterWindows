// Shared setup for the component tests of the desktop surface: a booted mock host + bridge, like main.ts does for the real page.
import { MockBridge } from '$mock/mockBridge';
import { resetMockHost } from '$mock/host';
import { setBridgeForTests, setInit } from '$lib/ipc';
import { i18n } from '$lib/i18n.svelte';
import { resetShared } from './shared.svelte';
import { weatherStore } from './weatherStore.svelte';

export const tick = (ms = 0) => new Promise((r) => setTimeout(r, ms));

/** Fresh mock host + a desktop bridge that already completed surface.ready (init is available through getInit()). */
export async function bootDesktop(monitorId = '1', locale = 'en') {
  resetMockHost();
  resetShared();
  weatherStore._reset();
  const bridge = new MockBridge({ surface: 'desktop', monitorId });
  setBridgeForTests(bridge);
  const init = await bridge.call('surface.ready', { surface: 'desktop', monitorId });
  setInit(init);
  i18n.set(locale);
  return bridge;
}

export function teardownDesktop() {
  resetShared();
  weatherStore._reset();
  setBridgeForTests(null);
  setInit(null);
  resetMockHost();
}

/**
 * A settings-window bridge on the same mock host. Settings-only calls (settings.patch, weather.setLocation, ...) are forbidden from the
 * desktop surface by CALL_SURFACES, so tests that change settings do it from here, the way the real settings window does.
 */
export function settingsBridge() {
  return new MockBridge({ surface: 'settings' });
}

/** Records every call made through the bridge while still running it. */
export function spyCalls(bridge: MockBridge) {
  const calls: Array<{ type: string; params: unknown }> = [];
  const orig = bridge.call.bind(bridge) as (type: string, ...args: unknown[]) => Promise<unknown>;
  (bridge as unknown as { call: typeof orig }).call = (type, ...args) => { calls.push({ type, params: args[0] }); return orig(type, ...args); };
  return calls;
}
