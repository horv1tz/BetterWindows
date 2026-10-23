<script lang="ts">
  import { getRowContext } from '../rowContext';

  let { checked, onchange, disabled = false, label }: { checked: boolean; onchange: (v: boolean) => void; disabled?: boolean; label?: string } = $props();
  const row = getRowContext();
  const off = $derived(disabled || !!row?.().disabled);
</script>

<button
  type="button"
  role="switch"
  class="sw"
  class:on={checked}
  aria-checked={checked}
  aria-label={label}
  aria-labelledby={label ? undefined : row?.().labelId}
  aria-describedby={row?.().descId}
  disabled={off}
  onclick={() => onchange(!checked)}
>
  <span class="knob"></span>
</button>

<style>
  .sw {
    position: relative; flex: none; width: 38px; height: 22px; padding: 0; border: 0; border-radius: 11px;
    background: var(--s-track); box-shadow: inset 0 0 0 0.5px rgba(0, 0, 0, 0.08);
  }
  .sw::before {
    content: ''; position: absolute; inset: 0; border-radius: inherit; background: var(--accent); opacity: 0;
    transition: opacity var(--dur-fast) var(--ease-out);
  }
  .sw.on::before { opacity: 1; }
  .knob {
    position: absolute; top: 2px; left: 2px; width: 18px; height: 18px; border-radius: 50%; background: #fff;
    box-shadow: 0 1px 2px rgba(0, 0, 0, 0.3), 0 0 0 0.5px rgba(0, 0, 0, 0.08);
    transition: transform var(--dur) var(--ease-spring);
  }
  .on .knob { transform: translateX(16px); }
  .sw:disabled { opacity: 0.45; }
</style>
