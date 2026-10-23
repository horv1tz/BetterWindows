import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { throttle } from './throttle';

beforeEach(() => vi.useFakeTimers());
afterEach(() => vi.useRealTimers());

describe('throttle', () => {
  it('runs the first call immediately and coalesces the rest to the last value', () => {
    const fn = vi.fn();
    const t = throttle(fn, 50);
    t(1); t(2); t(3);
    expect(fn).toHaveBeenCalledTimes(1);
    expect(fn).toHaveBeenLastCalledWith(1);
    vi.advanceTimersByTime(49);
    expect(fn).toHaveBeenCalledTimes(1);
    vi.advanceTimersByTime(1);
    expect(fn).toHaveBeenCalledTimes(2);
    expect(fn).toHaveBeenLastCalledWith(3);
  });

  it('allows an immediate call again after a quiet period', () => {
    const fn = vi.fn();
    const t = throttle(fn, 50);
    t(1);
    vi.advanceTimersByTime(200);
    t(2);
    expect(fn).toHaveBeenCalledTimes(2);
    expect(fn).toHaveBeenLastCalledWith(2);
  });

  it('spaces a continuous stream by at least the interval', () => {
    const fn = vi.fn();
    const t = throttle(fn, 50);
    for (let i = 0; i < 20; i++) { t(i); vi.advanceTimersByTime(10); }
    vi.advanceTimersByTime(100);
    // 200 ms of input => at most ~5 deliveries (+ the trailing one), never 20
    expect(fn.mock.calls.length).toBeLessThanOrEqual(6);
    expect(fn).toHaveBeenLastCalledWith(19);
  });

  it('flush delivers the pending value now and cancel drops it', () => {
    const fn = vi.fn();
    const t = throttle(fn, 50);
    t(1); t(2);
    t.flush();
    expect(fn).toHaveBeenCalledTimes(2);
    expect(fn).toHaveBeenLastCalledWith(2);
    t(3); t(4);
    t.cancel();
    vi.advanceTimersByTime(500);
    expect(fn).toHaveBeenCalledTimes(3); // 3 went through immediately, 4 was dropped
  });
});
