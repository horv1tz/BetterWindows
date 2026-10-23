<script lang="ts">
  // Owner: ui-overlay-search (this file). Plug-in components are owned as noted in AGENTS.md:
  //   spotlight/, launchpad/  -> ui-overlay-search      missioncontrol/, popovers/ -> ui-overlay-popovers
  import { getBridge } from '$lib/ipc';
  import type { OverlayShow } from '$lib/protocol/generated';
  import Spotlight from './spotlight/Spotlight.svelte';
  import Launchpad from './launchpad/Launchpad.svelte';
  import MissionControl from './missioncontrol/MissionControl.svelte';
  import Popover from './popovers/Popover.svelte';

  const bridge = getBridge();
  let show = $state<OverlayShow | null>(null);

  $effect(() => {
    const offShow = bridge.on('overlay.show', (s) => (show = s));
    const offHide = bridge.on('overlay.hide', () => (show = null));
    return () => { offShow(); offHide(); };
  });

  function close() {
    show = null;
    bridge.call('overlay.close').catch(() => {});
  }
</script>

<svelte:window onkeydown={(e) => e.key === 'Escape' && show && close()} />

{#if show}
  {#key show.token}
    {#if show.mode === 'spotlight'}
      <Spotlight {show} onclose={close} />
    {:else if show.mode === 'launchpad'}
      <Launchpad {show} onclose={close} />
    {:else if show.mode === 'missionControl'}
      <MissionControl {show} onclose={close} />
    {:else if show.mode === 'popover'}
      <Popover {show} onclose={close} />
    {/if}
  {/key}
{/if}
