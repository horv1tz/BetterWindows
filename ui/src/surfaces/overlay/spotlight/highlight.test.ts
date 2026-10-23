import { describe, expect, it } from 'vitest';
import { containsAllTokens, fold, fuzzyScore, highlightSegments, matchRanges, queryTokens } from './highlight';

const joined = (text: string, q: string) => highlightSegments(text, q).map((s) => (s.match ? `[${s.text}]` : s.text)).join('');

describe('fold', () => {
  it('lowercases and strips diacritics (Latin and Cyrillic)', () => {
    expect(fold('Café Ünïcode')).toBe('cafe unicode');
    expect(fold('Ёлка и Йога')).toBe('елка и иога');
  });
  it('keeps the string usable when a combining mark stands alone', () => {
    expect(fold('a\u0301b')).toBe('ab');
  });
});

describe('queryTokens', () => {
  it('splits on whitespace, folds, de-duplicates and puts long tokens first', () => {
    expect(queryTokens('  Vis  visual  VIS ')).toEqual(['visual', 'vis']);
    expect(queryTokens('')).toEqual([]);
  });
});

describe('highlightSegments', () => {
  it('emphasises a prefix match case-insensitively', () => {
    expect(joined('Visual Studio Code', 'vis')).toBe('[Vis]ual Studio Code');
  });
  it('prefers a word-start occurrence over an earlier inner one', () => {
    expect(joined('Decode Code', 'code')).toBe('Decode [Code]');
  });
  it('highlights every token', () => {
    expect(joined('Visual Studio Code', 'code vis')).toBe('[Vis]ual Studio [Code]');
  });
  it('is diacritics insensitive and maps back to the original characters', () => {
    expect(joined('Café au lait', 'cafe')).toBe('[Café] au lait');
    expect(joined('Ёлочные игрушки', 'елоч')).toBe('[Ёлоч]ные игрушки');
  });
  it('handles Cyrillic case-insensitively', () => {
    expect(joined('Заметки по встрече', 'ЗАМ')).toBe('[Зам]етки по встрече');
  });
  it('falls back to initials for abbreviations', () => {
    expect(joined('Visual Studio Code', 'vsc')).toBe('[V]isual [S]tudio [C]ode');
  });
  it('does not highlight when nothing matches', () => {
    expect(highlightSegments('Notepad', 'zzz')).toEqual([{ text: 'Notepad', match: false }]);
  });
  it('merges overlapping ranges', () => {
    expect(joined('Calculator', 'calc cul')).toBe('[Calcul]ator');
  });
  it('returns plain text for an empty query and nothing for empty text', () => {
    expect(highlightSegments('Hello', '')).toEqual([{ text: 'Hello', match: false }]);
    expect(highlightSegments('', 'x')).toEqual([]);
  });
  it('treats surrogate pairs as single characters', () => {
    expect(joined('😀 smile', 'smi')).toBe('😀 [smi]le');
  });
});

describe('matchRanges', () => {
  it('returns UTF-16 ranges into the original text', () => {
    expect(matchRanges('Café', 'fe')).toEqual([{ start: 2, end: 4 }]);
  });
});

describe('containsAllTokens / fuzzyScore', () => {
  it('requires every token', () => {
    expect(containsAllTokens('Visual Studio Code', 'studio code')).toBe(true);
    expect(containsAllTokens('Visual Studio Code', 'studio word')).toBe(false);
    expect(containsAllTokens('anything', '')).toBe(true);
  });
  it('scores prefix > word start > inner > initials and 0 for no match', () => {
    const pre = fuzzyScore('Notepad', 'note');
    const word = fuzzyScore('My Notepad', 'note');
    const inner = fuzzyScore('Keynote', 'note');
    const initials = fuzzyScore('Visual Studio Code', 'vsc');
    expect(pre).toBeGreaterThan(word);
    expect(word).toBeGreaterThan(inner);
    expect(inner).toBeGreaterThan(initials);
    expect(initials).toBeGreaterThan(0);
    expect(fuzzyScore('Notepad', 'xyz')).toBe(0);
  });
  it('matches Cyrillic', () => {
    expect(fuzzyScore('Калькулятор', 'каль')).toBeGreaterThan(0);
    expect(fuzzyScore('Ёж', 'еж')).toBeGreaterThan(0);
  });
});
