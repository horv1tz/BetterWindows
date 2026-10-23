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

// ---- hover, tooltip, sizing protocol, auto-hide, long press
describe('dock hover and window sizing', () => {
  const slotCentre = (key: string) => {
    const slot = btn(key).closest<HTMLElement>('.slot')!;
    return parseFloat(slot.style.left) + parseFloat(slot.style.width) / 2;
  };
  const move = (x: number, y: number) => fireEvent(document.querySelector('.root')!, new MouseEvent('pointermove', { bubbles: true, clientX: x, clientY: y }));
  const sizes = (env: Awaited<ReturnType<typeof mountDock>>) => env.events.filter((e) => e.type === 'surface.resize').map((e) => e.payload);

  it('hovering asks for headroom (bar + magnification + tooltip room) and leaving shrinks back to the bar; barThickness never changes', async () => {
    const env = await mountDock();
    await tick(30);
    const idle = sizes(env).at(-1);
    await move(slotCentre(terminal), -30);
    await tick(40);
    const hover = sizes(env).at(-1);
    expect(hover.height).toBeGreaterThan(idle.height + 60);
    expect(hover.width).toBeGreaterThan(idle.width);
    expect(hover.barThickness).toBe(idle.barThickness);
    await fireEvent.mouseLeave(document.documentElement);
    await tick(400);
    expect(sizes(env).at(-1)).toEqual(idle);
  });

  it('shows the tooltip after ~120 ms over an icon and hides it when the pointer leaves', async () => {
    await mountDock();
    await move(slotCentre(terminal), -30);
    await tick(40);
    expect(document.querySelector('.tip')).toBeNull();
    await tick(140);
    expect(document.querySelector('.tip')!.textContent).toBe('Terminal');
    await move(slotCentre(edge), -30);
    await tick(120); // the magnified layout follows the pointer with a short spring
    expect(document.querySelector('.tip')!.textContent).toBe('Microsoft Edge'); // moving between icons switches without the 120 ms delay
    await fireEvent.mouseLeave(document.documentElement);
    await tick(20);
    expect(document.querySelector('.tip')).toBeNull();
  });

  it('magnifies icons by transform (no layout properties) while hovering and restores them afterwards', async () => {
    await mountDock();
    await move(slotCentre(terminal), -30);
    await tick(400);
    const mag = btn(terminal).closest('.slot')!.querySelector<HTMLElement>('.mag')!;
    expect(mag.style.transform).toMatch(/^scale\(1\.[4-6]/);
    await fireEvent.mouseLeave(document.documentElement);
    await tick(1200);
    expect(mag.style.transform).toBe('');
  });

  it('overflow mode marks magnified icons as hit areas while hovering, tooltips included', async () => {
    await mountDock();
    expect(document.querySelectorAll('.hit')).toHaveLength(0);
    await move(slotCentre(terminal), -30);
    await tick(220);
    expect(document.querySelectorAll('.hit').length).toBeGreaterThan(5);
    expect(document.querySelector('.tip.tip-hit')).not.toBeNull();
  });

  it('without overflow support everything stays inside glass: no hit areas, tooltip is a glass bubble, the pill grows', async () => {
    history.replaceState({}, '', '/?glassOverflow=0');
    try {
      await mountDock();
      await move(slotCentre(terminal), -30);
      await tick(400);
      expect(document.querySelectorAll('.hit')).toHaveLength(0);
      const tip = document.querySelector('.tip')!;
      expect(tip.classList.contains('glass')).toBe(true);
      expect(tip.classList.contains('tip-hit')).toBe(false);
      const pill = document.querySelector<HTMLElement>('.pill')!;
      expect(parseFloat(pill.style.height)).toBeGreaterThan(80 + 20);
    } finally { history.replaceState({}, '', '/'); }
  });

  it('long press (touch) opens the dock menu and swallows the click that follows', async () => {
    const env = await mountDock();
    const down = new MouseEvent('pointerdown', { bubbles: true, button: 0, clientX: slotCentre(terminal), clientY: -30 });
    Object.defineProperty(down, 'pointerType', { value: 'touch' });
    await fireEvent(btn(terminal), down);
    await tick(650);
    expect(env.calls.filter((c) => c.type === 'overlay.open').map((c) => c.params.popover.kind)).toEqual(['dockMenu']);
    await fireEvent.click(btn(terminal));
    expect(env.calls.some((c) => c.type === 'dock.activate')).toBe(false);
  });

  it('touch input disables magnification', async () => {
    await mountDock();
    const mv = new MouseEvent('pointermove', { bubbles: true, clientX: slotCentre(terminal), clientY: -30 });
    Object.defineProperty(mv, 'pointerType', { value: 'touch' });
    await fireEvent(document.querySelector('.root')!, mv);
    await tick(300);
    expect(btn(terminal).closest('.slot')!.querySelector<HTMLElement>('.mag')!.style.transform).toBe('');
  });
});

describe('dock auto-hide', () => {
  it('starts slid out, collapses the window after the slide, and slides in on dock.reveal', async () => {
    const env = await boot('dock');
    await env.bridge.call('settings.patch', { patch: { dock: { autoHide: true } } });
    render(Surface);
    await tick(20);
    const stage = document.querySelector('.stage')!;
    expect(stage.classList.contains('slid')).toBe(true);
    await tick(500);
    const sizes = () => env.events.filter((e) => e.type === 'surface.resize').map((e) => e.payload);
    expect(sizes().at(-1)).toMatchObject({ width: 4, height: 4 });
    expect(stage.classList.contains('gone')).toBe(true);

    env.host.emit('dock.reveal', { revealed: true });
    await tick(150);
    expect(stage.classList.contains('slid')).toBe(false);
    expect(sizes().at(-1).height).toBeGreaterThan(60);
    expect(sizes().at(-1).barThickness).toBeGreaterThan(60);

    env.host.emit('dock.reveal', { revealed: false });
    await tick(550);
    expect(stage.classList.contains('slid')).toBe(true);
    expect(sizes().at(-1)).toMatchObject({ width: 4, height: 4 });
  });

  it('turning auto-hide off reveals the dock for good', async () => {
    const env = await boot('dock');
    await env.bridge.call('settings.patch', { patch: { dock: { autoHide: true } } });
    render(Surface);
    await tick(500);
    await env.bridge.call('settings.patch', { patch: { dock: { autoHide: false } } });
    await tick(150);
    const stage = document.querySelector('.stage')!;
    expect(stage.classList.contains('slid')).toBe(false);
    expect(stage.classList.contains('gone')).toBe(false);
  });

  it('ignores dock.reveal when auto-hide is off', async () => {
    const env = await boot('dock');
    render(Surface);
    await tick(30);
    env.host.emit('dock.reveal', { revealed: false });
    await tick(100);
    expect(document.querySelector('.stage')!.classList.contains('slid')).toBe(false);
  });
});

describe('dock bounce and launching', () => {
  it('a launch bounce makes room for the hop and the launching tile is dimmed and bounces until the window appears', async () => {
    const env = await mountDock();
    await fireEvent.click(btn(mail));
    await tick(20);
    expect(btn(mail).classList.contains('launching')).toBe(true);
    expect(btn(mail).getAttribute('aria-label')).toBe('Mail, Launching…');
    await tick(1000);
    expect(btn(mail).classList.contains('launching')).toBe(false);
    expect(btn(mail).getAttribute('aria-label')).toBe('Mail, running');
    expect(env.calls.some((c) => c.type === 'dock.activate' && c.params.itemId === mail)).toBe(true);
  });

  it('bounceOnLaunch=false keeps the dock still', async () => {
    const env = await mountDock();
    await env.bridge.call('settings.patch', { patch: { dock: { bounceOnLaunch: false } } });
    await tick(10);
    const before = env.events.filter((e) => e.type === 'surface.resize').length;
    await fireEvent.click(btn(mail));
    await tick(100);
    // no hop room requested: the window size stays the idle size
    const after = env.events.filter((e) => e.type === 'surface.resize');
    expect(after.length).toBe(before);
  });

  it('a flashing app (attention) is reflected in its label', async () => {
    const env = await mountDock();
    env.host.dockWorld.flash(edge);
    await tick(20);
    expect(btn(edge).getAttribute('aria-label')).toContain('needs attention');
    expect(btn(edge).classList.contains('attn')).toBe(true);
  });
});

describe('dock edge cases', () => {
  it('renders an empty dock without crashing and still reports a sane size', async () => {
    const env = await boot('dock');
    for (const w of [...env.host.dockWorld.windows]) env.host.dockWorld.closeWindow(w.handle);
    await env.bridge.call('settings.patch', { patch: { dock: { pins: [] } } });
    render(Surface);
    await tick(60);
    expect(document.querySelectorAll('button[data-key]')).toHaveLength(0);
    const last = env.events.filter((e) => e.type === 'surface.resize').at(-1)!.payload;
    expect(last.width).toBeGreaterThan(40);
    expect(last.height).toBeGreaterThan(60);
  });

  it('shrinks the icons when the dock would not fit the monitor', async () => {
    const env = await boot('dock');
    for (let i = 0; i < 40; i++) await env.bridge.call('dock.pinPath', { path: `C:\\Apps\\App${i}.exe` });
    render(Surface);
    await tick(60);
    const slot = document.querySelector<HTMLElement>('.slot.item')!;
    expect(parseFloat(slot.style.width)).toBeLessThan(56);
    const last = env.events.filter((e) => e.type === 'surface.resize').at(-1)!.payload;
    expect(last.width).toBeLessThanOrEqual(1920);
  });

  it('survives garbage settings values (icon size, magnified size, position)', async () => {
    const env = await boot('dock');
    await env.bridge.call('settings.patch', { patch: { dock: { iconSize: 9999, magnifiedSize: -5, position: 'sideways' } } });
    render(Surface);
    await tick(40);
    const slot = document.querySelector<HTMLElement>('.slot.item')!;
    expect(parseFloat(slot.style.width)).toBeLessThanOrEqual(128);
    expect(screen.getByRole('toolbar').getAttribute('aria-orientation')).toBe('horizontal');
  });

  it('honours showIndicators=false and the icon size setting', async () => {
    const env = await boot('dock');
    await env.bridge.call('settings.patch', { patch: { dock: { showIndicators: false, iconSize: 40 } } });
    render(Surface);
    await tick(40);
    expect(document.querySelectorAll('.dot')).toHaveLength(0);
    expect(parseFloat(document.querySelector<HTMLElement>('.slot.item')!.style.width)).toBe(40);
  });

  it('reacts live to settings changes (position, icon size) while mounted', async () => {
    const env = await boot('dock');
    render(Surface);
    await tick(40);
    await env.bridge.call('settings.patch', { patch: { dock: { iconSize: 72, position: 'right' } } });
    await tick(60);
    expect(screen.getByRole('toolbar').getAttribute('aria-orientation')).toBe('vertical');
    expect(parseFloat(document.querySelector<HTMLElement>('.slot.item')!.style.width)).toBeLessThanOrEqual(72);
    expect(document.querySelector('.root')!.classList.contains('right')).toBe(true);
  });
});
