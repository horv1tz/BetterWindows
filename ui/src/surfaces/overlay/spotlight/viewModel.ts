import type { SearchAction, SearchProviderId, SearchResult } from '$lib/protocol/generated';

/** One selectable entry of the Spotlight list, in the order it is navigated (top to bottom). */
export interface Entry {
  result: SearchResult;
  /** Position in the flat navigation order (what Up/Down/Ctrl+n address). */
  index: number;
  /** True for the calculator answer card (rendered above the list, spans the whole width). */
  card: boolean;
}

export interface Section {
  /** Provider of the group, or 'recent' for the empty-query list. */
  key: SearchProviderId | 'recent';
  entries: Entry[];
}

export interface ListModel {
  /** Calculator answer card (first calc result), if any. */
  card: Entry | null;
  sections: Section[];
  /** All entries in navigation order: card first, then each section top to bottom. */
  flat: Entry[];
}

/**
 * Group the engine's ordered result list for display. The engine's order is authoritative: groups appear in the order of
 * their best (first) result and the order inside a group is preserved. `recent` shows everything under a single header.
 */
export function buildListModel(results: readonly SearchResult[], opts: { recent?: boolean } = {}): ListModel {
  const flat: Entry[] = [];
  let card: Entry | null = null;
  const sections: Section[] = [];
  const byKey = new Map<string, Section>();
  const rest: SearchResult[] = [];
  for (const r of results) {
    if (!opts.recent && r.provider === 'calc' && !card) {
      card = { result: r, index: 0, card: true };
      continue;
    }
    rest.push(r);
  }
  if (card) flat.push(card);
  for (const r of rest) {
    const key: Section['key'] = opts.recent ? 'recent' : r.provider;
    let sec = byKey.get(key);
    if (!sec) { sec = { key, entries: [] }; byKey.set(key, sec); sections.push(sec); }
    sec.entries.push({ result: r, index: -1, card: false });
  }
  for (const sec of sections) for (const e of sec.entries) { e.index = flat.length; flat.push(e); }
  return { card, sections, flat };
}

/** Actions with the primary one first (Enter), the others in the engine's order (Tab cycles through this list). */
export function orderedActions(r: SearchResult): SearchAction[] {
  const primary = r.actions.find((a) => a.primary) ?? r.actions[0];
  if (!primary) return [];
  return [primary, ...r.actions.filter((a) => a !== primary)];
}

/**
 * Commands that are dangerous ask for a second Enter. The host marks them in the subtitle ("Asks for confirmation" /
 * "Потребуется подтверждение") — contract has no flag, so detect the wording (en/ru) or a confirm-ish action id.
 */
export function needsConfirm(r: SearchResult): boolean {
  if (r.provider !== 'commands') return false;
  const hay = `${r.subtitle ?? ''}`.toLowerCase();
  if (/confirm|подтвержд/.test(hay)) return true;
  return r.actions.some((a) => /confirm/i.test(a.id));
}

/** Keep the selection on the same result when the list is replaced by a newer event; otherwise fall back to the top. */
export function reconcileSelection(prev: { id: string | null; moved: boolean }, flat: readonly Entry[]): number {
  if (!flat.length) return 0;
  if (prev.moved && prev.id) {
    const at = flat.findIndex((e) => e.result.id === prev.id);
    if (at >= 0) return at;
  }
  return 0;
}
