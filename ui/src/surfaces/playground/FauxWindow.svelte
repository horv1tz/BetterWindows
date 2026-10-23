<script lang="ts">
  // A decorative application window (pure HTML/CSS): title bar with traffic lights, draggable, raised on click. Gives Mission Control,
  // the dock and the menu bar something to look at in the playground; it has no behaviour beyond moving and closing.
  import { ChevronLeft, ChevronRight, Lock, RotateCw } from '@lucide/svelte';
  import { clampWindow, type FauxSpec, type Frame, type Size } from './layout';
  import { t } from './strings';

  let { spec, frame, z, active, bounds, onraise, onmove, onclose }: {
    spec: FauxSpec; frame: Frame; z: number; active: boolean; bounds: Size;
    onraise: () => void; onmove: (f: Frame) => void; onclose: () => void;
  } = $props();

  let drag: { pointerId: number; sx: number; sy: number; origin: Frame } | null = null;
  let dragging = $state(false);

  function down(e: PointerEvent) {
    onraise();
    if (e.button !== 0 || (e.target as HTMLElement).closest('.lights')) return;
    drag = { pointerId: e.pointerId, sx: e.clientX, sy: e.clientY, origin: frame };
    dragging = true;
    (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
  }
  function move(e: PointerEvent) {
    if (!drag || e.pointerId !== drag.pointerId) return;
    onmove(clampWindow({ ...drag.origin, left: drag.origin.left + e.clientX - drag.sx, top: drag.origin.top + e.clientY - drag.sy }, bounds));
  }
  function up(e: PointerEvent) {
    if (!drag || e.pointerId !== drag.pointerId) return;
    drag = null;
    dragging = false;
  }
</script>

<!-- svelte-ignore a11y_no_static_element_interactions -->
<div
  class="win" class:active class:dragging data-faux={spec.id}
  style:transform="translate3d({frame.left}px, {frame.top}px, 0)" style:width="{frame.width}px" style:height="{frame.height}px" style:z-index={z}
  onpointerdown={() => onraise()}
>
  <div class="bar" role="presentation" onpointerdown={down} onpointermove={move} onpointerup={up} onpointercancel={up}>
    <div class="lights">
      <button type="button" class="dot close" aria-label={t('pg.close')} onclick={onclose}></button>
      <span class="dot min"></span><span class="dot max"></span>
    </div>
    <div class="title">{t(spec.titleKey)}</div>
  </div>
  <div class="body {spec.kind}">
    {#if spec.kind === 'browser'}
      <div class="toolbar">
        <ChevronLeft size={16} /><ChevronRight size={16} /><RotateCw size={13} />
        <div class="address"><Lock size={11} />{t('pg.browser.address')}</div>
      </div>
      <div class="page">
        <div class="hero"></div>
        <div class="cols">
          <div class="card"><i></i><b></b><b class="short"></b></div>
          <div class="card"><i></i><b></b><b class="short"></b></div>
          <div class="card"><i></i><b></b><b class="short"></b></div>
        </div>
      </div>
    {:else if spec.kind === 'terminal'}
      <pre><span class="g">you@pc</span> <span class="b">~/projects</span> $ git status
On branch <span class="y">main</span>
Your branch is up to date.

<span class="g">you@pc</span> <span class="b">~/projects</span> $ npm run build
<span class="d">vite v7 building for production...</span>
<span class="g">✓</span> 214 modules transformed.
<span class="g">✓</span> built in 2.31s

<span class="g">you@pc</span> <span class="b">~/projects</span> $ <span class="cursor"></span></pre>
    {:else}
      <div class="lines">
        {#each [92, 100, 78, 96, 0, 88, 100, 64, 0, 94, 82] as w, i (i)}
          {#if w}<i style:width="{w}%"></i>{:else}<br />{/if}
        {/each}
      </div>
    {/if}
  </div>
</div>

<style>
  .win {
    position: absolute; left: 0; top: 0; display: flex; flex-direction: column; border-radius: 12px; overflow: hidden;
    background: var(--glass-bg-strong); border: 0.5px solid var(--glass-edge);
    box-shadow: 0 10px 28px rgba(0, 0, 0, 0.28), 0 0 0 0.5px rgba(0, 0, 0, 0.25);
    will-change: transform;
  }
  .win.active { box-shadow: 0 22px 60px rgba(0, 0, 0, 0.42), 0 0 0 0.5px rgba(0, 0, 0, 0.3); }
  .bar { flex: none; height: 38px; display: flex; align-items: center; padding: 0 13px; background: var(--glass-bg-strong); border-bottom: 0.5px solid var(--separator); cursor: default; touch-action: none; }
  .lights { display: flex; gap: 8px; z-index: 1; }
  .dot { width: 12px; height: 12px; border-radius: 50%; border: 0; padding: 0; background: #ff5f57; box-shadow: inset 0 0 0 0.5px rgba(0, 0, 0, 0.2); }
  .dot.min { background: #febc2e; }
  .dot.max { background: #28c840; }
  .win:not(.active) .dot { background: var(--text-3); }
  .close:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
  .title { position: absolute; left: 70px; right: 70px; text-align: center; font-size: 12.5px; font-weight: 600; color: var(--text-2); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; pointer-events: none; }
  .win.active .title { color: var(--text); }
  .body { flex: 1; min-height: 0; overflow: hidden; }
  /* browser */
  .toolbar { display: flex; align-items: center; gap: 10px; padding: 8px 12px; color: var(--text-2); border-bottom: 0.5px solid var(--separator); }
  .address { flex: 1; display: flex; align-items: center; gap: 6px; height: 26px; padding: 0 12px; border-radius: 13px; background: var(--hover); font-size: 12px; color: var(--text-2); }
  .page { padding: 14px; display: flex; flex-direction: column; gap: 14px; }
  .hero { height: 112px; border-radius: 12px; background: linear-gradient(120deg, color-mix(in oklab, var(--accent) 80%, #fff), #9b6dff 60%, #ff7eb3); }
  .cols { display: grid; grid-template-columns: repeat(3, 1fr); gap: 12px; }
  .card { display: flex; flex-direction: column; gap: 8px; }
  .card i { height: 60px; border-radius: 9px; background: var(--hover); }
  .card b { height: 8px; border-radius: 4px; background: var(--press); }
  .card b.short { width: 60%; }
  /* terminal */
  .body.terminal { background: #1c1d22; color: #d7dae0; }
  pre { margin: 0; padding: 12px 14px; font: 12px/1.55 ui-monospace, 'Cascadia Code', Consolas, monospace; white-space: pre-wrap; }
  .g { color: #7fd962; } .b { color: #6cb6ff; } .y { color: #f0c674; } .d { color: #8a8f9c; }
  .cursor { display: inline-block; width: 7px; height: 14px; background: #d7dae0; vertical-align: -2px; animation: blink 1.1s steps(1) infinite; }
  @keyframes blink { 50% { opacity: 0; } }
  /* notepad */
  .lines { padding: 14px 16px; display: flex; flex-direction: column; gap: 9px; }
  .lines i { display: block; height: 7px; border-radius: 4px; background: var(--press); }
  .lines br { display: block; height: 6px; }
</style>
