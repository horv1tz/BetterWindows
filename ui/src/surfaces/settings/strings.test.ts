import { describe, expect, it } from 'vitest';
import { dicts } from './strings';
import { ROWS } from './rows';

/** Source text of every settings .svelte/.ts file (tests and the dictionary itself excluded). */
const FILES = import.meta.glob(['./**/*.svelte', './**/*.ts', '!./**/*.test.ts', '!./strings.ts'], { query: '?raw', import: 'default', eager: true }) as Record<string, string>;

describe('settings strings', () => {
  it('ru has exactly the keys of en', () => {
    const en = new Set(Object.keys(dicts.en));
    const ru = new Set(Object.keys(dicts.ru));
    expect([...en].filter((k) => !ru.has(k))).toEqual([]);
    expect([...ru].filter((k) => !en.has(k))).toEqual([]);
  });

  it("every literal t('key') used in the window exists in en", () => {
    const missing = new Set<string>();
    for (const [file, text] of Object.entries(FILES)) {
      for (const m of text.matchAll(/\bt\(\s*'([a-zA-Z0-9.]+)'/g)) {
        if (!(m[1] in dicts.en)) missing.add(`${file}: ${m[1]}`);
      }
    }
    expect([...missing]).toEqual([]);
  });

  it('every search row has a label in en and ru', () => {
    for (const r of ROWS) {
      expect(dicts.en[`${r.sid}.label`], r.sid).toBeTruthy();
      expect(dicts.ru[`${r.sid}.label`], r.sid).toBeTruthy();
    }
  });

  it('every literal Row sid in the markup is a search row or has a label', () => {
    const rowSids = new Set(ROWS.map((r) => r.sid));
    const unknown = new Set<string>();
    for (const [file, text] of Object.entries(FILES)) {
      if (!file.endsWith('.svelte')) continue;
      for (const m of text.matchAll(/<Row\s+sid="([^"]+)"/g)) {
        if (!rowSids.has(m[1]) && !(`${m[1]}.label` in dicts.en)) unknown.add(`${file}: ${m[1]}`);
      }
    }
    expect([...unknown]).toEqual([]);
  });
});
