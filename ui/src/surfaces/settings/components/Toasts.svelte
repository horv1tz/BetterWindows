<script lang="ts">
  import { X, CircleAlert, CircleCheck, Info } from '@lucide/svelte';
  import { dismissToast, toasts } from '../toast.svelte';
  import { t } from '../i18n';
</script>

<div class="toasts" role="region" aria-label={t('toast.region')} aria-live="polite">
  {#each toasts.list as toast (toast.id)}
    <div class="toast {toast.kind}" role={toast.kind === 'error' ? 'alert' : 'status'}>
      <span class="ico" aria-hidden="true">
        {#if toast.kind === 'error'}<CircleAlert size={16} />{:else if toast.kind === 'success'}<CircleCheck size={16} />{:else}<Info size={16} />{/if}
      </span>
      <span class="txt">{toast.text}</span>
      <button type="button" class="x" aria-label={t('common.dismiss')} onclick={() => dismissToast(toast.id)}><X size={13} /></button>
    </div>
  {/each}
</div>

<style>
  .toasts { position: fixed; z-index: 60; left: 50%; bottom: 18px; transform: translateX(-50%); display: flex; flex-direction: column; gap: 8px; align-items: center; pointer-events: none; width: min(560px, calc(100vw - 32px)); }
  .toast {
    pointer-events: auto; display: flex; align-items: center; gap: 10px; max-width: 100%; padding: 9px 10px 9px 12px; border-radius: 10px;
    background: var(--s-window); border: 0.5px solid var(--glass-border); box-shadow: var(--shadow-md); animation: rise var(--dur) var(--ease-out);
  }
  :root[data-theme='light'] .toast { background: #fbfbfc; }
  :root[data-theme='dark'] .toast { background: #303137; }
  .ico { display: flex; flex: none; }
  .error .ico { color: var(--s-danger); } .success .ico { color: var(--s-ok); } .info .ico { color: var(--s-info); }
  .txt { min-width: 0; line-height: 1.35; text-wrap: pretty; user-select: text; }
  .x { flex: none; display: grid; place-items: center; width: 20px; height: 20px; border: 0; border-radius: 5px; background: none; color: var(--text-2); padding: 0; }
  .x:hover { background: var(--hover); color: var(--text); }
  @keyframes rise { from { opacity: 0; transform: translateY(8px); } }
</style>
