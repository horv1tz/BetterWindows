// A trailing debouncer with flush/cancel, used by the notes widget (600 ms) so typing is persisted without one call per keystroke.
export interface Debouncer<T> {
  push(value: T): void;
  /** Run the pending call now (no-op when nothing is pending). */
  flush(): void;
  cancel(): void;
  readonly pending: boolean;
}

export function createDebouncer<T>(fn: (value: T) => void, ms: number): Debouncer<T> {
  let timer: ReturnType<typeof setTimeout> | null = null;
  let last: { value: T } | null = null;
  const run = () => {
    if (timer) { clearTimeout(timer); timer = null; }
    const l = last;
    last = null;
    if (l) fn(l.value);
  };
  return {
    push(value) {
      last = { value };
      if (timer) clearTimeout(timer);
      timer = setTimeout(run, ms);
    },
    flush: run,
    cancel() { if (timer) clearTimeout(timer); timer = null; last = null; },
    get pending() { return last !== null; },
  };
}
