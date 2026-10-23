<script lang="ts">
  import { highlightSegments } from './highlight';
  let { text, query }: { text: string; query: string } = $props();
  const segments = $derived(highlightSegments(text, query));
</script>

{#each segments as s, i (i)}{#if s.match}<mark>{s.text}</mark>{:else}{s.text}{/if}{/each}

<style>
  mark { background: none; color: inherit; font-weight: 750; }
</style>
