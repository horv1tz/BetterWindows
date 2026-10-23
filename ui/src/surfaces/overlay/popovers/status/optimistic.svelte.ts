/**
 * Optimistic value for a control the user is changing (slider, brightness, volume). After a user change the local value is
 * shown; it is dropped when the host echoes a value within `tolerance`, or after `timeoutMs` in case the host clamps or
 * ignores the change. Stale host events in between do not make the control jump back while the user drags.
 */
export function optimistic(opts: { tolerance: number; timeoutMs?: number }) {
  let local = $state<number | null>(null);
  let timer: ReturnType<typeof setTimeout> | null = null;

  function clear() {
    if (timer !== null) { clearTimeout(timer); timer = null; }
    local = null;
  }

  return {
    /** The pending local value, or null when the host value should be shown. */
    get value(): number | null { return local; },
    /** The user changed the control to v. */
    set(v: number) {
      local = v;
      if (timer !== null) clearTimeout(timer);
      timer = setTimeout(clear, opts.timeoutMs ?? 1500);
    },
    /** A host state event arrived with value v. */
    host(v: number) {
      if (local !== null && Math.abs(v - local) <= opts.tolerance) clear();
    },
    clear,
  };
}
