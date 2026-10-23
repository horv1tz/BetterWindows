import type { Bridge } from '../lib/ipc/bridge';
import type { CallType, HostEventType, UiEventType } from '../lib/protocol/generated';
import { getMockHost, type MockContext } from './host';

/** Bridge implementation backed by the in-browser MockHost. */
export class MockBridge implements Bridge {
  readonly kind = 'mock' as const;
  readonly ctx: MockContext;
  private listeners = new Map<string, Set<(p: any) => void>>();

  constructor(route: { surface: MockContext['surface']; monitorId?: string }) {
    this.ctx = { surface: route.surface, monitorId: route.monitorId, bridge: this };
    getMockHost().bridges.add(this);
  }

  call<K extends CallType>(type: K, ...args: any[]): Promise<any> {
    return getMockHost().dispatchCall(this.ctx, type, args[0]);
  }

  on<K extends HostEventType>(type: K, handler: (payload: any) => void): () => void {
    let set = this.listeners.get(type);
    if (!set) this.listeners.set(type, (set = new Set()));
    set.add(handler);
    return () => set!.delete(handler);
  }

  emit<K extends UiEventType>(type: K, ...args: any[]): void {
    getMockHost().dispatchUiEvent(this.ctx, type, args[0]);
  }

  /** Called by MockHost. Delivered asynchronously like a real WebView message. */
  deliver(type: string, payload: unknown) {
    const set = this.listeners.get(type);
    if (!set || set.size === 0) return;
    const copy = payload === undefined ? undefined : structuredClone(payload);
    queueMicrotask(() => { for (const h of [...set]) { try { h(copy); } catch (e) { console.error('[mock] listener threw', type, e); } } });
  }

  dispose() { getMockHost().bridges.delete(this); }
}
