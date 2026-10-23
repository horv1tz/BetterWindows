import { describe, expect, it } from 'vitest';
import { applyOverrides, dropOverride, pruneOverrides } from './optimistic';

interface W { id: string; x: number; y: number; config?: unknown }
const items: W[] = [{ id: 'a', x: 1, y: 2 }, { id: 'b', x: 5, y: 6, config: { text: 'hi' } }];

describe('optimistic overrides', () => {
  it('applies overrides over host values without mutating', () => {
    const r = applyOverrides(items, { a: { x: 100 } });
    expect(r[0]).toEqual({ id: 'a', x: 100, y: 2 });
    expect(r[1]).toBe(items[1]);
    expect(items[0].x).toBe(1);
  });
  it('ignores overrides of items that are gone', () => {
    expect(applyOverrides(items, { ghost: { x: 1 } })).toHaveLength(2);
  });
  it('prunes fields the host has caught up with, keeps the others', () => {
    const r = pruneOverrides<W>({ a: { x: 100, y: 2 }, b: { x: 5 } }, [{ id: 'a', x: 100, y: 7 }, items[1]]);
    expect(r).toEqual({ a: { y: 2 } });
  });
  it('compares structured values (config) by content', () => {
    const r = pruneOverrides<W>({ b: { config: { text: 'hi' } } }, items);
    expect(r).toEqual({});
    const keep = pruneOverrides<W>({ b: { config: { text: 'changed' } } }, items);
    expect(keep.b?.config).toEqual({ text: 'changed' });
  });
  it('drops overrides of items that disappeared', () => {
    expect(pruneOverrides<W>({ gone: { x: 1 } }, items)).toEqual({});
  });
  it('dropOverride removes everything or only some fields', () => {
    const o = { a: { x: 1, y: 2 }, b: { x: 3 } };
    expect(dropOverride(o, 'a')).toEqual({ b: { x: 3 } });
    expect(dropOverride<W>(o, 'a', ['x'])).toEqual({ a: { y: 2 }, b: { x: 3 } });
    expect(dropOverride<W>(o, 'b', ['x'])).toEqual({ a: { x: 1, y: 2 } });
    expect(dropOverride(o, 'zzz')).toBe(o);
  });
});
