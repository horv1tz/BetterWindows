<script lang="ts">
  import { clockParts, formatDigital, isValidTimeZone, msToNextTick, zoneCity } from '../clockLogic';
  import { locale, use24Hour } from '../shared.svelte';
  import { t } from '../strings';
  import AnalogFace from './AnalogFace.svelte';
  import type { WidgetProps } from './registry';

  let { config, size }: WidgetProps = $props();

  const style = $derived(config.style === 'digital' ? 'digital' : 'analog');
  const seconds = $derived(config.seconds === true);
  const zone = $derived(typeof config.timezone === 'string' && isValidTimeZone(config.timezone) ? config.timezone : undefined);
  const city = $derived(zoneCity(zone));

  let now = $state(new Date());
  $effect(() => {
    const withSeconds = seconds;
    let timer = 0;
    const tick = () => { now = new Date(); timer = window.setTimeout(tick, msToNextTick(Date.now(), withSeconds)); };
    tick();
    return () => clearTimeout(timer);
  });

  const parts = $derived(clockParts(now, zone));
  const digital = $derived(formatDigital(parts, use24Hour(), seconds));
  const fmt = (o: Intl.DateTimeFormatOptions) => new Intl.DateTimeFormat(locale(), { ...o, ...(zone ? { timeZone: zone } : {}) }).format(now);
  const weekday = $derived(fmt({ weekday: 'long' }));
  const dateText = $derived(fmt({ day: 'numeric', month: 'long' }));
  const label = $derived(`${digital.time}${digital.suffix ? ' ' + digital.suffix : ''}, ${weekday}, ${dateText}`);
</script>

<div class="clock {size} {style}" role="timer" aria-label="{t('w.name.clock')}: {label}">
  {#if style === 'analog'}
    <div class="face"><AnalogFace timeZone={zone} {seconds} /></div>
    {#if size !== 'small'}
      <div class="side">
        {#if city}<div class="city">{city}</div>{:else}<div class="city">{weekday}</div>{/if}
        <div class="time"><span>{digital.time}</span>{#if digital.suffix}<small>{digital.suffix}</small>{/if}</div>
        <div class="date">{city ? `${weekday}, ${dateText}` : dateText}</div>
      </div>
    {:else if city}
      <div class="city small-city">{city}</div>
    {/if}
  {:else}
    <div class="digital-block">
      <div class="city">{city || weekday}</div>
      <div class="time"><span>{digital.time}</span>{#if digital.suffix}<small>{digital.suffix}</small>{/if}</div>
      <div class="date">{city ? `${weekday}, ${dateText}` : dateText}</div>
    </div>
  {/if}
</div>

<style>
  .clock {
    position: absolute; inset: 0; display: flex; align-items: center; justify-content: center; gap: 18px; padding: 14px;
    --face-bg: #fdfdfe; --face-ink: #1d1d1f; --face-tick: rgba(0, 0, 0, 0.38); --face-rim: rgba(0, 0, 0, 0.16);
  }
  :global(:root[data-theme='dark']) .clock { --face-bg: #1d1d21; --face-ink: #f5f5f7; --face-tick: rgba(255, 255, 255, 0.4); --face-rim: rgba(255, 255, 255, 0.2); }
  .face { flex: none; }
  .clock.small .face { width: 152px; height: 152px; }
  .clock.medium .face { width: 150px; height: 150px; }
  .clock.large { flex-direction: column; gap: 14px; }
  .clock.large .face { width: 264px; height: 264px; }
  .small-city { position: absolute; top: 10px; left: 0; right: 0; text-align: center; }
  .side, .digital-block { display: flex; flex-direction: column; min-width: 0; }
  .clock.medium.analog .side { flex: 1; align-items: flex-start; }
  .clock.large .side, .clock.large .digital-block { align-items: center; text-align: center; }
  .city { font-size: 12px; font-weight: 600; letter-spacing: 0.02em; text-transform: uppercase; color: var(--accent); }
  .small-city { color: var(--text-2); font-size: 10.5px; }
  .time { display: flex; align-items: baseline; gap: 5px; font-variant-numeric: tabular-nums; font-weight: 300; letter-spacing: -0.02em; line-height: 1.05; }
  .time small { font-size: 0.34em; font-weight: 600; letter-spacing: 0.02em; color: var(--text-2); }
  .date { color: var(--text-2); font-size: 13px; margin-top: 2px; white-space: nowrap; }
  /* sizes */
  .clock.medium.analog .time { font-size: 40px; }
  .clock.large.analog .time { font-size: 44px; }
  .clock.small.digital .digital-block { align-items: center; text-align: center; }
  .clock.small.digital .time { font-size: 46px; }
  .clock.medium.digital .digital-block { align-items: flex-start; width: 100%; padding: 0 10px; }
  .clock.medium.digital .time { font-size: 76px; }
  .clock.large.digital .time { font-size: 92px; }
  .clock.large.digital .date { font-size: 16px; }
</style>
