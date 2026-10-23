import type { OverlayRequest, OverlayShow, PopoverKind, Rect } from '$lib/protocol/generated';
import type { ItemKey } from './status';

/**
 * "Pressed while its panel is open" state machine for the bar items.
 *
 * Sources of truth, in order of preference:
 *  1. overlay.show / overlay.hide events when the host delivers them to the menu bar (docs/IPC.md sends them to the overlay page; the
 *     integrator may fan them out to the bar, see the report's contractRequests). Exact, and it also covers panels opened by hotkeys;
 *  2. the optimistic state set the moment the user clicks an item;
 *  3. only while no overlay event has ever reached the bar: a fallback. Once the overlay (a full-monitor window) covers the bar the
 *     pointer leaves it, and when the overlay closes the pointer is back over the bar: leave -> move therefore means "closed"
 *     (the Surface adds a long timeout for the case that the pointer never returns).
 */
export interface Pressed {
  key: ItemKey | null;
  /** The pointer has left the bar since the panel opened (only then does a pointer move mean the panel closed). */
  armed: boolean;
}

/** The parts of an OverlayShow the tracker needs. */
type ShowLike = Pick<OverlayShow, 'mode' | 'popover'> & { monitorId?: string };

export const NOT_PRESSED: Pressed = { key: null, armed: false };

export type PressedEvent =
  | { type: 'click'; key: ItemKey }
  | { type: 'show'; show: ShowLike; monitorId: string | undefined }
  | { type: 'hide' }
  | { type: 'openFailed' }
  | { type: 'pointerLeave' }
  | { type: 'pointerMove' };

/** Which bar item corresponds to what the overlay is showing (null: something that did not originate from the bar). */
export function keyOfShow(show: ShowLike): ItemKey | null {
  if (show.mode === 'spotlight') return 'spotlight';
  if (show.mode !== 'popover' || !show.popover) return null;
  const kind: PopoverKind = show.popover.kind;
  switch (kind) {
    case 'apple': case 'appMenu': case 'controlCenter': case 'calendar':
    case 'battery': case 'network': case 'sound': case 'bluetooth': case 'media':
      return kind;
    default:
      return null; // dockMenu, windowList, desktopMenu
  }
}

export function reducePressed(state: Pressed, ev: PressedEvent): Pressed {
  switch (ev.type) {
    case 'click':
      return { key: ev.key, armed: false };
    case 'show': {
      // The overlay lives on one monitor. A panel on another monitor means ours (if any) is gone.
      if (ev.monitorId !== undefined && ev.show.monitorId && ev.show.monitorId !== ev.monitorId) return NOT_PRESSED;
      const key = keyOfShow(ev.show);
      return key === null ? NOT_PRESSED : { key, armed: state.key === key ? state.armed : false };
    }
    case 'hide':
    case 'openFailed':
      return state.key === null ? state : NOT_PRESSED;
    case 'pointerLeave':
      return state.key === null || state.armed ? state : { ...state, armed: true };
    case 'pointerMove':
      return state.key !== null && state.armed ? NOT_PRESSED : state;
  }
}

/** Builds the overlay.open payload for a bar item. `anchor` is the item's getBoundingClientRect in client DIPs. */
export function openRequest(key: ItemKey, anchor: Rect, monitorId: string | undefined, data?: unknown): OverlayRequest {
  const base = { monitorId };
  if (key === 'spotlight') return { mode: 'spotlight', ...base };
  return { mode: 'popover', ...base, popover: { kind: key as PopoverKind, anchor, ...(data === undefined ? {} : { data }) } };
}

export function toRect(r: Pick<DOMRect, 'x' | 'y' | 'width' | 'height'>): Rect {
  return { x: r.x, y: r.y, width: r.width, height: r.height };
}
