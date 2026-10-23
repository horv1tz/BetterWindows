<script lang="ts">
  // STUB — replace with the real dock surface.
  import { getBridge } from '$lib/ipc';
  const bridge = getBridge();
</script>

<div class="stub glass">dock surface (stub) · bridge: {bridge.kind}</div>

<style>
  .stub { position: fixed; left: 12px; top: 12px; padding: 8px 12px; border-radius: 10px; }
</style>
