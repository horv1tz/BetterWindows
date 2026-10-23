import { describe, expect, it } from 'vitest';
import type { SearchResult } from '$lib/protocol/generated';
import { buildListModel, needsConfirm, orderedActions, reconcileSelection } from './viewModel';

const r = (id: string, provider: SearchResult['provider'], extra: Partial<SearchResult> = {}): SearchResult => ({
  id, provider, title: id, score: 0.5, actions: [{ id: 'open', label: 'Open', primary: true }], ...extra,
});

describe('buildListModel', () => {
  it('puts the calculator answer in the card and numbers it first', () => {
    const m = buildListModel([r('calc', 'calc'), r('a1', 'apps'), r('s1', 'settings')]);
    expect(m.card?.result.id).toBe('calc');
    expect(m.card?.index).toBe(0);
    expect(m.flat.map((e) => e.result.id)).toEqual(['calc', 'a1', 's1']);
    expect(m.sections.map((s) => s.key)).toEqual(['apps', 'settings']);
  });

  it('groups by provider in order of first appearance, preserving order inside a group', () => {
    const m = buildListModel([r('a1', 'apps'), r('f1', 'files'), r('a2', 'apps'), r('w', 'web')]);
    expect(m.sections.map((s) => s.key)).toEqual(['apps', 'files', 'web']);
    expect(m.flat.map((e) => e.result.id)).toEqual(['a1', 'a2', 'f1', 'w']);
    expect(m.flat.map((e) => e.index)).toEqual([0, 1, 2, 3]);
  });

  it('only the first calc result becomes the card', () => {
    const m = buildListModel([r('c1', 'calc'), r('c2', 'calc')]);
    expect(m.card?.result.id).toBe('c1');
    expect(m.sections[0].entries.map((e) => e.result.id)).toEqual(['c2']);
  });

  it('shows recents under a single group and never makes a card', () => {
    const m = buildListModel([r('c', 'calc'), r('a', 'apps'), r('f', 'files')], { recent: true });
    expect(m.card).toBeNull();
    expect(m.sections).toHaveLength(1);
    expect(m.sections[0].key).toBe('recent');
    expect(m.flat).toHaveLength(3);
  });

  it('is empty for no results', () => {
    expect(buildListModel([])).toEqual({ card: null, sections: [], flat: [] });
  });
});

describe('orderedActions', () => {
  it('moves the primary action first', () => {
    const res = r('x', 'files', { actions: [{ id: 'reveal', label: 'Show', primary: false }, { id: 'open', label: 'Open', primary: true }, { id: 'copy', label: 'Copy', primary: false }] });
    expect(orderedActions(res).map((a) => a.id)).toEqual(['open', 'reveal', 'copy']);
  });
  it('falls back to the first action when none is primary, and to [] for no actions', () => {
    expect(orderedActions(r('x', 'apps', { actions: [{ id: 'a', label: 'A', primary: false }, { id: 'b', label: 'B', primary: false }] })).map((a) => a.id)).toEqual(['a', 'b']);
    expect(orderedActions(r('x', 'apps', { actions: [] }))).toEqual([]);
  });
});

describe('needsConfirm', () => {
  it('detects confirmation in command subtitles in both languages', () => {
    expect(needsConfirm(r('c', 'commands', { subtitle: 'Restart this PC · Asks for confirmation' }))).toBe(true);
    expect(needsConfirm(r('c', 'commands', { subtitle: 'Перезагрузить · Потребуется подтверждение' }))).toBe(true);
    expect(needsConfirm(r('c', 'commands', { subtitle: 'Lock this PC' }))).toBe(false);
  });
  it('is limited to commands', () => {
    expect(needsConfirm(r('f', 'files', { subtitle: 'C:\\confirm.txt' }))).toBe(false);
  });
  it('honours a confirm action id', () => {
    expect(needsConfirm(r('c', 'commands', { actions: [{ id: 'run.confirm', label: 'Run', primary: true }] }))).toBe(true);
  });
});

describe('reconcileSelection', () => {
  const flat = buildListModel([r('a', 'apps'), r('b', 'apps'), r('c', 'apps')]).flat;
  it('keeps the selected result when the user navigated and it is still there', () => {
    expect(reconcileSelection({ id: 'c', moved: true }, flat)).toBe(2);
  });
  it('goes back to the top hit when the user has not navigated', () => {
    expect(reconcileSelection({ id: 'c', moved: false }, flat)).toBe(0);
  });
  it('goes to the top when the selected result vanished or the list is empty', () => {
    expect(reconcileSelection({ id: 'zzz', moved: true }, flat)).toBe(0);
    expect(reconcileSelection({ id: 'a', moved: true }, [])).toBe(0);
  });
});
