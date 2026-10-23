import { describe, expect, it } from 'vitest';
import { ACCENTS, accentChoice, contrastOn } from './accents';
import { isValidAccent } from './validate';

describe('accents', () => {
  it('every preset is a valid settings value', () => {
    for (const a of ACCENTS) expect(isValidAccent(a.hex), a.id).toBe(true);
    expect(new Set(ACCENTS.map((a) => a.id)).size).toBe(ACCENTS.length);
  });
  it('accentChoice maps stored values to swatches', () => {
    expect(accentChoice('system')).toBe('system');
    expect(accentChoice('#0A84FF')).toBe('blue');
    expect(accentChoice('#ff453a80')).toBe('red'); // alpha is ignored
    expect(accentChoice('#123456')).toBe('custom');
  });
  it('contrastOn picks readable text', () => {
    expect(contrastOn('#0a84ff')).toBe('#ffffff');
    expect(contrastOn('#000000')).toBe('#ffffff');
    expect(contrastOn('#ffcc00')).toBe('#000000');
    expect(contrastOn('#ffffff')).toBe('#000000');
    expect(contrastOn('garbage')).toBe('#ffffff');
  });
});
