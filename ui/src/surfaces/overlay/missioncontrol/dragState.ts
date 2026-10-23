/**
 * Drag-a-window-to-a-desktop state machine. Pure: the component feeds pointer events and performs the returned effect.
 *
 *   idle --down--> pending --move (distance >= threshold)--> dragging --up(target on another desktop)--> idle + drop effect
 *                     |                                          \--up(no target / same desktop) or cancel--> idle
 *                     \--up / cancel--> idle (it was only a click)
 */

export type DragState =
  | { kind: 'idle' }
  | { kind: 'pending'; handle: string; sourceDesktopId: string | null; startX: number; startY: number }
  | { kind: 'dragging'; handle: string; sourceDesktopId: string | null; x: number; y: number; overDesktopId: string | null };

export type DragEvent =
  | { type: 'down'; handle: string; sourceDesktopId: string | null; x: number; y: number }
  | { type: 'move'; x: number; y: number; overDesktopId: string | null }
  | { type: 'up' }
  | { type: 'cancel' };

export type DragEffect = { type: 'drop'; handle: string; desktopId: string } | null;

/** Movement (CSS px) before a press becomes a drag; smaller movements stay a click. */
export const DRAG_THRESHOLD_PX = 6;

export const IDLE: DragState = { kind: 'idle' };

export function reduceDrag(state: DragState, ev: DragEvent): { state: DragState; effect: DragEffect } {
  switch (ev.type) {
    case 'down':
      return { state: { kind: 'pending', handle: ev.handle, sourceDesktopId: ev.sourceDesktopId, startX: ev.x, startY: ev.y }, effect: null };
    case 'move': {
      if (state.kind === 'pending') {
        if (Math.hypot(ev.x - state.startX, ev.y - state.startY) < DRAG_THRESHOLD_PX) return { state, effect: null };
        return {
          state: { kind: 'dragging', handle: state.handle, sourceDesktopId: state.sourceDesktopId, x: ev.x, y: ev.y, overDesktopId: ev.overDesktopId },
          effect: null,
        };
      }
      if (state.kind === 'dragging') return { state: { ...state, x: ev.x, y: ev.y, overDesktopId: ev.overDesktopId }, effect: null };
      return { state, effect: null };
    }
    case 'up':
      if (state.kind === 'dragging' && state.overDesktopId !== null && state.overDesktopId !== state.sourceDesktopId) {
        return { state: IDLE, effect: { type: 'drop', handle: state.handle, desktopId: state.overDesktopId } };
      }
      return { state: IDLE, effect: null };
    case 'cancel':
      return { state: IDLE, effect: null };
  }
}

/** True while a press has become a drag; the click that follows a drop must then be ignored. */
export function isDragging(state: DragState): boolean {
  return state.kind === 'dragging';
}
