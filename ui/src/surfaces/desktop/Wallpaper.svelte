<script lang="ts">
  // Wallpaper layer: crossfades when the URL changes, applies DesktopState.wallpaperFit, and keeps a theme-aware gradient behind it
  // as the "no wallpaper / still loading / failed" fallback.
  import { untrack } from 'svelte';
  import type { WallpaperFit } from '$lib/protocol/generated';
  import { fitStyle, markFailed, markReady, pushLayer, settle, type WpLayer } from './wallpaperLayers';

  let { url, fit = 'fill' }: { url?: string; fit?: WallpaperFit } = $props();

  let layers = $state<WpLayer[]>([]);
  let seq = 0;
  const style = $derived(fitStyle(fit));

  function preload(src: string): Promise<boolean> {
    return new Promise((resolve) => {
      const img = new Image();
      img.onload = () => { (img.decode ? img.decode() : Promise.resolve()).then(() => resolve(true), () => resolve(true)); };
      img.onerror = () => resolve(false);
      img.src = src;
    });
  }

  $effect(() => {
    const u = url;
    untrack(() => {
      if (!u) { layers = []; return; }
      if (layers.length && layers[layers.length - 1].url === u) return;
      const id = ++seq;
      layers = pushLayer(layers, u, id);
      preload(u).then((ok) => {
        if (!layers.some((l) => l.id === id)) return;
        if (!ok) { layers = markFailed(layers, id); return; }
        layers = markReady(layers, id);
        // the fade takes --dur-slow (360 ms); afterwards the layers below are invisible
        setTimeout(() => { layers = settle(layers, id); }, 420);
      });
    });
  });
</script>

<div class="wallpaper" aria-hidden="true">
  {#each layers as layer (layer.id)}
    {#if style.tile}
      <div class="layer tile" class:ready={layer.ready} style:background-image="url('{layer.url}')"></div>
    {:else}
      <img class="layer" class:ready={layer.ready} src={layer.url} alt="" draggable="false" style:object-fit={style.objectFit} />
    {/if}
  {/each}
</div>

<style>
  .wallpaper {
    position: absolute; inset: 0; overflow: hidden;
    background: linear-gradient(150deg, #3a5bd9 0%, #7a4fcf 48%, #e0709a 100%);
  }
  :global(:root[data-theme='dark']) .wallpaper { background: linear-gradient(150deg, #151a3b 0%, #2d1f56 52%, #4a2349 100%); }
  .layer {
    position: absolute; inset: 0; width: 100%; height: 100%;
    opacity: 0; transition: opacity var(--dur-slow) ease;
    user-select: none; -webkit-user-drag: none; pointer-events: none;
    object-position: center;
    background-color: #000; /* letterbox colour for "fit" / "center" */
  }
  .layer.ready { opacity: 1; }
  .tile { background-repeat: repeat; background-position: 0 0; background-size: auto; }
</style>
