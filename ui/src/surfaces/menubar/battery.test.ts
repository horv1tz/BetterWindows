import { describe, expect, it } from 'vitest';
import type { BatteryState } from '$lib/protocol/generated';
import { batteryVisual } from './battery';

const b = (o: Partial<BatteryState>): BatteryState => ({ present: true, percent: 50, charging: false, acOnline: false, ...o });

describe('batteryVisual', () => {
  it('is hidden when there is no battery or no state yet', () => {
    expect(batteryVisual(b({ present: false })).visible).toBe(false);
    expect(batteryVisual(null).visible).toBe(false);
    expect(batteryVisual(undefined).visible).toBe(false);
  });

  it('fill follows the percentage', () => {
    expect(batteryVisual(b({ percent: 76 })).fill).toBeCloseTo(0.76);
    expect(batteryVisual(b({ percent: 100 })).fill).toBe(1);
    expect(batteryVisual(b({ percent: 0 })).fill).toBe(0);
  });

  it('a nearly empty battery still shows a sliver', () => {
    expect(batteryVisual(b({ percent: 2 })).fill).toBeGreaterThan(0.05);
  });

  it('clamps and rounds out-of-range or fractional values', () => {
    expect(batteryVisual(b({ percent: 140 })).percent).toBe(100);
    expect(batteryVisual(b({ percent: -3 })).percent).toBe(0);
    expect(batteryVisual(b({ percent: 41.6 })).percent).toBe(42);
    expect(batteryVisual(b({ percent: Number.NaN })).percent).toBe(0);
  });

  it('tone: normal, low (<=20), critical (<=10) while discharging', () => {
    expect(batteryVisual(b({ percent: 21 })).tone).toBe('normal');
    expect(batteryVisual(b({ percent: 20 })).tone).toBe('low');
    expect(batteryVisual(b({ percent: 11 })).tone).toBe('low');
    expect(batteryVisual(b({ percent: 10 })).tone).toBe('critical');
    expect(batteryVisual(b({ percent: 1 })).tone).toBe('critical');
  });

  it('charging shows the bolt and the charging tone even when low', () => {
    const v = batteryVisual(b({ percent: 5, charging: true, acOnline: true }));
    expect(v).toMatchObject({ bolt: true, tone: 'charging', plugged: false });
  });

  it('mains connected but not charging (full) shows the plug, not the bolt, and is not alarming', () => {
    const v = batteryVisual(b({ percent: 100, charging: false, acOnline: true }));
    expect(v).toMatchObject({ bolt: false, plugged: true, tone: 'normal' });
    expect(batteryVisual(b({ percent: 8, acOnline: true })).tone).toBe('normal');
  });
});
