import { getBridge } from '$lib/ipc';
import type { DockItem, DockState } from '$lib/protocol/generated';

/**
 * Subscribes to dock.state for a dock popover (state events are replayed to late subscribers, so lazily mounted popovers are fine).
 * `item` is the dock item the popover belongs to; `loaded` tells "no state yet" apart from "the item is gone".
 */
export function useDockItem(itemId: () => string | undefined) {
  let state = $state<DockState>({ items: [], minimized: [] });
  let loaded = $state(false);
  const off = getBridge().on('dock.state', (s) => { state = s; loaded = true; });
  return {
    get state() { return state; },
    get loaded() { return loaded; },
    get item(): DockItem | undefined {
      const id = itemId();
      return id ? state.items.find((i) => i.id === id && i.kind !== 'separator') : undefined;
    },
    dispose: off,
  };
}
