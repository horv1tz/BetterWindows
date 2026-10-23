// Launchpad data model and reducers (pure, no DOM). Persisted shape = settings.launchpad:
//   order   top-level grid order: app ids AND folder ids ("folder:<guid>"); apps in a folder or hidden are NEVER in it
//   folders { id, name, appIds[] } (appIds in in-folder order)
//   hidden  app ids removed from Launchpad (never uninstalled)
import type { AppEntry, LaunchpadFolder, LaunchpadSettings } from '$lib/protocol/generated';
import { fuzzyScore } from '../spotlight/highlight';

/** Fully resolved layout: every visible app is either in `top` or in exactly one folder. */
export interface Layout {
  top: string[];
  folders: LaunchpadFolder[];
  hidden: string[];
}

export type TopItem =
  | { kind: 'app'; id: string; name: string; app: AppEntry }
  | { kind: 'folder'; id: string; name: string; folder: LaunchpadFolder; apps: AppEntry[] };

export const FOLDER_PREFIX = 'folder:';
export const MAX_FOLDER_NAME = 48;

export const isFolderId = (id: string) => id.startsWith(FOLDER_PREFIX);

const collator = new Intl.Collator(undefined, { sensitivity: 'base', numeric: true });
const byName = (a: string, b: string) => collator.compare(a, b) || (a < b ? -1 : a > b ? 1 : 0);

export function newFolderId(): string {
  const g = globalThis.crypto?.randomUUID?.() ?? `${Date.now().toString(16)}-${Math.random().toString(16).slice(2)}`;
  return `${FOLDER_PREFIX}${g}`;
}

/**
 * Turn persisted settings + the installed catalog into a complete layout.
 *  - uninstalled apps are dropped everywhere; hidden apps never show (hidden wins over folder membership)
 *  - an app lives in the first folder that lists it; an app inside a folder is removed from `order`
 *  - folders without any installed app disappear from the view
 *  - ids missing from `order` (new apps, new folders) are appended alphabetically
 */
export function materialize(settings: Pick<LaunchpadSettings, 'folders' | 'order' | 'hidden'>, apps: readonly AppEntry[]): Layout {
  const installed = new Map(apps.map((a) => [a.id, a]));
  const hidden = unique((settings.hidden ?? []).filter((id) => installed.has(id)));
  const hiddenSet = new Set(hidden);

  const used = new Set<string>();
  const folders: LaunchpadFolder[] = [];
  for (const f of settings.folders ?? []) {
    if (!f || typeof f.id !== 'string' || !isFolderId(f.id) || folders.some((x) => x.id === f.id)) continue;
    const appIds: string[] = [];
    for (const id of f.appIds ?? []) {
      if (!installed.has(id) || hiddenSet.has(id) || used.has(id)) continue;
      used.add(id);
      appIds.push(id);
    }
    if (appIds.length) folders.push({ id: f.id, name: f.name ?? '', appIds });
  }

  const folderIds = new Set(folders.map((f) => f.id));
  const top: string[] = [];
  const placed = new Set<string>();
  for (const id of settings.order ?? []) {
    if (placed.has(id)) continue;
    const ok = isFolderId(id) ? folderIds.has(id) : installed.has(id) && !hiddenSet.has(id) && !used.has(id);
    if (ok) { top.push(id); placed.add(id); }
  }
  const missing: Array<{ id: string; name: string }> = [];
  for (const f of folders) if (!placed.has(f.id)) missing.push({ id: f.id, name: f.name });
  for (const a of apps) if (!placed.has(a.id) && !hiddenSet.has(a.id) && !used.has(a.id) && !missing.some((m) => m.id === a.id)) missing.push({ id: a.id, name: a.name });
  missing.sort((a, b) => byName(a.name, b.name));
  for (const m of missing) top.push(m.id);

  return { top, folders, hidden };
}

/** Layout -> what gets written to settings.launchpad (same shape the contract documents). */
export function toSettings(layout: Layout): LaunchpadSettings {
  return { folders: layout.folders.map((f) => ({ ...f, appIds: [...f.appIds] })), order: [...layout.top], hidden: [...layout.hidden] };
}

/** Render model of the top level. */
export function toItems(layout: Layout, apps: readonly AppEntry[]): TopItem[] {
  const byId = new Map(apps.map((a) => [a.id, a]));
  const folderById = new Map(layout.folders.map((f) => [f.id, f]));
  const out: TopItem[] = [];
  for (const id of layout.top) {
    const folder = folderById.get(id);
    if (folder) {
      out.push({ kind: 'folder', id, name: folder.name, folder, apps: folder.appIds.map((a) => byId.get(a)).filter((a): a is AppEntry => !!a) });
      continue;
    }
    const app = byId.get(id);
    if (app) out.push({ kind: 'app', id, name: app.name, app });
  }
  return out;
}

/** Apps matching the search query (not hidden), best match first, then alphabetical. */
export function searchApps(apps: readonly AppEntry[], hidden: readonly string[], query: string): AppEntry[] {
  const hide = new Set(hidden);
  return apps
    .filter((a) => !hide.has(a.id))
    .map((a) => ({ a, s: fuzzyScore(a.name, query) }))
    .filter((x) => x.s > 0)
    .sort((x, y) => y.s - x.s || byName(x.a.name, y.a.name))
    .map((x) => x.a);
}

// ---------------------------------------------------------------------------------------------------------------------
// Reducers
// ---------------------------------------------------------------------------------------------------------------------

function unique<T>(xs: T[]): T[] { return [...new Set(xs)]; }

const clampIndex = (i: number, len: number) => Math.min(Math.max(Math.round(i), 0), len);

