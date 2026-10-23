/** The pages of the settings window (ids are the contract with settings.navigate / settings.openWindow {page}). */
export const PAGE_IDS = ['general', 'appearance', 'dock', 'menubar', 'spotlight', 'hotCorners', 'desktop', 'widgets', 'shortcuts', 'about'] as const;
export type SidebarPageId = (typeof PAGE_IDS)[number];
export type PageId = SidebarPageId | 'onboarding';

export const isPageId = (v: unknown): v is PageId => typeof v === 'string' && (v === 'onboarding' || (PAGE_IDS as readonly string[]).includes(v));

/** Sidebar groups (macOS-style blocks separated by a gap). */
export const PAGE_GROUPS: ReadonlyArray<readonly SidebarPageId[]> = [
  ['general', 'appearance'],
  ['dock', 'menubar', 'spotlight', 'hotCorners'],
  ['desktop', 'widgets'],
  ['shortcuts', 'about'],
];

/** Icon tile gradient per page [from, to]. Original colours, not tied to any platform's artwork. */
export const PAGE_COLORS: Record<SidebarPageId, readonly [string, string]> = {
  general: ['#9ba3b0', '#667085'],
  appearance: ['#b07cf5', '#6e56e0'],
  dock: ['#4a5060', '#232733'],
  menubar: ['#4cc3f0', '#1e86e8'],
  spotlight: ['#ffb341', '#f2701d'],
  hotCorners: ['#4fd77a', '#1fa653'],
  desktop: ['#56d4d8', '#1f8fcf'],
  widgets: ['#ff7a93', '#e8386a'],
  shortcuts: ['#ffd84a', '#f0a31a'],
  about: ['#7e8ca3', '#4d5a70'],
};

/**
 * Which page to show. An explicit page (route / settings.navigate) always wins; otherwise the first-run wizard until
 * settings.general.firstRunDone is true.
 */
export function resolvePage(requested: string | undefined | null, firstRunDone: boolean): PageId {
  if (isPageId(requested)) return requested;
  return firstRunDone ? 'general' : 'onboarding';
}
