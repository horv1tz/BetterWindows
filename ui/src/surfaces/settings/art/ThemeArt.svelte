<script lang="ts">
  /** Mini window in the light / dark / split (auto) look for the appearance cards. Decorative. */
  let { mode, width = 132 }: { mode: 'light' | 'dark' | 'auto'; width?: number } = $props();
</script>

{#snippet win(kind: 'light' | 'dark')}
  <div class="w {kind}">
    <div class="bar"><i></i><i></i><i></i></div>
    <div class="body">
      <div class="side"><b></b><b></b><b class="on"></b><b></b></div>
      <div class="main"><s class="h"></s><s></s><s class="short"></s><em></em></div>
    </div>
  </div>
{/snippet}

<div class="art" style:width="{width}px" aria-hidden="true">
  <div class="wall"></div>
  {#if mode === 'auto'}
    <div class="layer">{@render win('light')}</div>
    <div class="layer half">{@render win('dark')}</div>
  {:else}
    <div class="layer">{@render win(mode)}</div>
  {/if}
</div>

<style>
  .art { position: relative; aspect-ratio: 1.55; border-radius: 8px; overflow: hidden; box-shadow: 0 0 0 0.5px rgba(0, 0, 0, 0.25); }
  .wall { position: absolute; inset: 0; background: linear-gradient(135deg, #5b8def 0%, #a46ad8 55%, #f39a6b 100%); }
  .layer { position: absolute; inset: 0; display: grid; place-items: center; }
  .layer.half { clip-path: polygon(100% 0, 100% 100%, 0 100%); }
  .w { width: 74%; height: 66%; border-radius: 5px; overflow: hidden; display: flex; flex-direction: column; box-shadow: 0 3px 10px rgba(0, 0, 0, 0.35); }
  .light { background: #f7f7f9; --ink: rgba(0, 0, 0, 0.16); --side: #e8e8ee; }
  .dark { background: #2a2b31; --ink: rgba(255, 255, 255, 0.22); --side: #35363d; }
  .bar { height: 14%; display: flex; align-items: center; gap: 3px; padding: 0 5px; }
  .bar i { width: 4px; height: 4px; border-radius: 50%; background: var(--ink); }
  .body { flex: 1; display: flex; min-height: 0; }
  .side { width: 30%; background: var(--side); padding: 5px 4px; display: flex; flex-direction: column; gap: 3px; }
  .side b { height: 4px; border-radius: 2px; background: var(--ink); }
  .side b.on { background: var(--accent); }
  .main { flex: 1; padding: 6px; display: flex; flex-direction: column; gap: 4px; }
  .main s { display: block; height: 4px; border-radius: 2px; background: var(--ink); text-decoration: none; }
  .main s.h { width: 50%; height: 5px; }
  .main s.short { width: 70%; }
  .main em { margin-top: auto; align-self: flex-end; width: 22px; height: 8px; border-radius: 4px; background: var(--accent); }
</style>
