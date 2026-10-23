<script lang="ts">
  import { untrack } from 'svelte';
  import Sun from '@lucide/svelte/icons/sun';
  import Moon from '@lucide/svelte/icons/moon';
  import CloudSun from '@lucide/svelte/icons/cloud-sun';
  import CloudMoon from '@lucide/svelte/icons/cloud-moon';
  import Cloud from '@lucide/svelte/icons/cloud';
  import CloudFog from '@lucide/svelte/icons/cloud-fog';
  import CloudDrizzle from '@lucide/svelte/icons/cloud-drizzle';
  import CloudRain from '@lucide/svelte/icons/cloud-rain';
  import CloudRainWind from '@lucide/svelte/icons/cloud-rain-wind';
  import CloudSunRain from '@lucide/svelte/icons/cloud-sun-rain';
  import CloudMoonRain from '@lucide/svelte/icons/cloud-moon-rain';
  import CloudSnow from '@lucide/svelte/icons/cloud-snow';
  import Snowflake from '@lucide/svelte/icons/snowflake';
  import CloudLightning from '@lucide/svelte/icons/cloud-lightning';
  import CloudHail from '@lucide/svelte/icons/cloud-hail';
  import MapPin from '@lucide/svelte/icons/map-pin';
  import TriangleAlert from '@lucide/svelte/icons/triangle-alert';
  import RefreshCw from '@lucide/svelte/icons/refresh-cw';
  import Droplets from '@lucide/svelte/icons/droplets';
  import Wind from '@lucide/svelte/icons/wind';
  import type { Component } from 'svelte';
  import { getBridge } from '$lib/ipc';
  import { liveSettings, locale, use24Hour } from '../shared.svelte';
  import { weatherStore } from '../weatherStore.svelte';
  import { t } from '../strings';
  import {
    convertWind, describeWeather, forecastRows, formatHourLabel, formatTemp, isCurrentHour, parseLocalIso, rangeBar, sameDay, upcomingHours, weatherView,
    type WeatherIconKey,
  } from '../weatherLogic';
  import type { WidgetProps } from './registry';

  let { size }: WidgetProps = $props();

  const ICONS: Record<WeatherIconKey, Component<{ size?: number; strokeWidth?: number }>> = {
    sun: Sun, moon: Moon, 'cloud-sun': CloudSun, 'cloud-moon': CloudMoon, cloud: Cloud, 'cloud-fog': CloudFog, 'cloud-drizzle': CloudDrizzle,
    'cloud-rain': CloudRain, 'cloud-rain-wind': CloudRainWind, 'cloud-sun-rain': CloudSunRain, 'cloud-moon-rain': CloudMoonRain,
    'cloud-snow': CloudSnow, snowflake: Snowflake, 'cloud-lightning': CloudLightning, 'cloud-hail': CloudHail,
  };

  const settings = liveSettings();
  const unit = $derived(settings.value.weather.unit);
  const hasLocation = $derived(typeof settings.value.weather.latitude === 'number' && typeof settings.value.weather.longitude === 'number');
  const locKey = $derived(`${settings.value.weather.locationName}|${settings.value.weather.latitude}|${settings.value.weather.longitude}`);

  $effect(() => weatherStore.use());
  $effect(() => { const k = locKey; untrack(() => weatherStore.locationChanged(k)); });

  const snap = $derived(weatherStore.snapshot);
  const view = $derived(weatherView({ loading: weatherStore.loading, snapshot: snap, failed: weatherStore.failed && !snap?.available, hasLocation }));

  // the card follows the clock only for choosing "now" in the hourly strip / forecast
  let now = $state(new Date());
  $effect(() => { const id = setInterval(() => (now = new Date()), 60_000); return () => clearInterval(id); });

  const desc = $derived(snap ? describeWeather(snap.code, snap.isDay) : null);
  const Icon = $derived(desc ? ICONS[desc.icon] : Cloud);
  const mood = $derived(view === 'ok' && desc ? desc.mood : 'cloudy');
  const today = $derived(snap?.daily.find((d) => { const dt = parseLocalIso(d.date); return !!dt && sameDay(dt, now); }) ?? snap?.daily[0]);
  const hours = $derived(snap ? upcomingHours(snap.hourly, now, 6) : []);
  const days = $derived(snap ? forecastRows(snap.daily, now, locale(), t('weather.today'), 5) : []);
  const span = $derived({ min: Math.min(...days.map((d) => d.lowC), 0), max: Math.max(...days.map((d) => d.highC), 1) });
  const wind = $derived(snap?.windKph !== undefined ? convertWind(snap.windKph, unit) : null);
  const updated = $derived.by(() => {
    const d = parseLocalIso(snap?.updatedAt);
    return d ? new Intl.DateTimeFormat(locale(), { hour: '2-digit', minute: '2-digit', hour12: !use24Hour() }).format(d) : '';
  });
  const label = $derived(view === 'ok' && snap && desc ? `${snap.location}, ${formatTemp(snap.tempC, unit)}, ${t(desc.labelKey)}` : t('w.name.weather'));

  const openSettings = () => getBridge().call('settings.openWindow', { page: 'widgets' }).catch(() => {});
