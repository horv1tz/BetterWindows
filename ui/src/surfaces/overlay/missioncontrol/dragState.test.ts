import { describe, expect, it } from 'vitest';
import { DRAG_THRESHOLD_PX, IDLE, isDragging, reduceDrag, type DragState } from './dragState';

const down = { type: 'down' as const, handle: '42', sourceDesktopId: 'd1', x: 100, y: 100 };

function run(events: Parameters<typeof reduceDrag>[1][]) {
  let state: DragState = IDLE;
  let effect: ReturnType<typeof reduceDrag>['effect'] = null;
  for (const ev of events) {
    const r = reduceDrag(state, ev);
    state = r.state;
    if (r.effect) effect = r.effect;
  }
  return { state, effect };
}

describe('reduceDrag', () => {
  it('a press without movement is only a click: no drag, no drop', () => {
    const { state, effect } = run([down, { type: 'up' }]);
    expect(state).toEqual(IDLE);
    expect(effect).toBeNull();
  });

  it('movement below the threshold does not start a drag', () => {
    const { state } = run([down, { type: 'move', x: 103, y: 101, overDesktopId: 'd2' }]);
    expect(state.kind).toBe('pending');
    expect(isDragging(state)).toBe(false);
  });

  it('movement past the threshold starts a drag from the press point', () => {
    const { state } = run([down, { type: 'move', x: 100 + DRAG_THRESHOLD_PX, y: 100, overDesktopId: null }]);
    expect(state.kind).toBe('dragging');
    expect(isDragging(state)).toBe(true);
  });

  it('dropping on a different desktop emits a drop effect for that window', () => {
    const { state, effect } = run([
      down,
      { type: 'move', x: 130, y: 120, overDesktopId: null },
      { type: 'move', x: 400, y: 20, overDesktopId: 'd2' },
      { type: 'up' },
    ]);
    expect(state).toEqual(IDLE);
    expect(effect).toEqual({ type: 'drop', handle: '42', desktopId: 'd2' });
  });

  it('dropping on the window\'s own desktop is a no-op', () => {
    const { effect } = run([down, { type: 'move', x: 200, y: 200, overDesktopId: 'd1' }, { type: 'up' }]);
    expect(effect).toBeNull();
  });

  it('dropping outside any desktop tile is a no-op', () => {
    const { state, effect } = run([down, { type: 'move', x: 200, y: 200, overDesktopId: null }, { type: 'up' }]);
    expect(effect).toBeNull();
    expect(state).toEqual(IDLE);
  });

  it('leaving a tile again before releasing cancels the drop', () => {
    const { effect } = run([
      down,
      { type: 'move', x: 200, y: 200, overDesktopId: 'd2' },
      { type: 'move', x: 210, y: 200, overDesktopId: null },
      { type: 'up' },
    ]);
    expect(effect).toBeNull();
  });

  it('cancel resets to idle without an effect, from any state', () => {
    const dragging = run([down, { type: 'move', x: 200, y: 200, overDesktopId: 'd2' }]).state;
    expect(reduceDrag(dragging, { type: 'cancel' })).toEqual({ state: IDLE, effect: null });
    const pending = run([down]).state;
    expect(reduceDrag(pending, { type: 'cancel' })).toEqual({ state: IDLE, effect: null });
  });

  it('moves while idle are ignored', () => {
    const r = reduceDrag(IDLE, { type: 'move', x: 1, y: 1, overDesktopId: 'd2' });
    expect(r).toEqual({ state: IDLE, effect: null });
  });

  it('a second press replaces an earlier pending gesture', () => {
    const { state } = run([down, { type: 'down', handle: '7', sourceDesktopId: 'd3', x: 5, y: 5 }]);
    expect(state).toMatchObject({ kind: 'pending', handle: '7', sourceDesktopId: 'd3', startX: 5, startY: 5 });
  });

  it('keeps the source desktop through the drag so the same-desktop check works', () => {
    const { state } = run([down, { type: 'move', x: 300, y: 300, overDesktopId: 'd3' }]);
    expect(state).toMatchObject({ kind: 'dragging', sourceDesktopId: 'd1', overDesktopId: 'd3' });
  });
});
