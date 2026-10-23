<script lang="ts">
  import type { Snippet } from 'svelte';
  import { Info, TriangleAlert, CircleCheck, CircleAlert } from '@lucide/svelte';
  let { kind = 'info', children, action, role }: { kind?: 'info' | 'warn' | 'error' | 'ok'; children: Snippet; action?: Snippet; role?: 'status' | 'alert' } = $props();
</script>

<div class="banner {kind}" role={role ?? (kind === 'error' ? 'alert' : undefined)}>
  <span class="ico" aria-hidden="true">
    {#if kind === 'warn'}<TriangleAlert size={16} />{:else if kind === 'error'}<CircleAlert size={16} />{:else if kind === 'ok'}<CircleCheck size={16} />{:else}<Info size={16} />{/if}
  </span>
  <div class="body">{@render children()}</div>
  {#if action}<div class="act">{@render action()}</div>{/if}
</div>

<style>
  .banner { display: flex; align-items: flex-start; gap: 10px; padding: 10px 12px; border-radius: 10px; font-size: 12.5px; line-height: 1.4; margin-top: 16px;
    background: color-mix(in srgb, var(--c) 11%, transparent); border: 0.5px solid color-mix(in srgb, var(--c) 28%, transparent); }
  .info { --c: var(--s-info); } .warn { --c: var(--s-warn); } .error { --c: var(--s-danger); } .ok { --c: var(--s-ok); }
  .ico { color: var(--c); flex: none; margin-top: 1px; display: flex; }
  .body { flex: 1; min-width: 0; text-wrap: pretty; }
  .act { flex: none; align-self: center; }
</style>
