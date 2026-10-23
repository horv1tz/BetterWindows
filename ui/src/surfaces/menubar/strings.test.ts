import { describe, expect, it } from 'vitest';
import { dicts as barDicts } from './strings';
import { dicts as systemDicts } from '../overlay/popovers/system/strings';

const placeholders = (s: string) => [...s.matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort();

describe.each([['menu bar', barDicts], ['system menu', systemDicts]])('%s strings', (_name, dicts) => {
  it('ru has exactly the keys of en', () => {
    expect(Object.keys(dicts.ru).sort()).toEqual(Object.keys(dicts.en).sort());
  });
  it('no string is empty and every translation keeps the placeholders of the English text', () => {
    for (const [key, en] of Object.entries(dicts.en)) {
      const ru = dicts.ru[key as keyof typeof dicts.ru] as string;
      expect(en.trim(), key).not.toBe('');
      expect(ru.trim(), key).not.toBe('');
      expect(placeholders(ru), key).toEqual(placeholders(en));
    }
  });
  it('the Russian text is actually translated (contains Cyrillic) except for brand names', () => {
    // product / technical names that are the same word in Russian
    const brandOnly = new Set(['apple.store', 'bar.network.ethernet', 'bar.network.ethernetUnnamed']);
    for (const [key, ru] of Object.entries(dicts.ru)) {
      if (brandOnly.has(key)) continue;
      if (ru.replace(/\{\w+\}/g, '').replace(/[\s\p{P}]/gu, '') === '') continue; // pure pattern such as '{title} — {artist}'
      expect(/[А-Яа-яЁё]/.test(ru), key).toBe(true);
    }
  });
});
