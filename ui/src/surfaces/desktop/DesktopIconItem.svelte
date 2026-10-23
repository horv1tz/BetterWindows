<script lang="ts">
  // One desktop icon: artwork (with a glyph fallback when the shell icon fails to load), a 2-line label with a legibility shadow.
  import Monitor from '@lucide/svelte/icons/monitor';
  import Trash2 from '@lucide/svelte/icons/trash-2';
  import Folder from '@lucide/svelte/icons/folder';
  import FileText from '@lucide/svelte/icons/file-text';
  import AppIcon from '$lib/components/AppIcon.svelte';
  import type { DesktopIcon } from '$lib/protocol/generated';

  let { icon, size, selected = false, tabbable = false, dragging = false, offset = null, ghost = false }: {
    icon: DesktopIcon; size: number; selected?: boolean; tabbable?: boolean; dragging?: boolean; offset?: { x: number; y: number } | null; ghost?: boolean;
  } = $props();

  let failed = $state(false);
  $effect(() => { icon.iconUrl; failed = false; });
  const fallback = $derived(icon.kind === 'special' ? (icon.id.includes('recycle') ? 'recycle' : 'thispc') : icon.kind);
  const glyphSize = $derived(Math.round(size * 0.52));
</script>

<div
  class="icon" class:selected class:dragging class:ghost
  role="option" aria-selected={selected} aria-label={icon.name} title={icon.name}
  tabindex={tabbable ? 0 : -1} data-icon-id={icon.id} data-kind={icon.kind}
  style:--s="{size}px"
  style:transform={offset ? `translate3d(${offset.x}px, ${offset.y}px, 0)` : undefined}
>
  <div class="art" style:width="{size}px" style:height="{size}px">
    {#if icon.iconUrl && !failed}
      <img src={icon.iconUrl} alt="" width={size} height={size} draggable="false" onerror={() => (failed = true)} />
    {:else if fallback === 'thispc' || fallback === 'recycle'}
      <div class="tile special" data-fallback="special" style:border-radius="{size * 0.22}px">
        {#if fallback === 'recycle'}<Trash2 size={glyphSize} strokeWidth={1.7} />{:else}<Monitor size={glyphSize} strokeWidth={1.7} />{/if}
      </div>
    {:else if fallback === 'folder'}
      <div class="tile folder" data-fallback="folder" style:border-radius="{size * 0.22}px"><Folder size={glyphSize} strokeWidth={1.7} /></div>
    {:else if fallback === 'file'}
      <div class="tile file" data-fallback="file" style:border-radius="{size * 0.22}px"><FileText size={glyphSize} strokeWidth={1.7} /></div>
    {:else}
      <div data-fallback="shortcut"><AppIcon name={icon.name} {size} /></div>
    {/if}
  </div>
  <span class="label"><span class="text">{icon.name}</span></span>
</div>

<style>
  .icon {
    position: absolute; left: 4px; right: 4px; top: 2px; bottom: 4px; display: flex; flex-direction: column; align-items: center; padding: 5px 0 0;
    outline: none; touch-action: none; border-radius: 10px;
  }
  .art { flex: none; display: grid; place-items: center; border-radius: 12px; transition: background var(--dur-fast) ease; }
  .art :global(img), .art > :global(div) { filter: drop-shadow(0 2px 3px rgba(0, 0, 0, 0.35)); }
  img { display: block; object-fit: contain; user-select: none; -webkit-user-drag: none; }
  .tile { width: 100%; height: 100%; display: grid; place-items: center; color: #fff; }
  .tile.special { background: linear-gradient(145deg, #6c7a96, #3e4a63); }
  .tile.folder { background: linear-gradient(145deg, #4fb0ff, #2d6fe0); }
  .tile.file { background: linear-gradient(145deg, #9aa3b5, #6d7689); }
  .label {
    margin-top: 5px; max-width: 100%; display: flex; justify-content: center;
    font-size: 12px; line-height: 15px; font-weight: 500; color: #fff; text-align: center;
  }
  .text {
    display: -webkit-box; -webkit-line-clamp: 2; line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden;
    overflow-wrap: anywhere; padding: 1px 5px; border-radius: 5px;
    text-shadow: 0 1px 2px rgba(0, 0, 0, 0.75), 0 0 8px rgba(0, 0, 0, 0.45);
  }
  .selected .art { background: rgba(255, 255, 255, 0.2); box-shadow: 0 0 0 1px rgba(255, 255, 255, 0.22) inset; }
  .selected .text { background: var(--accent); text-shadow: none; }
  .icon:focus-visible { box-shadow: 0 0 0 2px color-mix(in oklab, var(--accent) 80%, white); }
  .dragging { z-index: 5; opacity: 0.9; will-change: transform; }
  .dragging .art { transform: scale(1.04); }
  .ghost { pointer-events: none; opacity: 0.4; }
  .ghost .text { background: transparent; }
</style>
