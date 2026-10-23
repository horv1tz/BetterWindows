<script lang="ts">
  /** Two toggle switches stacked (original glyph for the Control Center item). */
  let { size = 16 }: { size?: number } = $props();
</script>

<svg width={size} height={size} viewBox="0 0 16 16" fill="none" aria-hidden="true" focusable="false">
  <rect x="1.1" y="2.1" width="13.8" height="5" rx="2.5" stroke="currentColor" stroke-width="1.3" />
  <circle cx="4.1" cy="4.6" r="1.55" fill="currentColor" />
  <rect x="1.1" y="8.9" width="13.8" height="5" rx="2.5" stroke="currentColor" stroke-width="1.3" />
  <circle cx="11.9" cy="11.4" r="1.55" fill="currentColor" />
</svg>
