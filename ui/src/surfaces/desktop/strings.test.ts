import { describe, expect, it } from 'vitest';
import { dictionaries } from './strings';
import { dictionaries as playground } from '../playground/strings';
import { KNOWN_WEATHER_CODES } from './weatherLogic';

describe('strings', () => {
  for (const [name, d] of [['desktop', dictionaries], ['playground', playground]] as const) {
    it(`${name}: en and ru have exactly the same keys`, () => {
      expect(Object.keys(d.ru).sort()).toEqual(Object.keys(d.en).sort());
    });
    it(`${name}: no empty strings and the same placeholders in both languages`, () => {
      for (const k of Object.keys(d.en)) {
        expect(d.en[k].length, `en ${k}`).toBeGreaterThan(0);
        expect(d.ru[k].length, `ru ${k}`).toBeGreaterThan(0);
        const ph = (s: string) => (s.match(/\{\w+\}/g) ?? []).sort().join(',');
        expect(ph(d.ru[k]), k).toBe(ph(d.en[k]));
      }
    });
  }
  it('every WMO code of the weather table has a condition text', () => {
    for (const c of KNOWN_WEATHER_CODES) {
      expect(dictionaries.en[`wx.${c}`], `en wx.${c}`).toBeTruthy();
      expect(dictionaries.ru[`wx.${c}`], `ru wx.${c}`).toBeTruthy();
    }
    expect(dictionaries.en['wx.unknown']).toBeTruthy();
  });
});
