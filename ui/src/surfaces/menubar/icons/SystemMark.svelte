<script lang="ts">
  /**
   * The BetterWindows system-menu mark (original artwork): two stacked glass panes, the front one cut out of the back one.
   * Drawn with currentColor so it follows the bar text colour in both themes.
   */
  let { size = 16 }: { size?: number } = $props();
  const uid = $props.id();
</script>

<svg width={size} height={size} viewBox="0 0 16 16" fill="none" aria-hidden="true" focusable="false">
  <defs>
    <mask id="{uid}-cut" maskUnits="userSpaceOnUse" x="0" y="0" width="16" height="16">
      <rect width="16" height="16" fill="white" />
      <rect x="5.4" y="5.4" width="10" height="10" rx="3.4" fill="black" stroke="black" stroke-width="2" />
    </mask>
  </defs>
  <rect x="1" y="1" width="9.6" height="9.6" rx="3" fill="currentColor" mask="url(#{uid}-cut)" />
  <rect x="6.15" y="6.15" width="8.7" height="8.7" rx="2.7" stroke="currentColor" stroke-width="1.5" />
</svg>
