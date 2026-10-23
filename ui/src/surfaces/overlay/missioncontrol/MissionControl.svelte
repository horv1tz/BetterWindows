<script lang="ts">
  // STUB — replace. Props are fixed by overlay/Surface.svelte.
  import type { OverlayShow } from '$lib/protocol/generated';
  let { show, onclose }: { show: OverlayShow; onclose: () => void } = $props();
</script>

<!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
<div class="backdrop" onclick={onclose}>MissionControl (stub)</div>

<style>
  .backdrop { position: fixed; inset: 0; display: grid; place-items: center; background: var(--backdrop-dim); }
</style>
