<script lang="ts">
  // Desktop context menu (overlay popover kind desktopMenu). Only actions the protocol supports are listed:
  // - omitted "Show/Hide Desktop Icons": there is no per-user switch for it (native icons are a global policy owned by the
  //   host, and our own icons are governed by settings.desktop.enabled);
  // - omitted "Sort By", "Use Stacks", "Clean Up": the desktop lays its icons out on its own grid and has no arrangement call.
  import { onMount } from 'svelte';
  import { Image, LayoutGrid, SlidersHorizontal } from '@lucide/svelte';
  import { getBridge } from '$lib/ipc';
  import type { PopoverRequest } from '$lib/protocol/generated';
  import PopoverFrame from './PopoverFrame.svelte';
  import { t } from '../missioncontrol/strings';

  let { request, onclose }: { request: PopoverRequest; onclose: () => void } = $props();

  const bridge = getBridge();
  let buttons = $state<HTMLButtonElement[]>([]);

  // "Change Wallpaper" and "Desktop Settings" both open the desktop page of the settings window: the settings surface has
  // no wallpaper-only anchor yet, so they share the page.
  const items = [
    { key: 'menu.wallpaper', icon: Image, page: 'desktop', sep: false },
    { key: 'menu.widgets', icon: LayoutGrid, page: 'widgets', sep: true },
    { key: 'menu.settings', icon: SlidersHorizontal, page: 'desktop', sep: false },
  ] as const;

  function run(page: string) {
    void bridge.call('settings.openWindow', { page }).catch(() => {}).finally(() => onclose());
  }

  function onKey(e: KeyboardEvent) {
    if (e.key !== 'ArrowDown' && e.key !== 'ArrowUp') return;
    e.preventDefault();
    const i = buttons.indexOf(document.activeElement as HTMLButtonElement);
    const next = e.key === 'ArrowDown' ? (i + 1) % buttons.length : (i <= 0 ? buttons.length - 1 : i - 1);
    buttons[next]?.focus();
  }

  onMount(() => {
    buttons[0]?.focus();
  });
</script>

<PopoverFrame anchor={request.anchor} placement="below-start" width={236} label={t('menu.label')}>
  <div class="menu" role="menu" aria-label={t('menu.label')} tabindex="-1" onkeydown={onKey}>
    {#each items as item, i (item.key)}
      {@const Icon = item.icon}
      <button
        bind:this={buttons[i]}
        type="button"
        role="menuitem"
        class="item"
        onclick={() => run(item.page)}
      >
        <span class="ico"><Icon size={15} strokeWidth={1.8} /></span>
        <span class="label">{t(item.key)}</span>
      </button>
      {#if item.sep}
        <div class="sep" role="separator"></div>
      {/if}
    {/each}
  </div>
</PopoverFrame>

<style>
  .menu { display: flex; flex-direction: column; gap: 1px; }
  .item {
    display: flex;
    align-items: center;
    gap: 10px;
    width: 100%;
    padding: 6px 10px 6px 8px;
    border: 0;
    border-radius: 7px;
    background: none;
    color: var(--text);
    font-size: 13px;
    text-align: left;
    cursor: default;
  }
  .item:hover,
  .item:focus-visible {
    background: var(--accent);
    color: #fff;
    outline: none;
  }
  .ico { display: grid; place-items: center; width: 18px; color: var(--text-2); flex: none; }
  .item:hover .ico, .item:focus-visible .ico { color: #fff; }
  .label { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .sep { height: 1px; margin: 5px 8px; background: var(--separator); }
</style>
