<script lang="ts">
  // Owner: ui-overlay-search (this file). Mode router of the overlay surface. Plug-in components are owned as noted in AGENTS.md:
  //   spotlight/, launchpad/  -> ui-overlay-search      missioncontrol/ -> ui-overlay-mc      popovers/ -> ui-status-popovers & friends
  // Props contract with every mode component (do not change): { show: OverlayShow; onclose: () => void }.
  import { onDestroy } from 'svelte';
  import { getBridge } from '$lib/ipc';
  import type { OverlayShow } from '$lib/protocol/generated';
  import Spotlight from './spotlight/Spotlight.svelte';
  import Launchpad from './launchpad/Launchpad.svelte';
  import MissionControl from './missioncontrol/MissionControl.svelte';
  import Popover from './popovers/Popover.svelte';

  const bridge = getBridge();
  let show = $state<OverlayShow | null>(null);
  /** Playing the short out-animation of Spotlight/Launchpad before the host hides the window. */
  let leaving = $state(false);
  let leaveTimer: ReturnType<typeof setTimeout> | undefined;

  /** Modes that handle Esc themselves (clear the query / close the folder first) and animate out. */
  const OWN_ESC = new Set(['spotlight', 'launchpad']);
  const LEAVE_MS = 130;

  $effect(() => {
    const offShow = bridge.on('overlay.show', (s) => { cancelLeave(); show = s; });
    // The host (hotkey toggle, deactivation, an executed action) already hides the window: no animation, just drop the content.
    const offHide = bridge.on('overlay.hide', () => { cancelLeave(); show = null; });
    return () => { offShow(); offHide(); };
  });
  onDestroy(cancelLeave);

  function cancelLeave() {
    clearTimeout(leaveTimer);
    leaveTimer = undefined;
    leaving = false;
  }

  function reduceMotion(): boolean {
    return document.documentElement.dataset.reduceMotion === 'true';
  }

  function finish() {
    cancelLeave();
    const token = show?.token ?? 0;
    show = null;
    // let go of focus so nothing keeps typing into a hidden page, then ask the host to hide the window
    (document.activeElement as HTMLElement | null)?.blur?.();
    bridge.call('overlay.close', { token }).catch(() => {});
  }

  function close() {
    if (!show || leaving) return;
    if (OWN_ESC.has(show.mode) && !reduceMotion()) {
      leaving = true;
      leaveTimer = setTimeout(finish, LEAVE_MS);
    } else {
      finish();
    }
  }

  function onKey(e: KeyboardEvent) {
    if (e.key !== 'Escape' || !show || e.defaultPrevented) return;
    if (OWN_ESC.has(show.mode)) return; // Spotlight/Launchpad decide what Esc means (clear query, close folder, close)
    close();
  }
</script>

<svelte:window onkeydown={onKey} />

{#if show}
  {#key show.token}
    {#if show.mode === 'spotlight' || show.mode === 'launchpad'}
      <div class="stage" class:leaving data-mode={show.mode}>
        {#if show.mode === 'spotlight'}
          <Spotlight {show} onclose={close} />
        {:else}
          <Launchpad {show} onclose={close} />
        {/if}
      </div>
    {:else if show.mode === 'missionControl'}
      <MissionControl {show} onclose={close} />
    {:else if show.mode === 'popover'}
      <Popover {show} onclose={close} />
    {/if}
  {/key}
{/if}

<style>
  .stage { position: fixed; inset: 0; }
  .stage.leaving { pointer-events: none; animation: stage-out 130ms ease-in forwards; }
  .stage.leaving[data-mode='launchpad'] { animation-name: stage-out-zoom; }
  @keyframes stage-out { to { opacity: 0; transform: translateY(-4px); } }
  @keyframes stage-out-zoom { to { opacity: 0; transform: scale(1.03); } }
</style>
