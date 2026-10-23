import { describe, expect, it } from 'vitest';
import { initialNav, reduceKey, type KeyInput, type NavContext, type NavState } from './keyboard';

const key = (k: string, mods: Partial<KeyInput> = {}): KeyInput => ({ key: k, ctrlKey: false, altKey: false, shiftKey: false, metaKey: false, ...mods });
const ctx = (o: Partial<NavContext> = {}): NavContext => ({ count: 5, pageSize: 3, actionCount: () => 1, needsConfirm: () => false, hasQuery: true, ...o });
const at = (index: number, o: Partial<NavState> = {}): NavState => ({ ...initialNav(), index, ...o });

describe('arrow navigation', () => {
  it('moves down and up', () => {
    expect(reduceKey(at(0), key('ArrowDown'), ctx()).state.index).toBe(1);
    expect(reduceKey(at(3), key('ArrowUp'), ctx()).state.index).toBe(2);
  });
  it('wraps in both directions', () => {
    expect(reduceKey(at(4), key('ArrowDown'), ctx()).state.index).toBe(0);
    expect(reduceKey(at(0), key('ArrowUp'), ctx()).state.index).toBe(4);
  });
  it('marks the selection as user-moved and resets the action cycle', () => {
    const r = reduceKey(at(0, { actionIndex: 2, confirming: true }), key('ArrowDown'), ctx());
    expect(r.state).toMatchObject({ index: 1, actionIndex: 0, confirming: false, moved: true });
    expect(r.handled).toBe(true);
  });
  it('does nothing (but consumes the key) on an empty list', () => {
    const r = reduceKey(at(0), key('ArrowDown'), ctx({ count: 0 }));
    expect(r.state.index).toBe(0);
    expect(r.handled).toBe(true);
  });
  it('Home / End / PageUp / PageDown clamp instead of wrapping', () => {
    expect(reduceKey(at(2), key('Home'), ctx()).state.index).toBe(0);
    expect(reduceKey(at(2), key('End'), ctx()).state.index).toBe(4);
    expect(reduceKey(at(0), key('PageDown'), ctx()).state.index).toBe(3);
    expect(reduceKey(at(3), key('PageDown'), ctx()).state.index).toBe(4);
    expect(reduceKey(at(4), key('PageUp'), ctx()).state.index).toBe(1);
    expect(reduceKey(at(1), key('PageUp'), ctx()).state.index).toBe(0);
  });
  it('clamps a stale index that is beyond a shorter list', () => {
    expect(reduceKey(at(9), key('ArrowDown'), ctx()).state.index).toBe(0);
  });
});

describe('Enter', () => {
  it('runs the primary action of the selected row', () => {
    const r = reduceKey(at(2), key('Enter'), ctx());
    expect(r.effect).toEqual({ type: 'run', index: 2, actionIndex: 0 });
  });
  it('runs the action chosen with Tab', () => {
    const r = reduceKey(at(1, { actionIndex: 2 }), key('Enter'), ctx({ actionCount: () => 3 }));
    expect(r.effect).toEqual({ type: 'run', index: 1, actionIndex: 2 });
  });
  it('Alt+Enter and Ctrl+Enter run the second action', () => {
    const c = ctx({ actionCount: () => 3 });
    expect(reduceKey(at(1), key('Enter', { altKey: true }), c).effect).toEqual({ type: 'run', index: 1, actionIndex: 1 });
    expect(reduceKey(at(1), key('Enter', { ctrlKey: true }), c).effect).toEqual({ type: 'run', index: 1, actionIndex: 1 });
  });
  it('Alt+Enter does nothing when there is no second action', () => {
    const r = reduceKey(at(1), key('Enter', { altKey: true }), ctx({ actionCount: () => 1 }));
    expect(r.effect).toEqual({ type: 'none' });
    expect(r.handled).toBe(true);
  });
  it('does nothing without results', () => {
    expect(reduceKey(at(0), key('Enter'), ctx({ count: 0 })).effect).toEqual({ type: 'none' });
  });
  it('dangerous commands need a second Enter', () => {
    const c = ctx({ needsConfirm: (i) => i === 1 });
    const first = reduceKey(at(1), key('Enter'), c);
    expect(first.effect).toEqual({ type: 'none' });
    expect(first.state.confirming).toBe(true);
    const second = reduceKey(first.state, key('Enter'), c);
    expect(second.effect).toEqual({ type: 'run', index: 1, actionIndex: 0 });
    expect(second.state.confirming).toBe(false);
  });
  it('moving the selection cancels a pending confirmation', () => {
    const c = ctx({ needsConfirm: () => true });
    const armed = reduceKey(at(1), key('Enter'), c).state;
    expect(reduceKey(armed, key('ArrowDown'), c).state.confirming).toBe(false);
  });
});

