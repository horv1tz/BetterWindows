<script lang="ts">
  import type { WindowRef } from '$lib/protocol/generated';
  import AppIcon from '$lib/components/AppIcon.svelte';
  import { hitShape } from '$lib/shape';

  /** A minimised window in the right-hand section: thumbnail-like tile (thumbnail when the host has one, else the app icon on a plate). */
  let { win, w, h, label, tabbable, hit = false, hidden = false }: {
    win: WindowRef; w: number; h: number; label: string; tabbable: boolean; hit?: boolean; hidden?: boolean;
  } = $props();
  const glyph = $derived(Math.round(Math.min(w, h) * (win.thumbUrl ? 0.42 : 0.62)));
</script>

<div class="bounce" class:hidden>
  <button type="button" class="tile" data-key={`win:${win.handle}`} tabindex={tabbable ? 0 : -1} aria-label={label} draggable="false">
    <span class="frame">
      {#if win.thumbUrl}<img class="thumb" src={win.thumbUrl} alt="" draggable="false" />{/if}
    </span>
    {#if win.thumbUrl}
      <span class="badge"><AppIcon src={win.iconUrl} name={win.title} size={glyph} /></span>
    {:else}
      <span class="center"><AppIcon src={win.iconUrl} name={win.title} size={glyph} /></span>
    {/if}
  </button>
  {#if hit}<span class="hit" aria-hidden="true" use:hitShape={{ radius: 9 }}></span>{/if}
</div>

<style>
  .bounce { position: absolute; inset: 0; }
  .bounce.hidden { opacity: 0; }
  .tile { position: absolute; inset: var(--ext, 0 -3px); margin: 0; padding: 0; background: none; border: 0; cursor: default; touch-action: none; border-radius: 9px; -webkit-tap-highlight-color: transparent; }
  .frame {
    position: absolute; inset: var(--ext-in, 0 3px); border-radius: 9px; overflow: hidden;
    background: linear-gradient(160deg, color-mix(in srgb, var(--text) 16%, transparent), color-mix(in srgb, var(--text) 6%, transparent));
    border: 0.5px solid var(--glass-border); box-shadow: var(--shadow-sm), inset 0 1px 0 rgba(255, 255, 255, 0.12);
  }
  .tile:focus-visible { outline: none; }
  .tile:focus-visible .frame { box-shadow: 0 0 0 2px rgba(255, 255, 255, 0.9), 0 0 0 4px var(--accent); }
  .thumb { display: block; width: 100%; height: 100%; object-fit: cover; opacity: 0.95; }
  .center { position: absolute; inset: var(--ext-in, 0 3px); display: grid; place-items: center; filter: drop-shadow(0 1px 1.5px rgba(0, 0, 0, 0.3)); }
  .badge { position: absolute; right: 4px; bottom: 3px; filter: drop-shadow(0 1px 1.5px rgba(0, 0, 0, 0.4)); }
  .tile:active { filter: brightness(0.85); }
  .hit { position: absolute; inset: var(--ext, 0 -3px); pointer-events: none; }
</style>
