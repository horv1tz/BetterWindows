// The whole desktop surface against the mock host: what the user sees and which protocol calls their actions produce.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import Surface from './Surface.svelte';
import { bootDesktop, spyCalls, teardownDesktop, tick } from './testSupport';
import { cleanup, fire, flushSync, ke, me, pe, render, screen, waitFor } from './domTest';

vi.mock('svelte', async () => await import('../../../node_modules/svelte/src/index-client.js'));

const setViewport = (w: number, h: number) => { Object.assign(window, { innerWidth: w, innerHeight: h }); };
beforeEach(() => { window.history.replaceState(null, '', '/?w=1440&h=900'); setViewport(1440, 900); vi.spyOn(console, 'info').mockImplementation(() => {}); });
afterEach(() => { cleanup(); teardownDesktop(); vi.useRealTimers(); vi.restoreAllMocks(); window.history.replaceState(null, '', '/'); setViewport(1024, 768); });

async function mountSurface(query = '') {
  if (query) window.history.replaceState(null, '', `/${query}`);
  const bridge = await bootDesktop();
  const calls = spyCalls(bridge);
  render(Surface, {});
  await tick(30);
  flushSync();
  return { bridge, calls };
}
const layerEl = () => screen.getByRole('listbox', { name: 'Desktop icons' });
const card = (type: string) => document.querySelector<HTMLElement>(`.card.${type}`)!;
const iconEl = (id: string) => [...document.querySelectorAll<HTMLElement>('[data-icon-id]')].find((e) => e.dataset.iconId === id)!;

