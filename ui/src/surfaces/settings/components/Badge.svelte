<script lang="ts">
  import type { Snippet } from 'svelte';
  let { kind = 'neutral', children }: { kind?: 'neutral' | 'ok' | 'fail' | 'warn' | 'accent'; children: Snippet } = $props();
</script>

<span class="badge {kind}">{@render children()}</span>

<style>
  .badge { display: inline-flex; align-items: center; gap: 4px; height: 19px; padding: 0 8px; border-radius: 10px; font-size: 11px; font-weight: 600; letter-spacing: 0.01em; white-space: nowrap;
    color: var(--c); background: color-mix(in srgb, var(--c) 14%, transparent); }
  .neutral { --c: var(--text-2); } .ok { --c: var(--s-ok); } .fail { --c: var(--s-danger); } .warn { --c: var(--s-warn); } .accent { --c: var(--accent); }
</style>
