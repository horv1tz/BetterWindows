<script lang="ts">
  import './status/popover.css';
  import { onDestroy } from 'svelte';
  import { Music } from '@lucide/svelte';
  import { getBridge } from '$lib/ipc';
  import { hostState } from '$lib/ipc/state.svelte';
  import { defaultMediaState, type PopoverRequest } from '$lib/protocol/generated';
  import PopoverFrame from './PopoverFrame.svelte';
  import { send } from './status/actions';
  import { extrapolatePosition, formatClock } from './status/media';
  import { t } from './status/strings';
  import Cover from './status/Cover.svelte';
  import Slider from './status/Slider.svelte';
  import Transport from './status/Transport.svelte';

  let { request, onclose }: { request: PopoverRequest; onclose: () => void } = $props();

  const media = hostState('media.state', defaultMediaState());

  // Position is extrapolated between host events (the host sends it on every state change, not per second).
  let receivedAt = $state(performance.now());
  let clock = $state(performance.now());
  /** The position the user dragged to, shown until the host reports the new state. */
  let seekTo = $state<number | null>(null);
  /** The thumb position during a drag. */
  let scrubbing = $state<number | null>(null);

  const offEcho = getBridge().on('media.state', () => {
    receivedAt = performance.now();
    clock = receivedAt;
    seekTo = null;
  });
  onDestroy(() => { offEcho(); media.dispose(); });

  $effect(() => {
    if (!media.value.playing) return;
    const id = setInterval(() => (clock = performance.now()), 250);
    return () => clearInterval(id);
  });

  const m = $derived(media.value);
  const duration = $derived(m.durationSec > 0 ? m.durationSec : 0);
  const extrapolated = $derived(extrapolatePosition({ positionSec: m.positionSec, durationSec: m.durationSec, playing: m.playing }, clock - receivedAt));
  const position = $derived(scrubbing ?? seekTo ?? extrapolated);
  const canSeek = $derived(m.canSeek && duration > 0);

  function transport(action: 'previous' | 'next' | 'pause' | 'play') {
    void send('media.control', { action });
  }
  function seek(v: number) {
    scrubbing = null;
    seekTo = v;
    void send('media.control', { action: 'seek', positionSec: v });
  }
</script>

<PopoverFrame anchor={request.anchor} placement="below-end" width={300} label={t('med.label')}>
  <div class="sp media">
    {#if m.available && m.title}
      <div class="art"><Cover src={m.thumbUrl} size={236} radius={14} /></div>
      <div class="meta">
        <div class="sp-title big">{m.title}</div>
        <div class="sp-sub">{[m.artist, m.album].filter(Boolean).join(' — ') || m.appName || ''}</div>
        {#if m.appName}<span class="sp-pill">{t('med.from', { app: m.appName })}</span>{/if}
      </div>

      <div class="scrub">
        <Slider
          thin
          label={t('med.position')}
          value={position}
          min={0}
          max={duration || 1}
          step={1}
          disabled={!canSeek}
          valueText={formatClock(position)}
          oninput={(v) => (scrubbing = v)}
          onchange={seek}
        />
        <div class="times sp-small" aria-hidden="true">
          <span>{formatClock(position)}</span>
          {#if duration > 0}<span>{t('med.remaining', { time: formatClock(Math.max(0, duration - position)) })}</span>{/if}
        </div>
        {#if !m.canSeek}<div class="sp-small note">{t('med.noSeek')}</div>{/if}
      </div>

      <Transport
        large
        playing={m.playing}
        canPlay={m.canPlay}
        canPause={m.canPause}
        canPrevious={m.canPrevious}
        canNext={m.canNext}
        onprevious={() => transport('previous')}
        ontoggle={() => transport(m.playing ? 'pause' : 'play')}
        onnext={() => transport('next')}
      />
    {:else}
      <div class="empty">
        <span class="empty-glyph" aria-hidden="true"><Music size={26} strokeWidth={1.6} /></span>
        <div class="sp-title">{t('med.nothing')}</div>
        <div class="sp-sub hint">{t('med.nothingHint')}</div>
      </div>
    {/if}
  </div>
</PopoverFrame>

<style>
  .media { gap: 10px; padding: 4px 4px 6px; justify-items: stretch; }
  .art { display: grid; justify-content: center; }
  .art :global(img), .art :global(.ph) { box-shadow: 0 8px 24px rgba(0, 0, 0, 0.3); }
  .meta { display: grid; justify-items: center; gap: 2px; text-align: center; padding: 0 8px; min-width: 0; }
  .big { font-size: 15px; max-width: 100%; }
  .meta .sp-sub { max-width: 100%; }
  .scrub { display: grid; gap: 0; padding: 0 6px; }
  .times { display: flex; justify-content: space-between; font-variant-numeric: tabular-nums; margin-top: -4px; }
  .note { padding-top: 2px; }
  .empty { display: grid; justify-items: center; gap: 6px; padding: 26px 16px 22px; text-align: center; }
  .empty-glyph { display: grid; place-items: center; width: 56px; height: 56px; border-radius: 50%; background: var(--sp-card); color: var(--text-2); margin-bottom: 4px; }
  .hint { white-space: normal; max-width: 220px; line-height: 1.35; }
</style>
