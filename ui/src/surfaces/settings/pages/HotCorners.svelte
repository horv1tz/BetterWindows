<script lang="ts">
  import { HotCornerActionValues, type HotCornerAction, type HotCornerSettings } from '$lib/protocol/generated';
  import { t } from '../i18n';
  import { getStore } from '../store.svelte';
  import { rangeOf } from '../validate';
  import Card from '../components/Card.svelte';
  import PageHeader from '../components/PageHeader.svelte';
  import Row from '../components/Row.svelte';
  import Slider from '../components/Slider.svelte';
  import Toggle from '../components/Toggle.svelte';
  import ScreenArt from '../art/ScreenArt.svelte';

  const store = getStore();
  const s = $derived(store.s);
  const dwell = rangeOf('HotCornerSettings.dwellMs', { min: 50, max: 2000 });
  type Corner = 'topLeft' | 'topRight' | 'bottomLeft' | 'bottomRight';
  const CORNERS: Corner[] = ['topLeft', 'topRight', 'bottomLeft', 'bottomRight'];
  const options = $derived(HotCornerActionValues.map((a) => ({ value: a, label: t(`corner.action.${a}`) })));
  const on = $derived(s.hotCorners.enabled);
  const set = (corner: Corner, v: HotCornerAction) => store.patch({ hotCorners: { [corner]: v } as Partial<HotCornerSettings> });
</script>

<PageHeader title={t('page.hotCorners')} subtitle={t('hotCorners.subtitle')} />

<Card title={t('hotCorners.section.corners')}>
  <Row sid="hotCorners.enabled"><Toggle checked={on} onchange={(v) => store.patch({ hotCorners: { enabled: v } })} /></Row>
  <div class="stage" class:off={!on}>
    <ScreenArt width={420} aspect={1.72} dock="bottom" items={6}>
      {#each CORNERS as c (c)}
        <label class="corner {c}" class:active={s.hotCorners[c] !== 'none'}>
          <span class="sr-only">{t(`corner.${c}`)}</span>
          <span class="mark" aria-hidden="true"></span>
          <select
            value={s.hotCorners[c]} disabled={!on} aria-label={t(`corner.${c}`)}
            onchange={(e) => set(c, e.currentTarget.value as HotCornerAction)}
          >
            {#each options as o (o.value)}<option value={o.value} selected={o.value === s.hotCorners[c]}>{o.label}</option>{/each}
          </select>
        </label>
      {/each}
    </ScreenArt>
  </div>
  <Row sid="hotCorners.dwellMs" disabled={!on}>
    <Slider value={s.hotCorners.dwellMs} min={dwell.min} max={dwell.max} step={10} format={(v) => t('common.ms', { n: v })} onchange={(v) => store.patch({ hotCorners: { dwellMs: v } })} />
  </Row>
</Card>
<p class="foot">{t('hotCorners.note')}</p>

<style>
  .stage { display: grid; place-items: center; padding: 22px 12px 26px; border-top: 0.5px solid var(--separator); }
  .stage.off { opacity: 0.55; }
  .corner { position: absolute; z-index: 2; display: flex; align-items: center; }
  .corner.topLeft { top: 11%; left: 3%; } .corner.topRight { top: 11%; right: 3%; flex-direction: row-reverse; }
  .corner.bottomLeft { bottom: 5%; left: 3%; } .corner.bottomRight { bottom: 5%; right: 3%; flex-direction: row-reverse; }
  .mark { position: absolute; width: 10px; height: 10px; border-radius: 50%; border: 2px solid rgba(255, 255, 255, 0.9); background: transparent; box-shadow: 0 0 0 1px rgba(0, 0, 0, 0.2); display: none; }
  select {
    -webkit-appearance: none; appearance: none; max-width: 148px; height: 24px; padding: 0 22px 0 9px; border-radius: 6px; font-size: 12px; font-weight: 500;
    border: 0.5px solid var(--s-btn-border); background: var(--s-btn); box-shadow: var(--s-btn-shadow), 0 4px 12px rgba(0, 0, 0, 0.2); text-overflow: ellipsis;
    background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='10' height='10' viewBox='0 0 10 10'%3E%3Cpath d='M2 3.6 5 6.4 8 3.6' fill='none' stroke='%23888' stroke-width='1.5' stroke-linecap='round' stroke-linejoin='round'/%3E%3C/svg%3E");
    background-repeat: no-repeat; background-position: right 6px center;
  }
  select:hover:not(:disabled) { background-color: var(--s-btn-hover); }
  .corner.active select { color: var(--accent); }
  select:disabled { opacity: 0.6; }
  .foot { margin: 8px 6px 0; color: var(--text-2); font-size: 11.5px; line-height: 1.4; }
</style>
