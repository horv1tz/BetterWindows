import { describe, expect, it } from 'vitest';
import { hopAmplitude, hopDirection, hopPlan } from './hop';

const lift = (t: unknown) => Math.abs(parseFloat(String(t).replace('translate3d(', '').split(',').find((v, i) => i < 2 && parseFloat(v) !== 0) ?? '0'));

describe('hopPlan', () => {
  it('launch is three decaying hops that start and end at rest, played once', () => {
    const p = hopPlan('launch', 'bottom', 20);
    expect(p.options.iterations).toBe(1);
    const k = p.keyframes;
    expect(k[0].offset).toBe(0);
    expect(k.at(-1)!.offset).toBe(1);
    expect(k[0].transform).toBe('translate3d(0px, 0px, 0)');
    expect(k.at(-1)!.transform).toBe('translate3d(0px, 0px, 0)');
    const peaks = k.map((f) => lift(f.transform)).filter((v) => v > 0);
    expect(peaks).toEqual([20, 12.4, 6]);
  });
  it('offsets are strictly increasing', () => {
    for (const kind of ['launch', 'launching', 'attention'] as const) {
      const offs = hopPlan(kind, 'bottom', 18).keyframes.map((f) => f.offset as number);
      for (let i = 1; i < offs.length; i++) expect(offs[i]).toBeGreaterThan(offs[i - 1]);
    }
  });
  it('launching and attention loop forever with a calm tail', () => {
    for (const kind of ['launching', 'attention'] as const) {
      const p = hopPlan(kind, 'bottom', 18);
      expect(p.options.iterations).toBe(Infinity);
      const beforeLast = p.keyframes.at(-2)!;
      expect(beforeLast.offset as number).toBeLessThan(0.7);
    }
  });
  it('hops away from the docked edge', () => {
    expect(hopDirection('bottom')).toEqual({ x: 0, y: -1 });
    expect(hopDirection('left')).toEqual({ x: 1, y: 0 });
    expect(hopDirection('right')).toEqual({ x: -1, y: 0 });
    expect(hopPlan('launch', 'left', 10).keyframes[1].transform).toBe('translate3d(10px, 0px, 0)');
    expect(hopPlan('launch', 'right', 10).keyframes[1].transform).toBe('translate3d(-10px, 0px, 0)');
  });
  it('amplitude follows the icon size', () => {
    expect(hopAmplitude(56)).toBe(19);
    expect(hopAmplitude(32)).toBeLessThan(hopAmplitude(64));
  });
});
