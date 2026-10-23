// Test-only: vitest resolves `svelte` to its SERVER build here (vite.config.ts has no `browser` resolve condition), where mount()/onMount()
// are unavailable. Component tests mock `svelte` with this re-export of the client build:  vi.mock('svelte', () => import('./svelteClient'))
// (contract request: add resolve.conditions ['browser'] under vitest to vite.config.ts; then this indirection becomes unnecessary).
// @ts-expect-error -- plain JS entry without a declaration file
export * from '../../../../node_modules/svelte/src/index-client.js';
