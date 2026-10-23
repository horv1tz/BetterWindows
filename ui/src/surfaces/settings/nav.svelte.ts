import { isPageId, type PageId } from './pages';

/** Navigation state of the window: current page, sidebar search query and the row to flash after a search jump. */
export const nav = $state<{ page: PageId; query: string; highlight: string | null }>({ page: 'general', query: '', highlight: null });

let highlightTimer: ReturnType<typeof setTimeout> | null = null;

export function go(page: string | undefined | null, highlight?: string): void {
  if (!isPageId(page)) return;
  nav.page = page;
  nav.highlight = highlight ?? null;
  if (highlightTimer) clearTimeout(highlightTimer);
  if (highlight) highlightTimer = setTimeout(() => { nav.highlight = null; }, 2200);
}
