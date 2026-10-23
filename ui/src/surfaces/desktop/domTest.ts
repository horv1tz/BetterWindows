// Component-test helper. The shared vitest config resolves 'svelte' to its SERVER build (no resolve.conditions=['browser']), where mount()
// and onMount() are unavailable. Component tests therefore start with
//     vi.mock('svelte', async () => await import('../../../node_modules/svelte/src/index-client.js'));
// (hoisted by vitest) and mount through this helper, which uses the same client build. Once the shared config gets
// svelteTesting() (see the report's contractRequests) these two lines can go and @testing-library/svelte's render() can be used.
import { mount, unmount, flushSync, tick } from '../../../node_modules/svelte/src/index-client.js';
import { fireEvent, screen, within, waitFor } from '@testing-library/dom';
import type { Component } from 'svelte';

// jsdom lacks Element.getAnimations (used by svelte/animate flip in lists); no animations run under test.
if (typeof Element !== 'undefined' && !('getAnimations' in Element.prototype)) {
  (Element.prototype as unknown as { getAnimations: () => unknown[] }).getAnimations = () => [];
}

// jsdom lacks ResizeObserver (used by bind:offsetWidth and lib/shape.ts); nothing here depends on real layout.
if (!('ResizeObserver' in globalThis)) {
  (globalThis as unknown as { ResizeObserver: unknown }).ResizeObserver = class { observe() {} unobserve() {} disconnect() {} };
}

const mounted = new Set<{ target: HTMLElement; instance: Record<string, unknown> }>();

export function render<P extends Record<string, unknown>>(component: Component<P>, props: P) {
  const target = document.body.appendChild(document.createElement('div'));
  const instance = mount(component as never, { target, props }) as Record<string, unknown>;
  const entry = { target, instance };
  mounted.add(entry);
  flushSync();
  return { target, unmount: () => { unmount(instance); target.remove(); mounted.delete(entry); } };
}

/** Unmount everything the test rendered (call in afterEach). */
export function cleanup() {
  for (const m of [...mounted]) { try { unmount(m.instance); } catch { /* already gone */ } m.target.remove(); mounted.delete(m); }
  document.body.innerHTML = '';
}

/** fireEvent + flush Svelte's microtask-batched updates. */
export async function fire(el: Element | Window, event: Event): Promise<void> {
  await fireEvent(el, event);
  flushSync();
  await tick();
}

export { fireEvent, screen, within, waitFor, flushSync, tick };

type PointerInit = MouseEventInit & { pointerId?: number };

/** A pointer event: jsdom has no PointerEvent, so this is a MouseEvent of the pointer type carrying a pointerId. */
export function pe(type: string, init: PointerInit = {}): MouseEvent {
  const ev = new MouseEvent(type, { bubbles: true, cancelable: true, button: 0, ...init });
  Object.defineProperties(ev, { pointerId: { value: init.pointerId ?? 1 }, pointerType: { value: 'mouse' } });
  return ev;
}
export const ke = (type: string, key: string, init: KeyboardEventInit = {}) => new KeyboardEvent(type, { bubbles: true, cancelable: true, key, ...init });
export const me = (type: string, init: MouseEventInit = {}) => new MouseEvent(type, { bubbles: true, cancelable: true, ...init });
