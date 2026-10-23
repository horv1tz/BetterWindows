/**
 * Leading + trailing throttle: the first call runs immediately, later calls inside `ms` are coalesced and the LAST
 * value is delivered when the window ends. `flush()` delivers a pending value now, `cancel()` drops it.
 * Used for slider drags so the host sees at most one brightness/volume call per 50 ms but always the final value.
 */
export interface Throttled<A extends unknown[]> {
  (...args: A): void;
  flush(): void;
  cancel(): void;
}

export function throttle<A extends unknown[]>(fn: (...args: A) => void, ms: number): Throttled<A> {
  let timer: ReturnType<typeof setTimeout> | null = null;
  let pending: A | null = null;

  const fire = () => {
    timer = null;
    if (pending) {
      const args = pending;
      pending = null;
      fn(...args);
      timer = setTimeout(fire, ms); // keep spacing consecutive deliveries by `ms`
    }
  };

  const throttled = ((...args: A) => {
    if (timer === null) {
      fn(...args);
      timer = setTimeout(fire, ms);
    } else {
      pending = args;
    }
  }) as Throttled<A>;

  throttled.flush = () => {
    if (timer !== null) { clearTimeout(timer); timer = null; }
    if (pending) {
      const args = pending;
      pending = null;
      fn(...args);
    }
  };
  throttled.cancel = () => {
    if (timer !== null) { clearTimeout(timer); timer = null; }
    pending = null;
  };
  return throttled;
}
