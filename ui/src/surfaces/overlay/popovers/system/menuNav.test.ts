import { describe, expect, it } from 'vitest';
import { nextIndex, typeahead } from './menuNav';

describe('nextIndex', () => {
  it('moves down and up with wrap-around', () => {
    expect(nextIndex(4, 0, 'ArrowDown')).toBe(1);
    expect(nextIndex(4, 3, 'ArrowDown')).toBe(0);
    expect(nextIndex(4, 0, 'ArrowUp')).toBe(3);
    expect(nextIndex(4, 2, 'ArrowUp')).toBe(1);
  });
  it('starts at the first item going down and at the last going up when nothing is focused', () => {
    expect(nextIndex(4, -1, 'ArrowDown')).toBe(0);
    expect(nextIndex(4, -1, 'ArrowUp')).toBe(3);
  });
  it('Home / End', () => {
    expect(nextIndex(5, 2, 'Home')).toBe(0);
    expect(nextIndex(5, 2, 'End')).toBe(4);
  });
  it('an empty menu has nothing to focus', () => {
    expect(nextIndex(0, -1, 'ArrowDown')).toBe(-1);
  });
});

describe('typeahead', () => {
  const labels = ['About This PC', 'System Settings…', 'Sleep', 'Shut Down…', 'Lock Screen'];
  it('finds the next item starting with the typed text', () => {
    expect(typeahead(labels, -1, 's')).toBe(1);
    expect(typeahead(labels, 1, 's')).toBe(2);
    expect(typeahead(labels, 3, 's')).toBe(1);
  });
  it('is case-insensitive and supports longer prefixes', () => {
    expect(typeahead(labels, 0, 'SH')).toBe(3);
    expect(typeahead(labels, 0, 'sl')).toBe(2);
  });
  it('returns -1 for no match or an empty query', () => {
    expect(typeahead(labels, 0, 'zz')).toBe(-1);
    expect(typeahead(labels, 0, '  ')).toBe(-1);
    expect(typeahead([], 0, 'a')).toBe(-1);
  });
  it('works with Cyrillic labels', () => {
    expect(typeahead(['Об этом компьютере', 'Режим сна', 'Выключить…'], -1, 'вы')).toBe(2);
  });
});
