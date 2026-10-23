import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
vi.mock('svelte', () => import('../spotlight/svelteClient')); // see spotlight/svelteClient.ts
import { fireEvent, screen } from '@testing-library/svelte';
import { setBridgeForTests, setInit } from '$lib/ipc';
import { MockBridge } from '$mock/mockBridge';
import { getMockHost, resetMockHost } from '$mock/host';
import { APPS } from '$mock/fixtures';
import { i18n } from '$lib/i18n.svelte';
import { cleanup, render } from '../spotlight/testing';
import Launchpad from './Launchpad.svelte';

const show = { mode: 'launchpad' as const, monitorId: '1', token: 1 };
let bridge: MockBridge;
let calls: Array<{ type: string; params: any }>;
const id = (name: string) => APPS.find((a) => a.name === name)!.id;

async function boot(patch?: Record<string, unknown>) {
  vi.useFakeTimers();
  vi.stubGlobal('ResizeObserver', class { observe() {} unobserve() {} disconnect() {} });
  i18n.set('en');
  bridge = new MockBridge({ surface: 'overlay' });
  calls = [];
  const original = bridge.call.bind(bridge);
  (bridge as any).call = (type: string, params?: unknown) => { calls.push({ type, params }); return original(type as any, params as any); };
  setBridgeForTests(bridge);
  const init = await original('surface.ready', { surface: 'overlay' });
  if (patch) await original('settings.patch', { patch });
  setInit(patch ? { ...init, settings: getMockHost().settings } : init);
  await vi.advanceTimersByTimeAsync(20);
}
afterEach(() => { cleanup(); setBridgeForTests(null); setInit(null); resetMockHost(); vi.useRealTimers(); vi.unstubAllGlobals(); });
beforeEach(() => { document.documentElement.dataset.reduceMotion = 'true'; });

const advance = (ms: number) => vi.advanceTimersByTimeAsync(ms);
const tiles = () => [...document.querySelectorAll<HTMLElement>('.track .tile')].map((t) => t.getAttribute('aria-label'));
const key = (k: string, init: KeyboardEventInit = {}) => fireEvent.keyDown(window, { key: k, ...init });
/** Alt is a tap: down + up. */
const tapAlt = async () => { await fireEvent.keyDown(window, { key: 'Alt' }); await fireEvent.keyUp(window, { key: 'Alt' }); };
const search = () => screen.getByRole('textbox') as HTMLInputElement;

describe('Launchpad grid', () => {
  it('lists every installed app alphabetically for a fresh profile', async () => {
    await boot();
    render(Launchpad, { show, onclose: vi.fn() });
    await advance(50);
    expect(tiles()).toHaveLength(APPS.length);
    expect(tiles()[0]).toBe('Calculator');
    expect(tiles().at(-1)).toBe('Word');
  });

  it('applies settings.launchpad: order, folders (shown as one tile) and hidden apps', async () => {
    await boot({ launchpad: {
      folders: [{ id: 'folder:aaaa', name: 'Office', appIds: [id('Excel'), id('Word')] }],
      order: [id('Word'), 'folder:aaaa', id('Slack')],
      hidden: [id('Steam')],
    } });
    render(Launchpad, { show, onclose: vi.fn() });
    await advance(50);
    const t = tiles();
    expect(t[0]).toMatch(/^Folder Office, 2 apps/);
    expect(t[1]).toBe('Slack');
    expect(t).not.toContain('Steam');
    expect(t).not.toContain('Excel');
    expect(t).not.toContain('Word');
    expect(t).toHaveLength(APPS.length - 3 + 1);
  });

  it('splits into pages with dots and ArrowRight/ArrowLeft turn them', async () => {
    await boot();
    render(Launchpad, { show, onclose: vi.fn() });
    await advance(50);
    const dots = () => [...document.querySelectorAll('.dot')];
    expect(dots().length).toBeGreaterThan(1);
    expect(dots()[0].getAttribute('aria-current')).toBe('page');
    await key('ArrowRight');
    expect(dots()[1].getAttribute('aria-current')).toBe('page');
    await key('ArrowLeft');
    expect(dots()[0].getAttribute('aria-current')).toBe('page');
    await key('ArrowLeft'); // clamped
    expect(dots()[0].getAttribute('aria-current')).toBe('page');
    await fireEvent.click(dots()[1]);
    expect(dots()[1].getAttribute('aria-current')).toBe('page');
  });

  it('follows apps.changed', async () => {
    await boot();
    render(Launchpad, { show, onclose: vi.fn() });
    await advance(50);
    const next = [...APPS, { ...APPS[0], id: 'exe:c:\\new.exe', name: 'Zed', path: 'C:\\new.exe' }];
    getMockHost().emit('apps.changed', { apps: next });
    await advance(20);
    expect(tiles().at(-1)).toBe('Zed');
  });
});

