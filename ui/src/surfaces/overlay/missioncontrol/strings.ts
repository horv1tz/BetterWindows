import { createT, plural } from '$lib/i18n.svelte';

/** Strings of Mission Control and the desktop context menu (popovers/DesktopMenu.svelte uses the menu.* keys). */
export const t = createT({
  en: {
    'mc.label': 'Mission Control',
    'mc.desktops': 'Desktops',
    'mc.newDesktop': 'New desktop',
    'mc.removeDesktop': 'Remove {name}',
    'mc.current': 'Current desktop',
    'mc.count.one': '{n} window',
    'mc.count.other': '{n} windows',
    'mc.emptyDesktop': 'No windows on this desktop',
    'mc.empty': 'No open windows',
    'mc.loading': 'Loading windows',
    'mc.minimized': 'Minimized',
    'mc.close': 'Close {title}',
    'mc.window': '{title}, {app}',
    'mc.moveUnsupported': 'This Windows version does not allow moving windows between desktops',
    'mc.moveFailed': 'The window could not be moved',
    'mc.actionFailed': 'That did not work',
    'mc.moveTo': 'Move to {name}',
    'menu.label': 'Desktop',
    'menu.wallpaper': 'Change Wallpaper…',
    'menu.widgets': 'Edit Widgets…',
    'menu.settings': 'Desktop Settings…',
  },
  ru: {
    'mc.label': 'Обзор окон',
    'mc.desktops': 'Рабочие столы',
    'mc.newDesktop': 'Новый рабочий стол',
    'mc.removeDesktop': 'Удалить {name}',
    'mc.current': 'Текущий рабочий стол',
    'mc.count.one': '{n} окно',
    'mc.count.few': '{n} окна',
    'mc.count.many': '{n} окон',
    'mc.count.other': '{n} окна',
    'mc.emptyDesktop': 'На этом рабочем столе нет окон',
    'mc.empty': 'Нет открытых окон',
    'mc.loading': 'Загружаем окна',
    'mc.minimized': 'Свернуто',
    'mc.close': 'Закрыть «{title}»',
    'mc.window': '{title}, {app}',
    'mc.moveUnsupported': 'Эта версия Windows не позволяет перемещать окна между рабочими столами',
    'mc.moveFailed': 'Не удалось переместить окно',
    'mc.actionFailed': 'Не получилось',
    'mc.moveTo': 'Переместить на {name}',
    'menu.label': 'Рабочий стол',
    'menu.wallpaper': 'Сменить обои…',
    'menu.widgets': 'Изменить виджеты…',
    'menu.settings': 'Настройки рабочего стола…',
  },
});

/** "1 window" / "3 windows" with the correct Russian plural form. */
export function countLabel(n: number): string {
  return plural(n, {
    one: t('mc.count.one', { n }),
    few: t('mc.count.few', { n }),
    many: t('mc.count.many', { n }),
    other: t('mc.count.other', { n }),
  });
}
