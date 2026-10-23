import type { HostEventType, HostEvents } from '../protocol/generated';
import { getBridge } from './index';

/**
 * Reactive mirror of a host "state" event. `value` is `initial` until the first event arrives.
 * Usage (in a .svelte component or .svelte.ts):  const dock = hostState('dock.state', defaultDockState());
 *                                                {dock.value.items}
 * Call dispose() (or let component teardown do it via $effect) to unsubscribe.
 */
export function hostState<K extends HostEventType>(type: K, initial: HostEvents[K]) {
  let value = $state<HostEvents[K]>(initial);
  const off = getBridge().on(type, (p) => { value = p; });
  return {
    get value() { return value; },
    set value(v: HostEvents[K]) { value = v; },
    dispose: off,
  };
}
