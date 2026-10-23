import type { Bridge } from '../lib/ipc/bridge';
import type { CallType, HostEventType, UiEventType } from '../lib/protocol/generated';
import { Listeners } from '../lib/ipc/bridgeBase';
import { getMockHost, type MockContext } from './host';

/** Bridge implementation backed by the in-browser MockHost. */
export class MockBridge implements Bridge {
  readonly kind = 'mock' as const;
  readonly ctx: MockContext;
  private listeners = new Listeners();

  constructor(route: { surface: MockContext['surface']; monitorId?: string }) {
    this.ctx = { surface: route.surface, monitorId: route.monitorId, bridge: this };
    getMockHost().bridges.add(this);
  }

  /** True once the page has subscribed to `type` (transient events are not replayed, so a dev deep link waits for this). */
  hasListener(type: string): boolean {
    return this.listeners.has(type);
  }

  call<K extends CallType>(type: K, ...args: any[]): Promise<any> {
    return getMockHost().dispatchCall(this.ctx, type, args[0]);
  }

  on<K extends HostEventType>(type: K, handler: (payload: any) => void): () => void {
    return this.listeners.on(type, handler);
  }

  emit<K extends UiEventType>(type: K, ...args: any[]): void {
    getMockHost().dispatchUiEvent(this.ctx, type, args[0]);
  }

  /** Called by MockHost. Delivered asynchronously like a real WebView message. */
  deliver(type: string, payload: unknown) {
    const copy = payload === undefined ? undefined : structuredClone(payload);
    queueMicrotask(() => this.listeners.deliver(type, copy));
  }

  dispose() { getMockHost().bridges.delete(this); }
}
