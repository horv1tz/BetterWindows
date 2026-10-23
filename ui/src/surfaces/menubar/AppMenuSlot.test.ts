import { afterEach, describe, expect, it } from 'vitest';
import { mountComponent, type Mounted } from './testing/mount';
import SlotFixture from './testing/SlotFixture.svelte';

let mounted: Mounted;
afterEach(() => mounted?.unmount());

describe('AppMenuSlot', () => {
  it('renders nothing at all while features.globalMenu is off', () => {
    mounted = mountComponent(SlotFixture, { enabled: false, filled: true });
    expect(mounted.host.querySelector('[data-slot]')).toBeNull();
    expect(mounted.host.querySelector('.global-item')).toBeNull();
    expect(mounted.host.innerHTML.replace(/<!---->/g, '').trim()).toBe('');
  });

  it('renders nothing when enabled but nothing fills the slot (no empty container, no placeholders)', () => {
    mounted = mountComponent(SlotFixture, { enabled: true, filled: false });
    expect(mounted.host.querySelector('[data-slot]')).toBeNull();
  });

  it('hosts the supplied items once the feature is on', () => {
    mounted = mountComponent(SlotFixture, { enabled: true, filled: true });
    expect(mounted.host.querySelector('[data-slot="global-menu"] .global-item')).not.toBeNull();
  });
});
