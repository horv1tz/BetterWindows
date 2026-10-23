<script lang="ts">
  // One window in the Mission Control grid: thumbnail (or an icon tile while loading / when it fails), title and close control.
  import { X } from '@lucide/svelte';
  import AppIcon from '$lib/components/AppIcon.svelte';
  import type { McWindow } from '$lib/protocol/generated';
  import { hueFor } from './model';
  import { CARD_LABEL_H } from './layout';
  import { t } from './strings';

  interface Props {
    win: McWindow;
    /** Viewport box of the thumbnail. */
    box: { x: number; y: number; w: number; h: number };
    frontmost?: boolean;
    /** A card that is leaving the grid: purely visual, not focusable. */
    ghost?: boolean;
    onregister?: (id: string, el: HTMLElement, on: boolean) => void;
    onpress?: (e: PointerEvent, win: McWindow) => void;
    onactivate?: (win: McWindow) => void;
    onclose?: (win: McWindow) => void;
    ondone?: () => void;
  }
  let { win, box, frontmost = false, ghost = false, onregister, onpress, onactivate, onclose, ondone }: Props = $props();

  // Thumbnail state is derived from the URL it was loaded for, so a re-render with the same URL never resets to loading.
  let loadedUrl = $state<string | null>(null);
  let failedUrl = $state<string | null>(null);
  const url = $derived(win.thumbUrl ?? null);
  const status = $derived<'none' | 'loading' | 'ok' | 'error'>(
    url === null ? 'none' : loadedUrl === url ? 'ok' : failedUrl === url ? 'error' : 'loading',
  );
  const hue = $derived(hueFor(win.appId || win.appName));
  const iconSize = $derived(Math.round(Math.max(32, Math.min(96, Math.min(box.w, box.h) * 0.28))));

  /** Image load tracking: listeners first, then the already-complete case (cached images never fire load again). */
  function watchImage(node: HTMLImageElement, src: string) {
    const ok = () => (loadedUrl = src);
    const bad = () => (failedUrl = src);
    node.addEventListener('load', ok);
    node.addEventListener('error', bad);
    if (node.complete) (node.naturalWidth > 0 ? ok : bad)();
    return {
      destroy() {
        node.removeEventListener('load', ok);
        node.removeEventListener('error', bad);
      },
    };
  }

  /** Registers the live card element for focus and FLIP; ghosts register nothing. */
  function track(node: HTMLElement, id: string | undefined) {
    if (id !== undefined) onregister?.(id, node, true);
    return {
      destroy() {
        if (id !== undefined) onregister?.(id, node, false);
      },
    };
  }

  /** Ghost cards fade out and report back when done, so the grid can drop them. */
  function fadeOut(node: HTMLElement, isGhost: boolean) {
    if (!isGhost) return {};
    if (typeof node.animate !== 'function') {
      ondone?.();
      return {};
    }
    const anim = node.animate(
      [{ opacity: 1, transform: 'scale(1)' }, { opacity: 0, transform: 'scale(0.94)' }],
      { duration: 180, easing: 'cubic-bezier(0.2, 0.8, 0.2, 1)', fill: 'forwards' },
    );
    anim.finished.then(() => ondone?.()).catch(() => ondone?.());
    return {};
  }

  const label = $derived(t('mc.window', { title: win.title, app: win.appName }));
</script>

<div
  class="card"
  class:frontmost
  class:ghost
  class:minimized={win.minimized}
  style:left="{box.x}px"
  style:top="{box.y}px"
  style:width="{box.w}px"
  style:height="{box.h + CARD_LABEL_H}px"
  style:--hue={hue}
  use:track={ghost ? undefined : win.handle}
  use:fadeOut={ghost}
