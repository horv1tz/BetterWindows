<script lang="ts">
  /**
   * The BetterWindows system-menu mark (original artwork): a lowercase "b" whose bowl is a rounded window pane.
   * Drawn with currentColor so it follows the bar text colour in both themes.
   */
  let { size = 16 }: { size?: number } = $props();
</script>

<svg width={size} height={size} viewBox="0 0 16 16" fill="none" aria-hidden="true" focusable="false">
  <rect x="1.8" y="0.9" width="2.1" height="14.2" rx="1.05" fill="currentColor" />
  <rect x="2.85" y="5.2" width="11.1" height="9.8" rx="3.4" stroke="currentColor" stroke-width="1.9" />
</svg>
