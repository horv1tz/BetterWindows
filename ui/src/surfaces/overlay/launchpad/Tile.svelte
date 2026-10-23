<script lang="ts">
  // One Launchpad tile (app icon or folder with a 3x3 preview + label). Purely presentational: pointer logic lives in Launchpad.svelte.
  import AppIcon from '$lib/components/AppIcon.svelte';
  import type { AppEntry } from '$lib/protocol/generated';
  import { MINI_COUNT } from './layout';

  let {
    name, app, folderApps, size, labelWidth, editing = false, merge = false, selected = false,
  }: {
    name: string; app?: AppEntry; folderApps?: AppEntry[]; size: number; labelWidth: number;
    editing?: boolean; merge?: boolean; selected?: boolean;
  } = $props();

  const mini = $derived((folderApps ?? []).slice(0, MINI_COUNT));
  const miniSize = $derived((size * 0.84 - 2 * (size * 0.04)) / 3);
</script>

<span class="jig" class:editing class:merge class:selected>
  <span class="icon" style:width="{size}px" style:height="{size}px">
    {#if app}
      <AppIcon src={app.iconUrl} name={app.name} {size} />
    {:else}
      <span class="folder" style:border-radius="{size * 0.225}px" style:padding="{size * 0.08}px" style:gap="{size * 0.04}px">
        {#each mini as a (a.id)}
          <span class="mini"><AppIcon src={a.iconUrl} name={a.name} size={miniSize} radius={0.22} /></span>
        {/each}
      </span>
    {/if}
  </span>
  <span class="label" style:width="{labelWidth}px">{name}</span>
</span>

<style>
  .jig { display: flex; flex-direction: column; align-items: center; gap: 10px; transform-origin: 50% 40%; }
  .icon {
    display: block; position: relative; flex: none;
    filter: drop-shadow(0 5px 9px rgba(0, 0, 0, 0.3));
    transition: transform var(--dur) var(--ease-spring);
  }
  .folder {
    box-sizing: border-box; width: 100%; height: 100%; display: grid; grid-template-columns: repeat(3, 1fr); grid-template-rows: repeat(3, 1fr);
    background: rgba(255, 255, 255, 0.28); border: 0.5px solid rgba(255, 255, 255, 0.36);
    backdrop-filter: blur(14px) saturate(160%); -webkit-backdrop-filter: blur(14px) saturate(160%);
    box-shadow: 0 0.5px 0 rgba(255, 255, 255, 0.4) inset;
  }
  .mini { display: grid; place-items: center; }
  .label {
    display: -webkit-box; -webkit-box-orient: vertical; -webkit-line-clamp: 2; line-clamp: 2; overflow: hidden;
    text-align: center; font-size: 13px; line-height: 1.25; font-weight: 500; color: #fff; overflow-wrap: anywhere;
    text-shadow: 0 1px 2px rgba(0, 0, 0, 0.6), 0 0 10px rgba(0, 0, 0, 0.25); min-height: 2.5em;
  }
  .merge .icon { transform: scale(1.14); }
  .merge .icon::after { content: ''; position: absolute; inset: -6px; border-radius: 26%; background: rgba(255, 255, 255, 0.22); z-index: -1; }
  .selected .icon::before { content: ''; position: absolute; inset: -7px; border-radius: 26%; background: rgba(255, 255, 255, 0.2); box-shadow: 0 0 0 2px rgba(255, 255, 255, 0.55); z-index: -1; }

  .editing { animation: jiggle 0.24s ease-in-out infinite alternate; animation-delay: var(--jd, 0ms); }
  @keyframes jiggle { from { transform: rotate(-1.5deg) translateY(-0.4px); } to { transform: rotate(1.5deg) translateY(0.4px); } }
  :global(:root[data-reduce-motion='true']) .editing { animation: none; }
</style>
