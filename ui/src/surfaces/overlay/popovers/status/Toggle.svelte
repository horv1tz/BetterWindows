<script lang="ts">
  /** On/off switch with role=switch; the caller owns the state. */
  interface Props { checked: boolean; label: string; disabled?: boolean; onchange?: (checked: boolean) => void }
  let { checked, label, disabled = false, onchange }: Props = $props();
</script>

<button type="button" class="switch" class:on={checked} role="switch" aria-checked={checked} aria-label={label} {disabled} onclick={() => onchange?.(!checked)}>
  <span class="knob"></span>
</button>

<style>
  .switch {
    position: relative;
    width: 38px;
    height: 22px;
    border-radius: 999px;
    background: var(--sp-track);
    flex: none;
    transition: background var(--dur) var(--ease-out);
  }
  .switch:disabled { opacity: 0.4; }
  .switch.on { background: var(--accent); }
  .knob {
    position: absolute;
    top: 2px;
    left: 2px;
    width: 18px;
    height: 18px;
    border-radius: 50%;
    background: #fff;
    box-shadow: 0 1px 2px rgba(0, 0, 0, 0.3);
    transition: transform var(--dur) var(--ease-out);
  }
  .on .knob { transform: translateX(16px); }
</style>
