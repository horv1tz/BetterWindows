import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MockBridge } from '../mockBridge';
import { resetMockHost } from '../host';
import { i18n } from '../../lib/i18n.svelte';
import type { SearchResults } from '../../lib/protocol/generated';
import { evaluateExpression, formatAnswer, scoreText } from './search';

let overlay: MockBridge;
let events: SearchResults[];

beforeEach(() => {
  vi.useFakeTimers();
  i18n.set('en');
  overlay = new MockBridge({ surface: 'overlay' });
  events = [];
  overlay.on('search.results', (e) => events.push(e));
});
afterEach(() => { vi.useRealTimers(); resetMockHost(); });

const flush = (ms: number) => vi.advanceTimersByTimeAsync(ms);
/** Await a call that internally waits on (fake) timers. */
async function settle<T>(p: Promise<T>): Promise<T> { const guard = p.then((v) => v, (e) => { throw e; }); await flush(200); return guard; }

describe('evaluateExpression', () => {
  it.each([
    ['12*(3+4)', 84], ['2+3*4', 14], ['(2+3)*4', 20], ['2^3^2', 512], ['-3+5', 2], ['10/4', 2.5], ['50%', 0.5], ['200*10%', 20],
    ['sqrt(16)+1', 5], ['2*pi', 2 * Math.PI], ['12,5*2', 25], ['3 x 4', 12], ['7 × 6', 42], ['9 ÷ 3', 3], ['2 ** 10', 1024], ['  1 + 1  ', 2],
  ])('%s = %s', (input, expected) => {
    expect(evaluateExpression(input)?.value).toBeCloseTo(expected as number, 10);
  });

  it.each(['', '   ', '2+', '(1+2', '1+2)', 'foo(3)', 'ed', 'e5', '1/0', 'sqrt(-1)', '2 3', '1..2', 'alert(1)', 'process', '1+Math.PI'])('rejects %j', (input) => {
    expect(evaluateExpression(input)).toBeNull();
  });

  it('flags a bare number or constant as trivial', () => {
    expect(evaluateExpression('42')?.trivial).toBe(true);
    expect(evaluateExpression('-5')?.trivial).toBe(true);
    expect(evaluateExpression('pi')?.trivial).toBe(true);
    expect(evaluateExpression('1+1')?.trivial).toBe(false);
  });

  it('rejects absurdly long input', () => {
    expect(evaluateExpression('1+'.repeat(200) + '1')).toBeNull();
  });
});

describe('formatAnswer', () => {
  it('removes floating point noise and groups digits', () => {
    expect(formatAnswer(0.1 + 0.2)).toBe('0.3');
    expect(formatAnswer(1234567.891)).toBe('1,234,567.891');
    expect(formatAnswer(84)).toBe('84');
  });
});

describe('scoreText', () => {
  it('ranks prefix above word start above contains above initials, 0 for none', () => {
    expect(scoreText('Notepad', 'note')).toBeGreaterThan(scoreText('My Notepad', 'note'));
    expect(scoreText('My Notepad', 'note')).toBeGreaterThan(scoreText('Keynote', 'note'));
    expect(scoreText('Keynote', 'note')).toBeGreaterThan(scoreText('Visual Studio Code', 'vsc'));
    expect(scoreText('Visual Studio Code', 'vsc')).toBeGreaterThan(0);
    expect(scoreText('Notepad', 'zzz')).toBe(0);
  });
});

