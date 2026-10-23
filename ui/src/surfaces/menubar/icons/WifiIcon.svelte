<script lang="ts">
  /** Wi-Fi fan with 0..3 lit arcs, or the struck-through "no connection" variant. */
  let { arcs = 3, offline = false, size = 16 }: { arcs?: 0 | 1 | 2 | 3; offline?: boolean; size?: number } = $props();
  const uid = $props.id();
</script>

<svg width={size} height={size} viewBox="0 0 16 16" fill="none" aria-hidden="true" focusable="false" stroke="currentColor" stroke-linecap="round">
  {#if offline}
    <defs>
      <mask id="{uid}-slash" maskUnits="userSpaceOnUse" x="0" y="0" width="16" height="16">
        <rect width="16" height="16" fill="white" />
        <path d="M2.6 1.9 13.4 14.1" stroke="black" stroke-width="3.6" />
      </mask>
    </defs>
    <g mask="url(#{uid}-slash)" opacity="0.5">
      <path d="M5.45 9.85a3.6 3.6 0 0 1 5.1 0" stroke-width="1.6" />
      <path d="M3.47 7.87a6.4 6.4 0 0 1 9.06 0" stroke-width="1.6" />
      <path d="M1.5 5.9a9.2 9.2 0 0 1 13 0" stroke-width="1.6" />
      <circle cx="8" cy="12.7" r="1.15" fill="currentColor" stroke="none" />
    </g>
    <path d="M2.6 1.9 13.4 14.1" stroke-width="1.6" />
  {:else}
    <path d="M5.45 9.85a3.6 3.6 0 0 1 5.1 0" stroke-width="1.6" opacity={arcs >= 1 ? 1 : 0.28} />
    <path d="M3.47 7.87a6.4 6.4 0 0 1 9.06 0" stroke-width="1.6" opacity={arcs >= 2 ? 1 : 0.28} />
    <path d="M1.5 5.9a9.2 9.2 0 0 1 13 0" stroke-width="1.6" opacity={arcs >= 3 ? 1 : 0.28} />
    <circle cx="8" cy="12.7" r="1.15" fill="currentColor" stroke="none" />
  {/if}
</svg>
