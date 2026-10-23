import { afterEach, describe, expect, it, vi } from 'vitest';

// vitest resolves `svelte` to its SSR entry (vite.config.ts has no `browser` condition): use the client build so mount() works in jsdom.
vi.mock('svelte', () => import('../../../../../node_modules/svelte/src/index-client.js'));

import { cleanup, fireEvent, render, screen } from '@testing-library/svelte';
import { setBridgeForTests, setInit } from '$lib/ipc';
import type { PopoverRequest } from '$lib/protocol/generated';
import { boot, tick } from '../../../dock/testenv';
import { appByName } from '../../../../mock/fixtures';
import DockMenu from '../DockMenu.svelte';
import WindowList from '../WindowList.svelte';

const edge = appByName('Microsoft Edge').id;
const terminal = appByName('Terminal').id;
const mail = appByName('Mail').id;
const downloads = 'dir:C:\\Users\\you\\Downloads';

afterEach(() => { cleanup(); setBridgeForTests(null); setInit(null); });

const anchor = { x: 600, y: 700, width: 60, height: 76 };
const req = (kind: 'dockMenu' | 'windowList', itemId: string): PopoverRequest => ({ kind, anchor, itemId });

async function open(kind: 'dockMenu' | 'windowList', itemId: string, lang = 'en') {
  const env = await boot('overlay', lang);
  const onclose = vi.fn();
  const order: string[] = [];
  onclose.mockImplementation(() => order.push('close'));
  const call = env.bridge.call.bind(env.bridge);
  env.bridge.call = ((type: any, params: any) => { order.push(type); return call(type, params); }) as typeof env.bridge.call;
  render(kind === 'dockMenu' ? DockMenu : WindowList, { request: req(kind, itemId), onclose });
  await tick(30);
  return { ...env, onclose, order };
}
const row = (name: string | RegExp) => screen.getByRole('menuitem', { name });

