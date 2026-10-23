import { describe, expect, it } from 'vitest';
import { applyFrame, crossOf, mainOf, originOf, outsideOf, popoverAnchor, scaleOf, slotCenterClient, slotStyle, tooltipPlacement, translateOf, type SlotRefs } from './dom';
import { layoutStrip, metricsFor, pillThickness, magnifySpec, type SlotSpec } from './layout';
import { DockMotion } from './motion';

const m = metricsFor(56);
const specs: SlotSpec[] = [{ key: 'a', kind: 'item' }, { key: 's', kind: 'sep' }, { key: 'b', kind: 'item' }, { key: 't', kind: 'tile' }];

describe('slot styles', () => {
  it('anchor slots to the docked edge for every orientation', () => {
    const strip = layoutStrip(specs, m, 'bottom');
    expect(slotStyle('bottom', m, strip.slots[0])).toBe(`left:${m.pad}px;bottom:${m.pad}px;width:56px;height:56px`);
    const v = layoutStrip(specs, m, 'left');
    expect(slotStyle('left', m, v.slots[2])).toContain(`left:${m.pad}px`);
    expect(slotStyle('right', m, v.slots[2])).toContain(`right:${m.pad}px`);
    expect(slotStyle('left', m, v.slots[2])).toContain(`top:${m.pad + v.slots[2].start}px`);
  });
  it('centres separators and tiles on the icon row from the docked edge', () => {
    const strip = layoutStrip(specs, m, 'bottom');
    expect(slotStyle('bottom', m, strip.slots[1])).toContain(`bottom:${m.pad + (m.size - m.sepCross) / 2}px`);
    expect(slotStyle('bottom', m, strip.slots[3])).toContain(`bottom:${m.pad}px`);
  });
  it('transform helpers return empty strings at identity and the right axis otherwise', () => {
    expect(translateOf('bottom', 0)).toBe('');
    expect(translateOf('bottom', 12.34)).toBe('translate3d(12.35px,0,0)');
    expect(translateOf('left', -5)).toBe('translate3d(0,-5px,0)');
    expect(scaleOf('bottom', 'item', 1)).toBe('');
    expect(scaleOf('bottom', 'item', 1.5)).toBe('scale(1.5)');
    expect(scaleOf('bottom', 'sep', 1.5)).toBe('scaleY(1.5)');
    expect(scaleOf('right', 'sep', 1.5)).toBe('scaleX(1.5)');
    expect(originOf('bottom')).toBe('50% 100%');
    expect(originOf('left')).toBe('0% 50%');
    expect(originOf('right')).toBe('100% 50%');
  });
});

describe('pointer coordinates', () => {
  const stage = { left: 100, top: 800, right: 900, bottom: 872 };
  it('converts to strip coordinates and distance from the docked edge', () => {
    expect(mainOf('bottom', stage, m, 100 + m.pad + 30, 850)).toBe(30);
    expect(mainOf('left', stage, m, 0, 800 + m.pad + 12)).toBe(12);
    expect(crossOf('bottom', stage, 500, 860)).toBe(12);
    expect(crossOf('left', stage, 130, 0)).toBe(30);
    expect(crossOf('right', stage, 880, 0)).toBe(20);
  });
  it('outside is <= 0 over the rest pill and positive beyond its inner side', () => {
    const T = pillThickness(m);
    expect(outsideOf('bottom', stage, m, 500, 872 - T / 2)).toBeLessThan(0);
    expect(outsideOf('bottom', stage, m, 500, 872 - T - 25)).toBe(25);
  });
});