describe('Tab', () => {
  it('cycles actions forward and backward with wrap', () => {
    const c = ctx({ actionCount: () => 3 });
    let s = at(0);
    s = reduceKey(s, key('Tab'), c).state; expect(s.actionIndex).toBe(1);
    s = reduceKey(s, key('Tab'), c).state; expect(s.actionIndex).toBe(2);
    s = reduceKey(s, key('Tab'), c).state; expect(s.actionIndex).toBe(0);
    s = reduceKey(s, key('Tab', { shiftKey: true }), c).state; expect(s.actionIndex).toBe(2);
  });
  it('keeps focus in the field when there is nothing to cycle', () => {
    const r = reduceKey(at(0), key('Tab'), ctx());
    expect(r.handled).toBe(true);
    expect(r.state.actionIndex).toBe(0);
  });
  it('ignores Ctrl+Tab so the browser/host can use it', () => {
    expect(reduceKey(at(0), key('Tab', { ctrlKey: true }), ctx()).handled).toBe(false);
  });
});

describe('Ctrl+1..9', () => {
  it('runs the nth result', () => {
    const r = reduceKey(at(0), key('3', { ctrlKey: true }), ctx());
    expect(r.effect).toEqual({ type: 'run', index: 2, actionIndex: 0 });
    expect(r.state.index).toBe(2);
  });
  it('ignores numbers beyond the list but consumes the key', () => {
    const r = reduceKey(at(0), key('9', { ctrlKey: true }), ctx());
    expect(r.effect).toEqual({ type: 'none' });
    expect(r.handled).toBe(true);
  });
  it('asks for confirmation on dangerous commands instead of running them', () => {
    const r = reduceKey(at(0), key('2', { ctrlKey: true }), ctx({ needsConfirm: (i) => i === 1 }));
    expect(r.effect).toEqual({ type: 'none' });
    expect(r.state).toMatchObject({ index: 1, confirming: true });
  });
  it('does not react to plain digits or Ctrl+Alt+digit', () => {
    expect(reduceKey(at(0), key('3'), ctx()).handled).toBe(false);
    expect(reduceKey(at(0), key('3', { ctrlKey: true, altKey: true }), ctx()).handled).toBe(false);
  });
});

describe('Escape', () => {
  it('clears a non-empty query first', () => {
    expect(reduceKey(at(0), key('Escape'), ctx({ hasQuery: true })).effect).toEqual({ type: 'clear' });
  });
  it('closes when the query is empty', () => {
    expect(reduceKey(at(0), key('Escape'), ctx({ hasQuery: false })).effect).toEqual({ type: 'close' });
  });
  it('cancels a pending confirmation before anything else', () => {
    const r = reduceKey(at(0, { confirming: true }), key('Escape'), ctx({ hasQuery: true }));
    expect(r.effect).toEqual({ type: 'none' });
    expect(r.state.confirming).toBe(false);
  });
});

it('ignores unrelated keys', () => {
  expect(reduceKey(at(0), key('a'), ctx()).handled).toBe(false);
  expect(reduceKey(at(0), key('Shift'), ctx()).handled).toBe(false);
});
