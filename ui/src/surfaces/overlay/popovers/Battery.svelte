<script lang="ts">
  import './status/popover.css';
  import { BatteryCharging } from '@lucide/svelte';
  import { hostState } from '$lib/ipc/state.svelte';
  import { defaultBatteryState, type PopoverRequest } from '$lib/protocol/generated';
  import PopoverFrame from './PopoverFrame.svelte';
  import { openSettings } from './status/actions';
  import { batteryStatus, batteryTier, batteryTimeLine } from './status/format';
  import { currentLocale } from '$lib/i18n.svelte';
  import { t } from './status/strings';

  let { request, onclose }: { request: PopoverRequest; onclose: () => void } = $props();

  const battery = hostState('status.battery', defaultBatteryState());
  const RADIUS = 33;
  const CIRC = 2 * Math.PI * RADIUS;

  const b = $derived(battery.value);
  const pct = $derived(Math.round(Math.min(100, Math.max(0, b.percent || 0))));
  const status = $derived(batteryStatus(b));
  const tier = $derived(batteryTier(pct, b.charging));
  const headline = $derived.by(() => {
    if (status === 'none') return t('bat.none');
    if (tier === 'low') return t('bat.low');
    return t(
      status === 'charging' ? 'bat.charging'
        : status === 'full' ? 'bat.full'
        : status === 'pluggedIn' ? 'bat.pluggedIn'
        : 'bat.onBattery',
    );
  });
  const timeLine = $derived(batteryTimeLine(b, t, currentLocale()));
</script>

<PopoverFrame anchor={request.anchor} placement="below-end" width={280} label={t('bat.label')}>
  <div class="sp bat">
    <div class="sp-card hero">
      {#if b.present}
        <div class="ring" class:low={tier === 'low'} class:charging={b.charging} role="img" aria-label={`${pct}%`}>
          <svg viewBox="0 0 80 80" aria-hidden="true">
            <circle class="base" cx="40" cy="40" r={RADIUS} />
            <circle
              class="arc"
              cx="40"
              cy="40"
              r={RADIUS}
              stroke-dasharray={CIRC}
              stroke-dashoffset={CIRC * (1 - pct / 100)}
              transform="rotate(-90 40 40)"
            />
          </svg>
          <span class="pct">{pct}%</span>
        </div>
      {:else}
        <span class="no-battery" aria-hidden="true"><BatteryCharging size={34} /></span>
      {/if}
      <div class="info">
        <div class="sp-title" class:warn={tier === 'low' && status !== 'none'}>{headline}</div>
        {#if timeLine}<div class="sp-sub">{timeLine}</div>{/if}
      </div>
    </div>
    <div class="sp-sep"></div>
    <button type="button" class="sp-link" onclick={() => { void openSettings('ms-settings:powersleep'); onclose(); }}>
      <span>{t('bat.settings')}</span>
    </button>
  </div>
</PopoverFrame>

<style>
  .bat { gap: 6px; }
  .hero { grid-template-columns: auto 1fr; align-items: center; gap: 14px; padding: 12px; }
  .ring { position: relative; width: 76px; height: 76px; display: grid; place-items: center; }
  .ring svg { position: absolute; inset: 0; width: 100%; height: 100%; }
  .ring circle { fill: none; stroke-width: 7; }
  .ring .base { stroke: var(--sp-track); }
  .ring .arc { stroke: var(--sp-ok); stroke-linecap: round; transition: stroke-dashoffset var(--dur-slow) var(--ease-out); }
  .ring.low .arc { stroke: var(--sp-warn); }
  .pct { font-size: 17px; font-weight: 600; font-variant-numeric: tabular-nums; letter-spacing: -0.01em; }
  .no-battery { display: grid; place-items: center; width: 76px; height: 76px; color: var(--text-2); }
  .info { display: grid; gap: 3px; min-width: 0; }
  .info .sp-title { font-size: 14px; }
  .info .sp-sub { white-space: normal; }
  .warn { color: var(--sp-warn); }
</style>
