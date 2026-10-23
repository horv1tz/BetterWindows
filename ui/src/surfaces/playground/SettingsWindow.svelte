<script lang="ts">
  // The settings window as a floating, draggable window: an iframe of #/settings (opened by settings.openWindow).
  import { clampWindow, type Frame, type Size } from './layout';
  import { t } from './strings';

  let { src, frame, z, bounds, onmove, onclose, onraise, onframe }: {
    src: string; frame: Frame; z: number; bounds: Size; onmove: (f: Frame) => void; onclose: () => void; onraise: () => void; onframe: (el: HTMLIFrameElement) => void;
  } = $props();

  let drag: { id: number; sx: number; sy: number; origin: Frame } | null = null;
  let dragging = $state(false);
  let el: HTMLIFrameElement;
  $effect(() => { if (el) onframe(el); });

  function down(e: PointerEvent) {
    onraise();
    if (e.button !== 0 || (e.target as HTMLElement).closest('button')) return;
    drag = { id: e.pointerId, sx: e.clientX, sy: e.clientY, origin: frame };
    dragging = true;
    (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
  }
  const move = (e: PointerEvent) => { if (drag && e.pointerId === drag.id) onmove(clampWindow({ ...drag.origin, left: drag.origin.left + e.clientX - drag.sx, top: drag.origin.top + e.clientY - drag.sy }, bounds)); };
  const up = (e: PointerEvent) => { if (drag && e.pointerId === drag.id) { drag = null; dragging = false; } };
</script>

<!-- svelte-ignore a11y_no_static_element_interactions -->
<div class="win" data-faux="settings" style:transform="translate3d({frame.left}px, {frame.top}px, 0)" style:width="{frame.width}px" style:height="{frame.height}px" style:z-index={z} onpointerdown={onraise}>
  <div class="bar" role="presentation" onpointerdown={down} onpointermove={move} onpointerup={up} onpointercancel={up}>
    <div class="lights"><button type="button" class="dot close" aria-label={t('pg.close')} onclick={onclose}></button><span class="dot min"></span><span class="dot max"></span></div>
    <div class="title">{t('pg.settingsTitle')}</div>
  </div>
  <iframe bind:this={el} {src} title={t('pg.settingsTitle')} class:dragging></iframe>
</div>

<style>
  .win {
    position: absolute; left: 0; top: 0; display: flex; flex-direction: column; border-radius: 12px; overflow: hidden; background: var(--glass-bg-strong);
    border: 0.5px solid var(--glass-edge); box-shadow: 0 26px 70px rgba(0, 0, 0, 0.45), 0 0 0 0.5px rgba(0, 0, 0, 0.3); will-change: transform;
  }
  .bar { flex: none; position: relative; height: 38px; display: flex; align-items: center; padding: 0 13px; border-bottom: 0.5px solid var(--separator); touch-action: none; cursor: default; }
  .lights { display: flex; gap: 8px; z-index: 1; }
  .dot { width: 12px; height: 12px; border-radius: 50%; border: 0; padding: 0; background: #ff5f57; box-shadow: inset 0 0 0 0.5px rgba(0, 0, 0, 0.2); }
  .dot.min { background: #febc2e; } .dot.max { background: #28c840; }
  .title { position: absolute; left: 70px; right: 70px; text-align: center; font-size: 12.5px; font-weight: 600; pointer-events: none; }
  iframe { flex: 1; min-height: 0; border: 0; width: 100%; background: transparent; }
  iframe.dragging { pointer-events: none; }
</style>
