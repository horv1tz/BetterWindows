import { afterEach, describe, expect, it, vi } from 'vitest';
import type { DesktopIcon } from '$lib/protocol/generated';
import DesktopIconItem from './DesktopIconItem.svelte';
import { cleanup, fire, render, screen } from './domTest';

vi.mock('svelte', async () => await import('../../../node_modules/svelte/src/index-client.js'));
afterEach(cleanup);

const base: DesktopIcon = { id: 'x', name: 'Thing', iconUrl: 'https://app.local/dyn/icon/path/x', x: 0, y: 0, target: 'x', kind: 'shortcut' };
const failImage = () => fire(document.querySelector('img')!, new Event('error'));

describe('DesktopIconItem', () => {
  it('shows the shell icon and the label, exposes selection through aria-selected', () => {
    render(DesktopIconItem, { icon: base, size: 64, selected: true });
    expect(document.querySelector('img')?.getAttribute('src')).toBe(base.iconUrl);
    const opt = screen.getByRole('option', { name: 'Thing' });
    expect(opt.getAttribute('aria-selected')).toBe('true');
    expect(opt.textContent).toContain('Thing');
  });

  it('roving tabindex: only the tabbable icon is in the tab order', () => {
    render(DesktopIconItem, { icon: base, size: 64, tabbable: true });
    render(DesktopIconItem, { icon: { ...base, id: 'y', name: 'Other' }, size: 64 });
    expect(screen.getByRole('option', { name: 'Thing' }).tabIndex).toBe(0);
    expect(screen.getByRole('option', { name: 'Other' }).tabIndex).toBe(-1);
  });

  it('special icons fall back to a system glyph when the icon cannot be loaded', async () => {
    render(DesktopIconItem, { icon: { ...base, id: 'special:thisPc', name: 'This PC', kind: 'special' }, size: 64 });
    await failImage();
    expect(document.querySelector('[data-fallback="special"]')).not.toBeNull();
    expect(document.querySelector('img')).toBeNull();
  });

  it('the recycle bin gets its own glyph, folders and files theirs, shortcuts a letter tile', async () => {
    const kinds: Array<[Partial<DesktopIcon>, string]> = [
      [{ id: 'special:recycleBin', name: 'Recycle Bin', kind: 'special' }, 'special'],
      [{ id: 'fs:a', name: 'Projects', kind: 'folder' }, 'folder'],
      [{ id: 'fs:b', name: 'a.txt', kind: 'file' }, 'file'],
      [{ id: 'sc:c', name: 'Telegram', kind: 'shortcut' }, 'shortcut'],
    ];
    for (const [over, expected] of kinds) {
      cleanup();
      render(DesktopIconItem, { icon: { ...base, ...over } as DesktopIcon, size: 64 });
      await failImage();
      expect(document.querySelector(`[data-fallback="${expected}"]`), String(over.id)).not.toBeNull();
    }
    // the letter tile of a shortcut shows the first letter of its name
    expect(document.querySelector('[data-fallback="shortcut"]')?.textContent?.trim()).toBe('T');
  });

  it('an empty icon url goes straight to the fallback', () => {
    render(DesktopIconItem, { icon: { ...base, iconUrl: '', kind: 'folder' }, size: 64 });
    expect(document.querySelector('img')).toBeNull();
    expect(document.querySelector('[data-fallback="folder"]')).not.toBeNull();
  });
});
