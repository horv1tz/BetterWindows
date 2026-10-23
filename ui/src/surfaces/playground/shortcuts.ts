// Keyboard shortcuts the playground emulates (the real app registers them as global hotkeys: Alt+Space, Ctrl+Alt+L, Ctrl+Alt+M).
export type PlaygroundShortcut = 'spotlight' | 'launchpad' | 'missionControl' | 'escape';

interface KeyLike { key: string; code: string; altKey: boolean; ctrlKey: boolean; shiftKey: boolean; metaKey: boolean }

/** Uses `code` (physical key) so it works on any keyboard layout, and requires the exact modifier set. */
export function shortcutOf(e: KeyLike): PlaygroundShortcut | null {
  if (e.key === 'Escape' && !e.altKey && !e.ctrlKey && !e.shiftKey && !e.metaKey) return 'escape';
  if (e.shiftKey || e.metaKey) return null;
  if (e.altKey && !e.ctrlKey && e.code === 'Space') return 'spotlight';
  if (e.altKey && e.ctrlKey && e.code === 'KeyL') return 'launchpad';
  if (e.altKey && e.ctrlKey && e.code === 'KeyM') return 'missionControl';
  return null;
}

export type OverlayMode = 'spotlight' | 'launchpad' | 'missionControl' | 'popover';

/** Pressing a hotkey while its own overlay is open closes it; any other hotkey switches to that overlay. */
export function overlayAction(current: OverlayMode | null, wanted: Exclude<PlaygroundShortcut, 'escape'>): 'show' | 'hide' {
  return current === wanted ? 'hide' : 'show';
}
