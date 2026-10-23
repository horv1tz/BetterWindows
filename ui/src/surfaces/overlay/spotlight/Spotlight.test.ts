import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
vi.mock('svelte', () => import('./svelteClient')); // see svelteClient.ts
import { fireEvent, screen } from '@testing-library/svelte';
import { cleanup, render } from './testing';
import { setBridgeForTests } from '$lib/ipc';
import { MockBridge } from '$mock/mockBridge';
import { resetMockHost } from '$mock/host';
import { i18n } from '$lib/i18n.svelte';
import Spotlight from './Spotlight.svelte';

const show = { mode: 'spotlight' as const, monitorId: '1', token: 1 };
let bridge: MockBridge;
let calls: Array<{ type: string; params: any }>;

beforeEach(() => {
  vi.useFakeTimers();
  vi.stubGlobal('ResizeObserver', class { observe() {} unobserve() {} disconnect() {} });
  Element.prototype.scrollIntoView = vi.fn();
  HTMLElement.prototype.scrollTo = vi.fn() as any;
  i18n.set('en');
  bridge = new MockBridge({ surface: 'overlay' });
  calls = [];
  const original = bridge.call.bind(bridge);
  (bridge as any).call = (type: string, params?: unknown) => { calls.push({ type, params }); return original(type as any, params as any); };
  setBridgeForTests(bridge);
});
afterEach(() => { cleanup(); setBridgeForTests(null); resetMockHost(); vi.useRealTimers(); vi.unstubAllGlobals(); });

const advance = (ms: number) => vi.advanceTimersByTimeAsync(ms);
const input = () => screen.getByRole('combobox') as HTMLInputElement;
async function type(text: string, wait = 450) {
  await fireEvent.input(input(), { target: { value: text } });
  await advance(wait);
}
const key = (k: string, init: KeyboardEventInit = {}) => fireEvent.keyDown(window, { key: k, ...init });
const options = () => screen.queryAllByRole('option');
const selected = () => options().find((o) => o.getAttribute('aria-selected') === 'true');

