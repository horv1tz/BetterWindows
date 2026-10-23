<script lang="ts">
  import { Pause, Play, SkipBack, SkipForward } from '@lucide/svelte';
  import { t } from './strings';

  /** Previous / play-pause / next for Now Playing. Each button is enabled only when the host says the action is possible. */
  interface Props {
    playing: boolean;
    canPlay: boolean;
    canPause: boolean;
    canPrevious: boolean;
    canNext: boolean;
    large?: boolean;
    onprevious: () => void;
    ontoggle: () => void;
    onnext: () => void;
  }
  let { playing, canPlay, canPause, canPrevious, canNext, large = false, onprevious, ontoggle, onnext }: Props = $props();
  const canToggle = $derived(playing ? canPause : canPlay);
  const size = $derived(large ? 22 : 16);
</script>

<div class="transport" class:large>
  <button type="button" class="tp" aria-label={t('cc.prev')} disabled={!canPrevious} onclick={onprevious}>
    <SkipBack size={size} strokeWidth={2.2} />
  </button>
  <button type="button" class="tp main" aria-label={playing ? t('cc.pause') : t('cc.play')} disabled={!canToggle} onclick={ontoggle}>
    {#if playing}<Pause size={size + 4} fill="currentColor" strokeWidth={0} />{:else}<Play size={size + 4} fill="currentColor" strokeWidth={0} />{/if}
  </button>
  <button type="button" class="tp" aria-label={t('cc.next')} disabled={!canNext} onclick={onnext}>
    <SkipForward size={size} strokeWidth={2.2} />
  </button>
</div>

<style>
  .transport { display: flex; align-items: center; justify-content: center; gap: 14px; }
  .transport.large { gap: 26px; }
  .tp {
    display: grid; place-items: center; width: 30px; height: 30px; border-radius: 50%;
    transition: background var(--dur-fast) var(--ease-out);
  }
  .large .tp { width: 40px; height: 40px; }
  .tp.main { width: 36px; height: 36px; }
  .large .tp.main { width: 48px; height: 48px; }
  .tp:hover:not(:disabled) { background: var(--hover); }
  .tp:active:not(:disabled) { background: var(--press); }
</style>
