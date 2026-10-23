import type { DockItem, WindowRef } from '$lib/protocol/generated';

/** What the dock context menu offers for an item. Pure (unit tested): the component only renders it. */
export type MenuAction = 'open' | 'pin' | 'unpin' | 'reveal' | 'newWindow' | 'hide' | 'quit' | 'settings';

export interface MenuEntry {
  action: MenuAction;
  /** i18n key (strings.ts). */
  label: string;
  enabled: boolean;
  danger?: boolean;
}

export interface MenuModel {
  windows: WindowRef[];
  /** "Open" for items that are not running (apps) and folders. */
  primary: MenuEntry[];
  /** The Options section: Keep in Dock / Remove from Dock, Show in File Explorer. */
  options: MenuEntry[];
  /** New Window, Hide, Quit (apps only). */
  app: MenuEntry[];
  footer: MenuEntry[];
  subtitle: 'running' | 'notRunning' | 'folder' | 'launching';
}

export function buildMenu(item: DockItem): MenuModel {
  const isApp = item.kind === 'app';
  const running = item.running && item.windows.length > 0;
  const visible = item.windows.some((w) => !w.minimized);
  const primary: MenuEntry[] = running && isApp ? [] : [{ action: 'open', label: 'menu.open', enabled: !item.launching }];
  const options: MenuEntry[] = [
    item.pinned ? { action: 'unpin', label: 'menu.remove', enabled: true } : { action: 'pin', label: 'menu.keep', enabled: isApp },
    { action: 'reveal', label: 'menu.reveal', enabled: true },
  ];
  const app: MenuEntry[] = isApp
    ? [
        { action: 'newWindow', label: 'menu.newWindow', enabled: true },
        { action: 'hide', label: 'menu.hide', enabled: running && visible },
        { action: 'quit', label: 'menu.quit', enabled: running, danger: true },
      ]
    : [];
  const subtitle = item.launching ? 'launching' : !isApp ? 'folder' : running ? 'running' : 'notRunning';
  return { windows: item.windows, primary, options, app, footer: [{ action: 'settings', label: 'menu.settings', enabled: true }], subtitle };
}

/** Every actionable entry in visual order (window rows are handled separately by the component). */
export function flatEntries(m: MenuModel): MenuEntry[] {
  return [...m.primary, ...m.options, ...m.app, ...m.footer];
}
