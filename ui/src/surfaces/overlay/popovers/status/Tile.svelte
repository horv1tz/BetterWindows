<script lang="ts">
  import type { Component } from 'svelte';
  /** Control Center tile: icon, title and a status line; `active` paints it with the accent colour. */
  interface Props {
    icon: Component<{ size?: number | string; strokeWidth?: number | string }>;
    title: string;
    subtitle?: string;
    active?: boolean;
    disabled?: boolean;
    onclick?: () => void;
  }
  let { icon: Icon, title, subtitle, active = false, disabled = false, onclick }: Props = $props();
</script>

<button type="button" class="tile" class:active {disabled} aria-pressed={active} onclick={onclick}>
  <span class="glyph"><Icon size={18} strokeWidth={2} /></span>
  <span class="text">
    <span class="sp-title">{title}</span>
    {#if subtitle}<span class="sub">{subtitle}</span>{/if}
  </span>
</button>

<style>
  .tile {
    display: flex;
    align-items: center;
    gap: 9px;
    min-width: 0;
    padding: 10px;
    border-radius: 12px;
    background: var(--sp-card);
    text-align: left;
    transition: background var(--dur-fast) var(--ease-out);
  }
  .tile:hover:not(:disabled) { background: var(--hover); }
  .tile:active:not(:disabled) { background: var(--press); }
  .tile.active { background: var(--accent); color: #fff; }
  .tile.active:hover { background: color-mix(in srgb, var(--accent) 88%, #000); }
  .glyph {
    display: grid; place-items: center; width: 30px; height: 30px; border-radius: 50%; flex: none;
    background: var(--sp-track);
  }
  .tile.active .glyph { background: rgba(255, 255, 255, 0.22); }
  .text { display: grid; min-width: 0; gap: 1px; }
  .sub { font-size: 11px; opacity: 0.72; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .tile :global(.sp-title) { font-size: 12.5px; }
</style>
