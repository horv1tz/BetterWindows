<script lang="ts">
  // STUB — replace with the real settings window (System-Settings-like sidebar + panes, onboarding).
  import { getBridge } from '$lib/ipc';
  const bridge = getBridge();
</script>

<div class="stub glass">settings surface (stub) · bridge: {bridge.kind}</div>

<style>
  .stub { position: fixed; left: 12px; top: 12px; padding: 8px 12px; border-radius: 10px; }
</style>
