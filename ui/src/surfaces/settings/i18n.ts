import { createT, currentLocale, plural } from '$lib/i18n.svelte';
import { dicts } from './strings';

/** Reactive translator for the settings window (en + ru, follows lib/i18n.svelte). */
export const t = createT(dicts);
export { plural, currentLocale };

/** Does the English dictionary define this key? (optional strings such as row descriptions) */
export const has = (key: string): boolean => key in dicts.en;

/** Translate into a SPECIFIC locale regardless of the current one (settings search also matches English terms). */
export function tIn(locale: string, key: string, params?: Record<string, string | number>): string {
  const raw = dicts[locale]?.[key] ?? dicts.en[key] ?? key;
  return params ? raw.replace(/\{(\w+)\}/g, (_, k) => String(params[k] ?? `{${k}}`)) : raw;
}
