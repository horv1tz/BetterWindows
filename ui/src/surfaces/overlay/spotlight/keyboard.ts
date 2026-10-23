// Keyboard navigation of the Spotlight result list as a pure reducer (no DOM): easy to test, the component only maps events.

export interface NavState {
  /** Selected entry in the flat list. */
  index: number;
  /** Index into the selected result's ordered actions (0 = primary). Tab cycles it. */
  actionIndex: number;
  /** Waiting for the second Enter of a dangerous command. */
  confirming: boolean;
  /** The user navigated explicitly (stops "select the top hit" when the list is replaced). */
  moved: boolean;
}

export interface NavContext {
  count: number;
  pageSize: number;
  /** Number of actions of entry i. */
  actionCount(i: number): number;
  needsConfirm(i: number): boolean;
  /** Query is non-empty (Esc clears it first). */
  hasQuery: boolean;
}

export interface KeyInput {
  key: string;
  ctrlKey: boolean;
  altKey: boolean;
  shiftKey: boolean;
  metaKey: boolean;
}

export type NavEffect =
  | { type: 'none' }
  | { type: 'run'; index: number; actionIndex: number }
  | { type: 'clear' }
  | { type: 'close' };

export interface NavResult {
  state: NavState;
  effect: NavEffect;
  /** The component should preventDefault() (the key was consumed). */
  handled: boolean;
}

export const initialNav = (): NavState => ({ index: 0, actionIndex: 0, confirming: false, moved: false });

const none: NavEffect = { type: 'none' };

function select(s: NavState, index: number): NavState {
  return { ...s, index, actionIndex: 0, confirming: false, moved: true };
}

/** One key press -> next state + an effect for the component to execute. */
export function reduceKey(s: NavState, k: KeyInput, c: NavContext): NavResult {
  const handled = (state: NavState, effect: NavEffect = none): NavResult => ({ state, effect, handled: true });
  const ignored: NavResult = { state: s, effect: none, handled: false };
  const n = c.count;
  const last = n - 1;
  const idx = Math.min(Math.max(s.index, 0), Math.max(last, 0));

  switch (k.key) {
    case 'ArrowDown':
      if (k.altKey || k.metaKey) return ignored;
      return n ? handled(select(s, (idx + 1) % n)) : handled(s);
    case 'ArrowUp':
      if (k.altKey || k.metaKey) return ignored;
      return n ? handled(select(s, (idx - 1 + n) % n)) : handled(s);
    case 'Home':
      return n ? handled(select(s, 0)) : handled(s);
    case 'End':
      return n ? handled(select(s, last)) : handled(s);
    case 'PageDown':
      return n ? handled(select(s, Math.min(last, idx + Math.max(1, c.pageSize)))) : handled(s);
    case 'PageUp':
      return n ? handled(select(s, Math.max(0, idx - Math.max(1, c.pageSize)))) : handled(s);
    case 'Tab': {
      if (k.ctrlKey || k.altKey || k.metaKey) return ignored;
      const count = n ? c.actionCount(idx) : 0;
      if (count <= 1) return handled(s); // keep focus inside the field
      const step = k.shiftKey ? -1 : 1;
      return handled({ ...s, index: idx, actionIndex: (s.actionIndex + step + count) % count, confirming: false });
    }
    case 'Enter': {
      if (!n) return handled(s);
      if (k.altKey || k.ctrlKey) {
        // second action (e.g. "Show in folder"); nothing if the result has only one
        if (c.actionCount(idx) < 2) return handled(s);
        if (c.needsConfirm(idx) && !s.confirming) return handled({ ...s, index: idx, actionIndex: 1, confirming: true, moved: true });
        return handled({ ...s, index: idx, actionIndex: 1, confirming: false }, { type: 'run', index: idx, actionIndex: 1 });
      }
      if (k.shiftKey || k.metaKey) return ignored;
      if (c.needsConfirm(idx) && !s.confirming) return handled({ ...s, index: idx, confirming: true });
      return handled({ ...s, index: idx, confirming: false }, { type: 'run', index: idx, actionIndex: s.actionIndex });
    }
    case 'Escape':
      if (s.confirming) return handled({ ...s, confirming: false });
      if (c.hasQuery) return handled(s, { type: 'clear' });
      return handled(s, { type: 'close' });
    default: {
      // Ctrl+1..9 runs the nth result
      if (k.ctrlKey && !k.altKey && !k.metaKey && /^[1-9]$/.test(k.key)) {
        const at = Number(k.key) - 1;
        if (at >= n) return handled(s);
        if (c.needsConfirm(at)) return handled({ index: at, actionIndex: 0, confirming: true, moved: true });
        return handled({ index: at, actionIndex: 0, confirming: false, moved: true }, { type: 'run', index: at, actionIndex: 0 });
      }
      return ignored;
    }
  }
}