>
  <button
    type="button"
    class="hit"
    data-focusable={ghost ? undefined : ''}
    data-card={ghost ? undefined : win.handle}
    tabindex={ghost ? -1 : 0}
    aria-label={label}
    aria-current={frontmost ? 'true' : undefined}
    onpointerdown={(e) => { if (e.button === 0) onpress?.(e, win); }}
    onmousedown={(e) => { if (e.button === 1 && !ghost) { e.preventDefault(); onclose?.(win); } }}
    onclick={() => onactivate?.(win)}
  >
    <span class="thumb">
      {#if url !== null}
        {#key url}
          <img src={url} alt="" draggable="false" class:shown={status === 'ok'} use:watchImage={url} />
        {/key}
      {/if}
      {#if status !== 'ok'}
        <span class="fallback" class:shimmer={status === 'loading'}>
          {#if status !== 'loading'}
            <AppIcon src={win.iconUrl} name={win.appName} size={iconSize} />
          {/if}
        </span>
      {/if}
      {#if win.minimized}
        <span class="badge">{t('mc.minimized')}</span>
      {/if}
    </span>
    <span class="label">
      <span class="app"><AppIcon src={win.iconUrl} name={win.appName} size={16} radius={0.25} /></span>
      <span class="title">{win.title}</span>
    </span>
  </button>
  {#if !ghost}
    <button
      type="button"
      class="close"
      tabindex="-1"
      aria-label={t('mc.close', { title: win.title })}
      onpointerdown={(e) => e.stopPropagation()}
      onclick={(e) => { e.stopPropagation(); onclose?.(win); }}
    ><X size={12} strokeWidth={2.5} /></button>
  {/if}
</div>

<style>
  .card {
    position: absolute;
    left: 0;
    top: 0;
    will-change: transform;
  }
  .card.ghost { pointer-events: none; }

  .hit {
    all: unset;
    box-sizing: border-box;
    display: flex;
    flex-direction: column;
    width: 100%;
    height: 100%;
    cursor: default;
    border-radius: 10px;
  }
  .hit:focus-visible { outline: none; }

  .thumb {
    position: relative;
    flex: 1 1 auto;
    min-height: 0;
    overflow: hidden;
    border-radius: 10px;
    background: color-mix(in oklab, hsl(var(--hue) 45% 45%) 30%, transparent);
    box-shadow: var(--shadow-sm);
    transition: transform var(--dur-fast) var(--ease-out), box-shadow var(--dur-fast) var(--ease-out);
    transform-origin: center;
  }
  .hit:hover .thumb { transform: translateY(-3px) scale(1.015); box-shadow: var(--shadow-md); }
  .hit:focus-visible .thumb,
  .frontmost .thumb { box-shadow: 0 0 0 2px var(--accent), var(--shadow-md); }
  .hit:focus-visible .thumb { box-shadow: 0 0 0 3px var(--accent), 0 0 0 5px color-mix(in oklab, var(--accent) 35%, transparent), var(--shadow-md); }
  .minimized .thumb { opacity: 0.78; }

  img {
    position: absolute;
    inset: 0;
    width: 100%;
    height: 100%;
    object-fit: fill;
    opacity: 0;
    transition: opacity var(--dur) var(--ease-out);
    user-select: none;
    -webkit-user-drag: none;
  }
  img.shown { opacity: 1; }

  .fallback {
    position: absolute;
    inset: 0;
    display: grid;
    place-items: center;
    background:
      linear-gradient(160deg, hsl(var(--hue) 70% 62% / 0.38), hsl(calc(var(--hue) + 40) 65% 38% / 0.5)),
      var(--glass-bg);
  }
  .shimmer { overflow: hidden; background: var(--hover); }
  .shimmer::after {
    content: '';
    position: absolute;
    inset: 0;
    background: linear-gradient(100deg, transparent 30%, rgba(255, 255, 255, 0.28) 50%, transparent 70%);
    transform: translateX(-100%);
    animation: shimmer 1.3s var(--ease-out) infinite;
  }
  @keyframes shimmer { to { transform: translateX(100%); } }
  :root[data-reduce-motion='true'] .shimmer::after { animation: none; }

  .badge {
    position: absolute;
    left: 8px;
    bottom: 8px;
    padding: 2px 7px;
    border-radius: 999px;
    font-size: 11px;
    color: var(--text);
    background: var(--glass-bg-strong);
    border: 0.5px solid var(--glass-border);
  }

  .label {
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 6px;
    height: 26px;
    min-width: 0;
    padding: 0 4px;
    margin-top: 2px;
  }
  .app { flex: none; display: grid; }
  .title {
    min-width: 0;
    overflow: hidden;
    white-space: nowrap;
    text-overflow: ellipsis;
    font-size: 12px;
    font-weight: 500;
    color: var(--text);
    text-shadow: 0 1px 2px rgba(0, 0, 0, 0.35);
  }

  .close {
    position: absolute;
    left: -7px;
    top: -7px;
    z-index: 1;
    display: grid;
    place-items: center;
    width: 22px;
    height: 22px;
    padding: 0;
    border-radius: 50%;
    border: 0.5px solid var(--glass-border);
    background: var(--glass-bg-strong);
    color: var(--text);
    box-shadow: var(--shadow-sm);
    opacity: 0;
    transform: scale(0.8);
    transition: opacity var(--dur-fast) var(--ease-out), transform var(--dur-fast) var(--ease-out);
    cursor: default;
  }
  .card:hover .close,
  .card:focus-within .close { opacity: 1; transform: scale(1); }
  .close:hover { background: var(--press); }
  .close:focus-visible { outline: 2px solid var(--accent); outline-offset: 1px; opacity: 1; transform: scale(1); }
</style>
