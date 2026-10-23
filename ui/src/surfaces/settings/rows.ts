/**
 * Every settings row that the sidebar search can jump to: `sid` is the stable row id (labels come from `<sid>.label` in strings.ts),
 * `page` the pane it lives on. Keep in sync with the `<Row sid="...">` usages (rows.test.ts checks that every sid has a label).
 */
import type { SidebarPageId } from './pages';

export interface RowDef { sid: string; page: SidebarPageId }

const r = (page: SidebarPageId, sids: string[]): RowDef[] => sids.map((sid) => ({ sid, page }));

export const ROWS: RowDef[] = [
  ...r('general', ['general.autostart', 'general.language', 'general.checkUpdates', 'taskbar.mode', 'general.taskbar.show', 'general.setup', 'general.launchpad', 'general.reset',
    'general.features.internalVirtualDesktops', 'general.features.trayHosting', 'general.features.windowChrome', 'general.features.globalMenu', 'general.features.liveThumbnails']),
  ...r('appearance', ['appearance.mode', 'appearance.accent', 'appearance.reduceMotion', 'appearance.reduceTransparency']),
  ...r('dock', ['dock.enabled', 'dock.position', 'dock.monitor', 'dock.iconSize', 'dock.magnification', 'dock.magnifiedSize', 'dock.autoHide', 'dock.reserveSpace', 'dock.showIndicators', 'dock.bounceOnLaunch']),
  ...r('menubar', ['menubar.enabled', 'menubar.translucent', 'menubar.autoHide', 'menubar.allMonitors', 'menubar.showDate', 'menubar.showSeconds', 'menubar.use24Hour', 'menubar.showBatteryPercent']),
  ...r('spotlight', ['spotlight.hotkey', 'spotlight.maxResults', 'spotlight.providers', 'spotlight.webEngine', 'spotlight.webSearchUrl']),
  ...r('hotCorners', ['hotCorners.enabled', 'hotCorners.dwellMs']),
  ...r('desktop', ['desktop.enabled', 'desktop.hideNativeIcons', 'desktop.showDesktopFolder', 'desktop.wallpaper', 'desktop.fit', 'desktop.iconSize', 'desktop.gridSnap']),
  ...r('widgets', ['widgets.weather']),
  ...r('shortcuts', ['shortcuts.spotlight', 'shortcuts.launchpad', 'shortcuts.missionControl', 'shortcuts.exit', 'shortcuts.toggleTaskbar']),
  ...r('about', ['about.quit', 'about.diag']),
];
