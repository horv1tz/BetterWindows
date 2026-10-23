<script lang="ts">
  /**
   * The menu bar surface (#/menubar/<monitorId>): a 28 DIP bar across the monitor.
   * Left: system menu, active app name (opens the app menu), extension slot for a future global menu.
   * Right (left to right): tray, now playing, Bluetooth, network, sound, battery, Spotlight, Control Center, clock.
   * Every item opens its panel in the overlay surface via overlay.open with its own bounding rect as the anchor.
   */
  import Search from '@lucide/svelte/icons/search';
  import Bluetooth from '@lucide/svelte/icons/bluetooth';
  import BluetoothConnected from '@lucide/svelte/icons/bluetooth-connected';
  import EthernetPort from '@lucide/svelte/icons/ethernet-port';
  import Signal from '@lucide/svelte/icons/signal';
  import Globe from '@lucide/svelte/icons/globe';
  import Music2 from '@lucide/svelte/icons/music-2';
  import { getBridge, getInit } from '$lib/ipc';
  import { currentLocale } from '$lib/i18n.svelte';
  import { glassShape } from '$lib/shape';
  import type {
    ActiveApp, AudioState, BatteryState, BluetoothState, MediaState, NetworkState, Rect, TrayIcon, TrayState,
  } from '$lib/protocol/generated';
  import { t } from './strings';
  import { batteryVisual } from './battery';
  import { createClockTicker, formatClock, formatClockLabel, type ClockTicker } from './clock';
  import {
    barSettings, connectedBluetoothCount, networkVisual, soundLevel, soundPercent, visibleItems, visibleTrayIcons,
    type BarInputs, type ItemKey,
  } from './status';
  import { NOT_PRESSED, openRequest, reducePressed, toRect, type Pressed, type PressedEvent } from './popoverTracker';
  import MenuBarItem from './MenuBarItem.svelte';
  import AppMenuSlot from './AppMenuSlot.svelte';
  import SystemMark from './icons/SystemMark.svelte';
  import ControlCenterIcon from './icons/ControlCenterIcon.svelte';
  import BatteryIcon from './icons/BatteryIcon.svelte';
  import WifiIcon from './icons/WifiIcon.svelte';
  import SoundIcon from './icons/SoundIcon.svelte';

  const bridge = getBridge();
  const init = getInit();
  /** The monitor this bar lives on. Only used for the accessible name and to scope overlay.show/hide to this bar. */
  const monitorId = init.monitor.id;

  // ---- mirrors of host state (null = not received yet, so items do not flash a wrong default) ----
  let settings = $state(init.settings);
  let activeApp = $state<ActiveApp | undefined>(undefined);
  let battery = $state<BatteryState | null>(null);
  let network = $state<NetworkState | null>(null);
  let audio = $state<AudioState | null>(null);
  let bluetooth = $state<BluetoothState | null>(null);
  let media = $state<MediaState | null>(null);
  let tray = $state<TrayState | null>(null);

  const bar = $derived(barSettings(settings));
  const inputs: BarInputs = $derived({ battery, network, audio, bluetooth, media, tray });
  const items = $derived(visibleItems(inputs));
  const trayIcons = $derived(visibleTrayIcons(tray));
  const bat = $derived(batteryVisual(battery));

  // ---- pressed state: "the panel opened from this item is showing" ----
  let pressed = $state.raw<Pressed>(NOT_PRESSED);
  function press(ev: PressedEvent) {
    const next = reducePressed(pressed, ev);
    if (next !== pressed) pressed = next;
  }
  const isPressed = (k: ItemKey) => pressed.key === k;

  function open(key: ItemKey, anchor: Rect, data?: unknown) {
    press({ type: 'click', key });
    bridge.call('overlay.open', openRequest(key, anchor, monitorId, data)).catch((e) => {
      press({ type: 'openFailed' });
      bridge.emit('surface.log', { level: 'warn', message: `overlay.open(${key}) failed`, data: String(e) });
    });
  }

  function clickTray(icon: TrayIcon, button: 'left' | 'right' | 'double', r: Rect) {
    bridge.call('tray.click', { id: icon.id, button, anchor: { x: r.x + r.width / 2, y: r.y + r.height } }).catch(() => {});
  }

  // ---- clock ----
  let now = $state(new Date());
  let ticker: ClockTicker | undefined;
  const locale = $derived(currentLocale());
  const clockText = $derived(formatClock(now, locale, bar.clock));
  const clockLabel = $derived(t('bar.clock', { full: formatClockLabel(now, locale, bar.clock) }));

  $effect(() => {
    ticker = createClockTicker((d) => (now = d), () => bar.clock.showSeconds);
    const onVis = () => ticker?.setPaused('document', document.hidden);
    onVis();
    document.addEventListener('visibilitychange', onVis);
    return () => {
      document.removeEventListener('visibilitychange', onVis);
      ticker?.stop();
      ticker = undefined;
    };
  });
  // The cadence depends on showSeconds: re-align the schedule when the setting flips.
  $effect(() => {
    bar.clock.showSeconds;
    ticker?.refresh();
  });

  // ---- host events ----
  $effect(() => {
    const offs = [
      bridge.on('settings.changed', (s) => (settings = s)),
      bridge.on('activeApp.changed', (s) => (activeApp = s.app)),
      bridge.on('status.battery', (s) => (battery = s)),
      bridge.on('status.network', (s) => (network = s)),
      bridge.on('status.audio', (s) => (audio = s)),
      bridge.on('status.bluetooth', (s) => (bluetooth = s)),
      bridge.on('media.state', (s) => (media = s)),
      bridge.on('tray.state', (s) => (tray = s)),
      bridge.on('overlay.show', (show) => press({ type: 'show', show, monitorId })),
      bridge.on('overlay.hide', () => press({ type: 'hide' })),
      bridge.on('surface.visibility', (v) => ticker?.setPaused('surface', !v.visible)),
    ];
    // Fallback for hosts that do not forward overlay.hide to the bar (see popoverTracker.ts).
    const root = document.documentElement;
    const onLeave = () => press({ type: 'pointerLeave' });
    const onMove = () => press({ type: 'pointerMove' });
    root.addEventListener('pointerleave', onLeave);
    window.addEventListener('pointermove', onMove, { passive: true });
    return () => {
      offs.forEach((off) => off());
      root.removeEventListener('pointerleave', onLeave);
      window.removeEventListener('pointermove', onMove);
    };
  });

  // ---- derived labels ----
  const appName = $derived(activeApp?.name?.trim() || t('bar.desktop'));
  const barLabel = $derived(t('bar.labelFor', { monitor: init.monitor.name || monitorId }));

  const batteryLabel = $derived(
    bat.bolt ? t('bar.batteryCharging', { percent: bat.percent })
    : bat.plugged ? t('bar.batteryPlugged', { percent: bat.percent })
    : bat.tone === 'low' || bat.tone === 'critical' ? t('bar.batteryLow', { percent: bat.percent })
    : t('bar.battery', { percent: bat.percent }),
  );
  const net = $derived(network ? networkVisual(network) : null);
  const networkLabel = $derived.by(() => {
    if (!network || !net) return '';
    switch (net.glyph) {
      case 'none': return t('bar.network.none');
      case 'wifi': return network.name ? t('bar.network.wifi', { name: network.name, bars: Math.min(4, Math.max(0, network.signalBars)) }) : t('bar.network.wifiUnnamed', { bars: Math.min(4, Math.max(0, network.signalBars)) });
      case 'ethernet': return network.name ? t('bar.network.ethernet', { name: network.name }) : t('bar.network.ethernetUnnamed');
      case 'cellular': return t('bar.network.cellular');
      default: return t('bar.network.other');
    }
  });
  const level = $derived(audio ? soundLevel(audio) : 'zero');
  const soundLabel = $derived(audio ? (audio.muted ? t('bar.soundMuted') : t('bar.sound', { percent: soundPercent(audio) })) : '');
  const btConnected = $derived(bluetooth ? connectedBluetoothCount(bluetooth) : 0);
  const btLabel = $derived(!bluetooth ? '' : btConnected > 0 ? t('bar.bluetoothConnected', { count: btConnected }) : bluetooth.enabled ? t('bar.bluetoothOn') : t('bar.bluetoothOff'));
  const mediaTitle = $derived(media?.title?.trim() ?? '');
  const mediaLabel = $derived.by(() => {
    if (!media || !mediaTitle) return '';
    const text = media.artist ? t('bar.mediaBy', { title: mediaTitle, artist: media.artist }) : mediaTitle;
    return media.playing ? t('bar.media', { title: text }) : t('bar.mediaPaused', { title: text });
  });

  // ---- keyboard: arrow keys move between items (roving focus without changing Tab order) ----
  let barEl = $state<HTMLElement>();
  function onKeydown(e: KeyboardEvent) {
    if (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight' && e.key !== 'Home' && e.key !== 'End') return;
    const els = [...barEl!.querySelectorAll<HTMLButtonElement>('[data-bar-item]:not(:disabled)')];
    const at = els.indexOf(document.activeElement as HTMLButtonElement);
    if (at < 0 || els.length === 0) return;
    const next = e.key === 'Home' ? 0 : e.key === 'End' ? els.length - 1 : (at + (e.key === 'ArrowRight' ? 1 : -1) + els.length) % els.length;
    els[next].focus();
    e.preventDefault();
  }
</script>

<div
  bind:this={barEl}
  class="bar glass"
  class:solid={!bar.translucent}
  role="toolbar"
  aria-label={barLabel}
  tabindex="-1"
  use:glassShape={{ radius: 0 }}
  onkeydown={onKeydown}
>
  <div class="group left">
    <MenuBarItem label={t('bar.system')} haspopup="menu" pressed={isPressed('apple')} onactivate={(r) => open('apple', r)}>
      <SystemMark size={16} />
    </MenuBarItem>

    <MenuBarItem
      label={appName}
      strong
      haspopup="menu"
      disabled={!activeApp}
      pressed={isPressed('appMenu')}
      onactivate={(r) => activeApp && open('appMenu', r, { appId: activeApp.appId })}
    >
      {#key appName}<span class="app-name">{appName}</span>{/key}
    </MenuBarItem>

    <AppMenuSlot enabled={init.features.globalMenu} />
  </div>

  <div class="group right">
    {#each items as key (key)}
      {#if key === 'tray'}
        <div class="tray" role="group" aria-label="Tray">
          {#each trayIcons as icon (icon.id)}
            <MenuBarItem
              label={icon.tooltip || icon.id}
              pressed={false}
              onactivate={(r) => clickTray(icon, 'left', r)}
              onsecondary={(kind, r) => clickTray(icon, kind, r)}
            >
              <img class="tray-icon" src={icon.iconUrl} alt="" width="16" height="16" draggable="false" />
            </MenuBarItem>
          {/each}
        </div>
      {:else if key === 'media'}
        <MenuBarItem label={mediaLabel} pressed={isPressed('media')} dim={!media?.playing} onactivate={(r) => open('media', r)}>
          <Music2 size={14} strokeWidth={2} />
          <span class="media-title">{mediaTitle}</span>
        </MenuBarItem>
      {:else if key === 'bluetooth'}
        <MenuBarItem label={btLabel} pressed={isPressed('bluetooth')} dim={!bluetooth?.enabled} onactivate={(r) => open('bluetooth', r)}>
          {#if btConnected > 0}<BluetoothConnected size={15} strokeWidth={1.9} />{:else}<Bluetooth size={15} strokeWidth={1.9} />{/if}
        </MenuBarItem>
      {:else if key === 'network' && net}
        <MenuBarItem label={networkLabel} pressed={isPressed('network')} onactivate={(r) => open('network', r)}>
          {#if net.glyph === 'wifi'}<WifiIcon arcs={net.arcs} />
          {:else if net.glyph === 'none'}<WifiIcon offline />
          {:else if net.glyph === 'ethernet'}<EthernetPort size={16} strokeWidth={1.8} />
          {:else if net.glyph === 'cellular'}<Signal size={15} strokeWidth={1.9} />
          {:else}<Globe size={15} strokeWidth={1.8} />{/if}
        </MenuBarItem>
      {:else if key === 'sound'}
        <MenuBarItem label={soundLabel} pressed={isPressed('sound')} onactivate={(r) => open('sound', r)}>
          <SoundIcon {level} />
        </MenuBarItem>
      {:else if key === 'battery'}
        <MenuBarItem label={batteryLabel} pressed={isPressed('battery')} onactivate={(r) => open('battery', r)}>
          {#if bar.showBatteryPercent}<span class="pct">{bat.percent}%</span>{/if}
          <BatteryIcon visual={bat} />
        </MenuBarItem>
      {:else if key === 'spotlight'}
        <MenuBarItem label={t('bar.spotlight')} pressed={isPressed('spotlight')} onactivate={(r) => open('spotlight', r)}>
          <Search size={15} strokeWidth={2.1} />
        </MenuBarItem>
      {:else if key === 'controlCenter'}
        <MenuBarItem label={t('bar.controlCenter')} pressed={isPressed('controlCenter')} onactivate={(r) => open('controlCenter', r)}>
          <ControlCenterIcon />
        </MenuBarItem>
      {:else if key === 'calendar'}
        <MenuBarItem label={clockLabel} pressed={isPressed('calendar')} onactivate={(r) => open('calendar', r)}>
          <span class="clock">{clockText}</span>
        </MenuBarItem>
      {/if}
    {/each}
  </div>
</div>

<style>
  .bar {
    position: fixed;
    inset: 0 0 auto 0;
    height: var(--menubar-height);
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 12px;
    padding: 0 6px;
    background: var(--menubar-bg);
    border: 0;
    border-bottom: 0.5px solid var(--separator);
    box-shadow: none;
    border-radius: 0;
    font-size: 13px;
    line-height: 1;
    outline: none;
  }
  /* settings.menuBar.translucent = false: opaque bar, no blur. */
  :global(:root[data-theme='light']) .bar.solid { background: #f3f3f6; backdrop-filter: none; -webkit-backdrop-filter: none; }
  :global(:root[data-theme='dark']) .bar.solid { background: #1c1c20; backdrop-filter: none; -webkit-backdrop-filter: none; }

  .group { display: flex; align-items: center; min-width: 0; }
  .group.left { flex: 0 1 auto; gap: 1px; }
  .group.right { flex: 0 0 auto; gap: 1px; }
  .tray { display: flex; align-items: center; gap: 1px; margin-right: 2px; }

  .app-name {
    display: block;
    max-width: 260px;
    overflow: hidden;
    text-overflow: ellipsis;
    line-height: 1.3; /* overflow:hidden clips descenders at line-height 1 */
    animation: swap var(--dur) var(--ease-out);
  }
  @keyframes swap {
    from { opacity: 0; transform: translateY(2px); }
    to { opacity: 1; transform: none; }
  }

  .clock, .pct { font-variant-numeric: tabular-nums; font-weight: 500; }
  .pct { font-size: 12px; }
  .media-title {
    display: block;
    max-width: 150px;
    overflow: hidden;
    text-overflow: ellipsis;
    line-height: 1.3;
    font-weight: 500;
  }
  .tray-icon { display: block; object-fit: contain; -webkit-user-drag: none; }
</style>
