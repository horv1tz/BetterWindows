import { describe, expect, it } from 'vitest';
import type { AppEntry } from '$lib/protocol/generated';
import {
  addToFolder, cleanName, createFolder, hideApp, insertAt, layoutEquals, materialize, moveInFolder, moveTop, previewOrder, removeFromFolder,
  renameFolder, searchApps, suggestFolderName, toItems, toSettings, unhideAll, type Layout,
} from './model';

const mk = (name: string, category?: string): AppEntry => ({ id: `exe:${name.toLowerCase()}`, name, packaged: false, iconUrl: '', category });
const A = mk('Alpha', 'Dev'), B = mk('Bravo', 'Dev'), C = mk('Charlie'), D = mk('Delta'), E = mk('Echo');
const APPS = [E, C, A, D, B]; // deliberately unsorted catalog
const id = (a: AppEntry) => a.id;
const F1 = 'folder:11111111-1111-1111-1111-111111111111';

describe('materialize', () => {
  it('orders everything alphabetically for a fresh profile', () => {
    const l = materialize({ folders: [], order: [], hidden: [] }, APPS);
    expect(l.top).toEqual([A, B, C, D, E].map(id));
    expect(l.folders).toEqual([]);
    expect(l.hidden).toEqual([]);
  });

  it('honours order, then appends unknown apps alphabetically', () => {
    const l = materialize({ folders: [], order: [E.id, C.id], hidden: [] }, APPS);
    expect(l.top).toEqual([E.id, C.id, A.id, B.id, D.id]);
  });

  it('removes hidden apps from the grid and keeps them in hidden', () => {
    const l = materialize({ folders: [], order: [A.id, B.id], hidden: [B.id] }, APPS);
    expect(l.top).not.toContain(B.id);
    expect(l.hidden).toEqual([B.id]);
  });

  it('keeps folder members out of the top level even when order lists them', () => {
    const l = materialize({ folders: [{ id: F1, name: 'Dev', appIds: [A.id, B.id] }], order: [A.id, F1, C.id, B.id], hidden: [] }, APPS);
    expect(l.top).toEqual([F1, C.id, D.id, E.id]);
    expect(l.folders[0].appIds).toEqual([A.id, B.id]);
  });

  it('appends a folder missing from order alphabetically by name among apps', () => {
    const l = materialize({ folders: [{ id: F1, name: 'Cafe', appIds: [A.id, B.id] }], order: [], hidden: [] }, APPS);
    expect(l.top).toEqual([F1, C.id, D.id, E.id]);
  });

  it('drops uninstalled apps everywhere and removes folders that became empty', () => {
    const ghost = 'exe:ghost';
    const l = materialize({ folders: [{ id: F1, name: 'X', appIds: [ghost] }, { id: 'folder:2', name: 'Y', appIds: [ghost, C.id] }], order: [ghost, F1, 'folder:2', A.id], hidden: [ghost] }, APPS);
    expect(l.top).not.toContain(ghost);
    expect(l.top).not.toContain(F1);
    expect(l.folders.map((f) => f.id)).toEqual(['folder:2']);
    expect(l.folders[0].appIds).toEqual([C.id]);
    expect(l.hidden).toEqual([]);
  });

  it('puts an app in only the first folder that lists it and tolerates duplicates', () => {
    const l = materialize({
      folders: [{ id: F1, name: 'One', appIds: [A.id, A.id, B.id] }, { id: 'folder:2', name: 'Two', appIds: [B.id, C.id] }],
      order: [F1, F1, 'folder:2', D.id, D.id], hidden: [],
    }, APPS);
    expect(l.folders[0].appIds).toEqual([A.id, B.id]);
    expect(l.folders[1].appIds).toEqual([C.id]);
    expect(l.top).toEqual([F1, 'folder:2', D.id, E.id]);
  });

  it('lets hidden win over folder membership', () => {
    const l = materialize({ folders: [{ id: F1, name: 'F', appIds: [A.id, B.id, C.id] }], order: [], hidden: [B.id] }, APPS);
    expect(l.folders[0].appIds).toEqual([A.id, C.id]);
  });

  it('survives missing fields (older config files)', () => {
    const l = materialize({} as any, APPS);
    expect(l.top).toHaveLength(5);
  });

  it('is idempotent', () => {
    const l = materialize({ folders: [{ id: F1, name: 'Dev', appIds: [B.id, A.id] }], order: [E.id, F1], hidden: [D.id] }, APPS);
    expect(materialize(toSettings(l), APPS)).toEqual(l);
  });

  it('puts a brand new installed app at the end, alphabetically among new ones', () => {
    const l = materialize({ folders: [], order: [A.id, B.id, C.id, D.id, E.id], hidden: [] }, [...APPS, mk('Zulu'), mk('Foxtrot')]);
    expect(l.top.slice(5)).toEqual(['exe:foxtrot', 'exe:zulu']);
  });
});

