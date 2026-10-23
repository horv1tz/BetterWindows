<script lang="ts">
  import type { Snippet } from 'svelte';
  /** A tiny stylised monitor: wallpaper, menu bar, optional dock. Purely decorative. */
  let {
    aspect = 1.6, width = 120, dock = 'none', menubar = true, wallpaper, dark = false, items = 5, children,
  }: {
    aspect?: number; width?: number; dock?: 'bottom' | 'left' | 'right' | 'none'; menubar?: boolean; wallpaper?: string; dark?: boolean; items?: number;
    children?: Snippet;
  } = $props();
  let broken = $state(false);
  $effect(() => { wallpaper; broken = false; });
</script>

<div class="scr" class:dark style:width="{width}px" style:aspect-ratio={aspect} aria-hidden="true">
  <div class="wall"></div>
  {#if wallpaper && !broken}<img class="wallimg" src={wallpaper} alt="" draggable="false" onerror={() => (broken = true)} />{/if}
  {#if menubar}<div class="mb"><i></i><i></i><i class="r"></i><i class="r"></i></div>{/if}
  {#if dock !== 'none'}
    <div class="dk {dock}">{#each Array.from({ length: items }) as _, i (i)}<i style:--h={(i * 47) % 360}></i>{/each}</div>
  {/if}
  {@render children?.()}
</div>

<style>
  .scr {
    position: relative; flex: none; border-radius: 7px; overflow: hidden; container-type: size;
    box-shadow: 0 0 0 1.5px color-mix(in srgb, var(--text) 55%, transparent), 0 0 0 3px color-mix(in srgb, var(--text) 8%, transparent), 0 6px 14px rgba(0, 0, 0, 0.18);
  }
  .wall { position: absolute; inset: 0; background: linear-gradient(135deg, #3a74e6 0%, #8556d6 52%, #f28c62 100%); }
  .dark .wall { background: linear-gradient(135deg, #1d2c5e 0%, #4b2f86 52%, #a2503c 100%); }
  .wallimg { position: absolute; inset: 0; width: 100%; height: 100%; object-fit: cover; }
  .mb { position: absolute; top: 0; left: 0; right: 0; height: 7cqh; min-height: 3px; background: rgba(255, 255, 255, 0.32); display: flex; align-items: center; gap: 3cqw; padding: 0 3cqw; backdrop-filter: blur(4px); }
  .dark .mb { background: rgba(20, 20, 26, 0.45); }
  .mb i { width: 5cqw; height: 2.2cqh; border-radius: 2px; background: rgba(255, 255, 255, 0.85); }
  .mb i.r { margin-left: auto; width: 4cqw; } .mb i.r + i.r { margin-left: 0; }
  .dk { position: absolute; display: flex; gap: 1.6cqw; padding: 1.8cqw; border-radius: 3cqw; background: rgba(255, 255, 255, 0.32); border: 0.5px solid rgba(255, 255, 255, 0.5); backdrop-filter: blur(5px); }
  .dark .dk { background: rgba(24, 24, 30, 0.5); border-color: rgba(255, 255, 255, 0.18); }
  .dk i { width: 7cqw; height: 7cqw; border-radius: 1.8cqw; background: hsl(var(--h) 75% 62%); }
  .dk.bottom { bottom: 3cqh; left: 50%; transform: translateX(-50%); flex-direction: row; }
  .dk.left { left: 2.4cqw; top: 50%; transform: translateY(-50%); flex-direction: column; }
  .dk.right { right: 2.4cqw; top: 50%; transform: translateY(-50%); flex-direction: column; }
  .dk.left i, .dk.right i { width: 6cqh; height: 6cqh; }
</style>
