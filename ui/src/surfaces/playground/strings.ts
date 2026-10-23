import { createT, type Dictionaries } from '$lib/i18n.svelte';

/** Strings of the playground chrome (control strip, faux windows). */
export const dictionaries: Dictionaries = {
  en: {
    'pg.strip': 'Playground controls',
    'pg.theme': 'Theme',
    'pg.theme.light': 'Switch to light theme',
    'pg.theme.dark': 'Switch to dark theme',
    'pg.language': 'Language',
    'pg.language.en': 'English',
    'pg.language.ru': 'Russian',
    'pg.reset': 'Reset the playground',
    'pg.settings': 'Open settings window',
    'pg.monitors': 'Monitors',
    'pg.monitors.one': 'Single monitor',
    'pg.monitors.two': 'Two monitors',
    'pg.settingsTitle': 'Settings',
    'pg.close': 'Close',
    'pg.win.notepad': 'Untitled — Notepad',
    'pg.win.browser': 'Weather forecast — Microsoft Edge',
    'pg.win.terminal': 'Terminal',
    'pg.browser.address': 'weather.example.com/forecast',
    'pg.shortcuts': 'Alt+Space Spotlight · Ctrl+Alt+L Launchpad · Ctrl+Alt+M Mission Control · Esc closes',
  },
  ru: {
    'pg.strip': 'Управление песочницей',
    'pg.theme': 'Тема',
    'pg.theme.light': 'Включить светлую тему',
    'pg.theme.dark': 'Включить тёмную тему',
    'pg.language': 'Язык',
    'pg.language.en': 'Английский',
    'pg.language.ru': 'Русский',
    'pg.reset': 'Сбросить песочницу',
    'pg.settings': 'Открыть окно настроек',
    'pg.monitors': 'Мониторы',
    'pg.monitors.one': 'Один монитор',
    'pg.monitors.two': 'Два монитора',
    'pg.settingsTitle': 'Настройки',
    'pg.close': 'Закрыть',
    'pg.win.notepad': 'Без имени — Блокнот',
    'pg.win.browser': 'Прогноз погоды — Microsoft Edge',
    'pg.win.terminal': 'Терминал',
    'pg.browser.address': 'weather.example.com/forecast',
    'pg.shortcuts': 'Alt+Пробел Spotlight · Ctrl+Alt+L Launchpad · Ctrl+Alt+M Mission Control · Esc закрывает',
  },
};

export const t = createT(dictionaries);
