import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

// vitest resolves `svelte` to its SSR entry (vite.config.ts has no `browser` condition): use the client build so mount() works in jsdom.
vi.mock('svelte', () => import('../../../node_modules/svelte/src/index-client.js'));

import { cleanup, fireEvent, render, screen } from '@testing-library/svelte';
import { setBridgeForTests, setInit } from '$lib/ipc';
import Surface from './Surface.svelte';
import { boot, tick } from './testenv';
import { appByName } from '../../mock/fixtures';

const edge = appByName('Microsoft Edge').id;
const terminal = appByName('Terminal').id;
const mail = appByName('Mail').id;

afterEach(() => { cleanup(); setBridgeForTests(null); setInit(null); });

async function mountDock(lang = 'en') {
  const env = await boot('dock', lang);
  const view = render(Surface);
  await tick(20);
  return { ...env, view };
}
const btn = (key: string) => [...document.querySelectorAll<HTMLButtonElement>('button[data-key]')].find((b) => b.dataset.key === key)!;

describe('dock surface', () => {
  let env: Awaited<ReturnType<typeof mountDock>>;
  beforeEach(async () => { env = await mountDock(); });

  it('renders one labelled button per app/folder/minimised window inside a toolbar', () => {
    const toolbar = screen.getByRole('toolbar', { name: 'Dock' });
    expect(toolbar.getAttribute('aria-orientation')).toBe('horizontal');
    const buttons = toolbar.querySelectorAll('button[data-key]');
    expect(buttons.length).toBe(9 + 1 + 1 + 1); // pinned apps + Notepad + Downloads + minimised Spotify window
    expect(btn(edge).getAttribute('aria-label')).toBe('Microsoft Edge, 3 windows');
    expect(btn(terminal).getAttribute('aria-label')).toBe('Terminal, running');
    expect(btn(mail).getAttribute('aria-label')).toBe('Mail');
    expect(document.querySelector('button[aria-label$="minimized"]')).not.toBeNull();
    expect(btn(edge).getAttribute('aria-current')).toBe('true');
  });

  it('has exactly one tab stop (roving tabindex)', () => {
    const tabbable = [...document.querySelectorAll('button[data-key]')].filter((b) => (b as HTMLElement).tabIndex === 0);
    expect(tabbable).toHaveLength(1);
  });

  it('shows running indicators only for running apps', () => {
    expect(btn(terminal).closest('.slot')!.querySelector('.dot')).not.toBeNull();
    expect(btn(mail).closest('.slot')!.querySelector('.dot')).toBeNull();
  });

  it('announces its size to the host: bar thickness is always sent', async () => {
    await tick(40);
    const resize = env.events.filter((e) => e.type === 'surface.resize');
    expect(resize.length).toBeGreaterThan(0);
    const last = resize.at(-1)!.payload;
    expect(last.barThickness).toBeGreaterThan(60);
    expect(last.height).toBeGreaterThanOrEqual(last.barThickness);
    expect(last.width).toBeGreaterThan(400);
  });

  it('click on a single-window app activates it', async () => {
    await fireEvent.click(btn(terminal));
    expect(env.calls.filter((c) => c.type === 'dock.activate')).toEqual([{ type: 'dock.activate', params: { itemId: terminal } }]);
  });

  it('click on a not running app launches it (activate) and the host answers with a launch bounce', async () => {
    await fireEvent.click(btn(mail));
    expect(env.calls.some((c) => c.type === 'dock.activate' && c.params.itemId === mail)).toBe(true);
  });

  it('click on an app with several windows opens the window list popover instead of activating', async () => {
    await fireEvent.click(btn(edge));
    const open = env.calls.filter((c) => c.type === 'overlay.open');
    expect(open).toHaveLength(1);
    expect(open[0].params).toMatchObject({ mode: 'popover', popover: { kind: 'windowList', itemId: edge } });
    expect(env.calls.some((c) => c.type === 'dock.activate')).toBe(false);
  });

  it('right click (context menu) opens the dock menu for that item', async () => {
    await fireEvent.contextMenu(btn(terminal));
    const open = env.calls.find((c) => c.type === 'overlay.open')!;
    expect(open.params.popover).toMatchObject({ kind: 'dockMenu', itemId: terminal });
    expect(open.params.popover.anchor).toEqual(expect.objectContaining({ x: expect.any(Number), width: expect.any(Number) }));
  });

  it('the menu key and Shift+F10 open the dock menu from the keyboard', async () => {
    await fireEvent.keyDown(btn(terminal), { key: 'ContextMenu' });
    await fireEvent.keyDown(btn(edge), { key: 'F10', shiftKey: true });
    const kinds = env.calls.filter((c) => c.type === 'overlay.open').map((c) => `${c.params.popover.kind}:${c.params.popover.itemId}`);
    expect(kinds).toEqual([`dockMenu:${terminal}`, `dockMenu:${edge}`]);
  });

  it('arrow keys move focus along the dock, Home/End jump', async () => {
    const all = [...document.querySelectorAll<HTMLButtonElement>('button[data-key]')];
    all[0].focus();
    await fireEvent.keyDown(all[0], { key: 'ArrowRight' });
    expect(document.activeElement).toBe(all[1]);
    await fireEvent.keyDown(all[1], { key: 'End' });
    expect(document.activeElement).toBe(all.at(-1));
    await fireEvent.keyDown(all.at(-1)!, { key: 'Home' });
    expect(document.activeElement).toBe(all[0]);
    await fireEvent.keyDown(all[0], { key: 'ArrowLeft' });
    expect(document.activeElement).toBe(all[0]);
  });

  it('clicking a minimised window tile restores that window', async () => {
    const tile = document.querySelector<HTMLButtonElement>('button[data-key^="win:"]')!;
    await fireEvent.click(tile);
    expect(env.calls.some((c) => c.type === 'dock.activateWindow' && `win:${c.params.handle}` === tile.dataset.key)).toBe(true);
  });

  it('follows dock.state: unpinning in the host removes the pinned flag, closing windows updates the label', async () => {
    await env.bridge.call('dock.closeWindow', { handle: env.host.dockWorld.state().items.find((i: any) => i.id === edge).windows[0].handle });
    await tick(20);
    expect(btn(edge).getAttribute('aria-label')).toBe('Microsoft Edge, 2 windows');
    await env.bridge.call('dock.quit', { itemId: terminal });
    await tick(20);
    expect(btn(terminal).getAttribute('aria-label')).toBe('Terminal');
    expect(btn(terminal).closest('.slot')!.querySelector('.dot')).toBeNull();
  });
});

describe('dock surface (ru)', () => {
  it('speaks Russian: toolbar name, window counts with correct plural forms', async () => {
    const env = await mountDock('ru');
    expect(screen.getByRole('toolbar', { name: 'Док' })).toBeTruthy();
    expect(btn(edge).getAttribute('aria-label')).toBe('Microsoft Edge, 3 окна');
    expect(btn(terminal).getAttribute('aria-label')).toBe('Terminal, запущено');
    void env;
  });
});

describe('dock surface with a left dock', () => {
  it('is a vertical toolbar and sends width as the cross axis', async () => {
    const env = await boot('dock');
    await env.bridge.call('settings.patch', { patch: { dock: { position: 'left' } } });
    render(Surface);
    await tick(60);
    expect(screen.getByRole('toolbar').getAttribute('aria-orientation')).toBe('vertical');
    const last = env.events.filter((e) => e.type === 'surface.resize').at(-1)!.payload;
    expect(last.width).toBeLessThan(200);
    expect(last.height).toBeGreaterThan(400);
  });
});
