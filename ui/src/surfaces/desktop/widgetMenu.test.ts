import { describe, expect, it } from 'vitest';
import type { WidgetInstance } from '$lib/protocol/generated';
import { widgetMenu } from './widgetMenu';

const t = (k: string) => k;
const w = (type: WidgetInstance['type'], o: Partial<WidgetInstance> = {}): WidgetInstance => ({ id: 'w', type, size: 'medium', x: 0, y: 0, ...o });
const ids = (m: ReturnType<typeof widgetMenu>) => m.items.filter((i) => !i.separator).map((i) => i.id);

describe('widgetMenu', () => {
  it('every widget has the three sizes (current one checked) and Remove last', () => {
    const m = widgetMenu(w('notes', { size: 'large', config: { text: 'x' } }), t);
    expect(ids(m)).toEqual(['size.small', 'size.medium', 'size.large', 'remove']);
    expect(m.items.find((i) => i.id === 'size.large')!.checked).toBe(true);
    expect(m.items.find((i) => i.id === 'size.small')!.checked).toBe(false);
    expect(m.items.find((i) => i.id === 'remove')!.danger).toBe(true);
    expect(m.resolve('size.small')).toEqual({ kind: 'size', size: 'small' });
    expect(m.resolve('remove')).toEqual({ kind: 'remove' });
    expect(m.resolve('bogus')).toBeNull();
    expect(m.items.at(-2)!.separator).toBe(true);
  });
  it('the clock offers style and seconds and keeps the other config keys', () => {
    const m = widgetMenu(w('clock', { config: { style: 'analog', seconds: false, timezone: 'Europe/Berlin' } }), t);
    expect(ids(m).slice(0, 3)).toEqual(['style.analog', 'style.digital', 'seconds']);
    expect(m.items.find((i) => i.id === 'style.analog')!.checked).toBe(true);
    expect(m.resolve('style.digital')).toEqual({ kind: 'config', config: { style: 'digital', seconds: false, timezone: 'Europe/Berlin' } });
    expect(m.resolve('seconds')).toEqual({ kind: 'config', config: { style: 'analog', seconds: true, timezone: 'Europe/Berlin' } });
  });
  it('a clock without config defaults to analog without seconds', () => {
    const m = widgetMenu(w('clock'), t);
    expect(m.items.find((i) => i.id === 'style.analog')!.checked).toBe(true);
    expect(m.items.find((i) => i.id === 'seconds')!.checked).toBe(false);
  });
  it('calendar toggles week numbers', () => {
    const m = widgetMenu(w('calendar', { config: { weekNumbers: true } }), t);
    expect(m.items.find((i) => i.id === 'weekNumbers')!.checked).toBe(true);
    expect(m.resolve('weekNumbers')).toEqual({ kind: 'config', config: { weekNumbers: false } });
  });
  it('weather opens the settings page', () => {
    expect(widgetMenu(w('weather'), t).resolve('location')).toEqual({ kind: 'settings', page: 'widgets' });
  });
  it('tolerates a non-object config', () => {
    expect(() => widgetMenu(w('clock', { config: 'garbage' }), t)).not.toThrow();
    expect(() => widgetMenu(w('calendar', { config: [1, 2] }), t)).not.toThrow();
  });
});
