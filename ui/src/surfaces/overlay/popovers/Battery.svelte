<script lang="ts">
  // STUB — owner: ui-overlay-popovers-b. Render inside <PopoverFrame anchor={request.anchor} placement="..."> ... </PopoverFrame>.
  import type { PopoverRequest } from '$lib/protocol/generated';
  import PopoverFrame from './PopoverFrame.svelte';
  let { request, onclose }: { request: PopoverRequest; onclose: () => void } = $props();
</script>

<PopoverFrame anchor={request.anchor} label="Battery">
  <div style="padding: 10px">Battery (stub) <button onclick={onclose}>close</button></div>
</PopoverFrame>