/** Insert `id` so that it ends up at index `to` of the resulting array (clamped). */
export function insertAt(list: readonly string[], id: string, to: number): string[] {
  const rest = list.filter((x) => x !== id);
  rest.splice(clampIndex(to, rest.length), 0, id);
  return rest;
}

/** What a drag preview looks like: the dragged id removed and re-inserted at `hoverIndex`. */
export const previewOrder = (order: readonly string[], dragId: string, hoverIndex: number): string[] => (order.includes(dragId) ? insertAt(order, dragId, hoverIndex) : [...order]);

/** Move a top-level item (app or folder) so that it ends up at index `to`. */
export function moveTop(layout: Layout, id: string, to: number): Layout {
  if (!layout.top.includes(id)) return layout;
  return { ...layout, top: insertAt(layout.top, id, to) };
}

export function folderOf(layout: Layout, appId: string): LaunchpadFolder | undefined {
  return layout.folders.find((f) => f.appIds.includes(appId));
}

/** Drag app `dragId` onto app `targetId` (both top-level apps): a new folder takes the target's place with [target, dragged]. */
export function createFolder(layout: Layout, dragId: string, targetId: string, name: string, folderId: string = newFolderId()): { layout: Layout; folderId: string } | null {
  if (dragId === targetId || isFolderId(dragId) || isFolderId(targetId)) return null;
  if (!layout.top.includes(dragId) || !layout.top.includes(targetId)) return null;
  const top = layout.top.filter((x) => x !== dragId).map((x) => (x === targetId ? folderId : x));
  const folder: LaunchpadFolder = { id: folderId, name: cleanName(name), appIds: [targetId, dragId] };
  return { layout: { ...layout, top, folders: [...layout.folders, folder] }, folderId };
}

/** Drop a top-level app onto a folder tile: it is appended to the folder. */
export function addToFolder(layout: Layout, appId: string, folderId: string): Layout {
  const folder = layout.folders.find((f) => f.id === folderId);
  if (!folder || isFolderId(appId) || !layout.top.includes(appId)) return layout;
  return {
    ...layout,
    top: layout.top.filter((x) => x !== appId),
    folders: layout.folders.map((f) => (f.id === folderId ? { ...f, appIds: [...f.appIds, appId] } : f)),
  };
}

/** Reorder inside a folder so that `appId` ends at index `to`. */
export function moveInFolder(layout: Layout, folderId: string, appId: string, to: number): Layout {
  return { ...layout, folders: layout.folders.map((f) => (f.id === folderId && f.appIds.includes(appId) ? { ...f, appIds: insertAt(f.appIds, appId, to) } : f)) };
}

/**
 * Take an app out of its folder and put it into the top level at index `atTop` (default: right after the folder).
 * A folder left with one app dissolves (that app takes the folder's place); an empty folder disappears.
 * `atTop` is an index into the top level AFTER the dissolve step.
 */
export function removeFromFolder(layout: Layout, appId: string, atTop?: number): Layout {
  const folder = folderOf(layout, appId);
  if (!folder) return layout;
  const remaining = folder.appIds.filter((x) => x !== appId);
  const pos = layout.top.indexOf(folder.id);
  let top = [...layout.top];
  let folders = layout.folders;
  if (remaining.length >= 2) {
    folders = folders.map((f) => (f.id === folder.id ? { ...f, appIds: remaining } : f));
  } else {
    folders = folders.filter((f) => f.id !== folder.id);
    top.splice(pos, 1, ...(remaining.length === 1 ? [remaining[0]] : []));
  }
  const after = atTop ?? (remaining.length >= 2 ? pos + 1 : pos + remaining.length);
  top = insertAt(top, appId, after);
  return { ...layout, top, folders };
}

export function renameFolder(layout: Layout, folderId: string, name: string): Layout {
  return { ...layout, folders: layout.folders.map((f) => (f.id === folderId ? { ...f, name: cleanName(name) } : f)) };
}

/** Remove an app from Launchpad (never uninstalls): it leaves the grid/folder and joins `hidden`. */
export function hideApp(layout: Layout, appId: string): Layout {
  if (isFolderId(appId) || layout.hidden.includes(appId)) return layout;
  const unfoldered = folderOf(layout, appId) ? removeFromFolder(layout, appId, 0) : layout;
  return { ...unfoldered, top: unfoldered.top.filter((x) => x !== appId), hidden: [...unfoldered.hidden, appId] };
}

/** Bring every hidden app back; they are appended alphabetically (they have no remembered position). */
export function unhideAll(layout: Layout, apps: readonly AppEntry[]): Layout {
  if (!layout.hidden.length) return layout;
  const back = layout.hidden
    .map((id) => apps.find((a) => a.id === id))
    .filter((a): a is AppEntry => !!a)
    .sort((x, y) => byName(x.name, y.name))
    .map((a) => a.id);
  return { ...layout, hidden: [], top: [...layout.top, ...back.filter((id) => !layout.top.includes(id) && !folderOf(layout, id))] };
}

export function cleanName(name: string): string {
  return name.replace(/\s+/g, ' ').trim().slice(0, MAX_FOLDER_NAME);
}

/** Name for a folder created by dropping `a` on `b`: their shared category when they have one, else the generic default. */
export function suggestFolderName(a: AppEntry | undefined, b: AppEntry | undefined, fallback: string): string {
  const ca = a?.category?.trim();
  return ca && ca === b?.category?.trim() ? ca : fallback;
}

export const layoutEquals = (a: Layout, b: Layout): boolean => JSON.stringify(toSettings(a)) === JSON.stringify(toSettings(b));
