/**
 * Settings search: filters panes and individual settings (sidebar search field). Pure, unit-tested.
 * Matching is accent/case-insensitive (ё = е), every query word must match, and the English text is always searchable
 * so "dock" finds the Dock pane in the Russian UI too.
 */
import type { PageId, SidebarPageId } from './pages';

export type Translate = (key: string) => string;

export interface RowEntry { sid: string; page: SidebarPageId }

export interface IndexedEntry {
  kind: 'pane' | 'row';
  page: SidebarPageId;
  /** row id (kind 'row') */
  sid?: string;
  title: string;
  /** pane title for rows */
  subtitle: string;
  /** normalised strings, strongest first: [title (current), title (en), keywords, description] */
  fields: [string, string, string, string];
}

export const normalize = (s: string): string => s.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/ё/g, 'е').replace(/й/g, 'и');

const HAS = (t: Translate, key: string): string => { const v = t(key); return v === key ? '' : v; };

export function buildIndex(
  panes: readonly SidebarPageId[],
  rows: readonly RowEntry[],
  now: Translate,
  en: Translate,
): IndexedEntry[] {
  const out: IndexedEntry[] = [];
  for (const page of panes) {
    const title = now(`page.${page}`);
    out.push({
      kind: 'pane', page, title, subtitle: '',
      fields: [normalize(title), normalize(en(`page.${page}`)), normalize(`${HAS(now, `page.${page}.kw`)} ${HAS(en, `page.${page}.kw`)}`), ''],
    });
  }
  for (const r of rows) {
    const title = now(`${r.sid}.label`);
    out.push({
      kind: 'row', page: r.page, sid: r.sid, title, subtitle: now(`page.${r.page}`),
      fields: [
        normalize(title),
        normalize(en(`${r.sid}.label`)),
        normalize(`${HAS(now, `${r.sid}.kw`)} ${HAS(en, `${r.sid}.kw`)} ${now(`page.${r.page}`)} ${en(`page.${r.page}`)}`),
        normalize(`${HAS(now, `${r.sid}.desc`)} ${HAS(en, `${r.sid}.desc`)}`),
      ],
    });
  }
  return out;
}

const FIELD_WEIGHT = [6, 5, 3, 2] as const;

function scoreWord(entry: IndexedEntry, word: string): number {
  let best = 0;
  for (let i = 0; i < entry.fields.length; i++) {
    const f = entry.fields[i];
    if (!f) continue;
    let s = 0;
    if (f === word) s = FIELD_WEIGHT[i] * 3;
    else if (f.startsWith(word)) s = FIELD_WEIGHT[i] * 2.5;
    else if (f.split(/[\s,.;:/()-]+/).some((w) => w.startsWith(word))) s = FIELD_WEIGHT[i] * 2;
    else if (f.includes(word)) s = FIELD_WEIGHT[i];
    if (s > best) best = s;
  }
  return best;
}

/** Entries matching ALL words of the query, best first (panes before rows on ties, then catalogue order). Empty query -> []. */
export function searchIndex(index: readonly IndexedEntry[], query: string, limit = 40): IndexedEntry[] {
  const words = normalize(query).split(/\s+/).filter(Boolean);
  if (!words.length) return [];
  const scored: Array<{ e: IndexedEntry; score: number; order: number }> = [];
  index.forEach((e, order) => {
    let total = 0;
    for (const w of words) {
      const s = scoreWord(e, w);
      if (s === 0) return;
      total += s;
    }
    scored.push({ e, score: total + (e.kind === 'pane' ? 0.5 : 0), order });
  });
  scored.sort((a, b) => b.score - a.score || a.order - b.order);
  return scored.slice(0, limit).map((s) => s.e);
}

export const isPane = (e: IndexedEntry): boolean => e.kind === 'pane';
export type { PageId };