</script>

<div class="wx {size} {mood}" role="group" aria-label={label}>
  {#if view === 'ok' && snap && desc}
    <div class="top">
      <div class="now">
        <div class="place"><MapPin size={12} strokeWidth={2.4} />{snap.location}</div>
        <div class="temp">{formatTemp(snap.tempC, unit)}</div>
        {#if size === 'small'}
          <div class="cond"><Icon size={15} strokeWidth={2} /><span>{t(desc.labelKey)}</span></div>
          {#if today}<div class="range">{t('weather.high', { t: formatTemp(today.highC, unit) })} · {t('weather.low', { t: formatTemp(today.lowC, unit) })}</div>{/if}
        {/if}
      </div>
      {#if size !== 'small'}
        <div class="right">
          <div class="hero-icon" aria-hidden="true"><Icon size={size === 'large' ? 52 : 40} strokeWidth={1.5} /></div>
          <div class="cond"><span>{t(desc.labelKey)}</span></div>
          {#if size === 'medium'}<div class="feels">{t('weather.feelsLike', { t: formatTemp(snap.apparentC, unit) })}</div>{/if}
          {#if today}<div class="range">{t('weather.high', { t: formatTemp(today.highC, unit) })} · {t('weather.low', { t: formatTemp(today.lowC, unit) })}</div>{/if}
        </div>
      {/if}
    </div>

    {#if size === 'medium'}
      <ol class="hours" aria-label={t('weather.hourly')}>
        {#each hours as h, i (h.time)}
          {@const d = describeWeather(h.code, (parseLocalIso(h.time)?.getHours() ?? 12) >= 7 && (parseLocalIso(h.time)?.getHours() ?? 12) < 19)}
          {@const HIcon = ICONS[d.icon]}
          <li class:current={isCurrentHour(h.time, now)}>
            <span class="hl">{i === 0 ? t('weather.now') : formatHourLabel(h.time, locale(), use24Hour())}</span>
            <HIcon size={18} strokeWidth={1.9} />
            <span class="ht">{formatTemp(h.tempC, unit)}</span>
          </li>
        {/each}
      </ol>
    {/if}

    {#if size === 'large'}
      <div class="meta">
        <span><Droplets size={13} />{snap.humidity !== undefined ? t('weather.humidity', { n: snap.humidity }) : ''}</span>
        <span><Wind size={13} />{wind ? t('weather.wind', { v: wind.value, u: wind.unit }) : ''}</span>
        <span>{t('weather.feelsLike', { t: formatTemp(snap.apparentC, unit) })}</span>
      </div>
      <ol class="hours wide" aria-label={t('weather.hourly')}>
        {#each hours as h, i (h.time)}
          {@const hh = parseLocalIso(h.time)?.getHours() ?? 12}
          {@const d = describeWeather(h.code, hh >= 7 && hh < 19)}
          {@const HIcon = ICONS[d.icon]}
          <li>
            <span class="hl">{i === 0 ? t('weather.now') : formatHourLabel(h.time, locale(), use24Hour())}</span>
            <HIcon size={19} strokeWidth={1.9} />
            <span class="ht">{formatTemp(h.tempC, unit)}</span>
          </li>
        {/each}
      </ol>
      <ul class="days" aria-label={t('weather.daily')}>
        {#each days as d (d.label + d.highC)}
          {@const DIcon = ICONS[describeWeather(d.code, true).icon]}
          {@const bar = rangeBar(d.lowC, d.highC, span.min, span.max)}
          <li>
            <span class="dn">{d.label}</span>
            <DIcon size={17} strokeWidth={1.9} />
            <span class="lo">{formatTemp(d.lowC, unit)}</span>
            <span class="bar" aria-hidden="true"><i style:left="{bar.left}%" style:width="{bar.width}%"></i></span>
            <span class="hi">{formatTemp(d.highC, unit)}</span>
          </li>
        {/each}
      </ul>
    {/if}
  {:else if view === 'loading'}
    <div class="state" role="status">
      <div class="spinner" aria-hidden="true"></div>
      <div class="title">{t('weather.loading')}</div>
    </div>
  {:else if view === 'noLocation'}
    <div class="state">
      <MapPin size={size === 'small' ? 26 : 32} strokeWidth={1.6} />
      <div class="title">{t('weather.noLocation')}</div>
      {#if size !== 'small'}<div class="hint">{t('weather.noLocationHint')}</div>{/if}
      <button type="button" class="btn" onclick={openSettings}>{t('weather.openSettings')}</button>
    </div>
  {:else}
    <div class="state">
      <TriangleAlert size={size === 'small' ? 26 : 32} strokeWidth={1.6} />
      <div class="title">{t('weather.error')}</div>
      {#if size !== 'small'}<div class="hint">{t('weather.errorHint')}</div>{/if}
      <button type="button" class="btn" onclick={() => weatherStore.refresh(true)}><RefreshCw size={13} strokeWidth={2.2} />{t('weather.retry')}</button>
    </div>
  {/if}
  {#if view === 'ok' && updated && size === 'large'}<div class="updated">{t('weather.updated', { time: updated })}</div>{/if}
</div>

<style>
  .wx {
    position: absolute; inset: 0; padding: 14px 16px; color: #fff; display: flex; flex-direction: column;
    background: linear-gradient(165deg, var(--g1), var(--g2));
    text-shadow: 0 1px 2px rgba(0, 0, 0, 0.16);
    --g1: #5aa9f5; --g2: #2f6fd8;
  }
  .wx.night { --g1: #27345f; --g2: #121a38; }
  .wx.cloudy { --g1: #8499b4; --g2: #5b6f8c; }
  .wx.rain { --g1: #5e7390; --g2: #34475f; }
  .wx.snow { --g1: #a9bfd4; --g2: #7690aa; }
  .wx.storm { --g1: #4b4f74; --g2: #262a45; }
  .wx.fog { --g1: #98a3af; --g2: #707d8a; }
  .top { display: flex; justify-content: space-between; align-items: flex-start; gap: 10px; }
  .now { display: flex; flex-direction: column; align-items: flex-start; min-width: 0; }
  .wx.small .top, .wx.small .now { flex: 1; align-self: stretch; }
  .wx.small .cond { margin-top: auto; }
  .right { display: flex; flex-direction: column; align-items: flex-end; gap: 1px; text-align: right; }
  .place { display: flex; align-items: center; gap: 4px; font-size: 13px; font-weight: 600; max-width: 100%; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .temp { font-size: 50px; font-weight: 300; line-height: 1.02; letter-spacing: -0.03em; margin: 2px 0 0 -2px; }
  .wx.small .temp { font-size: 58px; }
  .cond { display: flex; align-items: center; gap: 6px; font-size: 13px; font-weight: 600; }
  .feels { font-size: 12px; opacity: 0.88; }
  .range { font-size: 12.5px; opacity: 0.9; margin-top: 1px; font-variant-numeric: tabular-nums; }
  .hero-icon { opacity: 0.95; filter: drop-shadow(0 3px 8px rgba(0, 0, 0, 0.22)); margin-bottom: 2px; }
  .hours { list-style: none; margin: 0; padding: 0; display: flex; justify-content: space-between; }
  .wx.medium .hours { position: absolute; left: 12px; right: 12px; bottom: 10px; }
  .hours li { display: flex; flex-direction: column; align-items: center; gap: 4px; min-width: 44px; padding: 5px 0; border-radius: 10px; font-size: 12px; font-variant-numeric: tabular-nums; }
  .hours li.current { background: rgba(255, 255, 255, 0.18); }
  .hl { opacity: 0.88; font-weight: 500; }
  .ht { font-weight: 600; }
  .wx.large { padding-top: 14px; }
  .meta { display: flex; gap: 14px; font-size: 12px; margin-top: 8px; opacity: 0.92; flex-wrap: wrap; }
  .meta span { display: inline-flex; align-items: center; gap: 4px; }
  .hours.wide { margin-top: 10px; padding-top: 8px; border-top: 0.5px solid rgba(255, 255, 255, 0.3); }
  .days { list-style: none; margin: 6px 0 0; padding: 0; display: flex; flex-direction: column; flex: 1; justify-content: space-around; border-top: 0.5px solid rgba(255, 255, 255, 0.3); }
  .days li { display: grid; grid-template-columns: 56px 22px 34px 1fr 34px; align-items: center; gap: 6px; font-size: 13px; font-variant-numeric: tabular-nums; }
  .dn { font-weight: 600; }
  .lo { text-align: right; opacity: 0.75; }
  .hi { font-weight: 600; }
  .bar { position: relative; height: 4px; border-radius: 2px; background: rgba(0, 0, 0, 0.22); }
  .bar i { position: absolute; top: 0; bottom: 0; border-radius: 2px; background: linear-gradient(90deg, #7fd4ff, #ffd36e 60%, #ff9a5a); }
  .updated { position: absolute; left: 16px; bottom: 6px; font-size: 10.5px; opacity: 0.7; }
  .wx.large .days { margin-bottom: 14px; }
  .state { flex: 1; display: flex; flex-direction: column; align-items: center; justify-content: center; text-align: center; gap: 6px; padding: 0 6px; }
  .title { font-size: 14px; font-weight: 600; }
  .hint { font-size: 12px; opacity: 0.85; max-width: 240px; }
  .btn {
    margin-top: 6px; display: inline-flex; align-items: center; gap: 5px; border: 0; border-radius: 999px; padding: 6px 12px; font-size: 12px; font-weight: 600;
    background: rgba(255, 255, 255, 0.22); color: #fff; cursor: default; text-shadow: none; transition: background var(--dur-fast) ease;
  }
  .btn:hover { background: rgba(255, 255, 255, 0.34); }
  .btn:active { background: rgba(255, 255, 255, 0.18); }
  .btn:focus-visible { outline: 2px solid #fff; outline-offset: 2px; }
  .wx.small .btn { padding: 5px 10px; font-size: 11px; }
  .spinner { width: 22px; height: 22px; border-radius: 50%; border: 2.5px solid rgba(255, 255, 255, 0.3); border-top-color: #fff; animation: spin 0.9s linear infinite; }
  @keyframes spin { to { transform: rotate(360deg); } }
</style>
