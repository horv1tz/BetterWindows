<script lang="ts">
  import Zap from '@lucide/svelte/icons/zap';
  import Headphones from '@lucide/svelte/icons/headphones';
  import Mouse from '@lucide/svelte/icons/mouse';
  import Keyboard from '@lucide/svelte/icons/keyboard';
  import Smartphone from '@lucide/svelte/icons/smartphone';
  import Watch from '@lucide/svelte/icons/watch';
  import Gamepad2 from '@lucide/svelte/icons/gamepad-2';
  import Speaker from '@lucide/svelte/icons/speaker';
  import Bluetooth from '@lucide/svelte/icons/bluetooth';
  import Laptop from '@lucide/svelte/icons/laptop';
  import PlugZap from '@lucide/svelte/icons/plug-zap';
  import type { Component } from 'svelte';
  import { defaultBatteryState, defaultBluetoothState } from '$lib/protocol/generated';
  import { useHostState } from '../shared.svelte';
  import { batteryTone, deviceKind, devicesWithBattery, splitMinutes, type DeviceKind } from '../mediaLogic';
  import { t } from '../strings';
  import type { WidgetProps } from './registry';

  let { size }: WidgetProps = $props();

  const battery = useHostState('status.battery', defaultBatteryState());
  const bluetooth = useHostState('status.bluetooth', defaultBluetoothState());

  const KIND_ICONS: Record<DeviceKind, Component<{ size?: number; strokeWidth?: number }>> = {
    headphones: Headphones, mouse: Mouse, keyboard: Keyboard, phone: Smartphone, watch: Watch, gamepad: Gamepad2, speaker: Speaker, other: Bluetooth,
  };

  const b = $derived(battery.value);
  const devices = $derived(devicesWithBattery(bluetooth.value.devices));
  const limit = $derived({ small: 0, medium: 3, large: 6 }[size]);
  const shown = $derived(devices.slice(0, limit));
  const remaining = $derived(b.present && !b.charging && b.minutesRemaining ? splitMinutes(b.minutesRemaining) : null);
  const status = $derived(
    !b.present ? t('bat.noBattery')
      : b.charging ? t('bat.charging')
        : b.acOnline ? t('bat.pluggedIn')
          : remaining ? (remaining.h > 0 ? t('bat.remaining', { h: remaining.h, m: remaining.m }) : t('bat.remainingMin', { m: remaining.m })) : '',
  );

  const C = 2 * Math.PI * 21; // ring circumference for r=21
  const dash = (pct: number) => `${(Math.max(0, Math.min(100, pct)) / 100) * C} ${C}`;
</script>

<div class="bat {size}" role="group" aria-label={t('bat.title')}>
  {#snippet ring(pct: number, charging: boolean, big: boolean)}
    {@const tone = batteryTone(pct, charging)}
    <svg viewBox="0 0 50 50" class="ring {tone}" class:big aria-hidden="true">
      <circle cx="25" cy="25" r="21" class="bg" />
      <circle cx="25" cy="25" r="21" class="fg" stroke-dasharray={dash(pct)} transform="rotate(-90 25 25)" />
    </svg>
  {/snippet}

  <section class="pc">
    <div class="ringbox big">
      {@render ring(b.present ? b.percent : 0, b.charging, true)}
      <div class="inner">
        {#if b.present}
          {#if b.charging}<span class="bolt"><Zap size={15} fill="currentColor" strokeWidth={0} /></span>{/if}
          <span class="pct" aria-label={t('bat.percent', { n: Math.round(b.percent) })}>{Math.round(b.percent)}<small>%</small></span>
        {:else}
          <PlugZap size={22} strokeWidth={1.7} />
        {/if}
      </div>
    </div>
    <div class="meta">
      <div class="name"><Laptop size={13} strokeWidth={2} />{t('bat.thisPc')}</div>
      <div class="status">{status}</div>
    </div>
  </section>

  {#if size !== 'small'}
    <section class="devices" class:grid={size === 'medium'}>
      {#each shown as d (d.id)}
        {@const K = KIND_ICONS[deviceKind(d.name)]}
        <div class="dev">
          <div class="ringbox">
            {@render ring(d.batteryPercent ?? 0, false, false)}
            <div class="inner"><K size={14} strokeWidth={2} /></div>
          </div>
          <div class="dmeta">
            <span class="dname">{d.name}</span>
            <span class="dpct">{Math.round(d.batteryPercent ?? 0)}%</span>
          </div>
        </div>
      {:else}
        <div class="none">{t('bat.noDevices')}</div>
      {/each}
    </section>
  {/if}
</div>

<style>
  .bat { position: absolute; inset: 0; padding: 14px 16px; display: flex; }
  .bat.small { align-items: center; justify-content: center; }
  .bat.medium { gap: 18px; align-items: center; }
  .bat.large { flex-direction: column; gap: 10px; }
  .pc { display: flex; align-items: center; gap: 12px; }
  .bat.small .pc { flex-direction: column; text-align: center; gap: 8px; }
  .bat.medium .pc { flex-direction: column; text-align: center; width: 128px; flex: none; gap: 8px; }
  .bat.large .pc { padding-bottom: 12px; border-bottom: 0.5px solid var(--separator); }
  .ringbox { position: relative; width: 38px; height: 38px; flex: none; }
  .ringbox.big { width: 84px; height: 84px; }
  .bat.large .ringbox.big { width: 72px; height: 72px; }
  .ring { width: 100%; height: 100%; display: block; }
  .ring .bg { fill: none; stroke: var(--hover); stroke-width: 5; }
  .ring.big .bg { stroke-width: 4.2; }
  .ring .fg { fill: none; stroke: #30d158; stroke-width: 5; stroke-linecap: round; transition: stroke-dasharray 600ms var(--ease-out); }
  .ring.big .fg { stroke-width: 4.2; }
  .ring.low .fg { stroke: #ff9f0a; }
  .ring.critical .fg { stroke: #ff453a; }
  .ring.charging .fg { stroke: #30d158; }
  .inner { position: absolute; inset: 0; display: flex; align-items: center; justify-content: center; color: var(--text-2); gap: 1px; }
  .ringbox.big .inner { flex-direction: column; color: var(--text); }
  .pct { font-size: 22px; font-weight: 600; letter-spacing: -0.02em; font-variant-numeric: tabular-nums; line-height: 1; }
  .pct small { font-size: 12px; font-weight: 600; color: var(--text-2); margin-left: 1px; }
  .bolt { color: #30d158; display: grid; margin-bottom: 1px; }
  .name { display: flex; align-items: center; justify-content: center; gap: 5px; font-size: 13px; font-weight: 600; }
  .status { font-size: 12px; color: var(--text-2); margin-top: 1px; }
  .devices { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 8px; }
  .bat.medium .devices { gap: 9px; border-left: 0.5px solid var(--separator); padding-left: 18px; align-self: stretch; justify-content: center; }
  .bat.large .devices { gap: 8px; overflow: hidden; }
  .dev { display: flex; align-items: center; gap: 10px; min-width: 0; }
  .dmeta { display: flex; flex-direction: column; min-width: 0; flex: 1; }
  .bat.large .dmeta, .bat.medium .dmeta { flex-direction: row; align-items: baseline; justify-content: space-between; gap: 8px; }
  .dname { font-size: 13px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .dpct { font-size: 13px; font-weight: 600; color: var(--text-2); font-variant-numeric: tabular-nums; }
  .none { color: var(--text-2); font-size: 12px; text-align: center; margin: auto 0; }
</style>
