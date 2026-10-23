import { createT, currentLocale, plural } from '$lib/i18n.svelte';

export const t = createT({
  en: {
    'dock.label': 'Dock',
    'dock.remove': 'Remove',
    'dock.launching': 'Launching…',
    'dock.running': 'running',
    'dock.notRunning': 'not running',
    'dock.minimized': 'minimized',
    'dock.attention': 'needs attention',
    'dock.folder': 'folder',
    'dock.active': 'active',
  },
  ru: {
    'dock.label': 'Док',
    'dock.remove': 'Убрать',
    'dock.launching': 'Запуск…',
    'dock.running': 'запущено',
    'dock.notRunning': 'не запущено',
    'dock.minimized': 'свёрнуто',
    'dock.attention': 'требует внимания',
    'dock.folder': 'папка',
    'dock.active': 'активно',
  },
});

const windowForms: Record<string, Parameters<typeof plural>[1]> = {
  en: { one: 'window', other: 'windows' },
  ru: { one: 'окно', few: 'окна', many: 'окон', other: 'окна' },
};

/** "3 windows" / "3 окна" */
export function windowsCount(n: number): string {
  return `${n} ${plural(n, windowForms[currentLocale()] ?? windowForms.en)}`;
}
