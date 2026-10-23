<script lang="ts">
  import { Music } from '@lucide/svelte';
  /** Album art from the host (media.state.thumbUrl) with a gradient placeholder when there is none or it fails to load. */
  interface Props { src?: string; size?: number; radius?: number }
  let { src, size = 40, radius = 8 }: Props = $props();
  let failed = $state(false);
  $effect(() => { src; failed = false; });
</script>

{#if src && !failed}
  <img {src} alt="" width={size} height={size} draggable="false" style:width="{size}px" style:height="{size}px" style:border-radius="{radius}px" onerror={() => (failed = true)} />
{:else}
  <span class="ph" style:width="{size}px" style:height="{size}px" style:border-radius="{radius}px" aria-hidden="true"><Music size={Math.round(size * 0.4)} strokeWidth={1.75} /></span>
{/if}

<style>
  img { display: block; object-fit: cover; flex: none; }
  .ph {
    display: grid; place-items: center; flex: none; color: rgba(255, 255, 255, 0.85);
    background: linear-gradient(135deg, var(--accent), color-mix(in srgb, var(--accent) 45%, #7a3cff));
  }
</style>
