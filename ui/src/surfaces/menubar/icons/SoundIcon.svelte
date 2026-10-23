<script lang="ts">
  import type { SoundLevel } from '../status';
  /** Speaker with 0..3 waves; the muted state swaps the waves for a cross. */
  let { level = 'high', size = 16 }: { level?: SoundLevel; size?: number } = $props();
  const waves = $derived(level === 'low' ? 1 : level === 'mid' ? 2 : level === 'high' ? 3 : 0);
</script>

<svg width={size} height={size} viewBox="0 0 16 16" fill="none" aria-hidden="true" focusable="false" stroke="currentColor" stroke-linecap="round" stroke-linejoin="round">
  <path d="M2 6.1h2.3L7.7 3.2v9.6L4.3 9.9H2z" fill="currentColor" stroke-width="1.1" />
  {#if level === 'muted'}
    <path d="m10.3 6.3 3.6 3.6M13.9 6.3l-3.6 3.6" stroke-width="1.5" />
  {:else}
    {#if waves >= 1}<path d="M9.7 6.3a2.6 2.6 0 0 1 0 3.4" stroke-width="1.5" />{/if}
    {#if waves >= 2}<path d="M11.3 4.7a5 5 0 0 1 0 6.6" stroke-width="1.5" />{/if}
    {#if waves >= 3}<path d="M12.9 3.1a7.3 7.3 0 0 1 0 9.8" stroke-width="1.5" />{/if}
  {/if}
</svg>
