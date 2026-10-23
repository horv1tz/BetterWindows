import { describe, expect, it } from 'vitest';
import { PAGE_GROUPS, PAGE_IDS, isPageId, resolvePage } from './pages';

describe('settings pages', () => {
  it('sidebar lists every page id exactly once', () => {
    const flat = PAGE_GROUPS.flat();
    expect(flat.sort()).toEqual([...PAGE_IDS].sort());
  });

  it('an explicit page always wins over the first-run state', () => {
    expect(resolvePage('dock', false)).toBe('dock');
    expect(resolvePage('onboarding', true)).toBe('onboarding');
  });

  it('without a page: wizard until the first run is done, then General', () => {
    expect(resolvePage(undefined, false)).toBe('onboarding');
    expect(resolvePage(null, true)).toBe('general');
    expect(resolvePage('nope', true)).toBe('general');
  });

  it('isPageId accepts only the contract ids', () => {
    expect(isPageId('hotCorners')).toBe(true);
    expect(isPageId('hotcorners')).toBe(false);
    expect(isPageId(3)).toBe(false);
  });
});