describe('applyFrame', () => {
  function build(pos: 'bottom' | 'left' = 'bottom') {
    const strip = layoutStrip(specs, m, pos);
    const motion = new DockMotion();
    motion.configure(strip.slots, strip.length, m, magnifySpec(m, true, 56, 88), false);
    const refs = new Map<string, SlotRefs>();
    for (const s of strip.slots) {
      const slot = document.createElement('div'); const mag = document.createElement('div'); slot.appendChild(mag);
      refs.set(s.key, { slot, mag });
    }
    return { strip, motion, refs, pill: document.createElement('div') };
  }
  it('leaves everything untransformed at rest and sizes the pill to the strip', () => {
    const { strip, motion, refs, pill } = build();
    applyFrame(motion.frame(), strip.slots, refs, pill, 'bottom', m, true);
    for (const r of refs.values()) { expect(r.slot.style.transform).toBe(''); expect(r.mag!.style.transform).toBe(''); }
    expect(pill.style.left).toBe('0px');
    expect(parseFloat(pill.style.width)).toBeCloseTo(strip.length + 2 * m.pad, 1);
    expect(pill.style.height).toBe(`${pillThickness(m)}px`);
  });
  it('writes translate + scale while magnified, and grows the pill only in the pill-grows mode', () => {
    for (const grows of [true, false]) {
      const { strip, motion, refs, pill } = build();
      motion.setPointer(strip.slots[0].start + 28);
      for (let i = 0; i < 90; i++) motion.step(1 / 60);
      applyFrame(motion.frame(), strip.slots, refs, pill, 'bottom', m, grows);
      expect(refs.get('a')!.mag!.style.transform).toMatch(/^scale\(1\.5/);
      expect(refs.get('b')!.slot.style.transform).toContain('translate3d');
      expect(parseFloat(pill.style.height)).toBeGreaterThan(pillThickness(m) - 0.001);
      if (grows) { expect(parseFloat(pill.style.height)).toBeGreaterThan(pillThickness(m) + 20); expect(parseFloat(pill.style.top)).toBeLessThan(-20); }
      else { expect(pill.style.height).toBe(`${pillThickness(m)}px`); expect(pill.style.top).toBe('0px'); }
    }
  });
  it('uses the vertical axis for left docks', () => {
    const { strip, motion, refs, pill } = build('left');
    motion.setPointer(strip.slots[0].start + 28);
    for (let i = 0; i < 90; i++) motion.step(1 / 60);
    applyFrame(motion.frame(), strip.slots, refs, pill, 'left', m, true);
    expect(refs.get('b')!.slot.style.transform).toMatch(/translate3d\(0,/);
    expect(pill.style.left).toBe('0px');
    expect(parseFloat(pill.style.width)).toBeGreaterThan(pillThickness(m) + 20);
  });
  it('ignores slots without refs', () => {
    const { strip, motion, pill } = build();
    expect(() => applyFrame(motion.frame(), strip.slots, new Map(), pill, 'bottom', m, true)).not.toThrow();
    expect(() => applyFrame(motion.frame(), strip.slots, new Map(), null, 'bottom', m, true)).not.toThrow();
  });
});

describe('tooltipPlacement', () => {
  const strip = layoutStrip([{ key: 'a', kind: 'item' }, { key: 'b', kind: 'item' }], m, 'bottom');
  const base = { pos: 'bottom' as const, m, slot: strip.slots[0], offset: 0, scale: 1, grow: 0, pillGrows: false, tipW: 100, tipH: 28, minMain: 0, maxMain: 400 };
  it('floats above the pill, centred on the icon (bottom dock)', () => {
    const p = tooltipPlacement(base);
    const T = pillThickness(m);
    expect(p.x).toBeCloseTo(m.pad + 28 - 50 < 0 ? 0 : m.pad + 28 - 50, 5);
    expect(p.y + 28 + 8).toBeCloseTo(0, 5); // bottom of the bubble 8px above the pill top (stage y = 0)
    expect(T).toBeGreaterThan(0);
  });
  it('rises with the magnified icon (overflow mode) or with the grown pill (pill-grows mode)', () => {
    const k = tooltipPlacement({ ...base, scale: 1.57 });
    expect(k.y).toBeLessThan(tooltipPlacement(base).y - 20);
    const g = tooltipPlacement({ ...base, pillGrows: true, grow: 32 });
    expect(g.y).toBeCloseTo(tooltipPlacement(base).y - 32, 5);
  });
  it('is clamped into the window', () => {
    expect(tooltipPlacement({ ...base, minMain: 20 }).x).toBe(20);
    expect(tooltipPlacement({ ...base, offset: 900, maxMain: 300 }).x).toBe(200);
  });
  it('sits beside the pill for left and right docks', () => {
    const v = layoutStrip([{ key: 'a', kind: 'item' }], m, 'left');
    const T = pillThickness(m);
    const left = tooltipPlacement({ ...base, pos: 'left', slot: v.slots[0], tipW: 90 });
    expect(left.x).toBe(T + 8);
    const right = tooltipPlacement({ ...base, pos: 'right', slot: v.slots[0], tipW: 90 });
    expect(right.x).toBe(-8 - 90);
    expect(left.y).toBeCloseTo(Math.max(0, m.pad + 28 - 14), 5);
  });
});

describe('slot centres and popover anchors', () => {
  const stage = { left: 100, top: 800, right: 900, bottom: 872 };
  const strip = layoutStrip([{ key: 'a', kind: 'item' }, { key: 'b', kind: 'item' }], m, 'bottom');
  it('computes the rest centre of a slot in client coordinates for every dock position', () => {
    const c = slotCenterClient('bottom', stage, m, strip.slots[1]);
    expect(c.x).toBe(100 + m.pad + strip.slots[1].start + 28);
    expect(c.y).toBe(872 - m.pad - 28);
    expect(slotCenterClient('bottom', stage, m, strip.slots[1], 10).x).toBe(c.x + 10);
    const v = layoutStrip([{ key: 'a', kind: 'item' }], m, 'left');
    expect(slotCenterClient('left', stage, m, v.slots[0])).toEqual({ x: 100 + m.pad + 28, y: 800 + m.pad + 28 });
    expect(slotCenterClient('right', stage, m, v.slots[0])).toEqual({ x: 900 - m.pad - 28, y: 800 + m.pad + 28 });
  });
  it('anchors popovers on the displaced icon along the main axis and on the whole pill across it', () => {
    const a = popoverAnchor('bottom', stage, m, strip.slots[0], 0, 1);
    expect(a).toEqual({ x: 100 + m.pad, y: 872 - pillThickness(m), width: 56, height: pillThickness(m) });
    const big = popoverAnchor('bottom', stage, m, strip.slots[0], 10, 1.5);
    expect(big.width).toBe(84);
    expect(big.x + big.width / 2).toBe(100 + m.pad + 28 + 10);
    const l = popoverAnchor('left', stage, m, layoutStrip([{ key: 'a', kind: 'item' }], m, 'left').slots[0], 0, 1);
    expect(l.x).toBe(100);
    expect(l.width).toBe(pillThickness(m));
    const r = popoverAnchor('right', stage, m, layoutStrip([{ key: 'a', kind: 'item' }], m, 'right').slots[0], 0, 1);
    expect(r.x).toBe(900 - pillThickness(m));
  });
});
