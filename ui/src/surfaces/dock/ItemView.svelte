<script lang="ts">
  import type { DockItem } from '$lib/protocol/generated';
  import AppIcon from '$lib/components/AppIcon.svelte';
  import { hitShape } from '$lib/shape';
  import { hopAmplitude, hopPlan } from './hop';
  import type { DockPos } from './layout';

  /** One app / folder icon: the focusable button, active plate, launch / attention hops. Magnification transforms live on its parents. */
  let {
    item, size, pos, label, tabbable, hidden = false, loop = null, burst = 0, motionOk = true, hit = false,
  }: {
    item: DockItem; size: number; pos: DockPos; label: string; tabbable: boolean;
    /** The icon is being dragged: its slot stays as an empty gap. */
    hidden?: boolean;
    /** Continuous hop: launch placeholder or attention. */
    loop?: 'launching' | 'attention' | null;
    /** Changes whenever a one-shot launch bounce must play. */
    burst?: number;
    motionOk?: boolean;
    /** Report this icon as interactive-but-not-glass (it may overflow the pill while magnified). */
    hit?: boolean;
  } = $props();

  let bounceEl = $state<HTMLElement>();

  $effect(() => {
    const n = burst;
    const el = bounceEl;
    if (!n || !el || !motionOk || typeof el.animate !== 'function') return;
    const plan = hopPlan('launch', pos, hopAmplitude(size));
    const a = el.animate(plan.keyframes, plan.options);
    return () => a.cancel();
  });

  $effect(() => {
    const kind = loop;
    const el = bounceEl;
    if (!kind || !el || !motionOk || typeof el.animate !== 'function') return;
    const plan = hopPlan(kind, pos, hopAmplitude(size));
    const a = el.animate(plan.keyframes, plan.options);
    return () => a.cancel();
  });
</script>

<div class="bounce" class:hidden bind:this={bounceEl}>
  <button
    type="button"
    class="icon"
    class:active={item.active}
    class:launching={item.launching}
    class:attn={item.attention}
    data-key={item.id}
    tabindex={tabbable ? 0 : -1}
    aria-label={label}
    aria-current={item.active ? 'true' : undefined}
    aria-haspopup={item.windows.length > 1 ? 'dialog' : 'menu'}
    draggable="false"
  >
    <span class="plate" aria-hidden="true"></span>
    <span class="art"><AppIcon src={item.iconUrl} name={item.name} {size} /></span>
  </button>
  {#if hit}<span class="hit" aria-hidden="true" use:hitShape={{ radius: size * 0.26 }}></span>{/if}
</div>

<style>
  .bounce { position: absolute; inset: 0; }
  .bounce.hidden { opacity: 0; }
  .icon {
    position: absolute; inset: var(--ext, 0 -3px); /* extends over the gaps: no dead zones between icons */
    margin: 0; padding: 0; border: 0; background: none; cursor: default;
    display: grid; place-items: center; border-radius: 24%;
    -webkit-tap-highlight-color: transparent;
    touch-action: none;
  }
  .icon:focus-visible { outline: 2px solid var(--accent); outline-offset: 1px; }
  .art { display: block; transition: transform 90ms var(--ease-out), filter 90ms var(--ease-out); filter: drop-shadow(0 1px 1.5px rgba(0, 0, 0, 0.28)); }
  .icon:active .art { transform: scale(0.92); filter: brightness(0.82) drop-shadow(0 1px 1px rgba(0, 0, 0, 0.2)); }
  .icon.launching .art { opacity: 0.58; filter: saturate(0.55) drop-shadow(0 1px 1.5px rgba(0, 0, 0, 0.2)); }
  .plate {
    position: absolute; inset: var(--ext-in, 0 3px); border-radius: 25%;
    background: var(--hover); opacity: 0; transition: opacity var(--dur) var(--ease-out);
  }
  .icon.active .plate { opacity: 0.55; }
  /* reduced motion: no hopping, so a flashing app gets a steady accent ring instead */
  :global(:root[data-reduce-motion='true']) .icon.attn .plate { opacity: 1; background: color-mix(in srgb, var(--accent) 38%, transparent); box-shadow: 0 0 0 1.5px var(--accent) inset; }
  .hit { position: absolute; inset: var(--ext, 0 -3px); pointer-events: none; }
</style>
