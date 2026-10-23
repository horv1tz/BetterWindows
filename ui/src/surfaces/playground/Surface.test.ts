// The playground page: surface iframes, theme/language switching, overlay plumbing, dock sizing, hotkeys.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import Surface from './Surface.svelte';
import { cleanup, fire, flushSync, ke, me, render, screen, waitFor } from '../desktop/domTest';
import { getMockHost, resetMockHost } from '$mock/host';
import { MockBridge } from '$mock/mockBridge';
import { i18n } from '$lib/i18n.svelte';

vi.mock('svelte', async () => await import('../../../node_modules/svelte/src/index-client.js'));

const setViewport = (w: number, h: number) => { Object.assign(window, { innerWidth: w, innerHeight: h }); };
beforeEach(() => { setViewport(1440, 900); vi.spyOn(console, 'info').mockImplementation(() => {}); });
afterEach(() => { cleanup(); resetMockHost(); vi.restoreAllMocks(); i18n.set('en'); window.history.replaceState(null, '', '/'); delete document.documentElement.dataset.theme; setViewport(1024, 768); });

async function open(query = '?theme=dark') {
  window.history.replaceState(null, '', `/${query}#/playground`);
  render(Surface, {});
  await waitFor(() => expect(document.querySelector('.playground[data-ready="true"]')).not.toBeNull());
  flushSync();
  return getMockHost();
}
const frame = (title: string) => document.querySelector<HTMLIFrameElement>(`iframe[title="${title}"]`)!;
const overlay = () => document.querySelector<HTMLIFrameElement>('iframe.overlay')!;
const flush = async () => { await new Promise((r) => setTimeout(r, 10)); flushSync(); };

