<script lang="ts">
  // macOS-style horizontal slider: thick rounded track, filled part and a thumb that grows on hover and drag.
  // The geometry and key/pointer math live in slider.ts (unit-tested); this file only wires DOM events and ARIA.
  import { fraction, snap, valueFromKey, valueFromPointer } from './slider';

  interface Props {
    value: number;
    min?: number;
    max?: number;
    step?: number;
    label: string;
    valueText?: string;
    disabled?: boolean;
    /** Thinner track for the media scrubber. */
    thin?: boolean;
    /** Every change while dragging (throttle the host call in the caller). */
    oninput?: (value: number) => void;
    /** The committed value: pointer released or a key was pressed. */
    onchange?: (value: number) => void;
  }
  let { value, min = 0, max = 1, step = 0.01, label, valueText, disabled = false, thin = false, oninput, onchange }: Props = $props();

  /** Thumb diameter in px; the pointer maps to the thumb centre, so the usable track is shortened by this on both ends. */
  const THUMB = 18;

  let track = $state<HTMLDivElement | undefined>();
  let dragging = $state<number | null>(null);
  let hovered = $state(false);
  const shown = $derived(dragging ?? snap(value, min, max, step));
  const f = $derived(fraction(shown, min, max));

  function fromPointer(e: PointerEvent): number {
    return valueFromPointer(e.clientX, track!.getBoundingClientRect(), min, max, step, THUMB / 2);
  }

  function down(e: PointerEvent) {
    if (disabled || e.button !== 0 || !track) return;
    // Capture so that dragging outside the track keeps updating; released in up().
    track.setPointerCapture(e.pointerId);
    const v = fromPointer(e);
    dragging = v;
    oninput?.(v);
  }

  function move(e: PointerEvent) {
    if (dragging === null) return;
    const v = fromPointer(e);
    if (v !== dragging) {
      dragging = v;
      oninput?.(v);
    }
  }

  function up() {
    if (dragging === null) return;
    const v = dragging;
    dragging = null;
    onchange?.(v);
  }

  function key(e: KeyboardEvent) {
    if (disabled) return;
    const v = valueFromKey(e.key, shown, min, max, step);
    if (v === null) return;
    e.preventDefault();
    oninput?.(v);
    onchange?.(v);
  }
</script>

<div
  class="slider"
  class:thin
  class:disabled
  class:active={dragging !== null || hovered}
  style:--f={f}
  bind:this={track}
  role="slider"
  tabindex={disabled ? -1 : 0}
  aria-label={label}
  aria-valuemin={min}
  aria-valuemax={max}
  aria-valuenow={shown}
  aria-valuetext={valueText}
  aria-disabled={disabled}
  onpointerdown={down}
  onpointermove={move}
  onpointerup={up}
  onpointercancel={up}
  onpointerenter={() => (hovered = true)}
  onpointerleave={() => (hovered = false)}
  onkeydown={key}
>
  <span class="track"></span>
  <span class="fill"></span>
  <span class="thumb"></span>
</div>

<style>
  .slider {
    position: relative;
    height: 22px;
    display: block;
    container-type: inline-size; /* the thumb is positioned with cqw, so the track width is the container's */
    touch-action: none;
    user-select: none;
    border-radius: 999px;
    outline: none;
  }
  .track, .fill {
    position: absolute;
    left: 0;
    top: 50%;
    width: 100%;
    height: 8px;
    margin-top: -4px;
    border-radius: 999px;
    pointer-events: none;
  }
  .thin .track, .thin .fill { height: 4px; margin-top: -2px; }
  .track { background: var(--sp-track); }
  .fill {
    background: var(--sp-fill);
    transform-origin: left center;
    transform: scaleX(var(--f));
  }
  .thumb {
    position: absolute;
    left: 0;
    top: 50%;
    width: 18px;
    height: 18px;
    margin-top: -9px;
    border-radius: 50%;
    background: var(--sp-thumb);
    box-shadow: 0 1px 3px rgba(0, 0, 0, 0.35), 0 0 0 0.5px rgba(0, 0, 0, 0.12);
    transform: translateX(calc(var(--f) * (100cqw - 18px)));
    scale: 1;
    transition: scale var(--dur-fast) var(--ease-out);
    pointer-events: none;
  }
  .active .thumb { scale: 1.12; }
  .slider:focus-visible .thumb { box-shadow: 0 0 0 3px var(--accent); }
  .disabled { opacity: 0.4; }
</style>
