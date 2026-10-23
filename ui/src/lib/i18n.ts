import { getInit } from './ipc';

/**
 * Tiny i18n. Each surface keeps its own dictionary (strings.ts) with at least 'en' and 'ru':
 *   const t = createT({ en: { 'dock.quit': 'Quit' }, ru: { 'dock.quit': 'Выйти' } });
 *   t('dock.quit')   t('cal.week', { n: 3 })  // "{n}" placeholders
 * Locale = SurfaceInit.locale ('en' | 'ru' | anything: unknown falls back to en). Missing keys fall back to en, then to the key.
 */
export type Dict = Record<string, string>;
export type Dictionaries = { en: Dict } & Record<string, Dict>;

export function currentLocale(): string {
  try { return getInit().locale.toLowerCase().split('-')[0]; } catch { return 'en'; }
}

export function createT(dicts: Dictionaries, locale: () => string = currentLocale) {
  return (key: string, params?: Record<string, string | number>): string => {
    const raw = dicts[locale()]?.[key] ?? dicts.en[key] ?? key;
    return params ? raw.replace(/\{(\w+)\}/g, (_, k) => String(params[k] ?? `{${k}}`)) : raw;
  };
}

/** Plural helper for ru/en: plural(n, ['файл','файла','файлов'] | ['file','files']). */
export function plural(n: number, forms: [string, string] | [string, string, string], locale = currentLocale()): string {
  const abs = Math.abs(n);
  if (locale === 'ru' && forms.length === 3) {
    const m10 = abs % 10, m100 = abs % 100;
    if (m10 === 1 && m100 !== 11) return forms[0];
    if (m10 >= 2 && m10 <= 4 && (m100 < 10 || m100 >= 20)) return forms[1];
    return forms[2];
  }
  return abs === 1 ? forms[0] : forms[forms.length === 3 ? 1 : 1];
}
