<script lang="ts">
  import { Folder, Globe, Mail, Calendar, Music, Image, Terminal, Settings } from '@lucide/svelte';
  import type { DockPosition } from '$lib/protocol/generated';
  import { dockPreviewGeometry, fitScale } from '../dockPreview';

  let { position, iconSize, magnification, magnifiedSize, indicators = true, autoHide = false }: {
    position: DockPosition; iconSize: number; magnification: boolean; magnifiedSize: number; indicators?: boolean; autoHide?: boolean;
  } = $props();

  const GLYPHS = [Folder, Globe, Mail, Calendar, Music, Image, Terminal, Settings];
  const HUES = [212, 190, 205, 8, 340, 150, 262, 222];
  const count = GLYPHS.length;
  const vertical = $derived(position !== 'bottom');

  let box: HTMLElement | undefined = $state();
  let boxW = $state(560);
  let boxH = $state(176);
  /** icon index under the pointer (fractional); null = the middle icon so the effect is visible without hovering */
  let pointer = $state<number | null>(null);
  const focus = $derived(pointer ?? (count - 1) / 2);

  $effect(() => {
    if (!box) return;
    const ro = new ResizeObserver(([e]) => { boxW = e.contentRect.width; boxH = e.contentRect.height; });
    ro.observe(box);
    return () => ro.disconnect();
  });

  const scale = $derived(fitScale({ count, iconSize, magnifiedSize, magnification, available: vertical ? boxH - 60 : boxW - 60, max: 0.62 }));
  const g = $derived(dockPreviewGeometry({ count, iconSize, magnifiedSize, magnification, focus, scale }));
  const rest = $derived(dockPreviewGeometry({ count, iconSize, magnifiedSize, magnification: false, focus: 0, scale }));
  const base = $derived(iconSize * scale);
  /** start of every icon along the dock axis (cumulative actual sizes) */
  const offsets = $derived(g.sizes.map((_, i) => g.padding + g.sizes.slice(0, i).reduce((a, b) => a + b, 0) + g.gap * i));
  const origin = $derived(position === 'bottom' ? '0% 100%' : position === 'left' ? '0% 0%' : '100% 0%');

  function move(e: PointerEvent) {
    if (!box) return;
    const r = box.getBoundingClientRect();
    const along = vertical ? e.clientY - r.top - (r.height - rest.length) / 2 : e.clientX - r.left - (r.width - rest.length) / 2;
    pointer = Math.max(-0.4, Math.min(count - 0.6, (along - rest.padding) / (base + rest.gap) - 0.5));
  }
</script>

<div class="stage" bind:this={box} onpointermove={move} onpointerleave={() => (pointer = null)} aria-hidden="true">
  <div class="wall"></div>
  <div class="mb"><i></i><i></i><i class="r"></i><i class="r"></i><i class="r"></i></div>
  <div class="win"><b></b><s></s><s></s><s class="short"></s></div>

  <div
    class="bar {position}" class:faded={autoHide}
    style:width="{vertical ? g.thickness : g.length}px" style:height="{vertical ? g.length : g.thickness}px" style:border-radius="{Math.max(8, base * 0.32)}px"
  >
    {#each GLYPHS as Glyph, i (i)}
      <span
        class="ico"
        style:width="{base}px" style:height="{base}px" style:border-radius="{base * 0.24}px"
        style:background="linear-gradient(145deg, hsl({HUES[i]} 82% 62%), hsl({(HUES[i] + 36) % 360} 72% 44%))"
        style:transform="translate({vertical ? g.padding : offsets[i]}px, {vertical ? offsets[i] : g.padding}px) scale({g.sizes[i] / base})"
        style:transform-origin={origin}
      >
        <Glyph size="54%" color="#fff" strokeWidth={2.2} />
        {#if indicators && (i === 0 || i === 2 || i === 5)}<em class="dot {position}"></em>{/if}
      </span>
    {/each}
  </div>
</div>

<style>
  .stage { position: relative; height: 188px; border-radius: 12px; overflow: hidden; box-shadow: 0 0 0 0.5px rgba(0, 0, 0, 0.25), inset 0 0 0 0.5px rgba(255, 255, 255, 0.1); }
  .wall { position: absolute; inset: 0; background: linear-gradient(135deg, #3a74e6 0%, #8556d6 52%, #f28c62 100%); }
  :root[data-theme='dark'] .wall { background: linear-gradient(135deg, #1d2c5e 0%, #4b2f86 52%, #a2503c 100%); }
  .mb { position: absolute; top: 0; left: 0; right: 0; height: 14px; display: flex; gap: 8px; padding: 0 10px; align-items: center; background: rgba(255, 255, 255, 0.28); backdrop-filter: blur(6px); }
  :root[data-theme='dark'] .mb { background: rgba(18, 18, 24, 0.42); }
  .mb i { width: 22px; height: 4px; border-radius: 2px; background: rgba(255, 255, 255, 0.85); }
  .mb i.r { margin-left: auto; width: 12px; }
  .mb i.r ~ i.r { margin-left: 0; }
  .win { position: absolute; left: 16%; top: 30px; width: 34%; height: 66px; border-radius: 7px; padding: 8px; background: rgba(255, 255, 255, 0.82); box-shadow: 0 6px 18px rgba(0, 0, 0, 0.28); display: flex; flex-direction: column; gap: 5px; }
  :root[data-theme='dark'] .win { background: rgba(44, 45, 52, 0.9); }
  .win b { width: 30%; height: 5px; border-radius: 3px; background: color-mix(in srgb, var(--accent) 80%, transparent); }
  .win s { display: block; height: 4px; border-radius: 2px; background: color-mix(in srgb, var(--text) 18%, transparent); }
  .win s.short { width: 60%; }
  .bar { position: absolute; background: rgba(255, 255, 255, 0.34); border: 0.5px solid rgba(255, 255, 255, 0.55); backdrop-filter: blur(10px) saturate(160%); box-shadow: 0 8px 22px rgba(0, 0, 0, 0.22); }
  :root[data-theme='dark'] .bar { background: rgba(26, 26, 32, 0.5); border-color: rgba(255, 255, 255, 0.16); }
  .bar.bottom { bottom: 10px; left: 50%; translate: -50% 0; }
  .bar.left { left: 10px; top: 50%; translate: 0 -50%; }
  .bar.right { right: 10px; top: 50%; translate: 0 -50%; }
  .bar.faded { opacity: 0.78; }
  .ico { position: absolute; left: 0; top: 0; display: grid; place-items: center; box-shadow: 0 1px 3px rgba(0, 0, 0, 0.28), inset 0 0.5px 0 rgba(255, 255, 255, 0.4); will-change: transform; }
  .dot { position: absolute; width: 3px; height: 3px; border-radius: 50%; background: rgba(255, 255, 255, 0.95); box-shadow: 0 0 2px rgba(0, 0, 0, 0.5); }
  .dot.bottom { bottom: -6px; left: calc(50% - 1.5px); }
  .dot.left { left: -6px; top: calc(50% - 1.5px); }
  .dot.right { right: -6px; top: calc(50% - 1.5px); }
</style>