describe('toItems', () => {
  it('builds renderable items for apps and folders', () => {
    const l = materialize({ folders: [{ id: F1, name: 'Dev', appIds: [B.id, A.id] }], order: [F1], hidden: [] }, APPS);
    const items = toItems(l, APPS);
    expect(items[0]).toMatchObject({ kind: 'folder', id: F1, name: 'Dev' });
    expect((items[0] as any).apps.map((a: AppEntry) => a.name)).toEqual(['Bravo', 'Alpha']);
    expect(items[1]).toMatchObject({ kind: 'app', name: 'Charlie' });
  });
});

describe('reordering', () => {
  const base: Layout = { top: [A.id, B.id, C.id, D.id], folders: [], hidden: [] };
  it('insertAt puts the id at the requested final index', () => {
    expect(insertAt(['a', 'b', 'c', 'd'], 'a', 2)).toEqual(['b', 'c', 'a', 'd']);
    expect(insertAt(['a', 'b', 'c', 'd'], 'd', 0)).toEqual(['d', 'a', 'b', 'c']);
    expect(insertAt(['a', 'b'], 'a', 99)).toEqual(['b', 'a']);
    expect(insertAt(['a', 'b'], 'x', 1)).toEqual(['a', 'x', 'b']);
  });
  it('moveTop moves an app forward and backward', () => {
    expect(moveTop(base, A.id, 2).top).toEqual([B.id, C.id, A.id, D.id]);
    expect(moveTop(base, D.id, 1).top).toEqual([A.id, D.id, B.id, C.id]);
  });
  it('moveTop ignores unknown ids', () => {
    expect(moveTop(base, 'nope', 1)).toBe(base);
  });
  it('previewOrder is the live-reflow order while dragging', () => {
    expect(previewOrder(base.top, A.id, 3)).toEqual([B.id, C.id, D.id, A.id]);
    expect(previewOrder(base.top, A.id, 0)).toEqual(base.top);
  });
});

describe('folders', () => {
  const base: Layout = { top: [A.id, B.id, C.id, D.id], folders: [], hidden: [] };

  it('creates a folder in the place of the target: [target, dragged]', () => {
    const r = createFolder(base, D.id, B.id, 'Stuff', F1)!;
    expect(r.layout.top).toEqual([A.id, F1, C.id]);
    expect(r.layout.folders).toEqual([{ id: F1, name: 'Stuff', appIds: [B.id, D.id] }]);
    expect(r.folderId).toBe(F1);
  });
  it('rejects invalid merges', () => {
    expect(createFolder(base, A.id, A.id, 'x', F1)).toBeNull();
    expect(createFolder(base, A.id, 'nope', 'x', F1)).toBeNull();
    expect(createFolder(base, F1, A.id, 'x', F1)).toBeNull();
  });
  it('generates a folder id when none is given', () => {
    const r = createFolder(base, A.id, B.id, 'x')!;
    expect(r.folderId).toMatch(/^folder:.+/);
  });
  it('adds a top-level app to a folder', () => {
    const l = createFolder(base, D.id, B.id, 'Stuff', F1)!.layout;
    const l2 = addToFolder(l, A.id, F1);
    expect(l2.top).toEqual([F1, C.id]);
    expect(l2.folders[0].appIds).toEqual([B.id, D.id, A.id]);
  });
  it('addToFolder ignores unknown folder or apps not on the top level', () => {
    expect(addToFolder(base, A.id, 'folder:nope')).toBe(base);
    const l = createFolder(base, D.id, B.id, 'S', F1)!.layout;
    expect(addToFolder(l, D.id, F1)).toBe(l);
  });
  it('reorders inside a folder', () => {
    const l: Layout = { top: [F1], folders: [{ id: F1, name: 'F', appIds: [A.id, B.id, C.id] }], hidden: [] };
    expect(moveInFolder(l, F1, A.id, 2).folders[0].appIds).toEqual([B.id, C.id, A.id]);
    expect(moveInFolder(l, F1, 'nope', 0)).toEqual(l);
  });
  it('takes an app out and keeps the folder when 2+ remain', () => {
    const l: Layout = { top: [F1, D.id], folders: [{ id: F1, name: 'F', appIds: [A.id, B.id, C.id] }], hidden: [] };
    const out = removeFromFolder(l, B.id);
    expect(out.top).toEqual([F1, B.id, D.id]);
    expect(out.folders[0].appIds).toEqual([A.id, C.id]);
  });
  it('dissolves a folder that is left with a single app (it takes the folder place)', () => {
    const l: Layout = { top: [D.id, F1, E.id], folders: [{ id: F1, name: 'F', appIds: [A.id, B.id] }], hidden: [] };
    const out = removeFromFolder(l, A.id, 3);
    expect(out.folders).toEqual([]);
    expect(out.top).toEqual([D.id, B.id, E.id, A.id]);
  });
  it('dissolving then inserting right after the folder position by default', () => {
    const l: Layout = { top: [D.id, F1, E.id], folders: [{ id: F1, name: 'F', appIds: [A.id, B.id] }], hidden: [] };
    expect(removeFromFolder(l, A.id).top).toEqual([D.id, B.id, A.id, E.id]);
  });
  it('removeFromFolder is a no-op for apps that are not in a folder', () => {
    const l: Layout = { top: [A.id], folders: [], hidden: [] };
    expect(removeFromFolder(l, A.id)).toBe(l);
  });
  it('renames with trimming, whitespace collapsing and a length cap', () => {
    const l: Layout = { top: [F1], folders: [{ id: F1, name: 'F', appIds: [A.id, B.id] }], hidden: [] };
    expect(renameFolder(l, F1, '  My   apps ').folders[0].name).toBe('My apps');
    expect(cleanName('x'.repeat(100))).toHaveLength(48);
  });
  it('suggests the shared category as the default name', () => {
    expect(suggestFolderName(A, B, 'Folder')).toBe('Dev');
    expect(suggestFolderName(A, C, 'Folder')).toBe('Folder');
    expect(suggestFolderName(undefined, undefined, 'Folder')).toBe('Folder');
  });
});

