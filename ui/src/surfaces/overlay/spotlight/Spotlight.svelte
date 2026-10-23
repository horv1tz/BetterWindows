<script lang="ts">
  // Spotlight overlay mode. Props are fixed by overlay/Surface.svelte.
  import { onDestroy, onMount, untrack } from 'svelte';
  import Search from '@lucide/svelte/icons/search';
  import X from '@lucide/svelte/icons/x';
  import SearchX from '@lucide/svelte/icons/search-x';
  import { getBridge } from '$lib/ipc';
  import { glassShape } from '$lib/shape';
  import type { OverlayShow, SearchProviderId } from '$lib/protocol/generated';
  import { SearchSession } from './session.svelte';
  import { buildListModel, needsConfirm, orderedActions, reconcileSelection, type Entry } from './viewModel';
  import { initialNav, reduceKey, type NavContext, type NavEffect, type NavState } from './keyboard';
  import SpotlightRow from './SpotlightRow.svelte';
  import CalcCard from './CalcCard.svelte';
  import PreviewPane from './PreviewPane.svelte';
  import { t, resultsCount } from './strings';

  let { onclose }: { show: OverlayShow; onclose: () => void } = $props();

  const bridge = getBridge();
  const session = new SearchSession(bridge);
  onDestroy(() => session.dispose());

  let input = $state<HTMLInputElement>();
  let listEl = $state<HTMLDivElement>();
  let listHeight = $state(0);
  let nav = $state<NavState>(initialNav());
  let intro = $state(true);

  const recent = $derived(session.shown.kind === 'recent');
  const model = $derived(buildListModel(session.shown.results, { recent }));
  const selected = $derived<Entry | undefined>(model.flat[Math.min(nav.index, model.flat.length - 1)]);
  const hasQuery = $derived(session.query.trim().length > 0);
  const answeredEmpty = $derived(hasQuery && session.answered && model.flat.length === 0);
  const showBody = $derived(model.flat.length > 0 || answeredEmpty || (!hasQuery && session.shown.results.length === 0 && session.shown.requestId !== '') || !!session.error);
  const pageSize = $derived(Math.max(1, Math.floor(listHeight / 46)));
  const selectedActions = $derived(selected ? orderedActions(selected.result) : []);
  const selectedAction = $derived(selectedActions[Math.min(nav.actionIndex, selectedActions.length - 1)]);
  const secondAction = $derived(selectedActions[1]);
  const previewData = $derived(session.preview && session.preview.key === selected?.result.id ? session.preview.data : null);

  const ctx = $derived<NavContext>({
    count: model.flat.length,
    pageSize,
    actionCount: (i) => orderedActions(model.flat[i].result).length,
    needsConfirm: (i) => needsConfirm(model.flat[i].result),
    hasQuery: session.query.length > 0,
  });

  // ---- selection bookkeeping ----
  let lastSelectedId: string | null = null;
  $effect(() => {
    const flat = model.flat; // a newer list replaced the old one
    untrack(() => {
      const idx = reconcileSelection({ id: lastSelectedId, moved: nav.moved }, flat);
      if (idx !== nav.index || nav.actionIndex !== 0 || nav.confirming) nav = { ...nav, index: idx, actionIndex: 0, confirming: false };
      lastSelectedId = flat[idx]?.result.id ?? null;
    });
  });
  $effect(() => { lastSelectedId = selected?.result.id ?? lastSelectedId; });

  // lazy preview of the selected result only
  $effect(() => {
    const r = selected?.result ?? null;
    untrack(() => session.select(r));
  });

  // keep the selection visible
  $effect(() => {
    const index = nav.index;
    void model;
    const el = document.getElementById(`sp-opt-${index}`);
    if (!el) return;
    if (index === 0 || (index === 1 && model.card)) listEl?.scrollTo({ top: 0 });
    else el.scrollIntoView({ block: 'nearest' });
  });

  // ---- focus ----
  function focusInput() { input?.focus({ preventScroll: true }); }
  onMount(() => {
    focusInput();
    // the host activates the window asynchronously: retry a few times
    const timers = [30, 90, 200].map((ms) => setTimeout(focusInput, ms));
    const introTimer = setTimeout(() => (intro = false), 500);
    return () => { timers.forEach(clearTimeout); clearTimeout(introTimer); };
  });

  // ---- actions ----
  function perform(list: typeof session.shown, entry: Entry | undefined, actionIndex: number) {
    if (!entry) return;
    const actions = orderedActions(entry.result);
    const action = actions[actionIndex] ?? actions[0];
    void session.perform(list, entry.result, action?.id);
    onclose(); // the host hides the overlay itself; close optimistically
  }

  function run(index: number, actionIndex: number, viaPointer = false) {
    const entry = model.flat[index];
    if (!entry) return;
    if (!session.current) {
      // the list on screen belongs to an older query: its ids are gone. Enter means "the top hit of what I typed".
      if (viaPointer) return;
      session.runWhenReady((list) => perform(list, buildListModel(list.results, { recent: list.kind === 'recent' }).flat[0], 0));
      return;
    }
    perform(session.shown, entry, actionIndex);
  }

  function apply(effect: NavEffect) {
    if (effect.type === 'run') run(effect.index, effect.actionIndex);
    else if (effect.type === 'clear') { setQuery(''); focusInput(); }
    else if (effect.type === 'close') onclose();
  }

  function setQuery(q: string) {
    session.setQuery(q);
    nav = { ...nav, moved: false, actionIndex: 0, confirming: false };
  }

  function onkeydown(e: KeyboardEvent) {
    if (e.isComposing || e.keyCode === 229) return;
    const res = reduceKey(nav, e, ctx);
    if (!res.handled) return;
    e.preventDefault();
    nav = res.state;
    apply(res.effect);
  }

  let lastX = -1;
  let lastY = -1;
  function hover(index: number, e: PointerEvent) {
    // ignore synthetic moves Chromium fires after the list scrolled under a still pointer
    if (e.clientX === lastX && e.clientY === lastY) return;
    lastX = e.clientX; lastY = e.clientY;
    if (nav.index !== index) nav = { ...nav, index, actionIndex: 0, confirming: false, moved: true };
  }

  function backdropDown(e: PointerEvent) {
    if (e.target === e.currentTarget) onclose();
  }
  function panelDown(e: PointerEvent) {
    // keep the caret in the field when clicking on non-interactive parts of the panel
    if (!(e.target as HTMLElement).closest('input, button')) e.preventDefault();
  }

  const groupTitle = (k: SearchProviderId | 'recent') => t(`sp.group.${k}`);