describe('search.query streaming', () => {
  it('streams fast providers first and files last with complete=true; each event is the full ordered list', async () => {
    await overlay.call('search.query', { requestId: 'r1', query: 'ed' });
    await flush(50);
    expect(events).toHaveLength(1);
    expect(events[0].requestId).toBe('r1');
    expect(events[0].complete).toBe(false);
    expect(events[0].results.some((r) => r.provider === 'apps')).toBe(true);
    expect(events[0].results.some((r) => r.provider === 'files')).toBe(false);

    await flush(400);
    expect(events).toHaveLength(2);
    expect(events[1].complete).toBe(true);
    const files = events[1].results.filter((r) => r.provider === 'files');
    expect(files.length).toBeGreaterThan(0);
    // the second event contains everything of the first (complete merged list, not a delta)
    for (const r of events[0].results) expect(events[1].results.map((x) => x.id)).toContain(r.id);
  });

  it('orders providers: calc, apps, settings, commands, windows, files, web', async () => {
    await overlay.call('search.query', { requestId: 'r', query: 'ed' });
    await flush(400);
    const order = ['calc', 'apps', 'settings', 'commands', 'windows', 'files', 'web'];
    const seen = events.at(-1)!.results.map((r) => order.indexOf(r.provider));
    expect(seen).toEqual([...seen].sort((a, b) => a - b));
    expect(events.at(-1)!.results.at(-1)!.provider).toBe('web');
  });

  it('answers a calculation with a calc result first and an inline preview', async () => {
    await overlay.call('search.query', { requestId: 'c', query: '12*(3+4)' });
    await flush(400);
    const first = events.at(-1)!.results[0];
    expect(first.provider).toBe('calc');
    expect(first.title).toBe('84');
    expect(first.subtitle).toBe('12*(3+4)');
    expect(first.preview).toEqual({ kind: 'text', text: '12*(3+4) = 84' });
    expect(first.actions.find((a) => a.primary)?.id).toBe('copy');
  });

  it('does not produce a calc result for plain words or bare numbers', async () => {
    await overlay.call('search.query', { requestId: 'a', query: 'ed' });
    await overlay.call('search.query', { requestId: 'b', query: '2024' });
    await flush(400);
    expect(events.flatMap((e) => e.results).some((r) => r.provider === 'calc')).toBe(false);
  });

  it('a new query cancels the previous request: no events for the old requestId and its ids stop resolving', async () => {
    await overlay.call('search.query', { requestId: 'old', query: 'note' });
    await flush(20);
    const oldId = events[0].results[0].id;
    await overlay.call('search.query', { requestId: 'new', query: 'mail' });
    await flush(500);
    expect(events.filter((e) => e.requestId === 'old')).toHaveLength(1); // only the early one
    expect(events.at(-1)!.requestId).toBe('new');
    await expect(overlay.call('search.perform', { requestId: 'old', resultId: oldId })).rejects.toMatchObject({ code: 'notFound' });
  });

  it('search.cancel stops pending events', async () => {
    await overlay.call('search.query', { requestId: 'x', query: 'note' });
    await overlay.call('search.cancel', { requestId: 'x' });
    await flush(500);
    expect(events).toHaveLength(0);
  });

  it('localises command and settings text', async () => {
    i18n.set('ru');
    await overlay.call('search.query', { requestId: 'ru', query: 'перез' });
    await flush(400);
    const cmd = events.at(-1)!.results.find((r) => r.provider === 'commands');
    expect(cmd?.title).toBe('Перезагрузить');
    expect(cmd?.subtitle).toMatch(/подтвержд/);
    expect(cmd?.actions[0].label).toBe('Выполнить');
  });

  it('matches Cyrillic file names case-insensitively', async () => {
    await overlay.call('search.query', { requestId: 'cy', query: 'РЕЗЮМЕ' });
    await flush(400);
    expect(events.at(-1)!.results.some((r) => r.provider === 'files' && r.title === 'Резюме.docx')).toBe(true);
  });

  it('validates the payload', async () => {
    await expect(overlay.call('search.query', { requestId: '', query: 'x' })).rejects.toMatchObject({ code: 'badRequest' });
    await expect(overlay.call('search.query', { requestId: 'z', query: 'x'.repeat(600) })).rejects.toMatchObject({ code: 'badRequest' });
  });
});

describe('search.recent / preview / perform', () => {
  it('returns recents with their own requestId that perform and preview accept', async () => {
    const rec = await overlay.call('search.recent');
    expect(rec.requestId).toMatch(/^recent-/);
    expect(rec.results.length).toBeGreaterThan(2);
    const file = rec.results.find((r) => r.title === 'Meeting notes.txt')!;
    const pv = await settle(overlay.call('search.preview', { requestId: rec.requestId, resultId: file.id }));
    expect(pv.kind).not.toBe('none');
    await overlay.call('search.perform', { requestId: rec.requestId, resultId: rec.results[0].id });
  });

  it('recents die with the next search.query', async () => {
    const rec = await overlay.call('search.recent');
    await overlay.call('search.query', { requestId: 'q', query: 'a' });
    await expect(overlay.call('search.perform', { requestId: rec.requestId, resultId: rec.results[0].id })).rejects.toMatchObject({ code: 'notFound' });
  });

  it('preview: text for text files, image for images, none otherwise, notFound for unknown ids', async () => {
    await overlay.call('search.query', { requestId: 'p', query: 'notes' });
    await flush(400);
    const results = events.at(-1)!.results;
    const txt = results.find((r) => r.title === 'Meeting notes.txt')!;
    const p1 = await settle(overlay.call('search.preview', { requestId: 'p', resultId: txt.id }));
    expect(p1.kind).toBe('text');
    expect(p1.text).toContain('Weekly sync');
    const app = results.find((r) => r.provider === 'apps');
    if (app) expect((await settle(overlay.call('search.preview', { requestId: 'p', resultId: app.id }))).kind).toBe('none');
    await expect(overlay.call('search.preview', { requestId: 'p', resultId: 'nope' })).rejects.toMatchObject({ code: 'notFound' });

    await overlay.call('search.query', { requestId: 'p2', query: 'vacation' });
    await flush(400);
    const img = events.at(-1)!.results.find((r) => r.provider === 'files')!;
    const p2 = await settle(overlay.call('search.preview', { requestId: 'p2', resultId: img.id }));
    expect(p2.kind).toBe('image');
    expect(p2.imageUrl).toMatch(/^data:image\/svg/);
  });

  it('perform hides the overlay and rejects unknown actions', async () => {
    const hidden = vi.fn();
    overlay.on('overlay.hide', hidden);
    await overlay.call('search.query', { requestId: 'h', query: 'notepad' });
    await flush(400);
    const app = events.at(-1)!.results.find((r) => r.provider === 'apps')!;
    await expect(overlay.call('search.perform', { requestId: 'h', resultId: app.id, actionId: 'bogus' })).rejects.toMatchObject({ code: 'badRequest' });
    await overlay.call('search.perform', { requestId: 'h', resultId: app.id, actionId: 'open' });
    await flush(10);
    expect(hidden).toHaveBeenCalledTimes(1);
  });
});
