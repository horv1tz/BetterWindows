import type { DockItem, DockState, WindowRef } from '$lib/protocol/generated';

/**
 * View helpers that turn the host's DockState into what the dock renders. Pure (unit tested).
 * The host sends: items = [pinned apps..., running-but-unpinned apps..., folders...] with `separator:N` items between the groups,
 * and `minimized` windows separately (right-hand section).
 */
export type Section = 'pinned' | 'running' | 'folder';

export type Entry =
  | { kind: 'item'; key: string; item: DockItem; section: Section }
  | { kind: 'sep'; key: string }
  | { kind: 'tile'; key: string; win: WindowRef };

export function sectionOf(item: DockItem): Section {
  if (item.kind === 'folder') return 'folder';
  return item.pinned ? 'pinned' : 'running';
}

/** Re-sorts the pinned items of `items` by `order` (optimistic order right after a drop), leaving every other item where it is. */
export function applyPinnedOrder(items: DockItem[], order: string[] | null | undefined): DockItem[] {
  if (!order || order.length === 0) return items;
  const rank = new Map(order.map((id, i) => [id, i] as const));
  const group = (it: DockItem) => (it.kind === 'folder' ? 'folder' : 'app');
  const out = items.slice();
  for (const g of ['app', 'folder'] as const) {
    const slots: number[] = [];
    items.forEach((it, i) => { if (it.pinned && it.kind !== 'separator' && group(it) === g) slots.push(i); });
    const sorted = slots.map((i) => items[i]).sort((a, b) => (rank.get(a.id) ?? 1e9) - (rank.get(b.id) ?? 1e9));
    slots.forEach((slot, n) => { out[slot] = sorted[n]; });
  }
  return out;
}

/** Entries in render order: separators normalised (none leading/trailing/doubled), minimised tiles after a divider. */
export function buildEntries(state: DockState, pinnedOrder?: string[] | null): Entry[] {
  const out: Entry[] = [];
  let sepCount = 0;
  const pushSep = (key?: string) => {
    if (out.length === 0 || out[out.length - 1].kind === 'sep') return;
    out.push({ kind: 'sep', key: key ?? `sep:${++sepCount}` });
  };
  const items = applyPinnedOrder(state.items ?? [], pinnedOrder);
  const seen = new Set<string>();
  for (const it of items) {
    if (it.kind === 'separator') { pushSep(it.id.startsWith('separator:') ? it.id : undefined); continue; }
    if (seen.has(it.id)) continue; // defensive: ids are keys
    seen.add(it.id);
    out.push({ kind: 'item', key: it.id, item: it, section: sectionOf(it) });
  }
  const tiles = state.minimized ?? [];
  if (tiles.length) {
    pushSep('sep:minimized');
    for (const w of tiles) out.push({ kind: 'tile', key: `win:${w.handle}`, win: w });
  }
  while (out.length && out[out.length - 1].kind === 'sep') out.pop();
  return out;
}

/** Entry indices of the pinned APPS (the zone a running app can be dragged into). */
export function pinnedAppIndices(entries: Entry[]): number[] {
  const r: number[] = [];
  entries.forEach((e, i) => { if (e.kind === 'item' && e.section === 'pinned') r.push(i); });
  return r;
}

/** Entry indices of the dragged item's reorder group: pinned apps among themselves, pinned folders among themselves. */
export function reorderGroup(entries: Entry[], index: number): number[] {
  const e = entries[index];
  if (!e || e.kind !== 'item' || !e.item.pinned) return [];
  const want = e.section;
  const r: number[] = [];
  entries.forEach((x, i) => { if (x.kind === 'item' && x.item.pinned && x.section === want) r.push(i); });
  return r;
}

/** Drag rules: pinned items reorder/unpin; running unpinned apps can be dragged into the pinned zone to pin them. */
export function dragKind(e: Entry): 'reorder' | 'pin' | null {
  if (e.kind !== 'item' || e.item.launching) return null;
  if (e.item.pinned) return 'reorder';
  return e.item.kind === 'app' && e.item.running ? 'pin' : null;
}

export function appIdOf(item: DockItem): string { return item.appId ?? item.id; }

/** Apps with several windows open the window-list popover instead of activating. */
export function opensWindowList(item: DockItem): boolean {
  return item.kind === 'app' && item.windows.length > 1;
}

export function openWindowCount(item: DockItem): number { return item.windows.length; }

/** Ids of all pinned items in dock order (what dock.reorder expects: pinned apps, then pinned folders). */
export function pinnedIds(items: DockItem[]): string[] {
  return items.filter((i) => i.pinned && i.kind !== 'separator').map((i) => i.id);
}

export function emptyDockState(): DockState { return { items: [], minimized: [] }; }

/** True if both states render identically as far as layout is concerned (same keys in the same order). */
export function sameLayout(a: Entry[], b: Entry[]): boolean {
  return a.length === b.length && a.every((e, i) => e.key === b[i].key && e.kind === b[i].kind);
}
