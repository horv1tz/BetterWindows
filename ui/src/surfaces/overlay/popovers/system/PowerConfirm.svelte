<script lang="ts">
  /** The confirmation sheet shown inside the system menu popover before restart / shut down / log out. Cancel has the focus. */
  import Power from '@lucide/svelte/icons/power';
  import RotateCw from '@lucide/svelte/icons/rotate-cw';
  import LogOut from '@lucide/svelte/icons/log-out';
  import { confirmKeys, type ConfirmAction, type ErrorKind } from './confirm';
  import { t } from './strings';

  let {
    action,
    busy = false,
    error = null,
    onconfirm,
    oncancel,
  }: { action: ConfirmAction; busy?: boolean; error?: ErrorKind | null; onconfirm: () => void; oncancel: () => void } = $props();

  const keys = $derived(confirmKeys(action));
  const uid = $props.id();
  let cancelBtn = $state<HTMLButtonElement>();
  let confirmBtn = $state<HTMLButtonElement>();

  // Safe default: the destructive action is never the focused one.
  $effect(() => { cancelBtn?.focus(); });

  function onkeydown(e: KeyboardEvent) {
    if (e.key === 'Escape') {
      // Escape dismisses the sheet only (back to the menu), not the whole popover.
      e.preventDefault();
      e.stopPropagation();
      oncancel();
    } else if (e.key === 'Tab' || e.key === 'ArrowLeft' || e.key === 'ArrowRight') {
      // Two buttons: any of these keys toggles between them and focus never leaves the sheet.
      e.preventDefault();
      (document.activeElement === cancelBtn && !cancelBtn?.disabled ? confirmBtn : cancelBtn)?.focus();
    }
  }
</script>

<div class="sheet" role="alertdialog" tabindex="-1" aria-labelledby="{uid}-title" aria-describedby="{uid}-body" {onkeydown}>
  <div class="icon" aria-hidden="true">
    {#if action === 'restart'}<RotateCw size={20} strokeWidth={2} />{:else if action === 'logout'}<LogOut size={20} strokeWidth={2} />{:else}<Power size={20} strokeWidth={2} />{/if}
  </div>
  <h2 id="{uid}-title">{t(keys.title)}</h2>
  <p id="{uid}-body" class="body">{t(keys.body)}</p>
  {#if error}
    <p class="error" role="alert">{t(`apple.error.${error}`)}</p>
  {/if}
  <div class="buttons">
    <button type="button" class="btn default" bind:this={cancelBtn} disabled={busy} onclick={oncancel}>{t('confirm.cancel')}</button>
    <button type="button" class="btn" class:busy bind:this={confirmBtn} disabled={busy} onclick={onconfirm}>
      {#if busy}<span class="spinner" aria-hidden="true"></span>{/if}
      <span>{busy ? t(keys.busy) : t(keys.action)}</span>
    </button>
  </div>
</div>

<style>
  .sheet {
    outline: none;
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 6px;
    padding: 14px 12px 10px;
    text-align: center;
    animation: sheet-in var(--dur) var(--ease-out);
  }
  .icon {
    display: grid;
    place-items: center;
    width: 40px;
    height: 40px;
    margin-bottom: 4px;
    border-radius: 11px;
    color: var(--accent);
    background: color-mix(in oklab, var(--accent) 16%, transparent);
  }
  h2 { margin: 0; font-size: 13.5px; font-weight: 650; line-height: 1.3; letter-spacing: -0.005em; text-wrap: balance; }
  .body, .error { margin: 0; font-size: 12px; line-height: 1.4; color: var(--text-2); text-wrap: balance; }
  .error { color: #ff5a52; font-weight: 500; }
  :global(:root[data-theme='light']) .error { color: #d92d26; }

  .buttons { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; width: 100%; margin-top: 10px; }
  .btn {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    gap: 6px;
    height: 28px;
    padding: 0 12px;
    border: 0.5px solid var(--glass-edge);
    border-radius: 7px;
    background: var(--hover);
    color: var(--text);
    font: inherit;
    font-size: 13px;
    font-weight: 500;
    cursor: default;
    outline: none;
  }
  .btn:hover:not(:disabled) { background: var(--press); }
  .btn:focus-visible { box-shadow: 0 0 0 2px var(--accent); }
  .btn:disabled { opacity: 0.6; }
  .btn.default { background: var(--accent); border-color: transparent; color: #fff; }
  .btn.default:hover:not(:disabled) { background: var(--accent); filter: brightness(1.08); }

  .spinner {
    width: 12px;
    height: 12px;
    border: 1.6px solid color-mix(in oklab, currentColor 30%, transparent);
    border-top-color: currentColor;
    border-radius: 50%;
    animation: spin 0.8s linear infinite;
  }
  @keyframes spin { to { transform: rotate(360deg); } }
  @keyframes sheet-in {
    from { opacity: 0; transform: scale(0.98); }
    to { opacity: 1; transform: none; }
  }
</style>
