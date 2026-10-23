import { describe, expect, it } from 'vitest';
import { fileGlyph } from './glyphs';

describe('fileGlyph', () => {
  it('maps extensions case-insensitively', () => {
    expect(fileGlyph('Vacation.JPG')).toBe('image');
    expect(fileGlyph('Budget.xlsx')).toBe('sheet');
    expect(fileGlyph('deck.pptx')).toBe('slides');
    expect(fileGlyph('main.ts')).toBe('code');
    expect(fileGlyph('notes.txt')).toBe('text');
  });
  it('treats names without an extension as folders and unknown extensions as plain files', () => {
    expect(fileGlyph('Downloads')).toBe('folder');
    expect(fileGlyph('trailing.')).toBe('folder');
    expect(fileGlyph('data.weird')).toBe('file');
  });
  it('uses the last dot', () => {
    expect(fileGlyph('archive.tar.gz')).toBe('archive');
  });
});
