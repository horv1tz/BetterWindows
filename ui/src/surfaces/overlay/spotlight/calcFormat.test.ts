import { describe, expect, it } from 'vitest';
import type { SearchResult } from '$lib/protocol/generated';
import { calcView } from './calcFormat';

const r = (o: Partial<SearchResult>): SearchResult => ({ id: 'calc', provider: 'calc', title: '', score: 1, actions: [], ...o });

describe('calcView', () => {
  it('takes the answer from the title and the expression from the subtitle', () => {
    expect(calcView(r({ title: '84', subtitle: '12*(3+4)' }))).toEqual({ expression: '12*(3+4)', answer: '84' });
  });
  it('strips a leading "= " from the title', () => {
    expect(calcView(r({ title: '= 84', subtitle: '12*(3+4)' })).answer).toBe('84');
  });
  it('falls back to the inline preview text "expr = value"', () => {
    expect(calcView(r({ title: '', preview: { kind: 'text', text: '2+2 = 4' } }))).toEqual({ expression: '2+2', answer: '4' });
    expect(calcView(r({ title: '4', preview: { kind: 'text', text: '2+2 = 4' } }))).toEqual({ expression: '2+2', answer: '4' });
  });
  it('copes with a result that has neither subtitle nor preview', () => {
    expect(calcView(r({ title: '42' }))).toEqual({ expression: '', answer: '42' });
  });
  it('uses the last "=" of the preview so expressions containing "=" survive', () => {
    expect(calcView(r({ title: '', preview: { kind: 'text', text: 'x=1+1 = 2' } })).answer).toBe('2');
  });
});