describe('DockMenu', () => {
  it('shows header, windows, options, app actions and settings for a running pinned app', async () => {
    await open('dockMenu', edge);
    expect(screen.getByRole('menu', { name: 'Dock menu' })).toBeTruthy();
    expect(screen.getByText('Microsoft Edge')).toBeTruthy();
    expect(screen.getByText('3 windows')).toBeTruthy();
    expect(screen.getByText('BetterWindows - GitHub - Microsoft Edge')).toBeTruthy();
    for (const label of ['Remove from Dock', 'Show in File Explorer', 'New Window', 'Hide', 'Quit', 'Dock Settings…']) expect(row(label)).toBeTruthy();
    expect(screen.queryByRole('menuitem', { name: 'Open' })).toBeNull();
  });

  it('every action closes the overlay first and then calls the host', async () => {
    const t = await open('dockMenu', terminal);
    await fireEvent.click(row('Quit'));
    expect(t.onclose).toHaveBeenCalledTimes(1);
    expect(t.order.indexOf('close')).toBeLessThan(t.order.indexOf('dock.quit'));
    expect(t.calls.find((c) => c.type === 'dock.quit')!.params).toEqual({ itemId: terminal });
  });

  it('maps each entry to its protocol call', async () => {
    const t = await open('dockMenu', edge);
    await fireEvent.click(row('Hide'));
    await fireEvent.click(row('New Window'));
    await fireEvent.click(row('Remove from Dock'));
    await fireEvent.click(row('Show in File Explorer'));
    await fireEvent.click(row('Dock Settings…'));
    const types = t.calls.map((c) => c.type).filter((x) => x.startsWith('dock.') || x === 'settings.openWindow');
    expect(types).toEqual(['dock.hide', 'dock.newWindow', 'dock.unpin', 'dock.revealInExplorer', 'settings.openWindow']);
    expect(t.calls.find((c) => c.type === 'settings.openWindow')!.params).toEqual({ page: 'dock' });
    expect(t.onclose).toHaveBeenCalledTimes(5);
  });

  it('clicking a window activates it; its close button closes just that window and keeps the menu open', async () => {
    const t = await open('dockMenu', edge);
    const rowBtn = screen.getByText('Inbox (3) - Mail').closest('button')!;
    await fireEvent.click(rowBtn);
    expect(t.calls.some((c) => c.type === 'dock.activateWindow')).toBe(true);
    expect(t.onclose).toHaveBeenCalledTimes(1);
    const closeBtns = screen.getAllByRole('button', { name: 'Close window' });
    await fireEvent.click(closeBtns[0]);
    expect(t.calls.filter((c) => c.type === 'dock.closeWindow')).toHaveLength(1);
    expect(t.onclose).toHaveBeenCalledTimes(1);
  });

  it('not running app: Open first, Hide and Quit disabled and inert', async () => {
    const t = await open('dockMenu', mail);
    expect(screen.getByText('Not running')).toBeTruthy();
    await fireEvent.click(row('Quit'));
    await fireEvent.click(row('Hide'));
    expect(t.calls.some((c) => c.type === 'dock.quit' || c.type === 'dock.hide')).toBe(false);
    expect(t.onclose).not.toHaveBeenCalled();
    await fireEvent.click(row('Open'));
    expect(t.calls.some((c) => c.type === 'dock.activate' && c.params.itemId === mail)).toBe(true);
  });

  it('folders get Open / Remove / Show in File Explorer only', async () => {
    await open('dockMenu', downloads);
    expect(row('Open')).toBeTruthy();
    expect(screen.queryByRole('menuitem', { name: 'Quit' })).toBeNull();
    expect(screen.queryByRole('menuitem', { name: 'New Window' })).toBeNull();
    expect(screen.getByText('Folder')).toBeTruthy();
  });

  it('keyboard: arrows and Home/End move between enabled rows, Delete closes the focused window, type-ahead jumps', async () => {
    const t = await open('dockMenu', edge);
    const menu = screen.getByRole('menu').querySelector<HTMLElement>('.menu')!;
    await fireEvent.keyDown(menu, { key: 'ArrowDown' });
    const first = document.activeElement as HTMLElement;
    expect(first.dataset.window).toBeTruthy();
    await fireEvent.keyDown(first, { key: 'ArrowDown' });
    expect((document.activeElement as HTMLElement).dataset.window).not.toBe(first.dataset.window);
    await fireEvent.keyDown(document.activeElement!, { key: 'End' });
    expect(document.activeElement!.textContent).toContain('Dock Settings');
    await fireEvent.keyDown(document.activeElement!, { key: 'ArrowDown' }); // wraps
    expect((document.activeElement as HTMLElement).dataset.window).toBe(first.dataset.window);
    await fireEvent.keyDown(document.activeElement!, { key: 'Delete' });
    expect(t.calls.some((c) => c.type === 'dock.closeWindow' && c.params.handle === first.dataset.window)).toBe(true);
    await fireEvent.keyDown(menu, { key: 'q' });
    expect(document.activeElement!.textContent).toContain('Quit');
  });

  it('disabled entries are skipped by keyboard navigation', async () => {
    await open('dockMenu', mail);
    const menu = screen.getByRole('menu').querySelector<HTMLElement>('.menu')!;
    const seen: string[] = [];
    for (let i = 0; i < 6; i++) { await fireEvent.keyDown(menu, { key: 'ArrowDown' }); seen.push(document.activeElement!.textContent!.trim()); }
    expect(seen.join('|')).not.toContain('Quit');
    expect(seen.join('|')).not.toContain('Hide');
  });

  it('elevated windows show a shield with the explanation and cannot be activated or closed', async () => {
    const t = await open('dockMenu', terminal);
    t.host.dockWorld.windows.find((w) => w.appId === terminal)!.elevated = true;
    t.host.emit('dock.state', t.host.dockWorld.state());
    await tick(20);
    const hint = 'Running as administrator: cannot be controlled';
    expect(screen.getAllByLabelText(hint).length).toBeGreaterThan(0);
    const winRow = screen.getByText('PowerShell').closest('button')!;
    expect(winRow.getAttribute('aria-disabled')).toBe('true');
    await fireEvent.click(winRow);
    expect(t.calls.some((c) => c.type === 'dock.activateWindow')).toBe(false);
    expect(t.onclose).not.toHaveBeenCalled();
    expect(screen.queryByRole('button', { name: 'Close window' })).toBeNull();
  });

  it('closes itself when the item disappears (app quit elsewhere)', async () => {
    const t = await open('dockMenu', 'exe:c:\\temp\\gone.exe');
    expect(t.onclose).toHaveBeenCalled();
  });

  it('is fully localised in Russian', async () => {
    await open('dockMenu', edge, 'ru');
    expect(screen.getByRole('menu', { name: 'Меню дока' })).toBeTruthy();
    for (const label of ['Убрать из дока', 'Показать в Проводнике', 'Новое окно', 'Скрыть', 'Завершить', 'Настройки дока…']) expect(row(label)).toBeTruthy();
    expect(screen.getByText('3 окна')).toBeTruthy();
  });
});