describe('Spotlight', () => {
  it('focuses the field and shows recents under a "Recent" header when the query is empty', async () => {
    const onclose = vi.fn();
    render(Spotlight, { show, onclose });
    await advance(50);
    expect(document.activeElement).toBe(input());
    expect(screen.getByText('Recent')).toBeTruthy();
    expect(options().length).toBeGreaterThan(2);
    expect(selected()?.textContent).toContain('Visual Studio Code');
  });

  it('groups results by provider with localised headers and emphasises the match', async () => {
    render(Spotlight, { show, onclose: vi.fn() });
    await type('ed');
    expect(screen.getByText('Applications')).toBeTruthy();
    expect(screen.getByText('System Settings')).toBeTruthy();
    const marks = [...document.querySelectorAll('mark')].map((m) => m.textContent?.toLowerCase());
    expect(marks).toContain('ed');
    expect(screen.getByText('Files')).toBeTruthy(); // files streamed in later and replaced the list
  });

  it('localises the headers in Russian', async () => {
    i18n.set('ru');
    render(Spotlight, { show, onclose: vi.fn() });
    await type('рез');
    expect(screen.getByText('Команды')).toBeTruthy();
    expect(screen.getByText('Файлы')).toBeTruthy();
  });

  it('ArrowDown wraps and Enter performs the primary action then closes', async () => {
    const onclose = vi.fn();
    render(Spotlight, { show, onclose });
    await type('notepad');
    const count = options().length;
    expect(selected()?.textContent).toContain('Notepad');
    await key('ArrowUp'); // wraps to the last (web) result
    expect(selected()).toBe(options()[count - 1]);
    await key('ArrowDown');
    expect(selected()).toBe(options()[0]);
    await key('Enter');
    const perform = calls.find((c) => c.type === 'search.perform');
    expect(perform?.params.actionId).toBe('open');
    expect(perform?.params.resultId).toBe('app:aumid:Microsoft.WindowsNotepad_8wekyb3d8bbwe!App');
    expect(onclose).toHaveBeenCalledTimes(1);
  });

  it('Tab cycles the actions of the selected row and Enter runs the chosen one', async () => {
    const onclose = vi.fn();
    render(Spotlight, { show, onclose });
    await type('notes');
    expect(selected()?.textContent).toContain('Meeting notes.txt');
    await key('Tab');
    await key('Enter');
    expect(calls.find((c) => c.type === 'search.perform')?.params.actionId).toBe('reveal');
  });

  it('Ctrl+Enter runs the second action', async () => {
    render(Spotlight, { show, onclose: vi.fn() });
    await type('notes');
    await key('Enter', { ctrlKey: true });
    expect(calls.find((c) => c.type === 'search.perform')?.params.actionId).toBe('reveal');
  });

  it('Ctrl+2 runs the second result', async () => {
    render(Spotlight, { show, onclose: vi.fn() });
    await type('ed');
    const second = options()[1];
    await key('2', { ctrlKey: true });
    const perform = calls.find((c) => c.type === 'search.perform');
    expect(perform).toBeTruthy();
    expect(second.textContent).toBeTruthy();
  });

  it('Esc clears the query first and closes only when it is already empty', async () => {
    const onclose = vi.fn();
    render(Spotlight, { show, onclose });
    await type('ed');
    await key('Escape');
    expect(input().value).toBe('');
    expect(onclose).not.toHaveBeenCalled();
    await advance(50);
    await key('Escape');
    expect(onclose).toHaveBeenCalledTimes(1);
  });

  it('shows the calculator answer card and Enter copies it', async () => {
    const onclose = vi.fn();
    render(Spotlight, { show, onclose });
    await type('12*(3+4)');
    expect(screen.getAllByText('84').length).toBeGreaterThan(0); // card + preview pane
    expect(options()[0].textContent).toContain('12*(3+4)');
    await key('Enter');
    expect(calls.find((c) => c.type === 'search.perform')?.params).toMatchObject({ resultId: 'calc:result', actionId: 'copy' });
  });

  it('dangerous commands need a second Enter', async () => {
    const onclose = vi.fn();
    render(Spotlight, { show, onclose });
    await type('restart');
    expect(selected()?.textContent).toContain('Restart');
    await key('Enter');
    expect(calls.some((c) => c.type === 'search.perform')).toBe(false);
    expect(screen.getAllByText('Press Enter again to confirm').length).toBeGreaterThan(0);
    await key('Enter');
    expect(calls.find((c) => c.type === 'search.perform')?.params.resultId).toBe('cmd:restart');
    expect(onclose).toHaveBeenCalled();
  });

  it('shows "No results" when nothing matches', async () => {
    render(Spotlight, { show, onclose: vi.fn() });
    await type('ъ');
    expect(screen.getAllByText('No results').length).toBeGreaterThan(0);
    expect(options()).toHaveLength(0);
  });

  it('Enter pressed before the answer arrives runs the top hit of the newest query', async () => {
    const onclose = vi.fn();
    render(Spotlight, { show, onclose });
    await advance(50);
    await fireEvent.input(input(), { target: { value: 'notepad' } });
    await key('Enter'); // list on screen is still the recents of the previous state
    expect(calls.some((c) => c.type === 'search.perform')).toBe(false);
    await advance(100);
    const perform = calls.find((c) => c.type === 'search.perform');
    expect(perform?.params.resultId).toContain('Notepad');
    expect(onclose).toHaveBeenCalledTimes(1);
  });

  it('requests the preview of the selected result only', async () => {
    render(Spotlight, { show, onclose: vi.fn() });
    await type('notes');
    await advance(300);
    const previews = calls.filter((c) => c.type === 'search.preview');
    // only ever the selected result: first the top hit of the fast providers, then the file that took over the selection
    expect(previews.length).toBeLessThanOrEqual(2);
    expect(previews.at(-1)?.params.resultId).toContain('Meeting notes.txt');
    expect(previews.every((p) => p.params.resultId.startsWith('file:') || p.params.resultId.startsWith('web:'))).toBe(true);
    expect(screen.getAllByText(/Weekly sync/).length).toBeGreaterThan(0);
  });

  it('clicking a row runs it', async () => {
    const onclose = vi.fn();
    render(Spotlight, { show, onclose });
    await type('notepad');
    await fireEvent.click(options()[0]);
    expect(calls.some((c) => c.type === 'search.perform')).toBe(true);
    expect(onclose).toHaveBeenCalled();
  });

  it('cancels the active request on teardown', async () => {
    const { unmount } = render(Spotlight, { show, onclose: vi.fn() });
    await type('note', 100);
    unmount();
    expect(calls.some((c) => c.type === 'search.cancel')).toBe(true);
  });
});
