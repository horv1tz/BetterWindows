/**
 * Component test helper for this stream.
 *
 * The shared vite.config.ts does not enable the 'browser' resolve condition, so under vitest the bare specifier `svelte` resolves to
 * Svelte's SERVER build, where mount() throws (and @testing-library/svelte, which imports it, cannot render). The compiled components
 * themselves use `svelte/internal/client` and are fine, so this helper loads the client entry (mount/unmount/flushSync/tick) by path.
 * (Components in this stream therefore avoid `onMount` - the server build makes it a no-op - and use `$effect` instead.)
 */
const client: any = await import('../../../../node_modules/svelte/src/index-client.js');

export const flushSync: () => void = client.flushSync;
export const tick: () => Promise<void> = client.tick;

export interface Mounted {
  host: HTMLElement;
  unmount: () => void;
}

/** Mounts `component` into a fresh element attached to document.body. */
export function mountComponent(component: unknown, props: Record<string, unknown> = {}): Mounted {
  const host = document.createElement('div');
  document.body.appendChild(host);
  const instance = client.mount(component, { target: host, props });
  flushSync();
  return {
    host,
    unmount: () => {
      client.unmount(instance);
      host.remove();
    },
  };
}
