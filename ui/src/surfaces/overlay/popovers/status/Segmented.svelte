<script lang="ts" generics="T extends string">
  /** Segmented control (radiogroup). Arrow keys move and select; focus follows the selection. */
  interface Option { value: T; label: string }
  interface Props { options: Option[]; value: T; label: string; onchange?: (value: T) => void }
  let { options, value, label, onchange }: Props = $props();

  function onkey(e: KeyboardEvent, i: number) {
    const dir = e.key === 'ArrowRight' || e.key === 'ArrowDown' ? 1 : e.key === 'ArrowLeft' || e.key === 'ArrowUp' ? -1 : 0;
    if (!dir) return;
    e.preventDefault();
    const next = (i + dir + options.length) % options.length;
    onchange?.(options[next].value);
    // Focus follows the selection: the buttons are the children of the radiogroup, in option order.
    const group = (e.currentTarget as HTMLElement).parentElement;
    (group?.children[next] as HTMLElement | undefined)?.focus();
  }
</script>

<div class="seg" role="radiogroup" aria-label={label}>
  {#each options as opt, i (opt.value)}
    {@const selected = opt.value === value}
    <button
      type="button"
      role="radio"
      aria-checked={selected}
      tabindex={selected ? 0 : -1}
      class:on={selected}
      onclick={() => onchange?.(opt.value)}
      onkeydown={(e) => onkey(e, i)}
    >{opt.label}</button>
  {/each}
</div>

<style>
  .seg {
    display: grid;
    grid-auto-flow: column;
    grid-auto-columns: 1fr;
    padding: 2px;
    border-radius: 8px;
    background: var(--sp-track);
  }
  button {
    font: inherit;
    font-size: 12px;
    padding: 4px 6px;
    border-radius: 6px;
    color: var(--text);
    background: transparent;
    border: 0;
    transition: background var(--dur-fast) var(--ease-out);
  }
  button:hover:not(.on) { background: var(--hover); }
  button.on {
    background: var(--sp-thumb);
    color: #1d1d1f;
    box-shadow: 0 1px 2px rgba(0, 0, 0, 0.2);
  }
  :root[data-theme='dark'] button.on { background: rgba(120, 120, 128, 0.7); color: #fff; }
  button:focus-visible { outline: 2px solid var(--accent); outline-offset: 1px; }
</style>
