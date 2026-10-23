import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fire, ke, me, pe, render, screen } from './domTest';
import type { DesktopIcon } from '$lib/protocol/generated';
import IconLayer from './IconLayer.svelte';

vi.mock('svelte', async () => await import('../../../node_modules/svelte/src/index-client.js'));
import { gridMetrics } from './layout';

const metrics = gridMetrics(64, { width: 1000, height: 700 }, { top: 0, right: 0, bottom: 0, left: 0 }); // cell 100x110, origin 8,8
const icon = (id: string, x: number, y: number, name = id): DesktopIcon => ({ id, name, iconUrl: '', x, y, target: id, kind: 'shortcut' });
const icons = [icon('a', 0, 0, 'Alpha'), icon('b', 0, 1, 'Beta'), icon('c', 1, 0, 'Gamma'), icon('d', 2, 2, 'Delta')];

function setup(extra: Partial<{ onmove: (m: unknown) => Promise<void> }> = {}) {
  const onopen = vi.fn();
  const onmove = vi.fn(extra.onmove ?? (async () => {}));
  const oniconcontext = vi.fn();
  const onbackgroundcontext = vi.fn();
  const view = render(IconLayer, { icons, metrics, onopen, onmove, oniconcontext, onbackgroundcontext });
  const opt = (name: string) => screen.getByRole('option', { name });
  const layer = screen.getByRole('listbox');
  const selected = () => screen.queryAllByRole('option').filter((o) => o.getAttribute('aria-selected') === 'true').map((o) => o.getAttribute('aria-label'));
  return { ...view, onopen, onmove, oniconcontext, onbackgroundcontext, opt, layer, selected };
}

type PInit = MouseEventInit;
const down = (el: Element, o: PInit = {}) => fire(el, pe('pointerdown', o));
const move = (el: Element, o: PInit = {}) => fire(el, pe('pointermove', o));
const up = (el: Element, o: PInit = {}) => fire(el, pe('pointerup', o));
const key = (el: Element, k: string, o: KeyboardEventInit = {}) => fire(el, ke('keydown', k, o));
const ctxMenu = (el: Element, o: MouseEventInit) => fire(el, me('contextmenu', o));
const dbl = (el: Element) => fire(el, me('dblclick'));

afterEach(() => { cleanup(); vi.useRealTimers(); });

