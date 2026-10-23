<script lang="ts">
  import type { Snippet } from 'svelte';
  import { nextId } from '../rowContext';
  import Button from './Button.svelte';
  import { t } from '../i18n';

  let { open, title, message, confirmLabel, cancelLabel, danger = false, onconfirm, oncancel, children }: {
    open: boolean; title: string; message?: string; confirmLabel: string; cancelLabel?: string; danger?: boolean;
    onconfirm: () => void; oncancel: () => void; children?: Snippet;
  } = $props();
  const titleId = nextId('dlg-title');
  const bodyId = nextId('dlg-body');
  let box: HTMLElement | undefined = $state();
  let previous: Element | null = null;

  $effect(() => {
    if (open) {
      previous = document.activeElement;
      // safe default: focus "Cancel" so an accidental Enter never confirms a destructive action
      queueMicrotask(() => box?.querySelector<HTMLElement>('.btns button')?.focus());
    } else if (previous instanceof HTMLElement) {
      previous.focus();
      previous = null;
    }
  });

  function key(e: KeyboardEvent) {
    if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); oncancel(); return; }
    if (e.key !== 'Tab' || !box) return;
    const items = [...box.querySelectorAll<HTMLElement>('button:not(:disabled), [href], input, select, [tabindex]:not([tabindex="-1"])')];
    if (!items.length) return;
    const first = items[0], last = items[items.length - 1];
    if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
    else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
  }
</script>

{#if open}
  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <div class="scrim" onkeydown={key} onpointerdown={(e) => { if (e.target === e.currentTarget) oncancel(); }}>
    <div class="dlg" bind:this={box} role="alertdialog" aria-modal="true" aria-labelledby={titleId} aria-describedby={message ? bodyId : undefined}>
      <h2 id={titleId}>{title}</h2>
      {#if message}<p id={bodyId}>{message}</p>{/if}
      {@render children?.()}
      <div class="btns">
        <Button onclick={oncancel}>{cancelLabel ?? t('common.cancel')}</Button>
        <Button variant={danger ? 'danger' : 'primary'} onclick={onconfirm}>{confirmLabel}</Button>
      </div>
    </div>
  </div>
{/if}

<style>
  .scrim { position: fixed; inset: 0; z-index: 50; display: grid; place-items: center; background: rgba(0, 0, 0, 0.28); animation: fade var(--dur) var(--ease-out); }
  .dlg {
    width: min(380px, calc(100vw - 48px)); padding: 20px 20px 16px; border-radius: 14px; color: var(--text);
    border: 0.5px solid var(--glass-border); box-shadow: var(--shadow-lg); animation: pop var(--dur) var(--ease-out);
    backdrop-filter: blur(24px) saturate(160%);
  }
  :root[data-theme='light'] .dlg { background: #f6f6f8; }
  :root[data-theme='dark'] .dlg { background: #2b2c31; }
  h2 { font-size: 15px; font-weight: 650; letter-spacing: -0.01em; }
  p { margin-top: 6px; color: var(--text-2); line-height: 1.45; text-wrap: pretty; }
  .btns { display: flex; justify-content: flex-end; gap: 8px; margin-top: 18px; }
  @keyframes fade { from { opacity: 0; } }
  @keyframes pop { from { opacity: 0; transform: scale(0.96) translateY(4px); } }
</style>