describe('Launchpad search', () => {
  it('filters live and Enter launches the first match then closes', async () => {
    await boot();
    const onclose = vi.fn();
    render(Launchpad, { show, onclose });
    await advance(50);
    await fireEvent.input(search(), { target: { value: 'cal' } });
    await advance(10);
    expect(tiles()).toEqual(['Calculator', 'Calendar']);
    await key('Enter');
    expect(calls.find((c) => c.type === 'apps.launch')?.params).toEqual({ appId: id('Calculator') });
    expect(onclose).toHaveBeenCalledTimes(1);
  });

  it('arrow keys move the selection inside the results and Enter launches it', async () => {
    await boot();
    render(Launchpad, { show, onclose: vi.fn() });
    await advance(50);
    await fireEvent.input(search(), { target: { value: 'cal' } });
    await key('ArrowRight');
    await key('Enter');
    expect(calls.find((c) => c.type === 'apps.launch')?.params).toEqual({ appId: id('Calendar') });
  });

  it('shows an empty state and Esc clears the query before closing', async () => {
    await boot();
    const onclose = vi.fn();
    render(Launchpad, { show, onclose });
    await advance(50);
    await fireEvent.input(search(), { target: { value: 'qqqq' } });
    expect(screen.getByText('No apps found')).toBeTruthy();
    await key('Escape');
    expect(search().value).toBe('');
    expect(onclose).not.toHaveBeenCalled();
    expect(tiles()).toHaveLength(APPS.length);
    await key('Escape');
    expect(onclose).toHaveBeenCalledTimes(1);
  });

  it('finds apps by initials and ignores hidden ones', async () => {
    await boot({ launchpad: { folders: [], order: [], hidden: [id('Terminal')] } });
    render(Launchpad, { show, onclose: vi.fn() });
    await advance(50);
    await fireEvent.input(search(), { target: { value: 'vsc' } });
    expect(tiles()).toEqual(['Visual Studio Code']);
    await fireEvent.input(search(), { target: { value: 'termin' } });
    expect(tiles()).toEqual([]);
  });
});

describe('Launchpad launching and closing', () => {
  it('a click on an app launches it and closes', async () => {
    await boot();
    const onclose = vi.fn();
    render(Launchpad, { show, onclose });
    await advance(50);
    await fireEvent.click(screen.getByRole('button', { name: 'Notepad' }));
    expect(calls.find((c) => c.type === 'apps.launch')?.params).toEqual({ appId: id('Notepad') });
    expect(onclose).toHaveBeenCalledTimes(1);
  });

  it('Esc closes when nothing else is open', async () => {
    await boot();
    const onclose = vi.fn();
    render(Launchpad, { show, onclose });
    await advance(50);
    await key('Escape');
    expect(onclose).toHaveBeenCalledTimes(1);
  });
});

describe('Launchpad edit mode and persistence', () => {
  it('Alt toggles edit mode; x hides an app, persisted after 400 ms without it in order', async () => {
    await boot();
    render(Launchpad, { show, onclose: vi.fn() });
    await advance(50);
    expect(screen.queryByRole('button', { name: /Remove .* from Launchpad/ })).toBeNull();
    await tapAlt();
    expect(screen.getAllByRole('button', { name: /Remove .* from Launchpad/ }).length).toBe(APPS.length);
    await fireEvent.click(screen.getByRole('button', { name: 'Remove Slack from Launchpad' }));
    expect(tiles()).not.toContain('Slack');
    expect(calls.some((c) => c.type === 'settings.patch')).toBe(false); // debounced
    await advance(450);
    const patch = calls.find((c) => c.type === 'settings.patch')!.params.patch.launchpad;
    expect(patch.hidden).toEqual([id('Slack')]);
    expect(patch.order).not.toContain(id('Slack'));
    expect(patch.order).toHaveLength(APPS.length - 1);
    expect(getMockHost().settings.launchpad.hidden).toEqual([id('Slack')]);
  });

  it('Alt+Tab (a chord) does not toggle edit mode', async () => {
    await boot();
    render(Launchpad, { show, onclose: vi.fn() });
    await advance(50);
    await fireEvent.keyDown(window, { key: 'Alt' });
    await fireEvent.keyDown(window, { key: 'Tab', altKey: true });
    await fireEvent.keyUp(window, { key: 'Alt' });
    expect(document.querySelector('.editbar')).toBeNull();
  });

  it('coalesces several edits into one settings.patch', async () => {
    await boot();
    render(Launchpad, { show, onclose: vi.fn() });
    await advance(50);
    await tapAlt();
    await fireEvent.click(screen.getByRole('button', { name: 'Remove Slack from Launchpad' }));
    await advance(200);
    await fireEvent.click(screen.getByRole('button', { name: 'Remove Steam from Launchpad' }));
    await advance(450);
    const patches = calls.filter((c) => c.type === 'settings.patch');
    expect(patches).toHaveLength(1);
    expect(patches[0].params.patch.launchpad.hidden).toEqual([id('Slack'), id('Steam')]);
  });

  it('restores hidden apps from the edit bar', async () => {
    await boot({ launchpad: { folders: [], order: [], hidden: [id('Slack'), id('Steam')] } });
    render(Launchpad, { show, onclose: vi.fn() });
    await advance(50);
    expect(tiles()).toHaveLength(APPS.length - 2);
    await tapAlt();
    await fireEvent.click(screen.getByRole('button', { name: 'Restore hidden apps (2)' }));
    expect(tiles()).toHaveLength(APPS.length);
    await advance(450);
    expect(getMockHost().settings.launchpad.hidden).toEqual([]);
  });

  it('Esc leaves edit mode first, and flushes pending edits when the overlay closes', async () => {
    await boot();
    const onclose = vi.fn();
    const view = render(Launchpad, { show, onclose });
    await advance(50);
    await tapAlt();
    await fireEvent.click(screen.getByRole('button', { name: 'Remove Slack from Launchpad' }));
    await key('Escape');
    expect(onclose).not.toHaveBeenCalled();
    expect(document.querySelector('.editbar')).toBeNull();
    view.unmount(); // closing the overlay within the debounce window must not lose the edit
    expect(calls.filter((c) => c.type === 'settings.patch')).toHaveLength(1);
  });
});

