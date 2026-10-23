import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createDebouncer } from './debounce';

beforeEach(() => vi.useFakeTimers());
afterEach(() => vi.useRealTimers());

describe('createDebouncer', () => {
  it('fires once with the last value after the quiet period', () => {
    const fn = vi.fn();
    const d = createDebouncer<string>(fn, 600);
    d.push('a'); vi.advanceTimersByTime(300);
    d.push('ab'); vi.advanceTimersByTime(599);
    expect(fn).not.toHaveBeenCalled();
    expect(d.pending).toBe(true);
    vi.advanceTimersByTime(1);
    expect(fn).toHaveBeenCalledExactlyOnceWith('ab');
    expect(d.pending).toBe(false);
  });
  it('flush runs immediately and only once', () => {
    const fn = vi.fn();
    const d = createDebouncer<number>(fn, 600);
    d.flush();
    expect(fn).not.toHaveBeenCalled();
    d.push(1); d.flush();
    expect(fn).toHaveBeenCalledExactlyOnceWith(1);
    vi.advanceTimersByTime(1000);
    expect(fn).toHaveBeenCalledTimes(1);
  });
  it('cancel drops the pending value', () => {
    const fn = vi.fn();
    const d = createDebouncer<number>(fn, 100);
    d.push(1); d.cancel();
    vi.advanceTimersByTime(500);
    expect(fn).not.toHaveBeenCalled();
    expect(d.pending).toBe(false);
  });
  it('falsy values are still delivered', () => {
    const fn = vi.fn();
    const d = createDebouncer<string>(fn, 10);
    d.push(''); vi.advanceTimersByTime(10);
    expect(fn).toHaveBeenCalledWith('');
  });
});
