<script lang="ts">
  // The settings window: sidebar (search + panes) and the selected pane. The first-run wizard replaces the whole window.
  import './settings.css';
  import type { Component } from 'svelte';
  import { Keyboard, Info, LayoutGrid, Monitor, Palette, PanelBottom, PanelTop, Scan, Search, Settings, X } from '@lucide/svelte';
  import { getBridge, getInit, parseRoute } from '$lib/ipc';
  import { t, tIn } from './i18n';
  import { PAGE_COLORS, PAGE_GROUPS, resolvePage, type PageId, type SidebarPageId } from './pages';
  import { go, nav } from './nav.svelte';
  import { ROWS } from './rows';
  import { buildIndex, searchIndex, isPane } from './searchIndex';
  import { getStore } from './store.svelte';
  import Toasts from './components/Toasts.svelte';
  import General from './pages/General.svelte';
  import Appearance from './pages/Appearance.svelte';
  import Dock from './pages/Dock.svelte';
  import MenuBar from './pages/MenuBar.svelte';
  import Spotlight from './pages/Spotlight.svelte';
  import HotCorners from './pages/HotCorners.svelte';
  import Desktop from './pages/Desktop.svelte';
  import Widgets from './pages/Widgets.svelte';
  import Shortcuts from './pages/Shortcuts.svelte';
  import About from './pages/About.svelte';
  import Onboarding from './pages/Onboarding.svelte';

  const bridge = getBridge();
  const init = getInit();
  const store = getStore();

  // The initial page: the route (#/settings/<page>) wins, otherwise the wizard until the first run is done.
  const route = parseRoute();
  nav.page = resolvePage(typeof route === 'object' ? route.page : undefined, init.settings.general.firstRunDone);
  nav.query = '';
  nav.highlight = null;

  const off = bridge.on('settings.navigate', (p) => go(p.page));
  $effect(() => () => off());

  const ICONS: Record<SidebarPageId, Component<any>> = {
    general: Settings, appearance: Palette, dock: PanelBottom, menubar: PanelTop, spotlight: Search, hotCorners: Scan,
    desktop: Monitor, widgets: LayoutGrid, shortcuts: Keyboard, about: Info,
  };
  const PANES = $derived(PAGE_GROUPS.flat());
  const index = $derived(buildIndex(PANES, ROWS, (k) => t(k), (k) => tIn('en', k)));
  const results = $derived(nav.query.trim() ? searchIndex(index, nav.query) : []);

  $effect(() => { document.title = t(`page.${nav.page}`); });

  let main: HTMLElement | undefined = $state();
  let lastPage: PageId | undefined;
  $effect(() => {
    const p = nav.page;
    if (p !== lastPage) { lastPage = p; if (main) main.scrollTop = 0; }
  });

  function pick(page: PageId, sid?: string) {
    nav.query = '';
    go(page, sid);
  }

  // Roving focus in the sidebar: arrows move between panes and select them (like a list box).
  function onSidebarKey(e: KeyboardEvent) {
    if (e.key !== 'ArrowDown' && e.key !== 'ArrowUp' && e.key !== 'Home' && e.key !== 'End') return;
    const list = (e.currentTarget as HTMLElement).closest('nav');
    const items = list ? [...list.querySelectorAll<HTMLButtonElement>('button[data-page]')] : [];
    const at = items.indexOf(document.activeElement as HTMLButtonElement);
    if (at < 0) return;
    e.preventDefault();
    const next = e.key === 'Home' ? 0 : e.key === 'End' ? items.length - 1 : (at + (e.key === 'ArrowDown' ? 1 : -1) + items.length) % items.length;
    const page = items[next].dataset.page as SidebarPageId;
    go(page);
    items[next].focus();
  }
</script>

