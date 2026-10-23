<script lang="ts">
  import './status/popover.css';
  import { onDestroy } from 'svelte';
  import { Bluetooth, Check, ChevronDown, Monitor, Moon, Sun, Volume, Volume1, Volume2, VolumeX, Wifi, WifiOff } from '@lucide/svelte';
  import { getBridge, getInit } from '$lib/ipc';
  import { hostState } from '$lib/ipc/state.svelte';
  import {
    defaultAudioState, defaultBluetoothState, defaultBrightnessState, defaultMediaState, defaultNetworkState,
    type PopoverRequest, type ThemePreference,
  } from '$lib/protocol/generated';
  import PopoverFrame from './PopoverFrame.svelte';
  import { send, openSettings, setThemePreference } from './status/actions';
  import { optimistic } from './status/optimistic.svelte';
  import { throttle } from './status/throttle';
  import { networkLabel, percentOf, volumeTier } from './status/format';
  import { t } from './status/strings';
  import Slider from './status/Slider.svelte';
  import Segmented from './status/Segmented.svelte';
  import Tile from './status/Tile.svelte';
  import Transport from './status/Transport.svelte';
  import Cover from './status/Cover.svelte';

  let { request, onclose }: { request: PopoverRequest; onclose: () => void } = $props();

  const settings = hostState('settings.changed', getInit().settings);
  const network = hostState('status.network', defaultNetworkState());
  const bluetooth = hostState('status.bluetooth', defaultBluetoothState());
  const audio = hostState('status.audio', defaultAudioState());
  const brightness = hostState('status.brightness', defaultBrightnessState());
  const media = hostState('media.state', defaultMediaState());

  // Optimistic values: the slider shows what the user just did until the host echoes it.
  const vol = optimistic({ tolerance: 0.005 });
  const bright = optimistic({ tolerance: 0.011 });
  let taskbarLocal = $state<boolean | null>(null);
  let devicesOpen = $state(false);
  let deviceError = $state(false);

  // Live drags reach the host at most every 50 ms; the final value is flushed on release and on teardown.
  const sendVolume = throttle((volume: number) => void send('audio.setVolume', { volume }), 50);
  const sendBrightness = throttle((level: number) => void send('brightness.set', { level }), 50);

  const offHost = [
    getBridge().on('status.audio', (a) => vol.host(a.volume)),
    getBridge().on('status.brightness', (b) => bright.host(b.level)),
    getBridge().on('settings.changed', () => (taskbarLocal = null)),
  ];
  onDestroy(() => {
    offHost.forEach((off) => off());
    sendVolume.flush();
    sendBrightness.flush();
    settings.dispose(); network.dispose(); bluetooth.dispose(); audio.dispose(); brightness.dispose(); media.dispose();
  });

  function done(target: string) {
    void openSettings(target);
    onclose();
  }

  const label = $derived(networkLabel(network.value));
  const wifiOn = $derived(label !== 'offline');
  const wifiSub = $derived(
    label === 'wifi' ? (network.value.name || t('net.unnamed'))
      : label === 'ethernet' ? t('net.ethernet')
      : label === 'offline' ? t('cc.wifi.none')
      : t('net.other'),
  );
  const btConnected = $derived(bluetooth.value.devices.filter((d) => d.connected).length);
  const btOn = $derived(bluetooth.value.supported && bluetooth.value.enabled);
  const btSub = $derived(
    !bluetooth.value.supported ? t('cc.bluetooth.unavailable')
      : !bluetooth.value.enabled ? t('common.off')
      : btConnected > 0 ? t('cc.bluetooth.connected', { n: btConnected })
      : t('common.on'),
  );

  const taskbarOn = $derived(taskbarLocal ?? settings.value.taskbar.mode !== 'hidden');

  const volumeShown = $derived(vol.value ?? audio.value.volume);
  const muted = $derived(audio.value.muted);
  const tier = $derived(volumeTier(volumeShown, muted));
  const VolumeIcon = $derived(tier === 'muted' ? VolumeX : tier === 'low' ? Volume : tier === 'medium' ? Volume1 : Volume2);
  const brightShown = $derived(bright.value ?? brightness.value.level);

  const themeOptions = $derived.by((): { value: ThemePreference; label: string }[] => [
    { value: 'light', label: t('cc.appearance.light') },
    { value: 'dark', label: t('cc.appearance.dark') },
    { value: 'auto', label: t('cc.appearance.auto') },
  ]);

  function setVolume(v: number) { vol.set(v); sendVolume(v); }
  function commitVolume(v: number) { vol.set(v); sendVolume(v); sendVolume.flush(); }
  function setBrightness(v: number) { bright.set(v); sendBrightness(v); }
  function commitBrightness(v: number) { bright.set(v); sendBrightness(v); sendBrightness.flush(); }

  async function pickDevice(id: string) {
    deviceError = false;
    const ok = await send('audio.setDevice', { deviceId: id });
    if (!ok) deviceError = true;
  }

  function toggleTaskbar() {
    const next = !taskbarOn;
    taskbarLocal = next;
    void send('system.showTaskbar', { show: next });
  }

  function mediaAction(action: 'previous' | 'next' | 'pause' | 'play') {
    void send('media.control', { action });
  }