describe('IconLayer', () => {
  it('renders every icon as a labelled option in a multi-select listbox', () => {
    const t = setup();
    expect(t.layer.getAttribute('aria-multiselectable')).toBe('true');
    expect(screen.getAllByRole('option')).toHaveLength(4);
    expect(t.opt('Alpha').getAttribute('aria-selected')).toBe('false');
  });

  it('click selects one icon, Ctrl+click toggles, Shift+click selects the range in reading order', async () => {
    const t = setup();
    await down(t.opt('Alpha')); await up(t.opt('Alpha'));
    expect(t.selected()).toEqual(['Alpha']);
    await down(t.opt('Gamma'), { ctrlKey: true }); await up(t.opt('Gamma'));
    expect(t.selected().sort()).toEqual(['Alpha', 'Gamma']);
    await down(t.opt('Gamma'), { ctrlKey: true }); await up(t.opt('Gamma'));
    expect(t.selected()).toEqual(['Alpha']);
    // Ctrl+click moved the anchor to Gamma; a plain click on Alpha sets it again, Shift+click then extends to Delta (a, b, c, d in column order)
    await down(t.opt('Alpha')); await up(t.opt('Alpha'));
    await down(t.opt('Delta'), { shiftKey: true }); await up(t.opt('Delta'));
    expect(t.selected().sort()).toEqual(['Alpha', 'Beta', 'Delta', 'Gamma']);
    await down(t.opt('Beta')); await up(t.opt('Beta'));
    expect(t.selected()).toEqual(['Beta']);
  });

  it('a plain click on a member of a multi-selection keeps the group until the button is released without dragging', async () => {
    const t = setup();
    await down(t.opt('Alpha')); await up(t.opt('Alpha'));
    await down(t.opt('Beta'), { ctrlKey: true }); await up(t.opt('Beta'));
    expect(t.selected().sort()).toEqual(['Alpha', 'Beta']);
    await down(t.opt('Alpha'));
    expect(t.selected().sort()).toEqual(['Alpha', 'Beta']);
    await up(t.opt('Alpha'));
    expect(t.selected()).toEqual(['Alpha']);
  });

  it('a click on the empty desktop clears the selection', async () => {
    const t = setup();
    await down(t.opt('Alpha')); await up(t.opt('Alpha'));
    await down(t.layer, { clientX: 900, clientY: 600 }); await up(t.layer, { clientX: 900, clientY: 600 });
    expect(t.selected()).toEqual([]);
  });

  it('rubber band selects the icons it touches', async () => {
    const t = setup();
    await down(t.layer, { clientX: 2, clientY: 2 });
    await move(t.layer, { clientX: 150, clientY: 150 }); // covers cells (0,0), (1,0), (0,1), (1,1) partly
    expect(t.selected().sort()).toEqual(['Alpha', 'Beta', 'Gamma']);
    await up(t.layer, { clientX: 150, clientY: 150 });
    expect(t.selected().sort()).toEqual(['Alpha', 'Beta', 'Gamma']);
    expect(document.querySelector('.marquee')).toBeNull();
  });

  it('keyboard: arrows move the selection, Enter opens, Home/End jump, Ctrl+A selects all, Escape clears', async () => {
    const t = setup();
    t.opt('Alpha').focus();
    await key(t.layer, 'ArrowDown');
    expect(t.selected()).toEqual(['Beta']);
    await key(t.layer, 'ArrowRight');
    expect(t.selected()).toEqual(['Gamma']); // nearest icon to the right of (0,1) is (1,0)
    await key(t.layer, 'Enter');
    expect(t.onopen).toHaveBeenCalledWith(['c']);
    await key(t.layer, 'End');
    expect(t.selected()).toEqual(['Delta']);
    await key(t.layer, 'Home');
    expect(t.selected()).toEqual(['Alpha']);
    await key(t.layer, 'a', { ctrlKey: true });
    expect(t.selected()).toHaveLength(4);
    await key(t.layer, 'Escape');
    expect(t.selected()).toEqual([]);
  });

  it('F2 and Delete do nothing (rename/delete are not supported)', async () => {
    const t = setup();
    t.opt('Alpha').focus();
    await key(t.layer, 'F2');
    await key(t.layer, 'Delete');
    expect(t.onopen).not.toHaveBeenCalled();
    expect(screen.getAllByRole('option')).toHaveLength(4);
  });

  it('double click opens the icon under the pointer, also after pointer capture retargeted the event', async () => {
    const t = setup();
    await down(t.opt('Beta')); await up(t.opt('Beta'));
    await dbl(t.opt('Beta'));
    expect(t.onopen).toHaveBeenLastCalledWith(['b']);
    await dbl(t.layer); // retargeted to the layer: falls back to the last pressed icon
    expect(t.onopen).toHaveBeenCalledTimes(2);
    await down(t.layer, { clientX: 900, clientY: 600 });
    await dbl(t.layer);
    expect(t.onopen).toHaveBeenCalledTimes(2); // empty desktop: nothing
  });

  it('right click: icons report their id (and become selected), the background reports the point', async () => {
    const t = setup();
    await ctxMenu(t.opt('Delta'), { clientX: 300, clientY: 250 });
    expect(t.oniconcontext).toHaveBeenCalledWith('d', { x: 300, y: 250 });
    expect(t.selected()).toEqual(['Delta']);
    await ctxMenu(t.layer, { clientX: 700, clientY: 400 });
    expect(t.onbackgroundcontext).toHaveBeenCalledWith({ x: 700, y: 400 });
    expect(t.selected()).toEqual([]);
  });

  it('dragging an icon reports its new grid cell; the cell snaps to the grid', async () => {
    const t = setup();
    await down(t.opt('Alpha'), { clientX: 58, clientY: 60 });
    await move(t.layer, { clientX: 58 + 2 * metrics.cellW, clientY: 60 + 3 * metrics.cellH });
    expect(document.querySelectorAll('.ghost')).toHaveLength(1);
    await up(t.layer, { clientX: 58 + 2 * metrics.cellW, clientY: 60 + 3 * metrics.cellH });
    expect(t.onmove).toHaveBeenCalledWith([{ id: 'a', x: 2, y: 3 }]);
    expect(document.querySelectorAll('.ghost')).toHaveLength(0);
  });

  it('dropping on another icon moves to the nearest free cell instead of overlapping', async () => {
    const t = setup();
    await down(t.opt('Alpha'), { clientX: 58, clientY: 60 });
    await move(t.layer, { clientX: 58 + 2 * metrics.cellW, clientY: 60 + 2 * metrics.cellH }); // onto Delta at (2,2)
    await up(t.layer, { clientX: 58 + 2 * metrics.cellW, clientY: 60 + 2 * metrics.cellH });
    const moved = (t.onmove.mock.calls[0][0] as Array<{ x: number; y: number }>)[0];
    expect(moved).not.toEqual({ id: 'a', x: 2, y: 2 });
    expect(`${moved.x},${moved.y}`).not.toBe('2,2');
  });

  it('a tiny movement is a click, not a drag', async () => {
    const t = setup();
    await down(t.opt('Alpha'), { clientX: 58, clientY: 60 });
    await move(t.layer, { clientX: 60, clientY: 61 });
    await up(t.layer, { clientX: 60, clientY: 61 });
    expect(t.onmove).not.toHaveBeenCalled();
  });

  it('moves the whole selection when dragging one of its members', async () => {
    const t = setup();
    await down(t.opt('Alpha')); await up(t.opt('Alpha'));
    await down(t.opt('Beta'), { ctrlKey: true }); await up(t.opt('Beta'));
    await down(t.opt('Alpha'), { clientX: 58, clientY: 60 });
    await move(t.layer, { clientX: 58 + 3 * metrics.cellW, clientY: 60 });
    await up(t.layer, { clientX: 58 + 3 * metrics.cellW, clientY: 60 });
    const moves = t.onmove.mock.calls[0][0] as Array<{ id: string; x: number; y: number }>;
    expect(moves).toEqual(expect.arrayContaining([{ id: 'a', x: 3, y: 0 }, { id: 'b', x: 3, y: 1 }]));
    expect(moves).toHaveLength(2);
  });

  it('snaps back when the host rejects the move', async () => {
    const t = setup({ onmove: async () => { throw new Error('nope'); } });
    await down(t.opt('Alpha'), { clientX: 58, clientY: 60 });
    await move(t.layer, { clientX: 58 + 4 * metrics.cellW, clientY: 60 });
    await up(t.layer, { clientX: 58 + 4 * metrics.cellW, clientY: 60 });
    await new Promise((r) => setTimeout(r, 0));
    const cell = t.opt('Alpha').parentElement as HTMLElement;
    expect(cell.style.left).toBe(`${metrics.originX}px`); // back in column 0
  });
});