describe('Launchpad folders', () => {
  const folderPatch = { launchpad: { folders: [{ id: 'folder:aaaa', name: 'Office', appIds: [APPS.find((a) => a.name === 'Excel')!.id, APPS.find((a) => a.name === 'Word')!.id, APPS.find((a) => a.name === 'PowerPoint')!.id] }], order: ['folder:aaaa'], hidden: [] } };

  it('opens a folder with its apps and a named dialog; Esc closes the folder, not the Launchpad', async () => {
    await boot(folderPatch);
    const onclose = vi.fn();
    render(Launchpad, { show, onclose });
    await advance(50);
    await fireEvent.click(screen.getByRole('button', { name: /^Folder Office/ }));
    const dialog = screen.getByRole('dialog', { name: 'Office' });
    expect(dialog.querySelectorAll('.tile')).toHaveLength(3);
    expect((screen.getByLabelText('Folder name') as HTMLInputElement).value).toBe('Office');
    await key('Escape');
    expect(screen.queryByRole('dialog', { name: 'Office' })).toBeNull();
    expect(onclose).not.toHaveBeenCalled();
    await key('Escape');
    expect(onclose).toHaveBeenCalledTimes(1);
  });

  it('launches an app from inside a folder', async () => {
    await boot(folderPatch);
    const onclose = vi.fn();
    render(Launchpad, { show, onclose });
    await advance(50);
    await fireEvent.click(screen.getByRole('button', { name: /^Folder Office/ }));
    await fireEvent.click(screen.getByRole('dialog', { name: 'Office' }).querySelector('button.tile[aria-label="Word"]')!);
    expect(calls.find((c) => c.type === 'apps.launch')?.params).toEqual({ appId: id('Word') });
    expect(onclose).toHaveBeenCalled();
  });

  it('renames a folder inline and persists the name', async () => {
    await boot(folderPatch);
    render(Launchpad, { show, onclose: vi.fn() });
    await advance(50);
    await fireEvent.click(screen.getByRole('button', { name: /^Folder Office/ }));
    const input = screen.getByLabelText('Folder name') as HTMLInputElement;
    input.focus();
    await fireEvent.input(input, { target: { value: '  Work  stuff ' } });
    await fireEvent.keyDown(input, { key: 'Enter' });
    await advance(450);
    expect(getMockHost().settings.launchpad.folders[0].name).toBe('Work stuff');
  });

  it('removing apps in edit mode dissolves a folder that is left with one app', async () => {
    await boot({ launchpad: { folders: [{ id: 'folder:aaaa', name: 'Pair', appIds: [id('Excel'), id('Word')] }], order: ['folder:aaaa'], hidden: [] } });
    render(Launchpad, { show, onclose: vi.fn() });
    await advance(50);
    await tapAlt();
    await fireEvent.click(screen.getByRole('button', { name: /^Folder Pair/ }));
    await fireEvent.click(screen.getByRole('button', { name: 'Remove Excel from Launchpad' }));
    await advance(450);
    const l = getMockHost().settings.launchpad;
    expect(l.folders).toEqual([]);
    expect(l.hidden).toEqual([id('Excel')]);
    expect(l.order).toContain(id('Word'));
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('adopts settings.changed from elsewhere (settings window) when there is no local edit in flight', async () => {
    await boot();
    render(Launchpad, { show, onclose: vi.fn() });
    await advance(50);
    expect(tiles()).toContain('Slack');
    await bridge.call('settings.patch', { patch: { launchpad: { folders: [], order: [], hidden: [id('Slack')] } } });
    await advance(20);
    expect(tiles()).not.toContain('Slack');
  });
});

describe('Launchpad Russian', () => {
  it('localises chrome strings', async () => {
    await boot();
    i18n.set('ru');
    render(Launchpad, { show, onclose: vi.fn() });
    await advance(50);
    expect(screen.getByLabelText('Поиск приложений')).toBeTruthy();
    await tapAlt();
    expect(screen.getByText('Готово')).toBeTruthy();
    expect(screen.getAllByRole('button', { name: /^Убрать «.*» из Launchpad$/ }).length).toBeGreaterThan(0);
  });
});