</script>

<svelte:window onkeydown={onkeydown} onfocus={focusInput} />

<div class="root" role="presentation" onpointerdown={backdropDown}>
  <div class="panel glass" class:open={showBody} role="dialog" tabindex="-1" aria-modal="true" aria-label={t('sp.dialog')} use:glassShape={{ radius: 18 }} onpointerdown={panelDown}>
    <div class="field">
      <span class="mag" aria-hidden="true"><Search size={22} strokeWidth={1.9} /></span>
      <input
        bind:this={input}
        class="q"
        type="text"
        role="combobox"
        aria-expanded={model.flat.length > 0}
        aria-controls="sp-list"
        aria-autocomplete="list"
        aria-activedescendant={selected ? `sp-opt-${selected.index}` : undefined}
        aria-label={t('sp.dialog')}
        placeholder={t('sp.placeholder')}
        autocomplete="off"
        autocorrect="off"
        spellcheck="false"
        value={session.query}
        oninput={(e) => setQuery(e.currentTarget.value)}
      />
      {#if session.query}
        <button class="clear" type="button" aria-label={t('sp.clear')} onclick={() => { setQuery(''); focusInput(); }}><X size={14} strokeWidth={2.4} /></button>
      {/if}
    </div>
    <div class="progress" class:on={session.searching} aria-hidden="true"><span class="bar"></span></div>

    {#if showBody}
      <div class="body">
        {#if model.card}
          <CalcCard
            result={model.card.result}
            id="sp-opt-0"
            selected={selected?.index === 0}
            action={selected?.index === 0 ? selectedAction : orderedActions(model.card.result)[0]}
            onrun={() => run(0, 0, true)}
            onhover={() => { if (nav.index !== 0) nav = { ...nav, index: 0, actionIndex: 0, confirming: false, moved: true }; }}
          />
        {/if}

        {#if model.flat.length > (model.card ? 1 : 0)}
          <div class="cols" class:with-card={!!model.card}>
            <div class="list" id="sp-list" role="listbox" aria-label={t('sp.results')} bind:this={listEl} bind:clientHeight={listHeight}>
              {#each model.sections as sec (sec.key)}
                <div class="group" role="group" aria-label={groupTitle(sec.key)}>
                  <div class="ghead" aria-hidden="true">{groupTitle(sec.key)}</div>
                  {#each sec.entries as e (`${e.result.provider}:${e.result.id}`)}
                    <SpotlightRow
                      entry={e}
                      query={recent ? '' : session.query}
                      id="sp-opt-{e.index}"
                      selected={selected?.index === e.index}
                      action={selected?.index === e.index ? selectedAction : orderedActions(e.result)[0]}
                      confirming={nav.confirming}
                      {intro}
                      onrun={() => run(e.index, 0, true)}
                      onhover={(ev: PointerEvent) => hover(e.index, ev)}
                    />
                  {/each}
                </div>
              {/each}
            </div>
            <PreviewPane result={selected?.result ?? null} preview={previewData} loading={session.previewLoading} />
          </div>
        {:else if model.flat.length === 0}
          <div class="empty" role="status">
            {#if session.error}
              <span class="e-ico"><SearchX size={28} strokeWidth={1.5} /></span>
              <span class="e-title">{t('sp.error')}</span>
            {:else if hasQuery}
              <span class="e-ico"><SearchX size={28} strokeWidth={1.5} /></span>
              <span class="e-title">{t('sp.empty')}</span>
              <span class="e-hint">{t('sp.empty.hint')}</span>
            {:else}
              <span class="e-ico"><Search size={28} strokeWidth={1.5} /></span>
              <span class="e-title">{t('sp.empty.start')}</span>
              <span class="e-hint">{t('sp.empty.startHint')}</span>
            {/if}
          </div>
        {/if}

        {#if model.flat.length > 0}
          <div class="foot">
            <span class="status">{#if session.searching}<i class="dots" aria-hidden="true"></i>{t('sp.searching')}{/if}</span>
            <span class="keys">
              {#if nav.confirming}
                <span class="k warn"><kbd>↵</kbd>{t('sp.hint.confirmShort')}</span>
              {:else}
                {#if selectedAction}<span class="k"><kbd>↵</kbd>{selectedAction.label}</span>{/if}
                {#if selectedActions.length > 1}<span class="k"><kbd>Tab</kbd>{t('sp.hint.more')}</span>{/if}
                {#if secondAction}<span class="k"><kbd>Ctrl ↵</kbd>{secondAction.label}</span>{/if}
              {/if}
            </span>
          </div>
        {/if}
      </div>
    {/if}
    <div class="sr-only" aria-live="polite" role="status">{#if answeredEmpty || (model.flat.length > 0 && session.complete)}{resultsCount(model.flat.length)}{/if}</div>
  </div>
</div>

<style>
  .root { position: fixed; inset: 0; }
  .panel {
    position: absolute; left: 0; right: 0; margin-inline: auto; top: 22vh;
    width: min(680px, calc(100vw - 24px)); border-radius: 18px; overflow: hidden;
    container-type: inline-size;
    background: color-mix(in oklab, var(--glass-bg-strong) 74%, transparent);
    box-shadow: var(--shadow-lg), 0 0 0 0.5px var(--glass-edge);
    animation: panel-in 190ms var(--ease-out) both;
    transform-origin: 50% 0;
    --sp-danger: #e5382f;
    --sp-danger-text: #ff7a70;
  }
  :global(:root[data-theme='light']) .panel { --sp-danger-text: #cf2a20; }
  @keyframes panel-in { from { opacity: 0; transform: translateY(-8px) scale(0.965); } to { opacity: 1; transform: none; } }

  .field { display: flex; align-items: center; gap: 12px; height: 60px; padding: 0 18px 0 20px; }
  .mag { display: grid; place-items: center; color: var(--text-2); flex: none; }
  .q {
    flex: 1; min-width: 0; height: 100%; border: 0; outline: 0; background: transparent; color: var(--text);
    font: 400 22px/1 var(--font-ui); letter-spacing: -0.012em; user-select: text; caret-color: var(--accent);
  }
  .q::placeholder { color: var(--text-3); }
  .clear {
    display: grid; place-items: center; width: 20px; height: 20px; padding: 0; border: 0; border-radius: 50%; flex: none;
    background: var(--text-3); color: var(--glass-bg-strong); cursor: default;
  }
  .clear:hover { background: var(--text-2); }
  .clear:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }

  .progress { position: relative; height: 2px; margin-top: -2px; overflow: hidden; opacity: 0; transition: opacity 120ms; pointer-events: none; }
  .progress.on { opacity: 1; transition-delay: 140ms; }
  .bar { position: absolute; inset: 0 auto 0 0; width: 34%; background: linear-gradient(90deg, transparent, var(--accent), transparent); animation: slide 1.15s var(--ease-out) infinite; }
  @keyframes slide { from { transform: translateX(-100%); } to { transform: translateX(300%); } }

  .body { border-top: 0.5px solid var(--separator); }
  .cols { display: flex; min-height: 300px; }
  .cols.with-card { border-top: 0.5px solid var(--separator); margin-top: 6px; }
  .list { flex: 1; min-width: 0; max-height: min(430px, calc(100vh - 22vh - 168px)); overflow-y: auto; padding: 4px 0 8px; overscroll-behavior: contain; scrollbar-width: thin; }
  .ghead { padding: 9px 20px 4px; font-size: 12px; font-weight: 600; color: var(--text-2); letter-spacing: 0.01em; }

  .empty { display: flex; flex-direction: column; align-items: center; gap: 4px; padding: 34px 24px 38px; text-align: center; }
  .e-ico { color: var(--text-3); margin-bottom: 6px; }
  .e-title { font-size: 15px; font-weight: 600; }
  .e-hint { font-size: 12px; color: var(--text-2); }

  .foot { display: flex; align-items: center; justify-content: space-between; gap: 12px; height: 36px; padding: 0 16px; border-top: 0.5px solid var(--separator); background: color-mix(in oklab, var(--text) 3.5%, transparent); font-size: 12px; color: var(--text-2); }
  .status { display: inline-flex; align-items: center; gap: 8px; min-width: 0; }
  .keys { display: inline-flex; align-items: center; gap: 16px; min-width: 0; }
  .k { display: inline-flex; align-items: center; gap: 6px; white-space: nowrap; }
  .k.warn { color: var(--sp-danger-text); font-weight: 600; }
  kbd { display: inline-grid; place-items: center; min-width: 20px; height: 20px; padding: 0 6px; border-radius: 5px; font: 600 11px/1 var(--font-ui); background: var(--hover); color: var(--text); }
  .dots { width: 6px; height: 6px; border-radius: 50%; background: var(--accent); animation: pulse 0.9s ease-in-out infinite; }
  @keyframes pulse { 0%, 100% { opacity: 0.3; transform: scale(0.8); } 50% { opacity: 1; transform: scale(1); } }

  @container (max-width: 600px) {
    :global(.panel .pane) { display: none; }
    .keys .k:nth-child(n + 3) { display: none; }
  }
  @media (prefers-reduced-motion: reduce) { .panel { animation: none; } }
</style>