describe('playground', () => {
  it('composes the surfaces: desktop below, menu bar on top (28 DIP), dock and the overlay above them', async () => {
    await open();
    expect(frame('desktop 1').src).toMatch(/#\/desktop\/1$/);
    expect(frame('menubar 1').src).toMatch(/#\/menubar\/1$/);
    expect(frame('menubar 1').style.height).toBe('28px');
    expect(frame('dock').src).toMatch(/#\/dock$/);
    expect(frame('overlay').src).toMatch(/#\/overlay$/);
    expect(frame('desktop 2')).toBeNull();
    // the query (mock flags) travels with the iframes, ?theme= does not (the playground owns the theme)
    expect(frame('dock').src).not.toContain('theme=');
    expect(document.querySelectorAll('[data-faux]')).toHaveLength(3);
  });

  it('takes over the theme: applies it to the page, and the strip toggles it for every surface', async () => {
    const host = await open('?theme=light');
    expect(document.documentElement.dataset.theme).toBe('light');
    expect(host.theme.mode).toBe('light');
    const seen: string[] = [];
    const other = new MockBridge({ surface: 'dock' });
    other.on('theme.changed', (t) => seen.push(t.mode));
    await fire(document.querySelector('[data-action=theme]')!, me('click'));
    await flush();
    expect(host.theme.mode).toBe('dark');
    expect(document.documentElement.dataset.theme).toBe('dark');
    expect(seen.at(-1)).toBe('dark');
    // and it sticks: a later settings change does not bring the old ?theme= back
    await other.call('settings.patch', { patch: { dock: { iconSize: 60 } } });
    await flush();
    expect(host.theme.mode).toBe('dark');
  });

  it('switches the language for the page and every surface (en <-> ru)', async () => {
    const host = await open('?theme=dark&lang=en');
    const other = new MockBridge({ surface: 'menubar', monitorId: '1' });
    const seen: string[] = [];
    other.on('locale.changed', (l) => seen.push(l.locale));
    expect(i18n.locale).toBe('en');
    await fire(document.querySelector('[data-action=lang]')!, me('click'));
    await flush();
    expect(i18n.locale).toBe('ru');
    expect(seen.at(-1)).toBe('ru');
    expect(screen.getByRole('toolbar').getAttribute('aria-label')).toBe('Управление песочницей');
    expect(host.settings.general.language).toBe('ru');
  });

  it('?lang=ru starts in Russian', async () => {
    await open('?theme=dark&lang=ru');
    expect(i18n.locale).toBe('ru');
    expect(screen.getByRole('toolbar').getAttribute('aria-label')).toBe('Управление песочницей');
  });

  it('the overlay iframe only takes the pointer while something is shown', async () => {
    const host = await open();
    expect(overlay().classList.contains('live')).toBe(false);
    host.emit('overlay.show', { mode: 'spotlight', monitorId: '1', token: 1 });
    await flush();
    expect(overlay().classList.contains('live')).toBe(true);
    host.emit('overlay.hide', undefined as never);
    await flush();
    expect(overlay().classList.contains('live')).toBe(false);
  });

  it('hotkeys: Alt+Space, Ctrl+Alt+L, Ctrl+Alt+M show the overlay, the same key again and Esc hide it', async () => {
    const host = await open();
    const shown: string[] = [];
    (host as unknown as { showOverlay: (r: { mode: string }) => void }).showOverlay = (r) => { shown.push(r.mode); host.emit('overlay.show', { mode: r.mode as never, monitorId: '1', token: shown.length }); };
    const key = (k: string, o: KeyboardEventInit) => fire(window, ke('keydown', k, o));
    await key(' ', { code: 'Space', altKey: true });
    expect(shown).toEqual(['spotlight']);
    await flush();
    expect(overlay().classList.contains('live')).toBe(true);
    await key(' ', { code: 'Space', altKey: true }); // toggles off
    await flush();
    expect(overlay().classList.contains('live')).toBe(false);
    await key('l', { code: 'KeyL', altKey: true, ctrlKey: true });
    await key('m', { code: 'KeyM', altKey: true, ctrlKey: true }); // switches to mission control
    expect(shown).toEqual(['spotlight', 'launchpad', 'missionControl']);
    await key('Escape', { code: 'Escape' });
    await flush();
    expect(overlay().classList.contains('live')).toBe(false);
  });

  it('the dock iframe follows the dock UI\'s surface.resize, centred on the bottom edge', async () => {
    await open();
    new MockBridge({ surface: 'dock' }).emit('surface.resize', { width: 700, height: 120 });
    await flush();
    const d = frame('dock');
    expect(d.style.width).toBe('700px');
    expect(d.style.height).toBe('120px');
    expect(d.style.left).toBe('370px'); // (1440 - 700) / 2
    expect(d.style.top).toBe('780px'); // 900 - 120
  });

  it('popover anchors from the dock are translated by the dock window position', async () => {
    const host = (await open()) as unknown as { surfaceOffsets: Record<string, { x: number; y: number }> };
    new MockBridge({ surface: 'dock' }).emit('surface.resize', { width: 700, height: 120 });
    await flush();
    expect(host.surfaceOffsets.dock).toEqual({ x: 370, y: 780 });
    expect(host.surfaceOffsets.menubar).toEqual({ x: 0, y: 0 });
  });

  it('settings.openWindow opens a floating settings window; closing it removes the iframe', async () => {
    await open();
    expect(document.querySelector('[data-faux=settings]')).toBeNull();
    const bridge = new MockBridge({ surface: 'menubar', monitorId: '1' });
    await bridge.call('settings.openWindow', { page: 'widgets' });
    await flush();
    const win = document.querySelector('[data-faux=settings]') as HTMLElement;
    expect(win).not.toBeNull();
    expect(win.querySelector('iframe')?.getAttribute('src')).toMatch(/#\/settings$/);
    await fire(win.querySelector('.close')!, me('click'));
    await flush();
    expect(document.querySelector('[data-faux=settings]')).toBeNull();
  });

  it('?monitors=2 shows two desktops and menu bars (no faux windows) and puts the overlay on the monitor that asked', async () => {
    const host = await open('?theme=dark&monitors=2');
    expect(frame('desktop 1').src).toMatch(/#\/desktop\/1$/);
    expect(frame('desktop 2').src).toMatch(/#\/desktop\/2$/);
    expect(frame('menubar 2').src).toMatch(/#\/menubar\/2$/);
    expect(frame('dock')).not.toBeNull();
    expect(document.querySelectorAll('iframe[title^="dock"]')).toHaveLength(1);
    expect(document.querySelectorAll('[data-faux]')).toHaveLength(0);
    expect(overlay().style.left).toBe('0px');
    host.emit('overlay.show', { mode: 'popover', monitorId: '2', token: 1, popover: { kind: 'calendar', anchor: { x: 600, y: 0, width: 40, height: 28 } } });
    await flush();
    expect(parseInt(overlay().style.left)).toBeGreaterThan(300);
    expect(overlay().classList.contains('live')).toBe(true);
  });

  it('?strip=0 hides the control strip, ?windows=0 the faux windows', async () => {
    await open('?theme=dark&strip=0&windows=0');
    expect(screen.queryByRole('toolbar')).toBeNull();
    expect(document.querySelectorAll('[data-faux]')).toHaveLength(0);
  });

  it('faux windows can be closed with the red light and are raised when clicked', async () => {
    const host = await open();
    const active: Array<string | null> = [];
    (host as unknown as { setActiveApp: (id: string | null) => void }).setActiveApp = (id) => active.push(id);
    const browser = document.querySelector<HTMLElement>('[data-faux=win-browser]')!;
    const z = (e: HTMLElement) => Number(e.style.zIndex);
    const terminal = document.querySelector<HTMLElement>('[data-faux=win-terminal]')!;
    expect(z(terminal)).toBeGreaterThan(z(browser));
    await fire(browser, new MouseEvent('pointerdown', { bubbles: true }));
    expect(z(browser)).toBeGreaterThan(z(terminal));
    expect(active.at(-1)).toMatch(/Edge|MSEdge/i);
    await fire(browser.querySelector('.close')!, me('click'));
    expect(document.querySelector('[data-faux=win-browser]')).toBeNull();
  });
});
