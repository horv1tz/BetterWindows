/** Keyboard navigation helpers for popover menus (pure, so they are unit tested without a DOM). */
export type NavKey = 'ArrowDown' | 'ArrowUp' | 'Home' | 'End';

/** Index of the item that should get focus next; -1 when the menu has no enabled items. Arrow keys wrap around. */
export function nextIndex(count: number, current: number, key: NavKey): number {
  if (count <= 0) return -1;
  switch (key) {
    case 'Home': return 0;
    case 'End': return count - 1;
    case 'ArrowDown': return current < 0 || current >= count ? 0 : (current + 1) % count;
    case 'ArrowUp': return current < 0 || current >= count ? count - 1 : (current - 1 + count) % count;
  }
}

/** Type-ahead: the next label (after `current`, wrapping) that starts with `query`, case-insensitively; -1 if none. */
export function typeahead(labels: readonly string[], current: number, query: string): number {
  const q = query.trim().toLocaleLowerCase();
  if (!q || labels.length === 0) return -1;
  for (let step = 1; step <= labels.length; step++) {
    const i = (Math.max(current, -1) + step + labels.length) % labels.length;
    if (labels[i].toLocaleLowerCase().startsWith(q)) return i;
  }
  return -1;
}
