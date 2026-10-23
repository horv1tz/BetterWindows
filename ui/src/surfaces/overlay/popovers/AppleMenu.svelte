<script lang="ts">
  /**
   * The system menu (popover kind 'apple', opened from the logo at the left end of the menu bar).
   *
   * About This PC / System Settings… / Microsoft Store, the power actions (restart, shut down and log out go through an in-popover
   * confirmation sheet), the temporary "Show Windows Taskbar" switch and Quit BetterWindows.
   *
   * "Force Quit…" is intentionally absent: its Windows counterpart is Task Manager, but system.open only allows http(s), ms-settings:
   * and file paths (security allowlist), so there is no permitted way to launch taskmgr. Per-app quitting lives in the app menu
   * (name of the active app -> "Quit <App>", dock.quit). See the report's contractRequests for a possible system.open allowance.
   */
  import type { PopoverRequest, PowerAction } from '$lib/protocol/generated';
  import { getBridge } from '$lib/ipc';
  import PopoverFrame from './PopoverFrame.svelte';
  import Menu, { type MenuEntry } from './system/Menu.svelte';
  import PowerConfirm from './system/PowerConfirm.svelte';
  import { MENU, errorKindOf, needsConfirmation, reduceSheet, type ErrorKind, type SheetEvent, type SheetState } from './system/confirm';
  import { taskbar } from './system/taskbar.svelte';
  import { t } from './system/strings';

  let { request, onclose }: { request: PopoverRequest; onclose: () => void } = $props();

  const bridge = getBridge();
  taskbar.init();
  const STORE_APP_ID = 'aumid:Microsoft.WindowsStore_8wekyb3d8bbwe!App';

  let sheet = $state.raw<SheetState>(MENU);
  let menuError = $state<ErrorKind | null>(null);
  const send = (ev: SheetEvent) => (sheet = reduceSheet(sheet, ev));

  const log = (message: string, e: unknown) => bridge.emit('surface.log', { level: 'warn', message, data: String(e) });

  /** Fire a call and close the popover first: the panel must not linger over the lock screen or after resume from sleep. */
  function fireAndClose(run: () => Promise<unknown>, what: string) {
    onclose();
    run().catch((e) => log(`${what} failed`, e));
  }

  function power(action: PowerAction) {
    if (needsConfirmation(action)) {
      menuError = null;
      send({ type: 'ask', action });
    } else {
      fireAndClose(() => bridge.call('system.power', { action }), `system.power(${action})`);
    }
  }

  async function confirmPower() {
    if (sheet.phase !== 'confirm' || sheet.busy) return;
    const action = sheet.action;
    send({ type: 'submit' });
    try {
      await bridge.call('system.power', { action });
      onclose();
    } catch (e) {
      log(`system.power(${action}) failed`, e);
      send({ type: 'failed', error: errorKindOf(e) });
    }
  }

  async function openStore() {
    onclose();
    try {
      // Preferred: launch the installed Store app through the catalog (aumid launch).
      await bridge.call('apps.launch', { appId: STORE_APP_ID });
    } catch {
      // ms-windows-store: is not on the system.open allowlist; the https page hands off to the Store app or opens in the browser.
      await bridge.call('system.open', { target: 'https://apps.microsoft.com/' }).catch((e) => log('open Store failed', e));
    }
  }

  async function toggleTaskbar() {
    try {
      await taskbar.toggle();
      onclose();
    } catch (e) {
      log('system.showTaskbar failed', e);
      menuError = errorKindOf(e);
    }
  }

  const entries: MenuEntry[] = $derived([
    { kind: 'item', id: 'about', label: t('apple.about'), onselect: () => fireAndClose(() => bridge.call('system.open', { target: 'ms-settings:about' }), 'open About') },
    { kind: 'separator', id: 's1' },
    { kind: 'item', id: 'settings', label: t('apple.settings'), onselect: () => fireAndClose(() => bridge.call('settings.openWindow', {}), 'settings.openWindow') },
    { kind: 'item', id: 'store', label: t('apple.store'), onselect: () => void openStore() },
    { kind: 'separator', id: 's2' },
    { kind: 'item', id: 'sleep', label: t('apple.sleep'), onselect: () => power('sleep') },
    { kind: 'item', id: 'restart', label: t('apple.restart'), onselect: () => power('restart') },
    { kind: 'item', id: 'shutdown', label: t('apple.shutdown'), onselect: () => power('shutdown') },
    { kind: 'separator', id: 's3' },
    { kind: 'item', id: 'lock', label: t('apple.lock'), onselect: () => power('lock') },
    { kind: 'item', id: 'logout', label: t('apple.logout'), onselect: () => power('logout') },
    { kind: 'separator', id: 's4' },
    { kind: 'item', id: 'taskbar', label: t('apple.taskbar'), checked: taskbar.visible, onselect: () => void toggleTaskbar() },
    { kind: 'item', id: 'quit', label: t('apple.quit'), onselect: () => fireAndClose(() => bridge.call('system.quit'), 'system.quit') },
  ]);
</script>

<!-- Width follows the content (long labels in other languages must not be truncated); the menu and the sheet set their own minimum. -->
<PopoverFrame anchor={request.anchor} placement="below-start" label={t('apple.label')}>
  {#if sheet.phase === 'confirm'}
    <PowerConfirm
      action={sheet.action}
      busy={sheet.busy}
      error={sheet.error}
      onconfirm={confirmPower}
      oncancel={() => send({ type: 'cancel' })}
    />
  {:else}
    <Menu {entries} label={t('apple.label')} minWidth={236} />
    {#if menuError}
      <p class="error" role="alert">{t(`apple.error.${menuError}`)}</p>
    {/if}
  {/if}
</PopoverFrame>

<style>
  .error { margin: 4px 10px 4px; font-size: 12px; color: #ff5a52; }
  :global(:root[data-theme='light']) .error { color: #d92d26; }
</style>
