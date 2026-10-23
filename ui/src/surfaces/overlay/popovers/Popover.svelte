<script lang="ts">
  // FROZEN (scaffold). Routes overlay mode 'popover' to the component for request.kind and provides the click-away backdrop.
  import type { OverlayShow, PopoverKind, PopoverRequest } from '$lib/protocol/generated';
  import type { Component } from 'svelte';
  import AppleMenu from './AppleMenu.svelte';
  import DockMenu from './DockMenu.svelte';
  import WindowList from './WindowList.svelte';
  import DesktopMenu from './DesktopMenu.svelte';
  import ControlCenter from './ControlCenter.svelte';
  import Calendar from './Calendar.svelte';
  import Battery from './Battery.svelte';
  import Network from './Network.svelte';
  import Sound from './Sound.svelte';
  import Bluetooth from './Bluetooth.svelte';
  import MediaPopover from './MediaPopover.svelte';

  let { show, onclose }: { show: OverlayShow; onclose: () => void } = $props();

  const registry: Record<PopoverKind, Component<{ request: PopoverRequest; onclose: () => void }>> = {
    apple: AppleMenu, dockMenu: DockMenu, windowList: WindowList, desktopMenu: DesktopMenu,
    controlCenter: ControlCenter, calendar: Calendar, battery: Battery, network: Network, sound: Sound, bluetooth: Bluetooth, media: MediaPopover,
  };
  const Body = $derived(show.popover ? registry[show.popover.kind] : null);
</script>

<!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
<div class="backdrop" onclick={onclose} oncontextmenu={(e) => { e.preventDefault(); onclose(); }}></div>
{#if Body && show.popover}
  <Body request={show.popover} {onclose} />
{/if}

<style>
  .backdrop { position: fixed; inset: 0; background: transparent; }
</style>
