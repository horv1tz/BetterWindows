<script lang="ts">
  // The expanded folder: glass panel with an editable name and the folder's apps. Pointer/drag logic is owned by Launchpad.svelte.
  import { onMount, tick } from 'svelte';
  import X from '@lucide/svelte/icons/x';
  import { glassShape } from '$lib/shape';
  import type { AppEntry, LaunchpadFolder } from '$lib/protocol/generated';
  import type { FolderGeometry } from './layout';
  import Tile from './Tile.svelte';
  import { t } from './strings';

  let {
    folder, apps, order, geometry, editing, dragId, autoRename, originX, originY, jiggle,
    onrename, onclose, ontiledown, onactivate, onhide, gridEl = $bindable(), panelEl = $bindable(),
  }: {
    folder: LaunchpadFolder; apps: Map<string, AppEntry>; order: string[]; geometry: FolderGeometry; editing: boolean; dragId: string | null;
    autoRename: boolean; originX: number; originY: number; jiggle: (id: string) => number;
    onrename: (name: string) => void; onclose: () => void; ontiledown: (e: PointerEvent, id: string) => void;
    onactivate: (app: AppEntry) => void; onhide: (app: AppEntry) => void;
    gridEl?: HTMLElement; panelEl?: HTMLElement;
  } = $props();

  let nameInput = $state<HTMLInputElement>();
  let draft = $state('');
  let editingName = $state(false);
  $effect(() => { if (!editingName) draft = folder.name; });

  onMount(async () => {
    await tick();
    if (autoRename) { nameInput?.focus(); nameInput?.select(); }
    else (panelEl?.querySelector('button.tile') as HTMLElement | null)?.focus({ preventScroll: true }); // keyboard users land inside the folder
  });

  function commit() {
    editingName = false;
    const next = draft.trim();
    if (next !== folder.name.trim()) onrename(next);
  }
  function nameKey(e: KeyboardEvent) {
    e.stopPropagation(); // typing in the name must never reach the Launchpad shortcuts
    if (e.key === 'Enter') { e.preventDefault(); nameInput?.blur(); }
    else if (e.key === 'Escape') { e.preventDefault(); draft = folder.name; editingName = false; nameInput?.blur(); }
  }

  const slot = (i: number) => ({ x: (i % geometry.cols) * geometry.cellW, y: Math.floor(i / geometry.cols) * geometry.cellH });
  const gridW = $derived(geometry.cols * geometry.cellW);
  const gridH = $derived(geometry.rows * geometry.cellH);
</script>

<div
  class="panel glass"
  role="dialog"
  aria-label={folder.name || t('lp.folder')}
  tabindex="-1"
  bind:this={panelEl}
  use:glassShape={{ radius: 34 }}
  style:transform-origin="{originX}px {originY}px"
>
  <input
    bind:this={nameInput}
    class="name"
    class:editing
    type="text"
    maxlength="48"
    spellcheck="false"
    autocomplete="off"
    aria-label={t('lp.folderName')}
    placeholder={t('lp.folder')}
    bind:value={draft}
    onfocus={() => { editingName = true; }}
    onblur={commit}
    onkeydown={nameKey}
  />
  <div class="scroll" style:height="{geometry.height}px" style:width="{gridW}px">
    <div class="grid" bind:this={gridEl} role="list" style:width="{gridW}px" style:height="{gridH}px">
      {#each order as id (id)}
        {@const app = apps.get(id)}
        {#if app}
          {@const p = slot(order.indexOf(id))}
          <div
            class="slot"
            class:ghosted={dragId === id}
            role="listitem"
            style:width="{geometry.cellW}px"
            style:height="{geometry.cellH}px"
            style:transform="translate3d({p.x}px, {p.y}px, 0)"
            style:--jd="{-jiggle(id)}ms"
            style:--ix="{(geometry.cellW - geometry.iconSize) / 2 - 9}px"
            style:--iy="{(geometry.cellH - geometry.contentH) / 2 - 9}px"
          >
            <button
              type="button"
              class="tile"
              aria-label={app.name}
              onpointerdown={(e) => ontiledown(e, id)}
              onclick={() => onactivate(app)}
            >
              <Tile name={app.name} {app} size={geometry.iconSize} labelWidth={geometry.cellW - 12} {editing} />
            </button>
            {#if editing}
              <button type="button" class="x" aria-label={t('lp.remove', { name: app.name })} onclick={() => onhide(app)}><X size={13} strokeWidth={3} /></button>
            {/if}
          </div>
        {/if}
      {/each}
    </div>
  </div>
</div>

<style>
  .panel {
    position: absolute; left: 50%; top: 50%; translate: -50% -52%;
    display: flex; flex-direction: column; align-items: center; gap: 8px; padding: 26px 28px 24px;
    border-radius: 34px; max-width: calc(100vw - 32px);
    background: color-mix(in oklab, var(--glass-bg-strong) 72%, transparent);
    box-shadow: 0 30px 80px rgba(0, 0, 0, 0.45), 0 0 0 0.5px var(--glass-edge);
    animation: folder-in 260ms var(--ease-out) both;
  }
  @keyframes folder-in { from { opacity: 0; transform: scale(0.55); } to { opacity: 1; transform: none; } }
  .name {
    width: min(100%, 420px); height: 40px; padding: 0 10px; border: 0; border-radius: 9px; outline: 0; text-align: center;
    background: transparent; color: var(--text); font: 600 22px/1 var(--font-ui); letter-spacing: -0.01em; user-select: text; cursor: text;
    text-overflow: ellipsis;
  }
  .name::placeholder { color: var(--text-3); }
  .name:hover { background: var(--hover); }
  .name:focus { background: var(--hover); box-shadow: 0 0 0 2px var(--accent); }
  .scroll { overflow-x: hidden; overflow-y: auto; scrollbar-width: thin; overscroll-behavior: contain; max-width: 100%; }
  .grid { position: relative; }
  .slot { position: absolute; left: 0; top: 0; display: grid; place-items: center; transition: transform 260ms var(--ease-out); }
  .slot.ghosted { visibility: hidden; }
  .tile { all: unset; box-sizing: border-box; display: grid; place-items: center; width: 100%; height: 100%; cursor: default; border-radius: 16px; touch-action: none; }
  .tile :global(.label) { color: var(--text); text-shadow: none; }
  .tile:focus-visible { outline: 3px solid var(--accent); outline-offset: -6px; }
  .x {
    position: absolute; left: var(--ix); top: var(--iy); width: 22px; height: 22px; padding: 0; display: grid; place-items: center;
    border-radius: 50%; border: 0.5px solid rgba(255, 255, 255, 0.35); background: rgba(60, 60, 66, 0.92); color: #fff; z-index: 2;
    box-shadow: 0 1px 4px rgba(0, 0, 0, 0.4); animation: pop 160ms var(--ease-spring) both;
  }
  .x:hover { background: rgba(90, 90, 98, 0.96); }
  .x:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
  @keyframes pop { from { transform: scale(0.4); opacity: 0; } to { transform: none; opacity: 1; } }
</style>
