import './styles/tokens.css';
import { mount } from 'svelte';
import { getBridge, parseRoute, setInit } from './lib/ipc';
import { applyTheme } from './lib/theme';
import { i18n, initLocale } from './lib/i18n.svelte';

const surfaces = import.meta.glob('./surfaces/*/Surface.svelte');

async function bootstrap() {
  const target = document.getElementById('app')!;
  const route = parseRoute();

  if (route === 'playground') {
    const mod: any = await surfaces['./surfaces/playground/Surface.svelte']();
    mount(mod.default, { target });
    return;
  }

  const bridge = getBridge(route);
  const init = await bridge.call('surface.ready', { surface: route.surface, monitorId: route.monitorId });
  setInit(init);
  initLocale();
  applyTheme(init.theme);
  bridge.on('theme.changed', applyTheme);
  bridge.on('locale.changed', (l) => i18n.set(l.locale));

  const loader = surfaces[`./surfaces/${route.surface}/Surface.svelte`];
  if (!loader) throw new Error(`no surface component for '${route.surface}'`);
  const mod: any = await loader();
  mount(mod.default, { target });
}

bootstrap().catch((e) => {
  console.error('bootstrap failed', e);
  document.body.insertAdjacentHTML('beforeend', `<pre style="color:#f55;padding:8px;font:12px monospace">bootstrap failed: ${String(e)}</pre>`);
});
