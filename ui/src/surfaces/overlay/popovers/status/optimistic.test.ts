import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { optimistic } from './optimistic.svelte';

beforeEach(() => vi.useFakeTimers());
afterEach(() => vi.useRealTimers());

describe('optimistic', () => {
  it('shows the local value until the host echoes it', () => {
    const o = optimistic({ tolerance: 0.01 });
    expect(o.value).toBeNull();
    o.set(0.4);
    expect(o.value).toBe(0.4);
    o.host(0.7); // stale echo of an earlier value: keep the local value
    expect(o.value).toBe(0.4);
    o.host(0.405); // within tolerance: the host has caught up
    expect(o.value).toBeNull();
  });

  it('drops the local value after the timeout when the host never matches', () => {
    const o = optimistic({ tolerance: 0.01, timeoutMs: 1000 });
    o.set(0.9);
    vi.advanceTimersByTime(999);
    expect(o.value).toBe(0.9);
    vi.advanceTimersByTime(1);
    expect(o.value).toBeNull();
  });

  it('restarts the timeout on every change', () => {
    const o = optimistic({ tolerance: 0, timeoutMs: 1000 });
    o.set(0.1);
    vi.advanceTimersByTime(800);
    o.set(0.2);
    vi.advanceTimersByTime(800);
    expect(o.value).toBe(0.2);
    vi.advanceTimersByTime(200);
    expect(o.value).toBeNull();
  });

  it('clear() drops the value and its timer', () => {
    const o = optimistic({ tolerance: 0 });
    o.set(0.5);
    o.clear();
    expect(o.value).toBeNull();
    expect(vi.getTimerCount()).toBe(0);
  });
});
