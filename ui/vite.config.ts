import { defineConfig } from 'vite';
import { svelte } from '@sveltejs/vite-plugin-svelte';
import { fileURLToPath } from 'node:url';

const lib = fileURLToPath(new URL('./src/lib', import.meta.url));
const mock = fileURLToPath(new URL('./src/mock', import.meta.url));

export default defineConfig({
  plugins: [svelte()],
  resolve: { alias: { $lib: lib, $mock: mock } },
  // The Host serves the built bundle from a virtual host (https://app.local/), so keep asset URLs relative.
  base: './',
  build: {
    outDir: '../src/BetterWindows.Host/wwwroot',
    emptyOutDir: true,
    target: 'chrome120', // WebView2 evergreen
    sourcemap: true,
  },
  server: { port: 5173, strictPort: false },
  test: {
    environment: 'jsdom',
    include: ['src/**/*.test.ts'],
    globals: false,
    alias: { $lib: lib, $mock: mock },
  },
});
