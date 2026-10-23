<script lang="ts">
  // Top strip of Mission Control: one mini preview per virtual desktop, a '+' tile and remove controls.
  import { Plus, X } from '@lucide/svelte';
  import type { DesktopInfo, McWindow } from '$lib/protocol/generated';
  import { previewRects } from './model';
  import { countLabel, t } from './strings';

  interface Props {
    desktops: DesktopInfo[];
    windows: McWindow[];
    monitor: { w: number; h: number };
    selectedId: string | null;
    /** Desktop under a dragged window (drop highlight). */
    dropId: string | null;
    canRemove: boolean;
    onpick: (id: string) => void;
    onswitch: (id: string) => void;
    oncreate: () => void;
    onremove: (id: string) => void;
  }
  let { desktops, windows, monitor, selectedId, dropId, canRemove, onpick, onswitch, oncreate, onremove }: Props = $props();

  const tiles = $derived(
    desktops.map((d) => {
      const own = windows.filter((w) => w.desktopId === d.id);
      return { d, count: own.length, rects: previewRects(own, monitor), hue: (d.index * 61 + 205) % 360 };
    }),
  );
</script>

<div class="strip glass" role="group" aria-label={t('mc.desktops')}>
  {#each tiles as tile (tile.d.id)}
    {@const selected = tile.d.id === selectedId}
    <div class="cell">
      <button
        type="button"
        class="tile"
        class:selected
        class:current={tile.d.current}
        class:drop={dropId === tile.d.id}
        data-focusable=""
        data-desktop-id={tile.d.id}
        aria-pressed={selected}
        aria-label={`${tile.d.name}, ${countLabel(tile.count)}${tile.d.current ? `, ${t('mc.current')}` : ''}`}
        style:--hue={tile.hue}
        onclick={() => (selected ? onswitch(tile.d.id) : onpick(tile.d.id))}
        onkeydown={(e) => {
          if (e.key === 'Enter') { e.preventDefault(); onswitch(tile.d.id); }
          else if (e.key === 'Delete' && canRemove) { e.preventDefault(); onremove(tile.d.id); }
        }}
      >
        <span class="shot">
          {#each tile.rects as r, i (i)}
            <span class="win" style:left="{r.x * 100}%" style:top="{r.y * 100}%" style:width="{r.w * 100}%" style:height="{r.h * 100}%"></span>
          {/each}
        </span>
        <span class="name">{tile.d.name}</span>
      </button>
      {#if canRemove}
        <button type="button" class="x" tabindex="-1" aria-label={t('mc.removeDesktop', { name: tile.d.name })} onclick={() => onremove(tile.d.id)}>
          <X size={11} strokeWidth={2.6} />
        </button>
      {/if}
    </div>
  {/each}
  <button type="button" class="tile add" data-focusable="" aria-label={t('mc.newDesktop')} onclick={oncreate}>
    <span class="shot"><span class="plus"><Plus size={22} strokeWidth={2} /></span></span>
    <span class="name">{t('mc.newDesktop')}</span>
  </button>
</div>

<style>
  .strip {
    position: absolute;
    left: 50%;
    top: 28px;
    transform: translateX(-50%);
    display: flex;
    align-items: flex-start;
    gap: 14px;
    max-width: calc(100vw - 48px);
    overflow-x: auto;
    padding: 12px 14px 10px;
    border-radius: 18px;
    scrollbar-width: none;
  }
  .cell { position: relative; flex: none; }

  .tile {
    all: unset;
    box-sizing: border-box;
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 6px;
    width: 148px;
    padding: 6px;
    border-radius: 12px;
    cursor: default;
    transition: background var(--dur-fast) var(--ease-out);
  }
  .tile:hover { background: var(--hover); }
  .tile:active { background: var(--press); }
  .tile.selected { background: var(--press); }
  .tile:focus-visible { outline: 2px solid var(--accent); outline-offset: 1px; }
  .tile.drop { background: color-mix(in oklab, var(--accent) 22%, transparent); }

  .shot {
    position: relative;
    display: grid;
    place-items: center;
    width: 136px;
    height: 85px;
    border-radius: 10px;
    overflow: hidden;
    background:
      radial-gradient(120% 90% at 20% 10%, hsl(var(--hue) 75% 70%), transparent 60%),
      linear-gradient(150deg, hsl(calc(var(--hue) + 30) 70% 52%), hsl(calc(var(--hue) + 80) 60% 28%));
    box-shadow: var(--shadow-sm), inset 0 0 0 0.5px rgba(255, 255, 255, 0.25);
    transition: box-shadow var(--dur-fast) var(--ease-out), transform var(--dur-fast) var(--ease-out);
  }
  .current .shot { box-shadow: 0 0 0 2px var(--accent), var(--shadow-sm); }
  .selected .shot { transform: translateY(-1px); }
  .drop .shot { box-shadow: 0 0 0 2px var(--accent), 0 0 0 6px color-mix(in oklab, var(--accent) 30%, transparent); transform: scale(1.04); }
  .add .shot { background: var(--glass-bg-strong); color: var(--text-2); box-shadow: inset 0 0 0 1px var(--separator); }
  .plus { display: grid; place-items: center; }

  .win {
    position: absolute;
    border-radius: 2px;
    background: rgba(255, 255, 255, 0.92);
    box-shadow: 0 0.5px 1px rgba(0, 0, 0, 0.35);
    min-width: 2px;
    min-height: 2px;
  }

  .name {
    max-width: 100%;
    overflow: hidden;
    white-space: nowrap;
    text-overflow: ellipsis;
    font-size: 12px;
    color: var(--text-2);
  }
  .current .name, .selected .name { color: var(--text); font-weight: 600; }

  .x {
    position: absolute;
    left: 2px;
    top: 2px;
    z-index: 1;
    display: grid;
    place-items: center;
    width: 20px;
    height: 20px;
    padding: 0;
    border: 0.5px solid var(--glass-border);
    border-radius: 50%;
    background: var(--glass-bg-strong);
    color: var(--text);
    box-shadow: var(--shadow-sm);
    opacity: 0;
    transform: scale(0.8);
    transition: opacity var(--dur-fast) var(--ease-out), transform var(--dur-fast) var(--ease-out);
    cursor: default;
  }
  .cell:hover .x, .cell:focus-within .x { opacity: 1; transform: scale(1); }
  .x:hover { background: var(--press); }
</style>
