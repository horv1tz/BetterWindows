<script lang="ts">
  /** App/file icon from the host's /dyn/ icon service with a letter-tile fallback while loading or on error. */
  let { src, name = '', size = 56, radius = 0.225 }: { src?: string; name?: string; size?: number; radius?: number } = $props();
  let failed = $state(false);
  $effect(() => { src; failed = false; });
  const letter = $derived((name.trim()[0] ?? '?').toUpperCase());
</script>

{#if src && !failed}
  <img {src} alt="" draggable="false" width={size} height={size} style:border-radius="{size * radius}px" onerror={() => (failed = true)} />
{:else}
  <div class="tile" style:width="{size}px" style:height="{size}px" style:border-radius="{size * radius}px" style:font-size="{size * 0.45}px">{letter}</div>
{/if}

<style>
  img { display: block; object-fit: contain; user-select: none; -webkit-user-drag: none; }
  .tile {
    display: grid; place-items: center; color: white; font-weight: 600;
    background: linear-gradient(135deg, var(--accent), color-mix(in oklab, var(--accent) 55%, #7a3cff));
    user-select: none;
  }
</style>
