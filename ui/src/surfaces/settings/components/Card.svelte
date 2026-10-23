<script lang="ts">
  import type { Snippet } from 'svelte';
  let { title, note, children, actions, flush = false }: { title?: string; note?: string; children: Snippet; actions?: Snippet; flush?: boolean } = $props();
</script>

<section class="group">
  {#if title || actions}
    <header>
      {#if title}<h2>{title}</h2>{/if}
      {#if actions}<div class="acts">{@render actions()}</div>{/if}
    </header>
  {/if}
  <div class="card" class:flush>{@render children()}</div>
  {#if note}<p class="note">{note}</p>{/if}
</section>

<style>
  .group { margin-top: 22px; }
  header { display: flex; align-items: center; justify-content: space-between; gap: 12px; margin: 0 4px 6px; min-height: 20px; }
  h2 { font-size: 12px; font-weight: 600; color: var(--text-2); letter-spacing: 0.01em; }
  .acts { display: flex; gap: 6px; }
  .card { border-radius: 10px; background: var(--s-card); border: 0.5px solid var(--s-card-border); box-shadow: var(--s-card-shadow); overflow: clip; }
  .card.flush { padding: 0; }
  .note { margin: 6px 6px 0; color: var(--text-2); font-size: 11.5px; line-height: 1.4; text-wrap: pretty; }
</style>
