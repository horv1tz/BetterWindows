import { describe, expect, it } from 'vitest';
import { buildIndex, normalize, searchIndex, type RowEntry, type Translate } from './searchIndex';

const EN: Record<string, string> = {
  'page.dock': 'Dock', 'page.dock.kw': 'bar launcher taskbar', 'page.general': 'General', 'page.spotlight': 'Spotlight',
  'dock.autoHide.label': 'Automatically hide the Dock', 'dock.autoHide.desc': 'The Dock slides away when you are not using it.',
  'dock.iconSize.label': 'Icon size', 'dock.iconSize.kw': 'big small scale',
  'general.autostart.label': 'Open at login', 'general.autostart.desc': 'Start BetterWindows when you sign in.',
  'spotlight.maxResults.label': 'Maximum results',
};
const RU: Record<string, string> = {
  'page.dock': 'Док', 'page.general': 'Основные', 'page.spotlight': 'Spotlight',
  'dock.autoHide.label': 'Автоматически скрывать Док', 'dock.autoHide.desc': 'Док уезжает, когда он не нужен.',
  'dock.iconSize.label': 'Размер значков', 'dock.iconSize.kw': 'большие маленькие масштаб',
  'general.autostart.label': 'Запускать при входе', 'general.autostart.desc': 'Запуск BetterWindows при входе в систему.',
  'spotlight.maxResults.label': 'Максимум результатов',
};
const tr = (d: Record<string, string>): Translate => (k) => d[k] ?? k;
const rows: RowEntry[] = [
  { sid: 'general.autostart', page: 'general' },
  { sid: 'dock.autoHide', page: 'dock' },
  { sid: 'dock.iconSize', page: 'dock' },
  { sid: 'spotlight.maxResults', page: 'spotlight' },
];
const panes = ['general', 'dock', 'spotlight'] as const;
const en = buildIndex(panes, rows, tr(EN), tr(EN));
const ru = buildIndex(panes, rows, tr(RU), tr(EN));

describe('normalize', () => {
  it('folds case, accents and ё/й', () => {
    expect(normalize('Ёлка ÉCOLE')).toBe('елка ecole');
    expect(normalize('Док')).toBe('док');
  });
});

describe('searchIndex', () => {
  it('returns nothing for an empty query', () => {
    expect(searchIndex(en, '')).toEqual([]);
    expect(searchIndex(en, '   ')).toEqual([]);
  });

  it('finds a pane by name and ranks it above its rows', () => {
    const r = searchIndex(en, 'dock');
    expect(r[0]).toMatchObject({ kind: 'pane', page: 'dock' });
    expect(r.map((x) => x.sid).filter(Boolean)).toContain('dock.autoHide');
  });

  it('finds rows by label, description and keywords', () => {
    expect(searchIndex(en, 'hide')[0]).toMatchObject({ sid: 'dock.autoHide' });
    expect(searchIndex(en, 'slides away')[0]).toMatchObject({ sid: 'dock.autoHide' });
    expect(searchIndex(en, 'scale')[0]).toMatchObject({ sid: 'dock.iconSize' });
    expect(searchIndex(en, 'launcher')[0]).toMatchObject({ kind: 'pane', page: 'dock' });
  });

  it('requires every word to match', () => {
    expect(searchIndex(en, 'icon size').map((e) => e.sid)).toEqual(['dock.iconSize']);
    expect(searchIndex(en, 'icon banana')).toEqual([]);
  });

  it('prefix beats substring', () => {
    const r = searchIndex(en, 'max');
    expect(r[0]).toMatchObject({ sid: 'spotlight.maxResults' });
  });

  it('is case-insensitive', () => {
    expect(searchIndex(en, 'OPEN AT')[0]).toMatchObject({ sid: 'general.autostart' });
  });

  it('works in Russian and still understands the English words', () => {
    expect(searchIndex(ru, 'размер')[0]).toMatchObject({ sid: 'dock.iconSize' });
    expect(searchIndex(ru, 'скрывать')[0]).toMatchObject({ sid: 'dock.autoHide' });
    expect(searchIndex(ru, 'док')[0]).toMatchObject({ kind: 'pane', page: 'dock' });
    expect(searchIndex(ru, 'dock')[0]).toMatchObject({ kind: 'pane', page: 'dock' });
    expect(searchIndex(ru, 'login')[0]).toMatchObject({ sid: 'general.autostart' });
  });

  it('respects the limit and keeps catalogue order on ties', () => {
    const r = searchIndex(en, 'o', 2);
    expect(r).toHaveLength(2);
  });

  it('rows carry the pane name as subtitle', () => {
    const e = searchIndex(ru, 'размер')[0];
    expect(e.subtitle).toBe('Док');
  });
});
