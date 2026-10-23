<script lang="ts">
  import './status/popover.css';
  import { Bluetooth, Headphones, Keyboard, Mouse } from '@lucide/svelte';
  import { hostState } from '$lib/ipc/state.svelte';
  import { defaultBluetoothState, type PopoverRequest } from '$lib/protocol/generated';
  import PopoverFrame from './PopoverFrame.svelte';
  import { openSettings } from './status/actions';
  import { batteryChip } from './status/format';
  import { t } from './status/strings';

  let { request, onclose }: { request: PopoverRequest; onclose: () => void } = $props();

  const bt = hostState('status.bluetooth', defaultBluetoothState());
  const state = $derived(!bt.value.supported ? 'unsupported' : !bt.value.enabled ? 'off' : 'on');

  /** Device glyph from its name (keyboards and mice get their own glyphs, everything else a generic Bluetooth mark). */
  function glyph(name: string) {
    if (/mouse|mx |master/i.test(name)) return Mouse;
    if (/keyboard|keychron|k\d/i.test(name)) return Keyboard;
    if (/head|buds|xm\d|wh-|earbud/i.test(name)) return Headphones;
    return Bluetooth;
  }
</script>

<PopoverFrame anchor={request.anchor} placement="below-end" width={280} label={t('bt.label')}>
  <div class="sp bt">
    <div class="sp-row head">
      <span class="sp-title sp-grow">{t('bt.label')}</span>
      <span class="sp-pill"><span class="sp-dot" class:ok={state === 'on'}></span>{state === 'on' ? t('common.on') : state === 'off' ? t('common.off') : t('cc.bluetooth.unavailable')}</span>
    </div>

    {#if state === 'unsupported'}
      <div class="sp-card sp-empty">{t('bt.unsupported')}</div>
    {:else if state === 'off'}
      <div class="sp-card sp-empty">{t('bt.off')}</div>
    {:else}
      <div class="sp-section">{t('bt.paired')}</div>
      <div class="sp-card list">
        {#each bt.value.devices as d (d.id)}
          {@const Icon = glyph(d.name)}
          {@const chip = batteryChip(d.batteryPercent, t)}
          <div class="dev">
            <span class="glyph" class:on={d.connected} aria-hidden="true"><Icon size={16} /></span>
            <div class="sp-grow info">
              <div class="sp-title">{d.name}</div>
              <div class="sp-sub">{d.connected ? t('bt.connected') : t('bt.notConnected')}</div>
            </div>
            {#if chip && d.connected}<span class="sp-pill" aria-label={chip}>{chip}</span>{/if}
          </div>
        {:else}
          <div class="sp-empty">{t('bt.noDevices')}</div>
        {/each}
      </div>
    {/if}

    <div class="sp-sep"></div>
    <button type="button" class="sp-link" onclick={() => { void openSettings('ms-settings:bluetooth'); onclose(); }}>
      <span>{t('bt.settings')}</span>
    </button>
  </div>
</PopoverFrame>

<style>
  .bt { gap: 6px; }
  .head { padding: 2px 6px 0; }
  .list { padding: 4px; gap: 2px; }
  .dev { display: flex; align-items: center; gap: 10px; padding: 6px; border-radius: 8px; min-width: 0; }
  .dev:hover { background: var(--hover); }
  .glyph { display: grid; place-items: center; width: 30px; height: 30px; border-radius: 50%; background: var(--sp-track); color: var(--text-2); flex: none; }
  .glyph.on { color: var(--text); }
  .info { min-width: 0; }
</style>
