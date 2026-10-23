<script lang="ts">
  // Analog clock face. Hands are rotated by transform (GPU) from a requestAnimationFrame loop: the second hand sweeps when
  // `sweep` is on, otherwise the hands advance once per second. Reduced motion falls back to one step per second.
  import { clockParts, handAngles } from '../clockLogic';

  let { timeZone, seconds = false, numerals = true }: { timeZone?: string; seconds?: boolean; numerals?: boolean } = $props();

  let hour: SVGGElement, minute: SVGGElement, second: SVGGElement;
  const ticks = Array.from({ length: 60 }, (_, i) => i);

  $effect(() => {
    const sweep = seconds;
    const zone = timeZone;
    let raf = 0;
    let timer = 0;
    let lastSecond = -1;
    const paint = (smooth: boolean) => {
      const a = handAngles(clockParts(new Date(), zone), smooth);
      hour.style.transform = `rotate(${a.hour}deg)`;
      minute.style.transform = `rotate(${a.minute}deg)`;
      second.style.transform = `rotate(${a.second}deg)`;
    };
    const reduce = () => document.documentElement.dataset.reduceMotion === 'true';
    const frame = () => {
      raf = requestAnimationFrame(frame);
      if (reduce()) { const s = Math.floor(Date.now() / 1000); if (s === lastSecond) return; lastSecond = s; paint(false); return; }
      paint(sweep);
    };
    if (sweep) { frame(); } else {
      const tick = () => { paint(false); timer = window.setTimeout(tick, 1000 - (Date.now() % 1000) + 5); };
      tick();
    }
    return () => { cancelAnimationFrame(raf); clearTimeout(timer); };
  });
</script>

<svg viewBox="0 0 100 100" class="face" aria-hidden="true">
  <circle cx="50" cy="50" r="48" class="plate" />
  <circle cx="50" cy="50" r="47.6" class="rim" />
  {#each ticks as i (i)}
    {#if i % 5 === 0}
      <line x1="50" y1="6.5" x2="50" y2={i % 15 === 0 ? 13 : 11.5} class="tick major" transform="rotate({i * 6} 50 50)" />
    {:else}
      <line x1="50" y1="6.5" x2="50" y2="9" class="tick" transform="rotate({i * 6} 50 50)" />
    {/if}
  {/each}
  {#if numerals}
    <text x="50" y="27" class="num">12</text>
    <text x="77" y="53.4" class="num">3</text>
    <text x="50" y="80" class="num">6</text>
    <text x="23" y="53.4" class="num">9</text>
  {/if}
  <g class="hand" bind:this={hour}><rect x="47.8" y="27" width="4.4" height="26" rx="2.2" class="hh" /></g>
  <g class="hand" bind:this={minute}><rect x="48.5" y="14" width="3" height="39" rx="1.5" class="mh" /></g>
  <g class="hand" bind:this={second} style:display={seconds ? '' : 'none'}>
    <rect x="49.2" y="12" width="1.6" height="46" rx=".8" class="sh" />
    <circle cx="50" cy="50" r="2.4" class="sh-cap" />
  </g>
  <circle cx="50" cy="50" r="2.8" class="cap" />
  <circle cx="50" cy="50" r="1.1" class="cap-dot" />
</svg>

<style>
  .face { display: block; width: 100%; height: 100%; filter: drop-shadow(0 2px 5px rgba(0, 0, 0, 0.22)); }
  .plate { fill: var(--face-bg, #fbfbfd); }
  .rim { fill: none; stroke: var(--face-rim, rgba(0, 0, 0, 0.14)); stroke-width: 0.6; }
  .tick { stroke: var(--face-tick, rgba(0, 0, 0, 0.4)); stroke-width: 0.7; stroke-linecap: round; }
  .tick.major { stroke: var(--face-ink, #1d1d1f); stroke-width: 1.5; }
  .num { fill: var(--face-ink, #1d1d1f); font: 600 9px/1 var(--font-ui); text-anchor: middle; font-variant-numeric: tabular-nums; }
  .hand { transform-origin: 50px 50px; transform-box: view-box; will-change: transform; }
  .hh, .mh { fill: var(--face-ink, #1d1d1f); }
  .sh, .sh-cap { fill: #ff9f0a; }
  .cap { fill: var(--face-ink, #1d1d1f); }
  .cap-dot { fill: #ff9f0a; }
</style>
