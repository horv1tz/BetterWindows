<script lang="ts">
  import AppWindow from '@lucide/svelte/icons/app-window';
  import Calculator from '@lucide/svelte/icons/calculator';
  import Settings from '@lucide/svelte/icons/settings';
  import Terminal from '@lucide/svelte/icons/terminal';
  import PanelsTopLeft from '@lucide/svelte/icons/panels-top-left';
  import Globe from '@lucide/svelte/icons/globe';
  import Folder from '@lucide/svelte/icons/folder';
  import FileText from '@lucide/svelte/icons/file-text';
  import FileImage from '@lucide/svelte/icons/file-image';
  import FileSpreadsheet from '@lucide/svelte/icons/file-spreadsheet';
  import FileCode from '@lucide/svelte/icons/file-code';
  import FileArchive from '@lucide/svelte/icons/file-archive';
  import FileAudio from '@lucide/svelte/icons/file-audio';
  import Presentation from '@lucide/svelte/icons/presentation';
  import FileIcon from '@lucide/svelte/icons/file';
  import type { Component } from 'svelte';
  import type { SearchResult } from '$lib/protocol/generated';
  import { fileGlyph, PROVIDER_TINT } from './glyphs';

  let { result, size = 30 }: { result: Pick<SearchResult, 'provider' | 'title' | 'iconUrl'>; size?: number } = $props();

  let failed = $state(false);
  $effect(() => { result.iconUrl; failed = false; });

  const FILE: Record<string, Component<any>> = { image: FileImage, sheet: FileSpreadsheet, slides: Presentation, code: FileCode, archive: FileArchive, audio: FileAudio, video: FileIcon, folder: Folder, text: FileText, file: FileIcon };
  const PROVIDER: Record<string, Component<any>> = { apps: AppWindow, calc: Calculator, settings: Settings, commands: Terminal, windows: PanelsTopLeft, web: Globe };
  const Glyph = $derived(result.provider === 'files' ? FILE[fileGlyph(result.title)] : PROVIDER[result.provider] ?? FileIcon);
  const tint = $derived(PROVIDER_TINT[result.provider]);
  const letter = $derived((result.title.trim()[0] ?? '?').toUpperCase());
</script>

{#if result.iconUrl && !failed}
  <img class="img" src={result.iconUrl} alt="" draggable="false" width={size} height={size} style:border-radius="{size * 0.2}px" onerror={() => (failed = true)} />
{:else if result.provider === 'apps' || result.provider === 'windows'}
  <span class="tile" style:width="{size}px" style:height="{size}px" style:border-radius="{size * 0.225}px" style:background="linear-gradient(145deg, {tint[0]}, {tint[1]})" style:font-size="{size * 0.46}px" aria-hidden="true">{letter}</span>
{:else}
  <span class="tile" style:width="{size}px" style:height="{size}px" style:border-radius="{size * 0.225}px" style:background="linear-gradient(145deg, {tint[0]}, {tint[1]})" aria-hidden="true">
    <Glyph size={Math.round(size * 0.58)} strokeWidth={1.8} />
  </span>
{/if}

<style>
  .img { display: block; object-fit: contain; -webkit-user-drag: none; }
  .tile {
    display: grid; place-items: center; color: #fff; font-weight: 600; flex: none;
    box-shadow: 0 0.5px 0 rgba(255, 255, 255, 0.35) inset, 0 1px 2px rgba(0, 0, 0, 0.25);
  }
</style>
