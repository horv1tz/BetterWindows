<script lang="ts">
  let { title, subtitle, id }: { title: string; subtitle?: string; id?: string } = $props();
</script>

<header class="ph">
  <h1 {id}>{title}</h1>
  {#if subtitle}<p>{subtitle}</p>{/if}
</header>

<style>
  .ph { padding-top: 4px; }
  h1 { font-size: 26px; line-height: 1.15; font-weight: 700; letter-spacing: -0.022em; }
  p { margin-top: 6px; color: var(--text-2); font-size: 13px; line-height: 1.45; max-width: 60ch; text-wrap: pretty; }
</style>