describe('desktop Surface', () => {
  it('renders the wallpaper, the icon grid and the seeded widgets of the monitor', async () => {
    await mountSurface();
    expect(document.querySelector('.wallpaper .layer')).not.toBeNull();
    expect(screen.getByRole('listbox', { name: 'Desktop icons' })).toBeTruthy();
    expect(screen.getAllByRole('option').length).toBeGreaterThanOrEqual(8);
    expect(screen.getByRole('option', { name: 'This PC' })).toBeTruthy();
    const types = [...document.querySelectorAll<HTMLElement>('.card')].map((c) => c.dataset.type).sort();
    expect(types).toEqual(['calendar', 'clock', 'notes', 'reminders', 'weather']);
    expect(document.querySelector('.desktop')?.getAttribute('data-monitor')).toBe('1');
  });

  it('renders nothing while the layer is disabled', async () => {
    const { bridge } = await mountSurface();
    await bridge.call('settings.patch', { patch: { desktop: { enabled: false } } });
    await tick(30);
    flushSync();
    expect(document.querySelector('.wallpaper')).toBeNull();
    expect(document.querySelectorAll('.card')).toHaveLength(0);
  });

  it('a second monitor shows its own widgets and no icons', async () => {
    await bootDesktop('2');
    render(Surface, {});
    await tick(30);
    flushSync();
    expect(document.querySelector('.desktop')?.getAttribute('data-monitor')).toBe('2');
    expect(screen.queryAllByRole('option')).toHaveLength(0);
    expect([...document.querySelectorAll<HTMLElement>('.card')].map((c) => c.dataset.type)).toEqual(expect.arrayContaining(['clock', 'media']));
  });

  it('opens the desktop menu popover through the overlay, anchored at the pointer (1x1), on right click of the empty desktop', async () => {
    const { calls } = await mountSurface();
    await fire(layerEl(), me('contextmenu', { clientX: 640, clientY: 380 }));
    const open = calls.find((c) => c.type === 'overlay.open');
    expect(open?.params).toEqual({ mode: 'popover', popover: { kind: 'desktopMenu', anchor: { x: 640, y: 380, width: 1, height: 1 } }, monitorId: '1' });
    expect(document.querySelector('[role=menu]')).toBeNull(); // no in-surface menu for the empty desktop
  });

  it('right click on an icon shows a compact menu with Open only (no delete); choosing it opens the icon', async () => {
    const { calls } = await mountSurface();
    await fire(iconEl('sc:documents'), me('contextmenu', { clientX: 60, clientY: 340 }));
    const menu = screen.getByRole('menu');
    expect(screen.getAllByRole('menuitem').map((i) => i.textContent?.trim())).toEqual(['Open']);
    await fire(screen.getByRole('menuitem', { name: 'Open' }), me('click'));
    expect(calls.find((c) => c.type === 'desktop.open')?.params).toEqual({ id: 'sc:documents' });
    expect(document.contains(menu)).toBe(false);
  });

  it('double click and Enter send desktop.open', async () => {
    const { calls } = await mountSurface();
    await fire(iconEl('special:thisPc'), me('dblclick'));
    expect(calls.filter((c) => c.type === 'desktop.open').map((c) => c.params)).toEqual([{ id: 'special:thisPc' }]);
    iconEl('sc:downloads').focus();
    await fire(layerEl(), ke('keydown', 'Enter'));
    expect(calls.filter((c) => c.type === 'desktop.open').at(-1)?.params).toEqual({ id: 'sc:downloads' });
  });

  it('dragging an icon to another cell sends desktop.moveIcon and the icon stays there', async () => {
    const { calls } = await mountSurface();
    const layer = layerEl();
    await fire(iconEl('sc:telegram'), pe('pointerdown', { clientX: 60, clientY: 760 }));
    await fire(layer, pe('pointermove', { clientX: 60 + 3 * 100, clientY: 760 }));
    await fire(layer, pe('pointerup', { clientX: 60 + 3 * 100, clientY: 760 }));
    const move = calls.find((c) => c.type === 'desktop.moveIcon')?.params as { id: string; x: number; y: number };
    expect(move).toMatchObject({ id: 'sc:telegram', x: 3, y: 6 });
    await tick(30);
    flushSync();
    expect(iconEl('sc:telegram').parentElement!.style.left).toMatch(/px$/);
  });

  it('the widget menu resizes (optimistically), toggles clock options and removes', async () => {
    const { calls } = await mountSurface();
    await fire(card('weather'), me('contextmenu', { clientX: 1100, clientY: 100 }));
    expect(screen.getByRole('menuitemradio', { name: 'Medium' }).getAttribute('aria-checked')).toBe('true');
    await fire(screen.getByRole('menuitemradio', { name: 'Large' }), me('click'));
    expect(calls.find((c) => c.type === 'desktop.updateWidget')?.params).toEqual({ id: 'w-weather', size: 'large' });
    expect(card('weather').dataset.size).toBe('large'); // optimistic, before the host answers
    await tick(30);
    flushSync();
    expect(card('weather').dataset.size).toBe('large'); // confirmed by desktop.state

    await fire(card('clock'), me('contextmenu', { clientX: 900, clientY: 100 }));
    await fire(screen.getByRole('menuitemradio', { name: 'Digital' }), me('click'));
    expect(calls.filter((c) => c.type === 'desktop.updateWidget').at(-1)?.params).toEqual({ id: 'w-clock', config: { style: 'digital', seconds: true } });

    await fire(card('calendar'), me('contextmenu', { clientX: 900, clientY: 300 }));
    await fire(screen.getByRole('menuitem', { name: 'Remove Widget' }), me('click'));
    expect(calls.find((c) => c.type === 'desktop.removeWidget')?.params).toEqual({ id: 'w-calendar' });
    await tick(30);
    flushSync();
    expect(card('calendar')).toBeNull();
  });

  it('dragging a widget moves it on the 8 DIP grid and keeps it inside the monitor', async () => {
    const { calls } = await mountSurface();
    const w = card('notes');
    // notes widget starts at (824, 440); drag by (+13, -250): snapped to the 8 grid, clamped below the menu bar (y >= 28)
    await fire(w, pe('pointerdown', { clientX: 900, clientY: 500 }));
    await fire(w, pe('pointermove', { clientX: 913, clientY: 250 }));
    await fire(w, pe('pointerup', { clientX: 913, clientY: 250 }));
    expect(calls.find((c) => c.type === 'desktop.moveWidget')?.params).toEqual({ id: 'w-notes', x: 840, y: 192 });
    await fire(w, pe('pointerdown', { clientX: 900, clientY: 500 }));
    await fire(w, pe('pointermove', { clientX: -5000, clientY: -5000 }));
    await fire(w, pe('pointerup', { clientX: -5000, clientY: -5000 }));
    expect(calls.filter((c) => c.type === 'desktop.moveWidget').at(-1)?.params).toEqual({ id: 'w-notes', x: 0, y: 28 });
  });

  it('a click on a widget button does not start a drag', async () => {
    const { calls } = await mountSurface();
    const input = screen.getByLabelText('New reminder');
    await fire(input, pe('pointerdown', { clientX: 1100, clientY: 400 }));
    await fire(card('reminders'), pe('pointermove', { clientX: 1300, clientY: 500 }));
    await fire(card('reminders'), pe('pointerup', { clientX: 1300, clientY: 500 }));
    expect(calls.some((c) => c.type === 'desktop.moveWidget')).toBe(false);
  });

  it('arrow keys on a focused widget move it by 8 (Shift: 32) and commit once', async () => {
    const { calls } = await mountSurface();
    vi.useFakeTimers();
    const w = card('calendar'); // at (824, 244)
    w.focus();
    await fire(w, ke('keydown', 'ArrowLeft'));
    await fire(w, ke('keydown', 'ArrowDown', { shiftKey: true }));
    expect(calls.some((c) => c.type === 'desktop.moveWidget')).toBe(false);
    await vi.advanceTimersByTimeAsync(400);
    const moves = calls.filter((c) => c.type === 'desktop.moveWidget');
    expect(moves).toHaveLength(1);
    expect(moves[0].params).toEqual({ id: 'w-calendar', x: 816, y: 280 });
  });

  it('keyboard users can open the widget menu with the Menu key', async () => {
    await mountSurface();
    await fire(card('notes'), ke('keydown', 'ContextMenu'));
    expect(screen.getByRole('menu', { name: 'Widget options' })).toBeTruthy();
    await fire(screen.getByRole('menu'), ke('keydown', 'Escape'));
    expect(document.querySelector('[role=menu]')).toBeNull();
  });

  it('is localised: widget names and menus follow the language', async () => {
    const bridge = await bootDesktop('1', 'ru');
    render(Surface, {});
    await tick(30);
    flushSync();
    expect(card('weather').getAttribute('aria-label')).toBe('Погода');
    await fire(card('weather'), me('contextmenu', { clientX: 1100, clientY: 100 }));
    expect(screen.getByRole('menuitem', { name: 'Удалить виджет' })).toBeTruthy();
    void bridge;
  });

  it('survives a host that rejects a widget move: the widget snaps back', async () => {
    const { bridge } = await mountSurface();
    const orig = bridge.call.bind(bridge) as (t: string, ...a: unknown[]) => Promise<unknown>;
    (bridge as unknown as { call: typeof orig }).call = (t, ...a) => (t === 'desktop.moveWidget' ? Promise.reject(new Error('refused')) : orig(t, ...a));
    const w = card('notes');
    const before = w.parentElement!.style.transform;
    await fire(w, pe('pointerdown', { clientX: 900, clientY: 500 }));
    await fire(w, pe('pointermove', { clientX: 700, clientY: 500 }));
    await fire(w, pe('pointerup', { clientX: 700, clientY: 500 }));
    await waitFor(() => expect(card('notes').parentElement!.style.transform).toBe(before));
  });
});
