import { STATE_EVENT_TYPES, type HostEventType } from '../protocol/generated';

const STATE = new Set<string>(STATE_EVENT_TYPES);

/**
 * Listener registry shared by HostBridge and MockBridge.
 * State events (idempotent full snapshots, see STATE_EVENT_TYPES) are cached and replayed to late subscribers, so a component
 * that mounts after the host already pushed the state (initial push after surface.ready, lazily mounted popovers) still sees it.
 */
export class Listeners {
  private map = new Map<string, Set<(p: any) => void>>();
  private cache = new Map<string, unknown>();

  /** True if at least one subscriber is registered for `type`. */
  has(type: string): boolean {
    return (this.map.get(type)?.size ?? 0) > 0;
  }

  deliver(type: string, payload: unknown) {
    if (STATE.has(type)) this.cache.set(type, payload);
    const set = this.map.get(type);
    if (!set) return;
    for (const h of [...set]) {
      try { h(payload); } catch (err) { console.error('[bridge] handler for', type, 'threw', err); }
    }
  }

  on<K extends HostEventType>(type: K, handler: (payload: any) => void): () => void {
    let set = this.map.get(type);
    if (!set) this.map.set(type, (set = new Set()));
    set.add(handler);
    if (this.cache.has(type)) {
      const cached = this.cache.get(type);
      queueMicrotask(() => { if (set!.has(handler)) { try { handler(cached); } catch (e) { console.error('[bridge] replay threw', type, e); } } });
    }
    return () => set!.delete(handler);
  }

  /** Latest cached value of a state event (undefined if none yet). */
  latest<T = unknown>(type: HostEventType): T | undefined { return this.cache.get(type) as T | undefined; }
}
