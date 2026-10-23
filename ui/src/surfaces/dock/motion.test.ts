import { describe, expect, it } from 'vitest';
import { layoutStrip, magnifySpec, metricsFor, type SlotSpec } from './layout';
import { DockMotion } from './motion';

function setup(n = 11, magnify = true, reduce = false) {
  const m = metricsFor(56);
  const specs: SlotSpec[] = Array.from({ length: n }, (_, i) => ({ key: `k${i}`, kind: 'item' }));
  const strip = layoutStrip(specs, m, 'bottom');
  const mag = magnifySpec(m, magnify, 56, 88);
  const motion = new DockMotion();
  motion.configure(strip.slots, strip.length, m, mag, reduce);
  return { m, strip, mag, motion };
}
const run = (motion: DockMotion, seconds: number) => { for (let t = 0; t < seconds; t += 1 / 60) motion.step(1 / 60); };

describe('DockMotion', () => {
  it('is at rest without a pointer', () => {
    const { motion, strip, m } = setup();
    const f = motion.frame();
    expect(f.peak).toBe(1);
    expect(f.hover).toBe(-1);
    expect(f.grow).toBe(0);
    f.slots.forEach((s) => { expect(s.offset).toBeCloseTo(0, 9); expect(s.scale).toBeCloseTo(1, 9); });
    expect(f.pillStart).toBeCloseTo(-m.pad, 9);
    expect(f.pillEnd).toBeCloseTo(strip.length + m.pad, 9);
  });

  it('magnifies the slot under the pointer, springs up smoothly and reaches the magnified size', () => {
    const { motion, strip, m } = setup();
    const c = strip.slots[5].start + 28;
    motion.setPointer(c);
    motion.step(1 / 60);
    const early = motion.frame().peak;
    expect(early).toBeGreaterThan(1);
    expect(early).toBeLessThan(1.3); // not a jump
    run(motion, 1);
    const f = motion.frame();
    expect(f.hover).toBe(5);
    expect(f.slots[5].scale * m.size).toBeCloseTo(88, 0);
    expect(f.slots[5].scale).toBeGreaterThan(f.slots[4].scale);
    expect(f.slots[4].scale).toBeGreaterThan(f.slots[2].scale);
    expect(f.grow).toBeCloseTo(32, 0);
  });

  it('spreads the neighbours symmetrically around a centred pointer and grows the pill on both ends', () => {
    const { motion, strip, m } = setup();
    motion.setPointer(strip.slots[5].start + 28);
    run(motion, 1);
    const f = motion.frame();
    expect(f.slots[4].offset).toBeLessThan(0);
    expect(f.slots[6].offset).toBeGreaterThan(0);
    expect(f.slots[4].offset).toBeCloseTo(-f.slots[6].offset, 1);
    expect(f.pillStart).toBeLessThan(-m.pad);
    expect(f.pillEnd).toBeGreaterThan(strip.length + m.pad);
  });

  it('returns to rest after the pointer leaves', () => {
    const { motion } = setup();
    motion.setPointer(200);
    run(motion, 0.6);
    motion.setPointer(null);
    run(motion, 1.5);
    const f = motion.frame();
    expect(f.peak).toBeCloseTo(1, 2);
    expect(motion.step(1 / 60)).toBe(false);
  });

  it('does not slide in from a stale anchor when the pointer re-enters elsewhere', () => {
    const { motion, strip } = setup();
    motion.setPointer(40);
    run(motion, 0.5);
    motion.setPointer(null);
    run(motion, 1);
    motion.setPointer(strip.length - 40);
    motion.step(1 / 60);
    expect(motion.frame().hover).toBe(strip.slots.length - 1);
  });

  it('does nothing when magnification is disabled, but still reports the hovered slot', () => {
    const { motion, strip } = setup(11, false);
    motion.setPointer(strip.slots[3].start + 10);
    run(motion, 0.5);
    const f = motion.frame();
    expect(f.peak).toBe(1);
    expect(f.hover).toBe(3);
  });

  it('reduce motion applies targets immediately and never animates', () => {
    const { motion, strip } = setup(11, true, true);
    motion.setPointer(strip.slots[3].start + 10);
    expect(motion.step(1 / 60)).toBe(false);
    expect(motion.frame().peak).toBe(1); // magnification is off under reduceMotion
    motion.setDrag(strip.slots.map((_, i) => (i === 2 ? 62 : 0)), 0);
    expect(motion.frame().slots[2].offset).toBeCloseTo(62, 9);
  });

  it('animates drag shifts (gap opening) and returns them to zero afterwards', () => {
    const { motion, strip } = setup();
    motion.setDrag(strip.slots.map((_, i) => (i >= 3 && i < 6 ? 62 : 0)), 0);
    expect(motion.step(1 / 60)).toBe(true);
    const mid = motion.frame().slots[4].offset;
    expect(mid).toBeGreaterThan(0);
    expect(mid).toBeLessThan(62);
    run(motion, 1.5);
    expect(motion.frame().slots[4].offset).toBeCloseTo(62, 0);
    motion.setDrag(null);
    run(motion, 1.5);
    expect(motion.frame().slots[4].offset).toBeCloseTo(0, 0);
    expect(motion.step(1 / 60)).toBe(false);
  });

  it('suppresses magnification while dragging and shortens the pill when a gap collapses', () => {
    const { motion, strip, m } = setup();
    motion.setPointer(strip.slots[5].start + 28);
    run(motion, 0.5);
    motion.setDrag(strip.slots.map(() => 0), -62);
    run(motion, 1.5);
    const f = motion.frame();
    expect(f.peak).toBeCloseTo(1, 2);
    expect(f.pillEnd).toBeCloseTo(strip.length - 62 + m.pad, 0);
  });

  it('bounce room grows the pill even without a pointer and resetShifts drops stale offsets', () => {
    const { motion, strip } = setup();
    motion.setBounce(20);
    run(motion, 1);
    expect(motion.frame().grow).toBeCloseTo(20, 0);
    motion.setDrag(strip.slots.map(() => 30));
    run(motion, 1);
    motion.resetShifts();
    motion.setDrag(null);
    expect(motion.frame().slots[0].offset).toBeCloseTo(0, 9);
  });

  it('resolves separators to no hover and survives reconfiguration with fewer slots', () => {
    const m = metricsFor(56);
    const specs: SlotSpec[] = [{ key: 'a', kind: 'item' }, { key: 's', kind: 'sep' }, { key: 'b', kind: 'item' }];
    const strip = layoutStrip(specs, m, 'bottom');
    const motion = new DockMotion();
    motion.configure(strip.slots, strip.length, m, magnifySpec(m, true, 56, 88), false);
    motion.setPointer(strip.slots[1].start + 2);
    motion.step(1 / 60);
    expect(motion.frame().hover).toBe(-1);
    motion.setDrag([5, 0, 0]);
    const two = layoutStrip(specs.slice(0, 1), m, 'bottom');
    motion.configure(two.slots, two.length, m, magnifySpec(m, true, 56, 88), false);
    expect(() => { motion.step(1 / 60); motion.frame(); }).not.toThrow();
  });
});
