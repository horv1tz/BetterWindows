// Test-only helpers: a minimal render() on top of the (mocked, see svelteClient.ts) client build of svelte.
import { flushSync, mount, unmount } from 'svelte';
import type { Component } from 'svelte';

const mounted: Array<{ instance: Record<string, unknown>; target: HTMLElement }> = [];

export function render<P extends Record<string, any>>(component: Component<P>, props: P) {
  const target = document.body.appendChild(document.createElement('div'));
  const instance = mount(component, { target, props }) as Record<string, unknown>;
  flushSync();
  const entry = { instance, target };
  mounted.push(entry);
  return {
    target,
    unmount() {
      const i = mounted.indexOf(entry);
      if (i >= 0) mounted.splice(i, 1);
      unmount(instance);
      target.remove();
    },
  };
}

export function cleanup() {
  for (const { instance, target } of mounted.splice(0)) { try { unmount(instance); } catch { /* already gone */ } target.remove(); }
  document.body.innerHTML = '';
}
