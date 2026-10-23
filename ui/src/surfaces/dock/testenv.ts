// Test-only helpers (jsdom has no layout, ResizeObserver or Web Animations): boot a surface against the in-browser mock host.
import { MockBridge } from '../../mock/mockBridge';
import { getMockHost, resetMockHost, type MockHost } from '../../mock/host';
import type { DockWorld } from '../../mock/domains/dock';
import { initLocale, i18n } from '$lib/i18n.svelte';
import { setBridgeForTests, setInit } from '$lib/ipc';
import type { SurfaceKind } from '$lib/protocol/generated';

export function installDomStubs() {
  const g = globalThis as any;
  g.ResizeObserver ??= class { observe() {} unobserve() {} disconnect() {} };
  if (!(Element.prototype as any).animate) (Element.prototype as any).animate = undefined;
  if (!g.requestAnimationFrame) g.requestAnimationFrame = (cb: FrameRequestCallback) => setTimeout(() => cb(performance.now()), 16);
  if (!g.cancelAnimationFrame) g.cancelAnimationFrame = (id: number) => clearTimeout(id);
  if (!Element.prototype.setPointerCapture) Element.prototype.setPointerCapture = () => {};
}

/** Fresh mock host + bridge for `surface`; returns the bridge with spies on call/emit recorded in `calls` / `events`. */
export async function boot(surface: SurfaceKind, lang = 'en') {
  installDomStubs();
  resetMockHost();
  const bridge = new MockBridge({ surface });
  setBridgeForTests(bridge);
  const calls: Array<{ type: string; params: any }> = [];
  const events: Array<{ type: string; payload: any }> = [];
  const call = bridge.call.bind(bridge);
  bridge.call = ((type: any, params: any) => { calls.push({ type, params }); return call(type, params); }) as typeof bridge.call;
  const emit = bridge.emit.bind(bridge);
  bridge.emit = ((type: any, payload: any) => { events.push({ type, payload }); return emit(type, payload); }) as typeof bridge.emit;
  const init = await call('surface.ready', { surface });
  setInit({ ...init, locale: lang });
  initLocale();
  i18n.set(lang);
  await new Promise((r) => setTimeout(r, 0)); // initial state events
  // settings.patch is only allowed from the settings surface (protocol 'surfaces'): tests that change settings use this bridge
  const settings = new MockBridge({ surface: 'settings' });
  return { bridge, settings, calls, events, host: getMockHost() as MockHost & { dockWorld: DockWorld } };
}

export const tick = (ms = 0) => new Promise((r) => setTimeout(r, ms));
