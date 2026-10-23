import { BridgeError } from '../lib/ipc/bridge';
import {
  CALL_SURFACES,
  INITIAL_STATE,
  PROTOCOL_VERSION,
  STATE_EVENT_TYPES,
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
  private providers = new Map<string, (ctx: MockContext) => unknown | undefined>();
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
  /** Called after a surface completed surface.ready: push your current state to it with emitTo(ctx, ...). Prefer provideState() for state events. */
  onSurfaceReady(fn: (ctx: MockContext) => void) { this.readyHandlers.push(fn); }

  /**
   * Declare the producer of a STATE event (like IStatePublisher.Declare in the real host): after surface.ready the host pushes exactly the events listed for that
   * surface kind in INITIAL_STATE (protocol 'initial' lists). Return undefined to send nothing to that surface. Use host.emit(type, payload) for later changes.
   */
  provideState<K extends HostEventType>(type: K, fn: (ctx: MockContext) => HostEvents[K] | undefined) {
    if (!(STATE_EVENT_TYPES as readonly string[]).includes(type)) throw new Error(`'${type}' is not a state event`);
    this.providers.set(type, fn);
  }

  /** Broadcast a host event to every surface (optionally filtered). */
  emit<K extends HostEventType>(type: K, payload: HostEvents[K], filter?: (ctx: MockContext) => boolean) {
    for (const b of this.bridges) if (this.consumes(b.ctx, type) && (!filter || filter(b.ctx))) b.deliver(type, payload);
  }
  emitTo<K extends HostEventType>(ctx: MockContext, type: K, payload: HostEvents[K]) {
    if (this.consumes(ctx, type)) ctx.bridge.deliver(type, payload);
  }

  /** State events only reach the surface kinds that consume them (INITIAL_STATE): a widget can never see data the real host would not send. */
  private consumes(ctx: MockContext, type: string): boolean {
    if (!(STATE_EVENT_TYPES as readonly string[]).includes(type)) return true;
    const ok = (INITIAL_STATE[ctx.surface] as readonly string[]).includes(type);
    if (!ok) console.debug(`[mock] '${type}' not delivered to ${ctx.surface} (not in its initial-state list)`);
    return ok;
  }

  async dispatchCall(ctx: MockContext, type: string, params: unknown): Promise<any> {
    if (this.latencyMs > 0) await new Promise((r) => setTimeout(r, this.latencyMs));
    const fn = this.calls.get(type);
    if (!fn) throw new BridgeError('notFound', `mock: no handler for call '${type}'`);
    const allowed = (CALL_SURFACES as Record<string, readonly string[] | undefined>)[type];
    if (allowed && !allowed.includes(ctx.surface)) throw new BridgeError('forbidden', `'${type}' is not allowed from ${ctx.surface}`);
    try {
      const result = await fn(params === undefined ? undefined : wire(params), ctx);
      if (type === 'surface.ready') queueMicrotask(() => this.pushInitial(ctx));
      return result === undefined ? undefined : wire(result);
    } catch (e) {
      if (e instanceof BridgeError) throw e;
      throw new BridgeError('internal', e instanceof Error ? e.message : String(e));
    }
  }

  dispatchUiEvent(ctx: MockContext, type: string, payload: unknown) {
    const allowed = (CALL_SURFACES as Record<string, readonly string[] | undefined>)[type];
    if (allowed && !allowed.includes(ctx.surface)) { console.warn(`[mock] forbidden ui event '${type}' from ${ctx.surface} dropped`); return; }
    for (const fn of this.uiEvents.get(type) ?? []) safe(() => fn(payload === undefined ? undefined : wire(payload), ctx));
  }

  /** Same order as the real host: provided state events of INITIAL_STATE[kind], then legacy onSurfaceReady handlers. */
  private pushInitial(ctx: MockContext) {
    for (const type of INITIAL_STATE[ctx.surface]) {
      const fn = this.providers.get(type);
      if (!fn) continue;
      safe(() => { const payload = fn(ctx); if (payload !== undefined) ctx.bridge.deliver(type, payload); });
    }
    this.readyHandlers.forEach((h) => safe(() => h(ctx)));
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

/** What the wire does to a value: JSON roundtrip (drops undefined, turns NaN/Infinity into null). */
export function wire<T>(v: T): T { return JSON.parse(JSON.stringify(v)) as T; }

/** RFC 7386 JSON Merge Patch: objects merge recursively, arrays/primitives replace, null DELETES the key (the caller restores defaults). */
export function deepMerge<T>(target: T, patch: unknown): T {
  if (patch === null || typeof patch !== 'object' || Array.isArray(patch)) return patch as T;
  const out: any = target !== null && typeof target === 'object' && !Array.isArray(target) ? { ...(target as object) } : {};
  for (const [k, v] of Object.entries(patch as object)) {
    if (v === null) delete out[k];
    else out[k] = typeof v === 'object' && !Array.isArray(v) ? deepMerge(out[k], v) : v;
  }
  return out;
}

/** Paths of keys in `patch` that do not exist in `schema` (objects whose default is an empty object are free-form maps). */
export function unknownKeys(schema: any, patch: any, path = ''): string[] {
  if (patch === null || typeof patch !== 'object' || Array.isArray(patch)) return [];
  if (schema === null || typeof schema !== 'object' || Array.isArray(schema)) return [];
  const isMap = Object.keys(schema).length === 0;
  const bad: string[] = [];
  for (const [k, v] of Object.entries(patch)) {
    if (isMap) continue;
    if (!(k in schema)) bad.push(path + k);
    else bad.push(...unknownKeys(schema[k], v, path + k + '.'));
  }
  return bad;
}
