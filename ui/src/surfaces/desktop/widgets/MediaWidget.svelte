<script lang="ts">
  import { Play, Pause, SkipBack, SkipForward, Music } from '@lucide/svelte';
  import { getBridge } from '$lib/ipc';
  import { defaultMediaState } from '$lib/protocol/generated';
  import { useHostState, callQuiet } from '../shared.svelte';
  import { estimatePosition, formatClock, progressFraction } from '../mediaLogic';
  import { t } from '../strings';
  import type { WidgetProps } from './registry';

  let { size }: WidgetProps = $props();

  const media = useHostState('media.state', defaultMediaState());
  let receivedAt = $state(Date.now());
  let nowMs = $state(Date.now());
  $effect(() => { media.value; receivedAt = Date.now(); nowMs = receivedAt; });
  // advance the progress bar once a second while playing (cheap: one number)
  $effect(() => {
    if (!media.value.playing) return;
    const id = setInterval(() => (nowMs = Date.now()), 1000);
    return () => clearInterval(id);
  });

  const m = $derived(media.value);
  const pos = $derived(estimatePosition(m, receivedAt, nowMs));
  const frac = $derived(progressFraction(pos, m.durationSec));
  const control = (action: 'toggle' | 'next' | 'previous') => callQuiet(getBridge().call('media.control', { action }), `media.control ${action}`);
  const seekTo = (sec: number) => callQuiet(getBridge().call('media.control', { action: 'seek', positionSec: Math.min(m.durationSec, Math.max(0, sec)) }), 'media.control seek');
  function seek(e: MouseEvent) {
    if (!m.canSeek || m.durationSec <= 0) return;
    const r = (e.currentTarget as HTMLElement).getBoundingClientRect();
    seekTo(Math.min(1, Math.max(0, (e.clientX - r.left) / r.width)) * m.durationSec);
  }
  function seekKey(e: KeyboardEvent) {
    if (!m.canSeek) return;
    if (e.key === 'ArrowRight') { e.preventDefault(); seekTo(pos + 5); }
    else if (e.key === 'ArrowLeft') { e.preventDefault(); seekTo(pos - 5); }
  }
</script>

