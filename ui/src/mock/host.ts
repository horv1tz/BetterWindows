import { BridgeError } from '../lib/ipc/bridge';
import {
  PROTOCOL_VERSION,
  defaultSettings,
  defaultThemeState,
  type CallType,
  type Calls,
  type HostEventType,
  type HostEvents,
  type Settings,
  type SurfaceKind,
  type ThemeState,
  type UiEventType,
  type UiEvents,
} from '../lib/protocol/generated';
import type { MockBridge } from './mockBridge';

export interface MockContext {
  surface: SurfaceKind;
  monitorId?: string;
  bridge: MockBridge;
}

type CallFn<K extends CallType> = (params: Calls[K]['params'], ctx: MockContext) => Calls[K]['result'] | Promise<Calls[K]['result']>;
type UiEventFn<K extends UiEventType> = (payload: UiEvents[K], ctx: MockContext) => void;

/**
 * An in-browser fake of the whole host (controllers + platform). One instance is shared by every surface/iframe of a
 * page tree via window.top, so the playground behaves like the real multi-WebView app.
 * Domain behaviour lives in src/mock/domains/*.ts — each file default-exports `(host: MockHost) => void` and is auto-loaded.
 */
export class MockHost {
  readonly bridges = new Set<MockBridge>();
  settings: Settings = defaultSettings();
  theme: ThemeState = defaultThemeState();
  /** Artificial latency for calls (ms); set via ?latency=NN to test loading states. */
  latencyMs = Number(new URLSearchParams(location.search).get('latency') ?? 0);

  private calls = new Map<string, CallFn<any>>();
  private uiEvents = new Map<string, UiEventFn<any>[]>();
  private readyHandlers: Array<(ctx: MockContext) => void> = [];

  constructor() {
    const modules = import.meta.glob('./domains/*.ts', { eager: true }) as Record<string, { default?: (h: MockHost) => void }>;
    for (const [path, mod] of Object.entries(modules).sort(([a], [b]) => a.localeCompare(b))) {
      try { mod.default?.(this); } catch (e) { console.error('[mock] domain failed to load', path, e); }
    }
  }

  /** Register the handler for a call. */
  handle<K extends CallType>(type: K, fn: CallFn<K>) { this.calls.set(type, fn); }
  /** Register a handler for a UI -> host event (multiple allowed). */
  onUiEvent<K extends UiEventType>(type: K, fn: UiEventFn<K>) {
    const list = this.uiEvents.get(type) ?? [];
    list.push(fn);
    this.uiEvents.set(type, list);
  }
  /** Called after a surface completed surface.ready: push your current state to it with emitTo(ctx, ...). */
  onSurfaceReady(fn: (ctx: MockContext) => void) { this.readyHandlers.push(fn); }

  /** Broadcast a host event to every surface (optionally filtered). */
  emit<K extends HostEventType>(type: K, payload: HostEvents[K], filter?: (ctx: MockContext) => boolean) {
    for (const b of this.bridges) if (!filter || filter(b.ctx)) b.deliver(type, payload);
  }
  emitTo<K extends HostEventType>(ctx: MockContext, type: K, payload: HostEvents[K]) { ctx.bridge.deliver(type, payload); }

  async dispatchCall(ctx: MockContext, type: string, params: unknown): Promise<any> {
    if (this.latencyMs > 0) await new Promise((r) => setTimeout(r, this.latencyMs));
    const fn = this.calls.get(type);
    if (!fn) throw new BridgeError('notFound', `mock: no handler for call '${type}'`);
    try {
      const result = await fn(params === undefined ? undefined : structuredClone(params), ctx);
      if (type === 'surface.ready') queueMicrotask(() => this.readyHandlers.forEach((h) => safe(() => h(ctx))));
      return result === undefined ? undefined : structuredClone(result);
    } catch (e) {
      if (e instanceof BridgeError) throw e;
      throw new BridgeError('internal', e instanceof Error ? e.message : String(e));
    }
  }

  dispatchUiEvent(ctx: MockContext, type: string, payload: unknown) {
    for (const fn of this.uiEvents.get(type) ?? []) safe(() => fn(payload === undefined ? undefined : structuredClone(payload), ctx));
  }

  /** Test helper: the SurfaceInit-like protocol version. */
  readonly protocolVersion = PROTOCOL_VERSION;
}

function safe(fn: () => void) { try { fn(); } catch (e) { console.error('[mock] handler threw', e); } }

declare global {
  interface Window { __bwMockHost?: MockHost }
}

/** One MockHost per page tree: iframes of the playground reach the top window's instance. */
export function getMockHost(): MockHost {
  let top: Window = window;
  try { if (window.top && window.top.location.origin === window.location.origin) top = window.top; } catch { /* cross-origin top */ }
  return (top.__bwMockHost ??= new MockHost());
}

/** Reset helper for unit tests. */
export function resetMockHost() { delete window.__bwMockHost; }

export function deepMerge<T>(target: T, patch: unknown): T {
  if (patch === null || typeof patch !== 'object' || Array.isArray(patch)) return (patch as T) ?? target;
  const out: any = Array.isArray(target) ? [...target] : { ...(target as object) };
  for (const [k, v] of Object.entries(patch as object)) {
    out[k] = v !== null && typeof v === 'object' && !Array.isArray(v) && typeof out[k] === 'object' && out[k] !== null && !Array.isArray(out[k]) ? deepMerge(out[k], v) : v;
  }
  return out;
}
