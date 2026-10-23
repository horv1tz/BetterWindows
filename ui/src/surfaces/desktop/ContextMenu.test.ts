import { afterEach, describe, expect, it, vi } from 'vitest';
import ContextMenu from './ContextMenu.svelte';
import type { MenuItem } from './menu';
import { cleanup, fire, ke, me, pe, render, screen } from './domTest';

vi.mock('svelte', async () => await import('../../../node_modules/svelte/src/index-client.js'));
afterEach(cleanup);

const items: MenuItem[] = [
  { id: 'small', label: 'Small', checked: false },
  { id: 'medium', label: 'Medium', checked: true },
  { id: 'sep', label: '', separator: true },
  { id: 'off', label: 'Disabled', disabled: true },
  { id: 'remove', label: 'Remove', danger: true },
];

function open() {
  const onselect = vi.fn(), onclose = vi.fn();
  render(ContextMenu, { x: 10, y: 10, items, label: 'Widget options', onselect, onclose });
  return { onselect, onclose, menu: screen.getByRole('menu', { name: 'Widget options' }) };
}

describe('ContextMenu', () => {
  it('renders items with radio semantics for checked ones and a separator', () => {
    const { menu } = open();
    expect(screen.getAllByRole('menuitemradio')).toHaveLength(2);
    expect(screen.getByRole('menuitemradio', { name: 'Medium' }).getAttribute('aria-checked')).toBe('true');
    expect(screen.getByRole('menuitemradio', { name: 'Small' }).getAttribute('aria-checked')).toBe('false');
    expect(screen.getByRole('menuitem', { name: 'Remove' })).toBeTruthy();
    expect(menu.querySelector('[role=separator]')).not.toBeNull();
    expect((screen.getByRole('menuitem', { name: 'Disabled' }) as HTMLButtonElement).disabled).toBe(true);
  });

  it('focuses the first enabled item and walks with the arrow keys, skipping disabled ones', async () => {
    const { menu } = open();
    expect(document.activeElement?.textContent).toContain('Small');
    await fire(menu, ke('keydown', 'ArrowDown'));
    expect(document.activeElement?.textContent).toContain('Medium');
    await fire(menu, ke('keydown', 'ArrowDown'));
    expect(document.activeElement?.textContent).toContain('Remove'); // "Disabled" is skipped
    await fire(menu, ke('keydown', 'ArrowDown'));
    expect(document.activeElement?.textContent).toContain('Small'); // wraps
    await fire(menu, ke('keydown', 'ArrowUp'));
    expect(document.activeElement?.textContent).toContain('Remove');
    await fire(menu, ke('keydown', 'Home'));
    expect(document.activeElement?.textContent).toContain('Small');
    await fire(menu, ke('keydown', 'End'));
    expect(document.activeElement?.textContent).toContain('Remove');
  });

  it('click selects an item; a disabled item does nothing', async () => {
    const { onselect } = open();
    await fire(screen.getByRole('menuitem', { name: 'Remove' }), me('click'));
    expect(onselect).toHaveBeenCalledWith('remove');
    await fire(screen.getByRole('menuitem', { name: 'Disabled' }), me('click'));
    expect(onselect).toHaveBeenCalledTimes(1);
  });

  it('Escape and Tab close it, so does a pointer press outside', async () => {
    const { menu, onclose } = open();
    await fire(menu, ke('keydown', 'Escape'));
    expect(onclose).toHaveBeenCalledTimes(1);
    await fire(menu, ke('keydown', 'Tab'));
    expect(onclose).toHaveBeenCalledTimes(2);
    await fire(document.body, pe('pointerdown'));
    expect(onclose).toHaveBeenCalledTimes(3);
    await fire(menu, pe('pointerdown'));
    expect(onclose).toHaveBeenCalledTimes(3); // inside the menu: no
  });
});
