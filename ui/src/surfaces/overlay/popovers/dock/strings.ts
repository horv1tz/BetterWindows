import { createT, currentLocale, plural } from '$lib/i18n.svelte';

export const t = createT({
  en: {
    'menu.label': 'Dock menu',
    'menu.windows': 'Windows',
    'menu.noWindows': 'No open windows',
    'menu.open': 'Open',
    'menu.options': 'Options',
    'menu.keep': 'Keep in Dock',
    'menu.remove': 'Remove from Dock',
    'menu.reveal': 'Show in File Explorer',
    'menu.newWindow': 'New Window',
    'menu.hide': 'Hide',
    'menu.quit': 'Quit',
    'menu.settings': 'Dock Settings…',
    'menu.close': 'Close window',
    'menu.minimized': 'Minimized',
    'menu.active': 'Active window',
    'menu.elevated': 'Running as administrator: cannot be controlled',
    'menu.running': 'Running',
    'menu.notRunning': 'Not running',
    'menu.folder': 'Folder',
    'menu.launching': 'Launching…',
    'wl.label': 'Windows of {app}',
    'wl.untitled': 'Untitled window',
  },
  ru: {
    'menu.label': 'Меню дока',
    'menu.windows': 'Окна',
    'menu.noWindows': 'Нет открытых окон',
    'menu.open': 'Открыть',
    'menu.options': 'Параметры',
    'menu.keep': 'Закрепить в доке',
    'menu.remove': 'Убрать из дока',
    'menu.reveal': 'Показать в Проводнике',
    'menu.newWindow': 'Новое окно',
    'menu.hide': 'Скрыть',
    'menu.quit': 'Завершить',
    'menu.settings': 'Настройки дока…',
    'menu.close': 'Закрыть окно',
    'menu.minimized': 'Свёрнуто',
    'menu.active': 'Активное окно',
    'menu.elevated': 'Запущено от имени администратора: управление невозможно',
    'menu.running': 'Запущено',
    'menu.notRunning': 'Не запущено',
    'menu.folder': 'Папка',
    'menu.launching': 'Запуск…',
    'wl.label': 'Окна «{app}»',
    'wl.untitled': 'Окно без названия',
  },
});

const forms: Record<string, Parameters<typeof plural>[1]> = {
  en: { one: 'window', other: 'windows' },
  ru: { one: 'окно', few: 'окна', many: 'окон', other: 'окна' },
};

/** "3 windows" / "3 окна" */
export function windowsCount(n: number): string {
  return `${n} ${plural(n, forms[currentLocale()] ?? forms.en)}`;
}
