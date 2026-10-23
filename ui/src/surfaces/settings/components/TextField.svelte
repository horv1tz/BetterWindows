<script lang="ts">
  import type { Snippet } from 'svelte';
  import { getRowContext, nextId } from '../rowContext';

  let {
    value, oncommit, validate, placeholder = '', mono = false, ariaLabel, disabled = false, maxlength, width, trailing, spellcheck = false, type = 'text', inputEl = $bindable(),
  }: {
    value: string; oncommit: (v: string) => void; validate?: (v: string) => string | null; placeholder?: string; mono?: boolean;
    ariaLabel?: string; disabled?: boolean; maxlength?: number; width?: number | string; trailing?: Snippet; spellcheck?: boolean; type?: 'text' | 'search';
    inputEl?: HTMLInputElement;
  } = $props();
  const row = getRowContext();
  const errId = nextId('err');
  /** text being edited; null = show the setting's value */
  let draft = $state<string | null>(null);
  let showError = $state(false);
  const shown = $derived(draft ?? value);
  const error = $derived(draft !== null && validate ? validate(draft) : null);
  const visibleError = $derived(showError ? error : null);

  function commit() {
    if (draft === null) return;
    showError = true;
    if (error) return;
    const v = draft;
    draft = null;
    showError = false;
    if (v !== value) oncommit(v);
  }
  function key(e: KeyboardEvent) {
    if (e.key === 'Enter') { e.preventDefault(); commit(); }
    else if (e.key === 'Escape' && draft !== null) { e.stopPropagation(); draft = null; showError = false; }
  }
</script>

<div class="tf" style:width={typeof width === 'number' ? `${width}px` : width}>
  <div class="box" class:invalid={!!visibleError}>
    <input
      bind:this={inputEl} {type} class:mono value={shown} {placeholder} {disabled} {maxlength} {spellcheck} autocomplete="off"
      aria-label={ariaLabel} aria-labelledby={ariaLabel ? undefined : row?.().labelId} aria-invalid={!!visibleError} aria-describedby={visibleError ? errId : undefined}
      oninput={(e) => { draft = e.currentTarget.value; }}
      onblur={commit}
      onkeydown={key}
    />
    {#if trailing}<div class="trail">{@render trailing()}</div>{/if}
  </div>
  {#if visibleError}<div class="err" id={errId} role="alert">{visibleError}</div>{/if}
</div>

<style>
  .tf { display: flex; flex-direction: column; gap: 4px; min-width: 0; }
  .box {
    display: flex; align-items: center; gap: 4px; height: 26px; padding: 0 4px 0 9px; border-radius: 6px;
    background: var(--s-field); border: 0.5px solid var(--s-field-border); min-width: 0;
  }
  .box:focus-within { box-shadow: 0 0 0 3px var(--focus); border-color: transparent; }
  .box.invalid { border-color: var(--s-danger); }
  .box.invalid:focus-within { box-shadow: 0 0 0 3px color-mix(in srgb, var(--s-danger) 45%, transparent); }
  input { flex: 1; min-width: 0; height: 100%; border: 0; background: none; padding: 0; outline: none !important; text-overflow: ellipsis; }
  input::placeholder { color: var(--text-3); }
  input:disabled { opacity: 0.5; }
  .mono { font-family: ui-monospace, 'Cascadia Mono', Consolas, monospace; font-size: 12px; }
  .trail { display: flex; align-items: center; gap: 2px; }
  .err { color: var(--s-danger); font-size: 11.5px; line-height: 1.3; }
</style>
