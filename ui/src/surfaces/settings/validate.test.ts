import { describe, expect, it } from 'vitest';
import { baseName, clamp, clampStep, isValidAccent, normalizeHex, parseNumber, previewSearchUrl, rangeOf, validateLocalPath, validateSearchUrl } from './validate';

describe('validateSearchUrl', () => {
  it('accepts http(s) templates with one {query}', () => {
    expect(validateSearchUrl('https://duckduckgo.com/?q={query}')).toEqual({ ok: true, value: 'https://duckduckgo.com/?q={query}' });
    expect(validateSearchUrl('  http://localhost:8080/s/{query}/page ').ok).toBe(true);
    expect(validateSearchUrl('HTTPS://example.org/{query}').ok).toBe(true);
  });
  it('explains each failure', () => {
    expect(validateSearchUrl('')).toEqual({ ok: false, reason: 'empty' });
    expect(validateSearchUrl('   ')).toEqual({ ok: false, reason: 'empty' });
    expect(validateSearchUrl('ftp://x/{query}')).toEqual({ ok: false, reason: 'scheme' });
    expect(validateSearchUrl('javascript:alert({query})')).toEqual({ ok: false, reason: 'scheme' });
    expect(validateSearchUrl('example.com/?q={query}')).toEqual({ ok: false, reason: 'scheme' });
    expect(validateSearchUrl('https://example.com/?q=')).toEqual({ ok: false, reason: 'noPlaceholder' });
    expect(validateSearchUrl('https://example.com/?q={Query}')).toEqual({ ok: false, reason: 'noPlaceholder' });
    expect(validateSearchUrl('https://example.com/{query}/{query}')).toEqual({ ok: false, reason: 'multiPlaceholder' });
    expect(validateSearchUrl('https:///{query}')).toEqual({ ok: false, reason: 'invalidUrl' });
    expect(validateSearchUrl('https://' + 'a'.repeat(600) + '/{query}')).toEqual({ ok: false, reason: 'tooLong' });
  });
  it('previewSearchUrl substitutes an escaped query', () => {
    expect(previewSearchUrl('https://x/?q={query}', 'a b&c')).toBe('https://x/?q=a%20b%26c');
  });
});

describe('numbers', () => {
  it('clamp handles NaN and bounds', () => {
    expect(clamp(5, 1, 3)).toBe(3);
    expect(clamp(-5, 1, 3)).toBe(1);
    expect(clamp(NaN, 1, 3)).toBe(1);
    expect(clamp(Infinity, 1, 3)).toBe(1);
  });
  it('clampStep snaps from the minimum', () => {
    expect(clampStep(70, 32, 128, 4)).toBe(72);
    expect(clampStep(33, 32, 128, 4)).toBe(32);
    expect(clampStep(500, 32, 128, 4)).toBe(128);
    expect(clampStep(0.12, 0, 1, 0.05)).toBe(0.1);
  });
  it('parseNumber accepts decimal comma', () => {
    expect(parseNumber(' 12 ')).toBe(12);
    expect(parseNumber('1,5')).toBe(1.5);
    expect(parseNumber('')).toBeUndefined();
    expect(parseNumber('abc')).toBeUndefined();
  });
  it('rangeOf reads the generated limits', () => {
    expect(rangeOf('DockSettings.iconSize', { min: 0, max: 1 })).toEqual({ min: 32, max: 128 });
    expect(rangeOf('HotCornerSettings.dwellMs', { min: 0, max: 1 })).toEqual({ min: 50, max: 2000 });
    expect(rangeOf('Nope.nope', { min: 3, max: 4 })).toEqual({ min: 3, max: 4 });
  });
});

describe('validateLocalPath', () => {
  it('accepts absolute drive paths, also quoted', () => {
    expect(validateLocalPath('C:\\Users\\me\\a.png')).toEqual({ ok: true, value: 'C:\\Users\\me\\a.png' });
    expect(validateLocalPath('"D:/pics/a.jpg"')).toEqual({ ok: true, value: 'D:/pics/a.jpg' });
  });
  it('rejects the rest', () => {
    expect(validateLocalPath('')).toEqual({ ok: false, reason: 'empty' });
    expect(validateLocalPath('pics\\a.png')).toEqual({ ok: false, reason: 'notAbsolute' });
    expect(validateLocalPath('\\\\server\\share\\a.png')).toEqual({ ok: false, reason: 'unc' });
    expect(validateLocalPath('C:\\a<b>.png')).toEqual({ ok: false, reason: 'invalidChars' });
    expect(validateLocalPath('C:\\' + 'a'.repeat(5000))).toEqual({ ok: false, reason: 'tooLong' });
  });
  it('checks image extensions when asked', () => {
    const ext = ['png', 'jpg'];
    expect(validateLocalPath('C:\\a.PNG', { extensions: ext }).ok).toBe(true);
    expect(validateLocalPath('C:\\a.txt', { extensions: ext })).toEqual({ ok: false, reason: 'badExtension' });
    expect(validateLocalPath('C:\\a', { extensions: ext })).toEqual({ ok: false, reason: 'badExtension' });
  });
  it('baseName', () => {
    expect(baseName('C:\\a\\b.txt')).toBe('b.txt');
    expect(baseName('C:\\a\\')).toBe('a');
    expect(baseName('C:/x/y/z')).toBe('z');
  });
});

describe('accent', () => {
  it('matches the protocol pattern', () => {
    expect(isValidAccent('system')).toBe(true);
    expect(isValidAccent('#0a84ff')).toBe(true);
    expect(isValidAccent('#0A84FF80')).toBe(true);
    expect(isValidAccent('red')).toBe(false);
    expect(isValidAccent('#fff')).toBe(false);
    expect(isValidAccent('#0a84ff; x')).toBe(false);
  });
  it('normalizeHex', () => {
    expect(normalizeHex('#ABC')).toBe('#aabbcc');
    expect(normalizeHex('0A84FF')).toBe('#0a84ff');
    expect(normalizeHex('#0a84ff80')).toBe('#0a84ff');
    expect(normalizeHex('blue')).toBeNull();
  });
});
