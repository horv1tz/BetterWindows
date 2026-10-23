import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

// vitest resolves `svelte` to its SSR entry (vite.config.ts has no `browser` condition): use the client build so mount() works in jsdom.
// @ts-expect-error untyped deep import (bypasses the package exports map on purpose)
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
    await env.bridge.call('dock.closeWindow', { handle: env.host.dockWorld.state().items.find((i) => i.id === edge)!.windows[0].handle });
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

// ---- drag & drop through real pointer events (jsdom has no layout: the stage rectangle is 0x0 at the origin, so a pointer at (x, -y) is y px above the dock's bottom edge)
describe('dock drag and drop', () => {
  const slotCentre = (key: string) => {
    const slot = btn(key).closest<HTMLElement>('.slot')!;
    return parseFloat(slot.style.left) + parseFloat(slot.style.width) / 2;
  };
  const ptr = (el: Element, type: string, x: number, y: number) =>
    fireEvent(el, new MouseEvent(type, { bubbles: true, cancelable: true, button: 0, clientX: x, clientY: y }));
  async function dragTo(key: string, x: number, y: number, release = true) {
    const from = slotCentre(key);
    await ptr(btn(key), 'pointerdown', from, -30);
    for (let i = 1; i <= 6; i++) await ptr(btn(key), 'pointermove', from + ((x - from) * i) / 6, -30 + ((y + 30) * i) / 6);
    if (release) await ptr(btn(key), 'pointerup', x, y);
    await tick(10);
  }
  const pinnedOrder = (env: Awaited<ReturnType<typeof mountDock>>) => env.host.settings.dock.pins.map((p) => p.id);

  it('a short press is still a click, not a drag', async () => {
    const env = await mountDock();
    await ptr(btn(terminal), 'pointerdown', slotCentre(terminal), -30);
    await ptr(btn(terminal), 'pointermove', slotCentre(terminal) + 2, -30);
    await ptr(btn(terminal), 'pointerup', slotCentre(terminal) + 2, -30);
    await fireEvent.click(btn(terminal));
    expect(env.calls.some((c) => c.type === 'dock.activate' && c.params.itemId === terminal)).toBe(true);
    expect(env.calls.some((c) => c.type === 'dock.reorder')).toBe(false);
  });

  it('dropping a pinned app on another slot sends the full new pinned order and updates the dock', async () => {
    const env = await mountDock();
    const before = pinnedOrder(env);
    await dragTo(edge, slotCentre(before[3]) + 5, -30);
    const reorder = env.calls.find((c) => c.type === 'dock.reorder')!;
    expect(reorder.params.itemIds).toEqual([before[0], before[2], before[3], before[1], ...before.slice(4)]);
    await tick(30);
    expect(pinnedOrder(env)).toEqual(reorder.params.itemIds);
    // the dragged click must not also activate
    expect(env.calls.some((c) => c.type === 'dock.activate')).toBe(false);
  });

  it('dropping on its own slot sends nothing', async () => {
    const env = await mountDock();
    await dragTo(edge, slotCentre(edge), -30);
    expect(env.calls.some((c) => c.type === 'dock.reorder')).toBe(false);
  });

  it('dragging a pinned app far out of the dock unpins it', async () => {
    const env = await mountDock();
    await dragTo(terminal, slotCentre(terminal), -260);
    expect(env.calls.find((c) => c.type === 'dock.unpin')!.params).toEqual({ itemId: terminal });
    await tick(30);
    expect(pinnedOrder(env)).not.toContain(terminal);
  });

  it('shows the Remove hint only while the drag is beyond the unpin distance, and Escape cancels', async () => {
    const env = await mountDock();
    await dragTo(terminal, slotCentre(terminal), -260, false);
    expect(document.querySelector('.ghost .remove')).not.toBeNull();
    await fireEvent.keyDown(window, { key: 'Escape' });
    await tick(260);
    expect(document.querySelector('.ghost')).toBeNull();
    expect(env.calls.some((c) => c.type === 'dock.unpin')).toBe(false);
  });

  it('dragging a running unpinned app into the pinned area pins it at that index', async () => {
    const env = await mountDock();
    const pinned = pinnedOrder(env);
    await dragTo(appByName('Notepad').id, slotCentre(pinned[1]) + 5, -30);
    const pin = env.calls.find((c) => c.type === 'dock.pin')!;
    expect(pin.params).toEqual({ appId: appByName('Notepad').id, index: 2 });
    await tick(30);
    expect(pinnedOrder(env)[2]).toBe(appByName('Notepad').id);
  });

  it('dropping a running unpinned app among the running apps / folders does nothing', async () => {
    const env = await mountDock();
    await dragTo(appByName('Notepad').id, slotCentre(appByName('Notepad').id) + 90, -30);
    expect(env.calls.some((c) => c.type === 'dock.pin' || c.type === 'dock.reorder' || c.type === 'dock.unpin')).toBe(false);
  });

  it('folders can be dragged but only among folders; minimised tiles and separators are not draggable', async () => {
    const env = await mountDock();
    const tile = document.querySelector<HTMLButtonElement>('button[data-key^="win:"]')!;
    const from = parseFloat(tile.closest<HTMLElement>('.slot')!.style.left);
    await ptr(tile, 'pointerdown', from, -30);
    await ptr(tile, 'pointermove', from - 200, -30);
    await ptr(tile, 'pointerup', from - 200, -30);
    expect(document.querySelector('.ghost')).toBeNull();
    expect(env.calls.some((c) => c.type === 'dock.reorder' || c.type === 'dock.pin' || c.type === 'dock.unpin')).toBe(false);
  });
});
