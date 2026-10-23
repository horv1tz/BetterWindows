<script lang="ts">
  // Test fixture: AppMenuSlot with (or without) content, since snippets cannot be created through the server build of `svelte`.
  import AppMenuSlot from '../AppMenuSlot.svelte';
  let { enabled = true, filled = true }: { enabled?: boolean; filled?: boolean } = $props();
</script>

{#if filled}
  <AppMenuSlot {enabled}><button class="global-item">File</button></AppMenuSlot>
{:else}
  <AppMenuSlot {enabled} />
{/if}
