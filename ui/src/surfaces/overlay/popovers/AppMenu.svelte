<script lang="ts">
  // STUB — owner: ui-menubar. The active app's menu opened from its name in the menu bar: request.data = { appId };
  // items: Hide (dock.hide {itemId: appId}), Quit (dock.quit {itemId: appId}); more only if backed by a real call.
  import type { PopoverRequest } from '$lib/protocol/generated';
  import PopoverFrame from './PopoverFrame.svelte';
  let { request, onclose }: { request: PopoverRequest; onclose: () => void } = $props();
</script>

<PopoverFrame anchor={request.anchor} label="AppMenu">
  <div style="padding: 10px">AppMenu (stub) <button onclick={onclose}>close</button></div>
</PopoverFrame>
