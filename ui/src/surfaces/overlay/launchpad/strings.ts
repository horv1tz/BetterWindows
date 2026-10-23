import { createT, currentLocale, plural } from '$lib/i18n.svelte';

export const t = createT({
  en: {
    'lp.dialog': 'All apps',
    'lp.search': 'Search',
    'lp.searchLabel': 'Search apps',
    'lp.clear': 'Clear search',
    'lp.empty': 'No apps found',
    'lp.empty.hint': 'Try a different name.',
    'lp.noApps': 'No apps to show',
    'lp.noApps.hint': 'Installed apps will appear here.',
    'lp.pages': 'Pages',
    'lp.page': 'Page {n} of {total}',
    'lp.folder': 'Folder',
    'lp.folderName': 'Folder name',
    'lp.openFolder': 'Folder {name}, {count}',
    'lp.remove': 'Remove {name} from Launchpad',
    'lp.done': 'Done',
    'lp.restore': 'Restore hidden apps ({n})',
    'lp.editHint': 'Drag to rearrange. Drop one app onto another to make a folder.',
  },
  ru: {
    'lp.dialog': 'Все приложения',
    'lp.search': 'Поиск',
    'lp.searchLabel': 'Поиск приложений',
    'lp.clear': 'Очистить поиск',
    'lp.empty': 'Приложения не найдены',
    'lp.empty.hint': 'Попробуйте другое название.',
    'lp.noApps': 'Нет приложений',
    'lp.noApps.hint': 'Установленные приложения появятся здесь.',
    'lp.pages': 'Страницы',
    'lp.page': 'Страница {n} из {total}',
    'lp.folder': 'Папка',
    'lp.folderName': 'Название папки',
    'lp.openFolder': 'Папка {name}, {count}',
    'lp.remove': 'Убрать «{name}» из Launchpad',
    'lp.done': 'Готово',
    'lp.restore': 'Вернуть скрытые приложения ({n})',
    'lp.editHint': 'Перетаскивайте значки. Бросьте приложение на другое, чтобы создать папку.',
  },
});

/** "3 apps" / "3 приложения" for the folder aria label. */
export function appsCount(n: number): string {
  if (currentLocale() === 'ru') return `${n} ${plural(n, { one: 'приложение', few: 'приложения', many: 'приложений', other: 'приложения' })}`;
  return `${n} ${plural(n, { one: 'app', other: 'apps' })}`;
}