</script>

<PopoverFrame anchor={request.anchor} placement="below-end" width={340} label={t('cc.label')}>
  <div class="sp cc">
    <div class="tiles">
      <Tile
        icon={wifiOn ? Wifi : WifiOff}
        title={t('cc.wifi')}
        subtitle={wifiSub}
        active={wifiOn}
        onclick={() => done('ms-settings:network-wifi')}
      />
      <Tile
        icon={Bluetooth}
        title={t('cc.bluetooth')}
        subtitle={btSub}
        active={btOn}
        onclick={() => done('ms-settings:bluetooth')}
      />
      <Tile icon={Moon} title={t('cc.focus')} subtitle={t('cc.focus.sub')} onclick={() => done('ms-settings:quietmomentshome')} />
      <Tile icon={Monitor} title={t('cc.taskbar')} subtitle={taskbarOn ? t('cc.taskbar.on') : t('cc.taskbar.off')} active={taskbarOn} onclick={toggleTaskbar} />
    </div>

    {#if media.value.available && media.value.title}
      <div class="sp-card now">
        <div class="sp-row">
          <Cover src={media.value.thumbUrl} size={40} radius={8} />
          <div class="sp-grow">
            <div class="sp-title">{media.value.title}</div>
            <div class="sp-sub">{media.value.artist ?? media.value.appName ?? t('cc.nowPlaying')}</div>
          </div>
        </div>
        <Transport
          playing={media.value.playing}
          canPlay={media.value.canPlay}
          canPause={media.value.canPause}
          canPrevious={media.value.canPrevious}
          canNext={media.value.canNext}
          onprevious={() => mediaAction('previous')}
          ontoggle={() => mediaAction(media.value.playing ? 'pause' : 'play')}
          onnext={() => mediaAction('next')}
        />
      </div>
    {/if}

    <div class="sp-card">
      <Segmented
        label={t('cc.appearance')}
        options={themeOptions}
        value={settings.value.appearance.mode}
        onchange={(v) => void setThemePreference(v)}
      />
    </div>

    {#if brightness.value.supported}
      <div class="sp-card">
        <div class="sp-row">
          <Sun size={16} class="sp-dim" aria-hidden="true" />
          <div class="sp-grow">
            <Slider
              label={t('cc.slider.brightness')}
              value={brightShown}
              valueText={`${percentOf(brightShown)}%`}
              oninput={setBrightness}
              onchange={commitBrightness}
            />
          </div>
        </div>
      </div>
    {/if}

    <div class="sp-card">
      <div class="sp-row">
        <button
          type="button"
          class="sp-icon-btn"
          aria-label={muted ? t('cc.unmute') : t('cc.mute')}
          aria-pressed={muted}
          onclick={() => void send('audio.setMuted', { muted: !muted })}
        ><VolumeIcon size={17} /></button>
        <div class="sp-grow">
          <Slider
            label={t('cc.slider.volume')}
            value={volumeShown}
            valueText={`${percentOf(volumeShown)}%`}
            oninput={setVolume}
            onchange={commitVolume}
          />
        </div>
        <button
          type="button"
          class="sp-icon-btn"
          aria-label={t('cc.expandDevices')}
          aria-expanded={devicesOpen}
          onclick={() => (devicesOpen = !devicesOpen)}
        ><ChevronDown size={16} class={devicesOpen ? 'chev open' : 'chev'} /></button>
      </div>
      <div class="sp-sub out">{t('cc.outputDevice')}: {audio.value.deviceName ?? '—'}</div>
      {#if devicesOpen}
        <div class="devices" role="radiogroup" aria-label={t('cc.outputDevice')}>
          {#each audio.value.devices as d (d.id)}
            <button type="button" role="radio" aria-checked={d.isDefault} class="dev" class:on={d.isDefault} onclick={() => pickDevice(d.id)}>
              <span class="sp-grow sp-title dev-name">{d.name}</span>
              {#if d.isDefault}<Check size={15} />{/if}
            </button>
          {:else}
            <div class="sp-empty">{t('snd.noDevices')}</div>
          {/each}
          {#if deviceError}<div class="sp-error">{t('cc.setDeviceFailed')}</div>{/if}
        </div>
      {/if}
    </div>

    <div class="sp-sep"></div>
    <button type="button" class="sp-link" onclick={() => { void send('settings.openWindow', {}); onclose(); }}>
      <span>{t('common.settings')}</span>
    </button>
  </div>
</PopoverFrame>

<style>
  .cc { gap: 8px; }
  .tiles { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; }
  .now { gap: 10px; }
  .out { padding: 0 2px; }
  .devices { display: grid; gap: 2px; margin-top: 2px; }
  .dev {
    display: flex; align-items: center; gap: 8px; padding: 6px 8px; border-radius: 8px; text-align: left;
    font-size: 12.5px; min-width: 0;
  }
  .dev:hover { background: var(--hover); }
  .dev-name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  :global(.sp-dim) { color: var(--text-2); flex: none; }
  :global(.chev) { transition: transform var(--dur) var(--ease-out); }
  :global(.chev.open) { transform: rotate(180deg); }
</style>
