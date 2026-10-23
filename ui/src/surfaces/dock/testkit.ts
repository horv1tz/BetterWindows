// Test helpers shared by the dock unit tests (not imported by production code).
import type { DockItem, DockState, WindowRef } from '$lib/protocol/generated';

export function win(handle: string, appId: string, o: Partial<WindowRef> = {}): WindowRef {
  return { handle, title: `Window ${handle}`, appId, iconUrl: `icon:${appId}`, minimized: false, maximized: false, active: false, elevated: false, ...o };
}

export function item(id: string, o: Partial<DockItem> = {}): DockItem {
  return {
    id, kind: 'app', appId: id, name: id, iconUrl: `icon:${id}`, pinned: false, running: false, active: false, attention: false,
    windows: [], launching: false, ...o,
  };
}

export const sep = (n: number): DockItem => item(`separator:${n}`, { kind: 'separator', name: '', appId: undefined });

export function state(items: DockItem[], minimized: WindowRef[] = []): DockState {
  return { items, minimized };
}
