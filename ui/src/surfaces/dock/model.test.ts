import { describe, expect, it } from 'vitest';
import { appIdOf, applyPinnedOrder, buildEntries, dragKind, opensWindowList, pinnedAppIndices, pinnedIds, reorderGroup, sectionOf } from './model';
import { item, sep, state, win } from './testkit';

const pinned = (id: string, o = {}) => item(id, { pinned: true, ...o });
const running = (id: string, n = 1) => item(id, { running: true, windows: Array.from({ length: n }, (_, i) => win(`${id}${i}`, id)) });
const folder = (id: string) => item(id, { kind: 'folder', pinned: true, target: `C:\\${id}` });

describe('buildEntries', () => {
  it('keeps host order and turns separator items into separators', () => {
    const e = buildEntries(state([pinned('a'), pinned('b'), sep(1), running('c'), sep(2), folder('dir:x')]));
    expect(e.map((x) => x.key)).toEqual(['a', 'b', 'separator:1', 'c', 'separator:2', 'dir:x']);
    expect(e.map((x) => x.kind)).toEqual(['item', 'item', 'sep', 'item', 'sep', 'item']);
  });
  it('drops leading, trailing and doubled separators', () => {
    const e = buildEntries(state([sep(1), pinned('a'), sep(2), sep(3), running('b'), sep(4)]));
    expect(e.map((x) => x.key)).toEqual(['a', 'separator:2', 'b']);
  });
  it('appends minimised windows after a divider, once, as tiles', () => {
    const w = win('9', 'x', { minimized: true });
    const e = buildEntries(state([pinned('a')], [w]));
    expect(e.map((x) => x.key)).toEqual(['a', 'sep:minimized', 'win:9']);
    expect(e[2]).toMatchObject({ kind: 'tile' });
    const f = buildEntries(state([pinned('a'), sep(1), folder('dir:x')], [w]));
    expect(f.filter((x) => x.kind === 'sep')).toHaveLength(2);
  });
  it('does not emit a divider before the tiles when the dock has only tiles', () => {
    const e = buildEntries(state([], [win('1', 'x', { minimized: true })]));
    expect(e.map((x) => x.kind)).toEqual(['tile']);
  });
  it('ignores duplicate ids defensively and handles an empty state', () => {
    expect(buildEntries(state([pinned('a'), pinned('a')])).length).toBe(1);
    expect(buildEntries(state([]))).toEqual([]);
  });
  it('classifies sections', () => {
    expect(sectionOf(pinned('a'))).toBe('pinned');
    expect(sectionOf(running('a'))).toBe('running');
    expect(sectionOf(folder('d'))).toBe('folder');
  });
});

describe('applyPinnedOrder', () => {
  const items = [pinned('a'), pinned('b'), pinned('c'), sep(1), running('r'), sep(2), folder('f1'), folder('f2')];
  it('re-sorts pinned apps and folders within their own slots', () => {
    const out = applyPinnedOrder(items, ['c', 'a', 'b', 'f2', 'f1']);
    expect(out.map((i) => i.id)).toEqual(['c', 'a', 'b', 'separator:1', 'r', 'separator:2', 'f2', 'f1']);
  });
  it('is a no-op without an order and tolerates unknown ids', () => {
    expect(applyPinnedOrder(items, null)).toBe(items);
    expect(applyPinnedOrder(items, ['zzz']).map((i) => i.id)).toEqual(items.map((i) => i.id));
  });
  it('is stable for ids missing from the order', () => {
    const out = applyPinnedOrder(items, ['b']);
    expect(out.slice(0, 3).map((i) => i.id)).toEqual(['b', 'a', 'c']);
  });
});

describe('drag rules', () => {
  const entries = buildEntries(state([pinned('a'), pinned('b'), sep(1), running('c'), item('l', { launching: true }), sep(2), folder('dir:f')]));
  it('pinned items reorder, running unpinned apps pin, the rest cannot be dragged', () => {
    expect(dragKind(entries[0])).toBe('reorder');
    expect(dragKind(entries[3])).toBe('pin');
    expect(dragKind(entries[2])).toBeNull(); // separator
    expect(dragKind(entries.find((e) => e.key === 'l')!)).toBeNull(); // launching placeholder
    expect(dragKind(entries.find((e) => e.key === 'dir:f')!)).toBe('reorder');
  });
  it('reorder groups never mix apps and folders', () => {
    expect(reorderGroup(entries, 0)).toEqual([0, 1]);
    expect(reorderGroup(entries, entries.findIndex((e) => e.key === 'dir:f'))).toEqual([entries.findIndex((e) => e.key === 'dir:f')]);
    expect(reorderGroup(entries, 3)).toEqual([]);
    expect(pinnedAppIndices(entries)).toEqual([0, 1]);
  });
});

describe('helpers', () => {
  it('opensWindowList only for apps with more than one window', () => {
    expect(opensWindowList(running('a', 2))).toBe(true);
    expect(opensWindowList(running('a', 1))).toBe(false);
    expect(opensWindowList(pinned('a'))).toBe(false);
    expect(opensWindowList(item('dir:x', { kind: 'folder', windows: [win('1', 'x'), win('2', 'x')] }))).toBe(false);
  });
  it('appIdOf prefers appId; pinnedIds lists pinned items in dock order', () => {
    expect(appIdOf(item('x', { appId: 'exe:y' }))).toBe('exe:y');
    expect(appIdOf(item('x', { appId: undefined }))).toBe('x');
    expect(pinnedIds([pinned('a'), sep(1), running('b'), folder('f')])).toEqual(['a', 'f']);
  });
});
