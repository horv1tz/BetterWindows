<script lang="ts">
  import { Check, Pipette } from '@lucide/svelte';
  import { ACCENTS, accentChoice } from '../accents';
  import { getRowContext } from '../rowContext';
  import { normalizeHex } from '../validate';
  import { t } from '../i18n';

  let { value, onchange }: { value: string; onchange: (v: string) => void } = $props();
  const row = getRowContext();
  const choice = $derived(accentChoice(value));
  const customHex = $derived(choice === 'custom' ? value.slice(0, 7) : '#7a5cff');
  let host: HTMLElement | undefined = $state();

  const items = $derived([
    { id: 'system', value: 'system', name: t('accent.system') },
    ...ACCENTS.map((a) => ({ id: a.id, value: a.hex, name: t(`accent.${a.id}`) })),
  ]);

  function key(e: KeyboardEvent) {
    const d = e.key === 'ArrowRight' || e.key === 'ArrowDown' ? 1 : e.key === 'ArrowLeft' || e.key === 'ArrowUp' ? -1 : 0;
    if (!d) return;
    e.preventDefault();
    const i = Math.max(0, items.findIndex((x) => x.id === choice));
    const next = items[(i + d + items.length) % items.length];
    onchange(next.value);
    queueMicrotask(() => host?.querySelectorAll<HTMLElement>('[role=radio]')[items.indexOf(next)]?.focus());
  }
  function pick(e: Event) {
    const hex = normalizeHex((e.currentTarget as HTMLInputElement).value);
    if (hex) onchange(hex);
  }
  const currentName = $derived(choice === 'custom' ? t('accent.custom') : (items.find((x) => x.id === choice)?.name ?? ''));
</script>

<div class="picker">
  <div class="sw" role="radiogroup" aria-labelledby={row?.().labelId} bind:this={host}>
    {#each items as it (it.id)}
      <button
        type="button" role="radio" class="dot" class:sel={choice === it.id} class:system={it.id === 'system'} aria-checked={choice === it.id} tabindex={choice === it.id ? 0 : -1}
        aria-label={it.name} title={it.name} style:--c={it.id === 'system' ? undefined : it.value} onclick={() => onchange(it.value)} onkeydown={key}
      >{#if choice === it.id}<Check size={13} strokeWidth={3} color={it.id === 'system' ? 'var(--text)' : '#fff'} />{/if}</button>
    {/each}
    <label class="dot custom" class:sel={choice === 'custom'} title={t('accent.custom')} style:--c={choice === 'custom' ? customHex : undefined}>
      <input type="color" value={customHex} aria-label={t('accent.custom')} onchange={pick} />
      {#if choice === 'custom'}<Check size={13} strokeWidth={3} color="#fff" />{:else}<Pipette size={12} strokeWidth={2.4} />{/if}
    </label>
  </div>
  <div class="name" aria-live="polite">{currentName}</div>
</div>

<style>
  .picker { display: flex; align-items: center; gap: 14px; flex-wrap: wrap; }
  .sw { display: flex; gap: 10px; flex-wrap: wrap; }
  .dot {
    position: relative; width: 24px; height: 24px; padding: 0; border: 0; border-radius: 50%; display: grid; place-items: center; background: var(--c);
    box-shadow: inset 0 0 0 0.5px rgba(0, 0, 0, 0.25), inset 0 1px 0 rgba(255, 255, 255, 0.35); transition: transform var(--dur-fast) var(--ease-out);
  }
  .dot:hover { transform: scale(1.1); }
  .dot.sel::after { content: ''; position: absolute; inset: -4px; border-radius: 50%; border: 2px solid var(--c, var(--text-2)); opacity: 0.55; pointer-events: none; }
  .dot.system { background: conic-gradient(from 200deg, #4a9cff, #a96bff, #ff6b9a, #ffb14a, #5fd68a, #4a9cff); }
  .dot.system.sel::after { border-color: var(--text-2); }
  .custom { background: var(--c, var(--s-seg)); color: var(--text-2); cursor: default; overflow: hidden; }
  .custom:not(.sel) { background: var(--s-seg); box-shadow: inset 0 0 0 0.5px var(--s-field-border); }
  .custom input { position: absolute; inset: 0; opacity: 0; width: 100%; height: 100%; cursor: default; }
  .custom:focus-within { outline: 2px solid var(--focus); outline-offset: 2px; }
  .dot:focus-visible { outline: 2px solid var(--focus); outline-offset: 3px; }
  .name { color: var(--text-2); }
</style>