{#if nav.page === 'onboarding'}
  <div class="bw onboarding-root">
    <Onboarding />
    <Toasts />
  </div>
{:else}
  <div class="bw shell">
    <aside class="side" aria-label={t('sidebar.label')}>
      <div class="search" role="search">
        <Search size={13} aria-hidden="true" />
        <input
          type="search"
          placeholder={t('search.placeholder')}
          aria-label={t('search.placeholder')}
          bind:value={nav.query}
          onkeydown={(e) => { if (e.key === 'Escape' && nav.query) { e.preventDefault(); nav.query = ''; } }}
        />
        {#if nav.query}
          <button type="button" class="clear" aria-label={t('search.clear')} onclick={() => (nav.query = '')}><X size={12} /></button>
        {/if}
      </div>

      {#if nav.query.trim()}
        <ul class="hits" aria-live="polite">
          {#each results as e (`${e.page}:${e.sid ?? ''}`)}
            <li>
              <button type="button" class="hit" onclick={() => pick(e.page, isPane(e) ? undefined : e.sid)}>
                <span class="hit-title">{e.title}</span>
                <span class="hit-sub">{isPane(e) ? t('search.pane') : e.subtitle}</span>
              </button>
            </li>
          {:else}
            <li class="none">{t('search.empty', { query: nav.query.trim() })}</li>
          {/each}
        </ul>
      {:else}
        <nav aria-label={t('sidebar.label')}>
          {#each PAGE_GROUPS as group, gi (gi)}
            <ul class="group">
              {#each group as id (id)}
                {@const Icon = ICONS[id]}
                <li>
                  <button
                    type="button"
                    class="pane"
                    class:active={nav.page === id}
                    data-page={id}
                    aria-current={nav.page === id ? 'page' : undefined}
                    tabindex={nav.page === id ? 0 : -1}
                    onclick={() => go(id)}
                    onkeydown={onSidebarKey}
                  >
                    <span class="tile" style:--c1={PAGE_COLORS[id][0]} style:--c2={PAGE_COLORS[id][1]} aria-hidden="true"><Icon size={14} strokeWidth={2.2} /></span>
                    <span class="name">{t(`page.${id}`)}</span>
                  </button>
                </li>
              {/each}
            </ul>
          {/each}
        </nav>
      {/if}
    </aside>

    <main class="main" bind:this={main} tabindex="-1" aria-label={t(`page.${nav.page}`)}>
      <div class="page">
        {#if nav.page === 'general'}<General />
        {:else if nav.page === 'appearance'}<Appearance />
        {:else if nav.page === 'dock'}<Dock />
        {:else if nav.page === 'menubar'}<MenuBar />
        {:else if nav.page === 'spotlight'}<Spotlight />
        {:else if nav.page === 'hotCorners'}<HotCorners />
        {:else if nav.page === 'desktop'}<Desktop />
        {:else if nav.page === 'widgets'}<Widgets />
        {:else if nav.page === 'shortcuts'}<Shortcuts />
        {:else if nav.page === 'about'}<About />
        {/if}
      </div>
    </main>
    <Toasts />
  </div>
{/if}

<style>
  .shell { display: grid; grid-template-columns: 200px minmax(0, 1fr); height: 100%; }
  .onboarding-root { height: 100%; }
  .side {
    display: flex; flex-direction: column; min-height: 0; background: var(--s-sidebar); border-right: 0.5px solid var(--separator);
    padding: 12px 10px 14px; overflow: auto;
  }
  .search {
    display: flex; align-items: center; gap: 6px; height: 28px; padding: 0 8px; border-radius: 7px; color: var(--text-2);
    background: var(--s-field); border: 0.5px solid var(--s-field-border); margin-bottom: 12px; flex: none;
  }
  .search input { flex: 1; min-width: 0; height: 100%; border: 0; background: transparent; color: var(--text); font-size: 12.5px; padding: 0; }
  .search input::-webkit-search-cancel-button { display: none; }
  .search input:focus { outline: none; }
  .search:focus-within { box-shadow: 0 0 0 2px var(--focus); }
  .clear { display: grid; place-items: center; width: 18px; height: 18px; border: 0; border-radius: 50%; background: var(--s-seg); color: var(--text-2); padding: 0; }
  .group + .group { margin-top: 14px; }
  .group { list-style: none; margin: 0; padding: 0; display: grid; gap: 1px; }
  .pane {
    display: flex; align-items: center; gap: 9px; width: 100%; height: 30px; padding: 0 8px 0 6px; border: 0; border-radius: 7px;
    background: transparent; color: var(--text); text-align: left; font-size: 13px;
  }
  .pane:hover { background: var(--s-row-hover); }
  .pane.active { background: color-mix(in srgb, var(--accent) 18%, transparent); font-weight: 550; }
  .pane:focus-visible { outline: 2px solid var(--focus); outline-offset: -2px; }
  .tile {
    display: grid; place-items: center; width: 22px; height: 22px; border-radius: 6px; color: #fff; flex: none;
    background: linear-gradient(160deg, var(--c1), var(--c2)); box-shadow: inset 0 0 0 0.5px rgba(255, 255, 255, 0.25), 0 0.5px 1px rgba(0, 0, 0, 0.2);
  }
  .name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .hits { list-style: none; margin: 0; padding: 0; display: grid; gap: 2px; }
  .hit { display: grid; width: 100%; padding: 6px 8px; border: 0; border-radius: 7px; background: transparent; text-align: left; }
  .hit:hover { background: var(--s-row-hover); }
  .hit:focus-visible { outline: 2px solid var(--focus); outline-offset: -2px; }
  .hit-title { font-size: 13px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .hit-sub { font-size: 11px; color: var(--text-2); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .none { padding: 8px; font-size: 12px; color: var(--text-2); }
  .main { position: relative; min-width: 0; overflow-y: auto; overflow-x: hidden; padding: 6px 28px 36px; container-type: inline-size; outline: none; }
  .page { max-width: 660px; margin: 0 auto; }
</style>
