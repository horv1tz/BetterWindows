<script lang="ts">
  import './status/popover.css';
  import { EthernetPort, Smartphone, WifiOff } from '@lucide/svelte';
  import { hostState } from '$lib/ipc/state.svelte';
  import { defaultNetworkState, type PopoverRequest } from '$lib/protocol/generated';
  import PopoverFrame from './PopoverFrame.svelte';
  import { openSettings } from './status/actions';
  import { networkLabel, signalBars } from './status/format';
  import { t } from './status/strings';

  let { request, onclose }: { request: PopoverRequest; onclose: () => void } = $props();

  const network = hostState('status.network', defaultNetworkState());
  const label = $derived(networkLabel(network.value));
  const connected = $derived(label !== 'offline');
  const bars = $derived(signalBars(network.value.signalBars));
  const title = $derived(
    label === 'wifi' ? t('net.wifi')
      : label === 'ethernet' ? t('net.ethernet')
      : label === 'cellular' ? t('net.cellular')
      : label === 'other' ? t('net.other')
      : t('net.offline'),
  );
  const subtitle = $derived(
    label === 'wifi' ? (network.value.name || t('net.unnamed'))
      : connected ? t('net.other') : '',
  );
  const BAR_HEIGHTS = [4, 7, 10, 13];
</script>

<PopoverFrame anchor={request.anchor} placement="below-end" width={280} label={t('net.label')}>
  <div class="sp net">
    <div class="sp-card">
      <div class="sp-row">
        <span class="glyph" class:on={connected} aria-hidden="true">
          {#if label === 'wifi'}
            <svg class="signal" viewBox="0 0 17 14">
              {#each BAR_HEIGHTS as h, i (i)}
                <rect x={i * 4.4 + 0.5} y={14 - h} width="3" height={h} rx="1" class:on={i < bars} />
              {/each}
            </svg>
          {:else if label === 'ethernet'}
            <EthernetPort size={17} />
          {:else if label === 'cellular'}
            <Smartphone size={17} />
          {:else}
            <WifiOff size={17} />
          {/if}
        </span>
        <div class="sp-grow">
          <div class="sp-title">{title}</div>
          {#if subtitle}<div class="sp-sub">{subtitle}</div>{/if}
        </div>
        <span class="sp-dot" class:ok={connected}></span>
      </div>
      {#if label === 'wifi' && connected}
        <div class="sp-small" aria-label={t('net.signal', { n: bars })}>{t('net.signal', { n: bars })}</div>
      {/if}
    </div>
    <div class="sp-sep"></div>
    <button type="button" class="sp-link" onclick={() => { void openSettings('ms-settings:network'); onclose(); }}>
      <span>{t('net.settings')}</span>
    </button>
  </div>
</PopoverFrame>

<style>
  .net { gap: 6px; }
  .sp-row { gap: 12px; }
  .glyph { display: grid; place-items: center; width: 34px; height: 34px; border-radius: 50%; background: var(--sp-track); flex: none; color: var(--text-2); }
  .glyph.on { color: var(--text); }
  .signal { width: 17px; height: 14px; }
  .signal rect { fill: var(--sp-track); }
  .signal rect.on { fill: var(--text); }
</style>
