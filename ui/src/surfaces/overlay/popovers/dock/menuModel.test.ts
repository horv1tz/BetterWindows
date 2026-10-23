import { describe, expect, it } from 'vitest';
import { buildMenu, flatEntries } from './menuModel';
import type { DockItem, WindowRef } from '$lib/protocol/generated';

const w = (h: string, o: Partial<WindowRef> = {}): WindowRef => ({ handle: h, title: h, appId: 'a', iconUrl: '', minimized: false, maximized: false, active: false, elevated: false, ...o });
const app = (o: Partial<DockItem> = {}): DockItem => ({ id: 'a', kind: 'app', name: 'A', iconUrl: '', pinned: false, running: false, active: false, attention: false, windows: [], launching: false, ...o });
const act = (m: ReturnType<typeof buildMenu>) => flatEntries(m).map((e) => `${e.action}${e.enabled ? '' : '!'}`);

describe('buildMenu', () => {
  it('running pinned app: windows, remove from dock, new window / hide / quit, settings', () => {
    const m = buildMenu(app({ pinned: true, running: true, windows: [w('1'), w('2')] }));
    expect(m.windows).toHaveLength(2);
    expect(m.primary).toEqual([]);
    expect(act(m)).toEqual(['unpin', 'reveal', 'newWindow', 'hide', 'quit', 'settings']);
    expect(m.subtitle).toBe('running');
    expect(m.app.find((e) => e.action === 'quit')!.danger).toBe(true);
  });
  it('running unpinned app offers Keep in Dock', () => {
    expect(act(buildMenu(app({ running: true, windows: [w('1')] })))).toContain('pin');
  });
  it('app that is not running: Open first; Hide and Quit are disabled; New Window launches', () => {
    const m = buildMenu(app({ pinned: true }));
    expect(act(m)).toEqual(['open', 'unpin', 'reveal', 'newWindow', 'hide!', 'quit!', 'settings']);
    expect(m.subtitle).toBe('notRunning');
  });
  it('Hide is disabled when every window is already minimised', () => {
    const m = buildMenu(app({ pinned: true, running: true, windows: [w('1', { minimized: true })] }));
    expect(act(m)).toContain('hide!');
    expect(act(m)).toContain('quit');
  });
  it('folders: Open, Remove from Dock, Show in File Explorer, no app actions', () => {
    const m = buildMenu(app({ kind: 'folder', pinned: true, target: 'C:\\x' }));
    expect(act(m)).toEqual(['open', 'unpin', 'reveal', 'settings']);
    expect(m.subtitle).toBe('folder');
  });
  it('a launching placeholder cannot be opened again and is reported as launching', () => {
    const m = buildMenu(app({ launching: true }));
    expect(act(m)[0]).toBe('open!');
    expect(m.subtitle).toBe('launching');
  });
  it('an unpinned folder cannot be pinned from the menu (apps only)', () => {
    expect(act(buildMenu(app({ kind: 'folder' })))).toContain('pin!');
  });
});
