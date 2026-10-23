import { getInit } from './ipc';

/**
 * Tiny reactive i18n. Each surface keeps its own dictionary (strings.ts) with at least 'en' and 'ru':
 *   import { createT } from '$lib/i18n.svelte';
 *   const t = createT({ en: { 'dock.quit': 'Quit' }, ru: { 'dock.quit': 'Выйти' } });
 *   {t('dock.quit')}   t('cal.week', { n: 3 })  // "{n}" placeholders
 * The locale is reactive: main.ts initialises it from SurfaceInit.locale and updates it on the host's 'locale.changed' event, so every
 * t() call inside a template/derived re-evaluates when the language setting changes. Unknown locales fall back to en, missing keys to en, then the key.
 */
export type Dict = Record<string, string>;
export type Dictionaries = { en: Dict } & Record<string, Dict>;

const state = $state({ locale: 'en' });

export const i18n = {
  get locale() { return state.locale; },
  set(locale: string) { state.locale = (locale || 'en').toLowerCase().split('-')[0]; },
};

/** Called by main.ts after surface.ready. */
export function initLocale(): void {
  try { i18n.set(getInit().locale); } catch { i18n.set('en'); }
}

export function currentLocale(): string { return state.locale; }

export function createT(dicts: Dictionaries) {
  return (key: string, params?: Record<string, string | number>): string => {
    const raw = dicts[state.locale]?.[key] ?? dicts.en[key] ?? key;
    return params ? raw.replace(/\{(\w+)\}/g, (_, k) => String(params[k] ?? `{${k}}`)) : raw;
  };
}

/** Plural by CLDR rules (Intl.PluralRules): plural(n, { one: 'файл', few: 'файла', many: 'файлов', other: 'файла' }). Falls back to 'other'. */
export function plural(n: number, forms: Partial<Record<Intl.LDMLPluralRule, string>> & { other: string }, locale: string = state.locale): string {
  const rule = new Intl.PluralRules(locale).select(Math.abs(n));
  return forms[rule] ?? forms.other;
}
