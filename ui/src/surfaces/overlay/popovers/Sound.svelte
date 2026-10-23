<script lang="ts">
  import './status/popover.css';
  import { Check, Headphones, Monitor, Speaker, Volume, Volume1, Volume2, VolumeX } from '@lucide/svelte';
  import { onDestroy } from 'svelte';
  import { getBridge } from '$lib/ipc';
  import { hostState } from '$lib/ipc/state.svelte';
  import { defaultAudioState, type PopoverRequest } from '$lib/protocol/generated';
  import PopoverFrame from './PopoverFrame.svelte';
  import { openSettings, send } from './status/actions';
  import { optimistic } from './status/optimistic.svelte';
  import { throttle } from './status/throttle';
  import { percentOf, volumeTier } from './status/format';
  import { t } from './status/strings';
  import Slider from './status/Slider.svelte';

  let { request, onclose }: { request: PopoverRequest; onclose: () => void } = $props();

  const audio = hostState('status.audio', defaultAudioState());
  const vol = optimistic({ tolerance: 0.005 });
  let deviceError = $state(false);

  const sendVolume = throttle((volume: number) => void send('audio.setVolume', { volume }), 50);
  const offEcho = getBridge().on('status.audio', (a) => vol.host(a.volume));
  onDestroy(() => { offEcho(); sendVolume.flush(); audio.dispose(); });

  const level = $derived(vol.value ?? audio.value.volume);
  const tier = $derived(volumeTier(level, audio.value.muted));
  const VolumeIcon = $derived(tier === 'muted' ? VolumeX : tier === 'low' ? Volume : tier === 'medium' ? Volume1 : Volume2);

  /** Device glyph from its name: headphones, a display, or a speaker. */
  function deviceIcon(name: string) {
    if (/head|airpods|buds|earbud|WH-|WF-/i.test(name)) return Headphones;
    if (/hdmi|display|monitor|dell|lg |benq|aoc|samsung/i.test(name)) return Monitor;
    return Speaker;
  }

  async function pick(id: string) {
    deviceError = false;
    if (!(await send('audio.setDevice', { deviceId: id }))) deviceError = true;
  }
</script>

<PopoverFrame anchor={request.anchor} placement="below-end" width={280} label={t('snd.label')}>
  <div class="sp snd">
    <div class="sp-card">
      <div class="sp-row">
        <button
          type="button"
          class="sp-icon-btn"
          aria-label={audio.value.muted ? t('cc.unmute') : t('cc.mute')}
          aria-pressed={audio.value.muted}
          onclick={() => void send('audio.setMuted', { muted: !audio.value.muted })}
        ><VolumeIcon size={17} /></button>
        <div class="sp-grow">
          <Slider
            label={t('snd.volume')}
            value={level}
            valueText={audio.value.muted ? t('snd.muted') : `${percentOf(level)}%`}
            oninput={(v) => { vol.set(v); sendVolume(v); }}
            onchange={(v) => { vol.set(v); sendVolume(v); sendVolume.flush(); }}
          />
        </div>
        <span class="sp-small pct">{audio.value.muted ? t('snd.muted') : `${percentOf(level)}%`}</span>
      </div>
    </div>

    <div class="sp-section">{t('snd.devices')}</div>
    <div class="sp-card list" role="radiogroup" aria-label={t('snd.devices')}>
      {#each audio.value.devices as d (d.id)}
        {@const Icon = deviceIcon(d.name)}
        <button type="button" role="radio" aria-checked={d.isDefault} class="dev" class:on={d.isDefault} onclick={() => pick(d.id)}>
          <Icon size={16} />
          <span class="dev-name sp-grow">{d.name}</span>
          {#if d.isDefault}<Check size={15} aria-label={t('snd.default')} />{/if}
        </button>
      {:else}
        <div class="sp-empty">{t('snd.noDevices')}</div>
      {/each}
      {#if deviceError}<div class="sp-error" role="alert">{t('cc.setDeviceFailed')}</div>{/if}
    </div>

    <div class="sp-sep"></div>
    <button type="button" class="sp-link" onclick={() => { void openSettings('ms-settings:sound'); onclose(); }}>
      <span>{t('snd.settings')}</span>
    </button>
  </div>
</PopoverFrame>

<style>
  .snd { gap: 6px; }
  .list { padding: 4px; gap: 2px; }
  .dev {
    display: flex; align-items: center; gap: 9px; padding: 7px 8px; border-radius: 8px; text-align: left; min-width: 0;
    font-size: 12.5px; transition: background var(--dur-fast) var(--ease-out);
  }
  .dev:hover { background: var(--hover); }
  .dev.on { font-weight: 600; }
  .dev-name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .pct { min-width: 34px; text-align: right; font-variant-numeric: tabular-nums; }
</style>