describe('WindowList', () => {
  it('lists every window as a card with thumbnail, marks the active one and shows the app header', async () => {
    await open('windowList', edge);
    expect(screen.getByRole('dialog', { name: 'Windows of Microsoft Edge' })).toBeTruthy();
    expect(screen.getByText('3 windows')).toBeTruthy();
    const cards = document.querySelectorAll('[data-card]');
    expect(cards).toHaveLength(3);
    expect(document.querySelectorAll('.thumb img')).toHaveLength(3);
    expect(document.querySelector('.card.active [data-card]')).not.toBeNull();
  });

  it('click activates the window and closes the overlay first', async () => {
    const t = await open('windowList', edge);
    const card = document.querySelectorAll<HTMLElement>('[data-card]')[1];
    await fireEvent.click(card);
    expect(t.calls.find((c) => c.type === 'dock.activateWindow')!.params).toEqual({ handle: card.dataset.card });
    expect(t.order.indexOf('close')).toBeLessThan(t.order.indexOf('dock.activateWindow'));
  });

  it('middle click and the close button close one window without dismissing the popover', async () => {
    const t = await open('windowList', edge);
    const cards = document.querySelectorAll<HTMLElement>('[data-card]');
    await fireEvent(cards[2], new MouseEvent('auxclick', { button: 1, bubbles: true, cancelable: true }));
    expect(t.calls.filter((c) => c.type === 'dock.closeWindow')).toEqual([{ type: 'dock.closeWindow', params: { handle: cards[2].dataset.card } }]);
    await tick(20);
    expect(document.querySelectorAll('[data-card]')).toHaveLength(2);
    await fireEvent.click(screen.getAllByRole('button', { name: 'Close window' })[0]);
    expect(t.calls.filter((c) => c.type === 'dock.closeWindow')).toHaveLength(2);
    expect(t.onclose).not.toHaveBeenCalled();
  });

  it('closes itself once the last window is gone', async () => {
    const t = await open('windowList', appByName('Terminal').id);
    await t.bridge.call('dock.quit', { itemId: terminal });
    await tick(20);
    expect(t.onclose).toHaveBeenCalled();
  });

  it('falls back to a list with large icons when there are no thumbnails, and shows minimised state', async () => {
    const t = await boot('overlay');
    t.host.dockWorld.windows.forEach((w) => { delete w.thumbUrl; });
    t.host.dockWorld.windows.find((w) => w.appId === edge && !w.active)!.minimized = true;
    t.host.emit('dock.state', t.host.dockWorld.state());
    await tick(5); // let the state event land before the popover subscribes (the bridge replays the cached value to late subscribers)
    const onclose = vi.fn();
    render(WindowList, { request: req('windowList', edge), onclose });
    await tick(30);
    expect(document.querySelectorAll('.thumb')).toHaveLength(0);
    expect(document.querySelectorAll('.card.list')).toHaveLength(3);
    expect(screen.getByText('Minimized')).toBeTruthy();
  });

  it('elevated windows are marked, explained and inert', async () => {
    const t = await boot('overlay');
    t.host.dockWorld.windows.find((w) => w.appId === edge)!.elevated = true;
    t.host.emit('dock.state', t.host.dockWorld.state());
    await tick(5); // let the state event land before the popover subscribes (the bridge replays the cached value to late subscribers)
    const onclose = vi.fn();
    render(WindowList, { request: req('windowList', edge), onclose });
    await tick(30);
    const disabled = document.querySelector<HTMLElement>('[data-card][aria-disabled="true"]')!;
    expect(disabled).not.toBeNull();
    expect(disabled.getAttribute('aria-label')).toContain('Running as administrator');
    await fireEvent.click(disabled);
    expect(t.calls.some((c) => c.type === 'dock.activateWindow')).toBe(false);
    expect(onclose).not.toHaveBeenCalled();
  });

  it('arrow keys navigate the grid, Delete closes the focused window', async () => {
    const t = await open('windowList', edge);
    const root = document.querySelector<HTMLElement>('.wl')!;
    const cards = [...document.querySelectorAll<HTMLElement>('[data-card]')];
    cards[0].focus();
    await fireEvent.keyDown(root, { key: 'ArrowRight' });
    expect(document.activeElement).toBe(cards[1]);
    await fireEvent.keyDown(root, { key: 'ArrowDown' }); // two columns: next row
    expect(document.activeElement).toBe(cards[2]);
    await fireEvent.keyDown(root, { key: 'Home' });
    expect(document.activeElement).toBe(cards[0]);
    await fireEvent.keyDown(root, { key: 'Delete' });
    expect(t.calls.some((c) => c.type === 'dock.closeWindow' && c.params.handle === cards[0].dataset.card)).toBe(true);
  });

  it('is localised in Russian', async () => {
    await open('windowList', edge, 'ru');
    expect(screen.getByRole('dialog', { name: 'Окна «Microsoft Edge»' })).toBeTruthy();
    expect(screen.getByText('3 окна')).toBeTruthy();
  });
});