describe('hide / unhide', () => {
  it('hides a top-level app', () => {
    const l = hideApp({ top: [A.id, B.id], folders: [], hidden: [] }, A.id);
    expect(l).toEqual({ top: [B.id], folders: [], hidden: [A.id] });
  });
  it('hides an app out of a folder and dissolves a folder left with one app', () => {
    const l = hideApp({ top: [F1], folders: [{ id: F1, name: 'F', appIds: [A.id, B.id] }], hidden: [] }, A.id);
    expect(l.top).toEqual([B.id]);
    expect(l.folders).toEqual([]);
    expect(l.hidden).toEqual([A.id]);
  });
  it('never hides folders and is idempotent', () => {
    const l: Layout = { top: [F1], folders: [{ id: F1, name: 'F', appIds: [A.id, B.id] }], hidden: [] };
    expect(hideApp(l, F1)).toBe(l);
    const once = hideApp({ top: [A.id], folders: [], hidden: [] }, A.id);
    expect(hideApp(once, A.id)).toBe(once);
  });
  it('unhideAll appends the apps alphabetically and clears hidden', () => {
    const l = unhideAll({ top: [C.id], folders: [], hidden: [E.id, A.id, 'exe:ghost'] }, APPS);
    expect(l.top).toEqual([C.id, A.id, E.id]);
    expect(l.hidden).toEqual([]);
  });
  it('the persisted result never lists hidden or folder apps in order (contract c)', () => {
    let l = materialize({ folders: [], order: [], hidden: [] }, APPS);
    l = createFolder(l, B.id, A.id, 'F', F1)!.layout;
    l = hideApp(l, E.id);
    const s = toSettings(l);
    expect(s.order).not.toContain(A.id);
    expect(s.order).not.toContain(B.id);
    expect(s.order).not.toContain(E.id);
    expect(s.order).toContain(F1);
    expect(s.hidden).toEqual([E.id]);
  });
});

describe('searchApps', () => {
  it('filters live, ignoring hidden apps, case and diacritics', () => {
    const apps = [mk('Калькулятор'), mk('Календарь'), mk('Café'), mk('Notepad')];
    expect(searchApps(apps, [], 'кал').map((a) => a.name).sort()).toEqual(['Календарь', 'Калькулятор']);
    expect(searchApps(apps, [], 'CAFE').map((a) => a.name)).toEqual(['Café']);
    expect(searchApps(apps, [apps[0].id], 'кал').map((a) => a.name)).toEqual(['Календарь']);
    expect(searchApps(apps, [], 'zzz')).toEqual([]);
  });
  it('ranks a prefix match first and is alphabetical for ties', () => {
    const apps = [mk('My Notes'), mk('Notebook'), mk('Note')];
    expect(searchApps(apps, [], 'note').map((a) => a.name)).toEqual(['Note', 'Notebook', 'My Notes']);
  });
  it('supports initials', () => {
    expect(searchApps([mk('Visual Studio Code'), mk('Paint')], [], 'vsc').map((a) => a.name)).toEqual(['Visual Studio Code']);
  });
});

it('layoutEquals compares persisted shapes', () => {
  const l: Layout = { top: [A.id], folders: [], hidden: [] };
  expect(layoutEquals(l, { ...l })).toBe(true);
  expect(layoutEquals(l, { ...l, hidden: [B.id] })).toBe(false);
});
