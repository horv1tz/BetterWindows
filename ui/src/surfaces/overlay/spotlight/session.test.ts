import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MockBridge } from '$mock/mockBridge';
import { resetMockHost } from '$mock/host';
import { i18n } from '$lib/i18n.svelte';
import { SearchSession } from './session.svelte';

let bridge: MockBridge;
let calls: Array<{ type: string; params: any }>;
let session: SearchSession;

beforeEach(() => {
  vi.useFakeTimers();
  i18n.set('en');
  bridge = new MockBridge({ surface: 'overlay' });
  calls = [];
  const original = bridge.call.bind(bridge);
  (bridge as any).call = (type: string, params?: unknown) => { calls.push({ type, params }); return original(type as any, params as any); };
  session = new SearchSession(bridge, { debounceMs: 30, previewDelayMs: 120 });
});
afterEach(() => { session.dispose(); vi.useRealTimers(); resetMockHost(); });

const advance = (ms: number) => vi.advanceTimersByTimeAsync(ms);
const queries = () => calls.filter((c) => c.type === 'search.query').map((c) => c.params);

describe('recents', () => {
  it('loads search.recent on start and shows it under its own requestId', async () => {
    await advance(10);
    expect(session.shown.kind).toBe('recent');
    expect(session.shown.requestId).toMatch(/^recent-/);
    expect(session.shown.results.length).toBeGreaterThan(0);
    expect(session.current).toBe(true);
  });

  it('shows recents again (fresh requestId) after the query is cleared and cancels the active request', async () => {
    await advance(10);
    const firstRecent = session.shown.requestId;
    session.setQuery('note');
    await advance(40);
    const active = queries()[0].requestId;
    session.setQuery('');
    await advance(10);
    expect(calls.some((c) => c.type === 'search.cancel' && c.params.requestId === active)).toBe(true);
    expect(session.shown.kind).toBe('recent');
    expect(session.shown.requestId).not.toBe(firstRecent);
  });
});

describe('querying', () => {
  it('debounces keystrokes (30 ms) and uses a fresh requestId per query', async () => {
    session.setQuery('n');
    await advance(10);
    session.setQuery('no');
    await advance(10);
    session.setQuery('not');
    await advance(29);
    expect(queries()).toHaveLength(0);
    await advance(2);
    expect(queries()).toHaveLength(1);
    expect(queries()[0].query).toBe('not');
    session.setQuery('note');
    await advance(40);
    expect(queries()).toHaveLength(2);
    expect(queries()[1].requestId).not.toBe(queries()[0].requestId);
  });

  it('renders only the latest request: replaces (not appends) and keeps the old list while loading', async () => {
    await advance(10);
    session.setQuery('mail');
    await advance(60);
    const first = session.shown.results.map((r) => r.id);
    expect(session.shown.kind).toBe('results');
    expect(session.complete).toBe(false);
    expect(session.searching).toBe(true);
    await advance(400);
    expect(session.complete).toBe(true);
    expect(session.searching).toBe(false);
    // files arrived: the list is replaced by the complete merged list, never duplicated
    const ids = session.shown.results.map((r) => r.id);
    expect(new Set(ids).size).toBe(ids.length);
    for (const id of first) expect(ids).toContain(id);

    const before = session.shown.results.map((r) => r.id);
    session.setQuery('note'); // typed, not yet answered: the previous list stays visible (no flicker)
    expect(session.shown.results.map((r) => r.id)).toEqual(before);
    expect(session.answered).toBe(false);
    await advance(60);
    expect(session.shown.results.map((r) => r.id)).not.toEqual(before);
  });

  it('ignores late events of an older request', async () => {
    session.setQuery('mail');
    await advance(40);
    const oldId = queries()[0].requestId;
    session.setQuery('notepad');
    await advance(40);
    const newId = queries()[1].requestId;
    // simulate a stale delivery from the first request
    (bridge as any).deliver('search.results', { requestId: oldId, results: [{ id: 'stale', provider: 'apps', title: 'stale', score: 1, actions: [] }], complete: true });
    await advance(5);
    expect(session.shown.requestId).toBe(newId);
    expect(session.shown.results.some((r) => r.id === 'stale')).toBe(false);
  });

  it('trims the query and treats whitespace as empty', async () => {
    session.setQuery('   ');
    await advance(100);
    expect(queries()).toHaveLength(0);
    session.setQuery('  note  ');
    await advance(40);
    expect(queries()[0].query).toBe('note');
  });
});

describe('perform', () => {
  it('uses the requestId of the displayed list (recents included)', async () => {
    await advance(10);
    const r = session.shown.results[0];
    await session.perform(session.shown, r, 'open');
    const call = calls.find((c) => c.type === 'search.perform')!;
    expect(call.params).toEqual({ requestId: session.shown.requestId, resultId: r.id, actionId: 'open' });
  });

  it('runWhenReady waits for the first answer of the newest query and flushes the debounce', async () => {
    await advance(10);
    session.setQuery('notepad');
    const ran = vi.fn();
    session.runWhenReady(ran);
    expect(queries()).toHaveLength(1); // flushed immediately
    expect(ran).not.toHaveBeenCalled();
    await advance(50);
    expect(ran).toHaveBeenCalledTimes(1);
    expect(ran.mock.calls[0][0].results[0].title).toBe('Notepad');
  });

  it('typing again drops a queued run', async () => {
    session.setQuery('notepad');
    const ran = vi.fn();
    session.runWhenReady(ran);
    session.setQuery('notepa');
    await advance(500);
    expect(ran).not.toHaveBeenCalled();
  });

  it('records an error when perform fails (stale ids) instead of throwing', async () => {
    await advance(10);
    await session.perform({ kind: 'results', requestId: 'gone', results: [] }, { id: 'x', provider: 'apps', title: 'x', score: 1, actions: [] });
    expect(session.error).not.toBe('');
  });
});

describe('preview', () => {
  async function filesResult() {
    session.setQuery('notes');
    await advance(500);
    return session.shown.results.find((r) => r.title === 'Meeting notes.txt')!;
  }

  it('requests the preview of the selected result after 120 ms and ignores stale answers', async () => {
    const file = await filesResult();
    const other = session.shown.results.find((r) => r.provider === 'files' && r.id !== file.id)!;
    session.select(other);
    await advance(50);
    session.select(file); // superseded before the first request fired
    await advance(100);
    expect(calls.filter((c) => c.type === 'search.preview')).toHaveLength(0);
    await advance(60);
    expect(calls.filter((c) => c.type === 'search.preview')).toHaveLength(1);
    await advance(100);
    expect(session.preview?.key).toBe(file.id);
    expect(session.preview?.data.kind).toBe('text');
    expect(session.previewLoading).toBe(false);
  });

  it('uses the inline preview (calculator) without a call', async () => {
    session.setQuery('12*(3+4)');
    await advance(60);
    const calc = session.shown.results[0];
    session.select(calc);
    expect(session.preview?.data).toEqual({ kind: 'text', text: '12*(3+4) = 84' });
    await advance(300);
    expect(calls.filter((c) => c.type === 'search.preview')).toHaveLength(0);
  });

  it('caches previews per result and clears with null', async () => {
    const file = await filesResult();
    session.select(file);
    await advance(300);
    const n = calls.filter((c) => c.type === 'search.preview').length;
    session.select(null);
    expect(session.preview).toBeNull();
    session.select(file);
    expect(session.preview?.key).toBe(file.id);
    await advance(300);
    expect(calls.filter((c) => c.type === 'search.preview').length).toBe(n);
  });
});
