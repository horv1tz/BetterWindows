<script lang="ts">
  /** Original BetterWindows mark: a squircle with a 2x2 pane grid whose top-right pane floats free. */
  let { size = 64 }: { size?: number } = $props();
</script>

<svg width={size} height={size} viewBox="0 0 64 64" aria-hidden="true">
  <defs>
    <linearGradient id="bw-mark-bg" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#4f8cff" /><stop offset="1" stop-color="#7a4dff" /></linearGradient>
    <linearGradient id="bw-mark-pane" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#fff" stop-opacity=".98" /><stop offset="1" stop-color="#fff" stop-opacity=".78" /></linearGradient>
  </defs>
  <rect width="64" height="64" rx="15" fill="url(#bw-mark-bg)" />
  <rect x="14" y="14" width="16" height="16" rx="4" fill="url(#bw-mark-pane)" />
  <rect x="14" y="34" width="16" height="16" rx="4" fill="url(#bw-mark-pane)" opacity=".86" />
  <rect x="34" y="34" width="16" height="16" rx="4" fill="url(#bw-mark-pane)" opacity=".7" />
  <rect x="34" y="11" width="18" height="18" rx="5" fill="url(#bw-mark-pane)" transform="rotate(8 43 20)" />
</svg>
