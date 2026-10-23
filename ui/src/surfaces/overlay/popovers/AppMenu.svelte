<script lang="ts">
  /**
   * The menu of the active app, opened from its name in the menu bar (popover kind 'appMenu', request.data = { appId }).
   * Hide <App> minimises all its windows (dock.hide), Quit <App> closes them (dock.quit). Both take the app id as itemId, which the host
   * accepts for ANY running app. The Ctrl+H / Ctrl+Q hints are labels only: they are deliberately not registered as hotkeys.
   */
  import type { PopoverRequest } from '$lib/protocol/generated';
  import { getBridge } from '$lib/ipc';
  import { hostState } from '$lib/ipc/state.svelte';
  import AppIcon from '$lib/components/AppIcon.svelte';
  import PopoverFrame from './PopoverFrame.svelte';
  import Menu, { type MenuEntry } from './system/Menu.svelte';
  import { nameFromAppId } from './system/appName';
  import { t } from './system/strings';

  let { request, onclose }: { request: PopoverRequest; onclose: () => void } = $props();

  const bridge = getBridge();
  const active = hostState('activeApp.changed', {});

  /** request.data comes from the (untrusted) menu bar page: accept only a plain non-empty string. */
  const requestedId = $derived.by(() => {
    const id = (request.data as { appId?: unknown } | null | undefined)?.appId;
    return typeof id === 'string' && id.length > 0 && id.length <= 1024 ? id : null;
  });
  const appId = $derived(requestedId ?? active.value.app?.appId ?? null);
  // The foreground app normally is the app the menu was opened for; if it changed meanwhile fall back to a name derived from the id.
  const info = $derived(active.value.app && active.value.app.appId === appId ? active.value.app : null);
  const name = $derived(info?.name?.trim() || (appId ? nameFromAppId(appId) : t('app.unknown')));

  function run(call: 'dock.hide' | 'dock.quit') {
    onclose();
    if (!appId) return;
    bridge.call(call, { itemId: appId }).catch((e) => bridge.emit('surface.log', { level: 'warn', message: `${call} failed`, data: String(e) }));
  }

  const entries: MenuEntry[] = $derived([
    { kind: 'item', id: 'hide', label: t('app.hide', { app: name }), hint: 'Ctrl+H', disabled: !appId, onselect: () => run('dock.hide') },
    { kind: 'item', id: 'quit', label: t('app.quit', { app: name }), hint: 'Ctrl+Q', disabled: !appId, onselect: () => run('dock.quit') },
  ]);
</script>

<PopoverFrame anchor={request.anchor} placement="below-start" label={t('app.label', { app: name })}>
  <Menu {entries} label={t('app.label', { app: name })} minWidth={232}>
    {#snippet header()}
      <div class="head">
        <AppIcon src={info?.iconUrl} {name} size={30} />
        <div class="text">
          <div class="name">{name}</div>
          {#if info?.title}<div class="title">{info.title}</div>{/if}
        </div>
      </div>
    {/snippet}
  </Menu>
</PopoverFrame>

<style>
  .head { display: flex; align-items: center; gap: 10px; padding: 6px 12px 4px; min-width: 0; max-width: 320px; }
  .text { min-width: 0; }
  .name { font-weight: 650; font-size: 13px; line-height: 1.3; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .title { font-size: 12px; line-height: 1.3; color: var(--text-2); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
</style>