<div class="media {size}" class:idle={!m.available} role="group" aria-label={m.available ? `${m.title ?? t('media.untitled')} — ${m.artist ?? ''}` : t('media.nothing')}>
  {#if m.available}
    {#if m.thumbUrl}<img class="backdrop" src={m.thumbUrl} alt="" draggable="false" />{/if}
    <div class="veil"></div>
    <div class="layout">
      <div class="cover">{#if m.thumbUrl}<img src={m.thumbUrl} alt="" draggable="false" />{:else}<Music size={size === 'large' ? 56 : 30} strokeWidth={1.4} />{/if}</div>
      <div class="info">
        <div class="title">{m.title ?? t('media.untitled')}</div>
        {#if m.artist}<div class="artist">{m.artist}</div>{/if}
        {#if size === 'large' && m.album}<div class="album">{m.album}</div>{/if}
      </div>
      {#if size !== 'small'}
        <div class="progress">
          <span class="time">{formatClock(pos)}</span>
          <div class="track" role="slider" tabindex="0" aria-label={t('media.progress')} aria-valuemin="0" aria-valuemax={Math.round(m.durationSec)} aria-valuenow={Math.round(pos)} aria-valuetext={formatClock(pos)} data-nodrag onclick={seek} onkeydown={seekKey}>
            <i style:transform="scaleX({frac})"></i>
          </div>
          <span class="time">-{formatClock(Math.max(0, m.durationSec - pos))}</span>
        </div>
      {/if}
      <div class="controls">
        {#if size !== 'small'}<button type="button" aria-label={t('media.previous')} disabled={!m.canPrevious} onclick={() => control('previous')}><SkipBack size={18} fill="currentColor" strokeWidth={0} /></button>{/if}
        <button type="button" class="main" aria-label={m.playing ? t('media.pause') : t('media.play')} disabled={m.playing ? !m.canPause : !m.canPlay} onclick={() => control('toggle')}>
          {#if m.playing}<Pause size={size === 'small' ? 18 : 22} fill="currentColor" strokeWidth={0} />{:else}<Play size={size === 'small' ? 18 : 22} fill="currentColor" strokeWidth={0} />{/if}
        </button>
        {#if size !== 'small'}<button type="button" aria-label={t('media.next')} disabled={!m.canNext} onclick={() => control('next')}><SkipForward size={18} fill="currentColor" strokeWidth={0} /></button>{/if}
      </div>
    </div>
  {:else}
    <div class="empty"><Music size={30} strokeWidth={1.4} /><b>{t('media.nothing')}</b>{#if size !== 'small'}<span>{t('media.nothingHint')}</span>{/if}</div>
  {/if}
</div>

<style>
  .media { position: absolute; inset: 0; overflow: hidden; color: #fff; text-shadow: 0 1px 2px rgba(0, 0, 0, 0.25); }
  .media.idle { color: var(--text); text-shadow: none; }
  .backdrop { position: absolute; inset: -30px; width: calc(100% + 60px); height: calc(100% + 60px); object-fit: cover; filter: blur(28px) saturate(1.6); }
  .veil { position: absolute; inset: 0; background: linear-gradient(180deg, rgba(0, 0, 0, 0.18), rgba(0, 0, 0, 0.42)); }
  .layout { position: absolute; inset: 0; padding: 14px; display: grid; gap: 4px 14px; }
  /* small: cover + title stacked, one button */
  .small .layout { grid-template-columns: 1fr auto; grid-template-rows: auto 1fr auto; }
  .small .cover { grid-column: 1 / 3; width: 88px; height: 88px; }
  .small .info { grid-column: 1; grid-row: 3; align-self: end; }
  .small .controls { grid-column: 2; grid-row: 3; align-self: end; }
  /* medium: cover left, text + progress + controls right */
  .medium .layout { grid-template-columns: 120px 1fr; grid-template-rows: 1fr auto auto; padding: 16px; }
  .medium .cover { grid-row: 1 / 4; width: 120px; height: 120px; align-self: center; }
  .medium .info { align-self: end; grid-column: 2; }
  .medium .progress { grid-column: 2; }
  .medium .controls { grid-column: 2; justify-content: flex-start; }
  /* large: cover on top */
  .large .layout { grid-template-columns: 1fr; grid-template-rows: auto auto auto auto; justify-items: stretch; align-content: space-between; padding: 18px; }
  .large .cover { width: 220px; height: 220px; justify-self: center; }
  .large .info { text-align: center; }
  .large .controls { justify-content: center; gap: 20px; }
  .cover { border-radius: 12px; overflow: hidden; display: grid; place-items: center; background: rgba(255, 255, 255, 0.18); box-shadow: 0 6px 18px rgba(0, 0, 0, 0.35); }
  .cover img { width: 100%; height: 100%; object-fit: cover; display: block; user-select: none; -webkit-user-drag: none; }
  .info { min-width: 0; }
  .title { font-size: 14px; font-weight: 700; line-height: 1.2; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .artist, .album { font-size: 12.5px; opacity: 0.85; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .large .title { font-size: 17px; }
  .progress { display: flex; align-items: center; gap: 8px; font-size: 10.5px; font-variant-numeric: tabular-nums; opacity: 0.9; }
  .track:focus-visible { outline: 2px solid #fff; outline-offset: 2px; border-radius: 4px; }
  .track { flex: 1; height: 14px; display: flex; align-items: center; cursor: default; position: relative; }
  .track::before { content: ''; position: absolute; left: 0; right: 0; height: 4px; border-radius: 2px; background: rgba(255, 255, 255, 0.3); }
  .track i { position: absolute; left: 0; right: 0; height: 4px; border-radius: 2px; background: #fff; transform-origin: left; transition: transform 900ms linear; }
  .controls { display: flex; align-items: center; gap: 12px; }
  .controls button { display: grid; place-items: center; width: 32px; height: 32px; border: 0; border-radius: 50%; background: none; color: #fff; cursor: default; transition: background var(--dur-fast) ease, transform var(--dur-fast) var(--ease-out); }
  .controls button:hover:not(:disabled) { background: rgba(255, 255, 255, 0.2); }
  .controls button:active:not(:disabled) { transform: scale(0.92); }
  .controls button:disabled { opacity: 0.4; }
  .controls button:focus-visible { outline: 2px solid #fff; outline-offset: 1px; }
  .controls .main { width: 38px; height: 38px; background: rgba(255, 255, 255, 0.24); }
  .empty { position: absolute; inset: 0; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 4px; color: var(--text-2); text-align: center; padding: 14px; font-size: 12px; }
  .empty b { color: var(--text); font-size: 13px; }
</style>
