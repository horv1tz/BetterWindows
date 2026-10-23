<script lang="ts">
  import type { Snippet } from 'svelte';

  /**
   * Extension point for the global (application) menu that follows the app name in the bar. The global menu needs UI Automation
   * access to the foreground app's menu bar and is a later feature: while `features.globalMenu` is off this renders nothing, not even an
   * empty container, so it never takes up space or shows placeholders. When the feature ships, the implementation supplies its items
   * through `children` (a snippet) and the bar lays them out in this slot.
   */
  let { enabled = false, children }: { enabled?: boolean; children?: Snippet } = $props();
</script>

{#if enabled && children}
  <div class="slot" role="group" data-slot="global-menu">
    {@render children()}
  </div>
{/if}

<style>
  .slot { display: flex; align-items: center; min-width: 0; }
</style>
