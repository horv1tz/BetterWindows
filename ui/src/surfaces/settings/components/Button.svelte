<script lang="ts">
  import type { Snippet } from 'svelte';
  let {
    variant = 'default', size = 'md', disabled = false, busy = false, type = 'button', title, ariaLabel, onclick, children, block = false,
  }: {
    variant?: 'default' | 'primary' | 'danger' | 'ghost'; size?: 'md' | 'sm'; disabled?: boolean; busy?: boolean; type?: 'button' | 'submit';
    title?: string; ariaLabel?: string; onclick?: (e: MouseEvent) => void; children: Snippet; block?: boolean;
  } = $props();
</script>

<button {type} class="btn {variant} {size}" class:block class:busy disabled={disabled || busy} {title} aria-label={ariaLabel} aria-busy={busy} {onclick}>
  {@render children()}
</button>

<style>
  .btn {
    display: inline-flex; align-items: center; justify-content: center; gap: 6px; height: 26px; padding: 0 12px; border-radius: 6px; white-space: nowrap;
    border: 0.5px solid var(--s-btn-border); background: var(--s-btn); box-shadow: var(--s-btn-shadow); font-weight: 500;
  }
  .btn.sm { height: 22px; padding: 0 9px; font-size: 12px; border-radius: 5px; }
  .btn.block { width: 100%; }
  .btn:hover:not(:disabled) { background: var(--s-btn-hover); }
  .btn:active:not(:disabled) { filter: brightness(0.94); }
  .btn:disabled { opacity: 0.45; }
  .btn.busy { opacity: 0.7; }
  .primary { background: var(--accent); border-color: transparent; color: var(--accent-fg); box-shadow: 0 0.5px 1px rgba(0, 0, 0, 0.25), inset 0 0.5px 0 rgba(255, 255, 255, 0.25); }
  .primary:hover:not(:disabled) { background: var(--accent); filter: brightness(1.06); }
  .primary:active:not(:disabled) { filter: brightness(0.92); }
  .danger { color: var(--s-danger); }
  .ghost { background: none; border-color: transparent; box-shadow: none; }
  .ghost:hover:not(:disabled) { background: var(--hover); }
</style>
