<script lang="ts">
  import { Battery, Wifi, Volume2, Search, SlidersHorizontal } from '@lucide/svelte';
  import { currentLocale, t } from '../i18n';
  import { formatMenuBarClock } from '../format';
  import { getStore } from '../store.svelte';
  import Banner from '../components/Banner.svelte';
  import Card from '../components/Card.svelte';
  import PageHeader from '../components/PageHeader.svelte';
  import Row from '../components/Row.svelte';
  import Toggle from '../components/Toggle.svelte';
  import AppMark from '../art/AppMark.svelte';

  const store = getStore();
  const s = $derived(store.s);
  let now = $state(new Date());
  $effect(() => {
    const id = setInterval(() => (now = new Date()), 1000);
    return () => clearInterval(id);
  });
  const clock = $derived(formatMenuBarClock(s.menuBar, now, currentLocale()));
</script>

<PageHeader title={t('page.menubar')} subtitle={t('menubar.subtitle')} />

{#if !s.menuBar.enabled}
  <Banner kind="info">{t('menubar.disabledNote')}</Banner>
{/if}

<Card title={t('menubar.section.preview')}>
  <div class="stage">
    <div class="wall"></div>
    <div class="bar" class:solid={!s.menuBar.translucent} class:off={!s.menuBar.enabled} aria-hidden="true">
      <span class="logo"><AppMark size={14} /></span>
      <b>{t('menubar.preview.app')}</b>
      <span class="m">{t('menubar.preview.file')}</span><span class="m">{t('menubar.preview.edit')}</span><span class="m">{t('menubar.preview.view')}</span>
      <span class="sp"></span>
      <span class="ic"><Wifi size={13} /></span>
      <span class="ic"><Volume2 size={13} /></span>
      <span class="ic bat"><Battery size={15} />{#if s.menuBar.showBatteryPercent}<i>76%</i>{/if}</span>
      <span class="ic"><Search size={12} /></span>
      <span class="ic"><SlidersHorizontal size={12} /></span>
      <span class="clk">{clock}</span>
    </div>
  </div>
</Card>

<Card title={t('menubar.section.general')}>
  <Row sid="menubar.enabled"><Toggle checked={s.menuBar.enabled} onchange={(v) => store.patch({ menuBar: { enabled: v } })} /></Row>
  <Row sid="menubar.translucent" disabled={!s.menuBar.enabled}><Toggle checked={s.menuBar.translucent} onchange={(v) => store.patch({ menuBar: { translucent: v } })} /></Row>
  <Row sid="menubar.autoHide" disabled={!s.menuBar.enabled}><Toggle checked={s.menuBar.autoHide} onchange={(v) => store.patch({ menuBar: { autoHide: v } })} /></Row>
  <Row sid="menubar.allMonitors" disabled={!s.menuBar.enabled}><Toggle checked={s.menuBar.allMonitors} onchange={(v) => store.patch({ menuBar: { allMonitors: v } })} /></Row>
</Card>

<Card title={t('menubar.section.clock')}>
  <Row sid="menubar.showDate" disabled={!s.menuBar.enabled}><Toggle checked={s.menuBar.showDate} onchange={(v) => store.patch({ menuBar: { showDate: v } })} /></Row>
  <Row sid="menubar.showSeconds" disabled={!s.menuBar.enabled}><Toggle checked={s.menuBar.showSeconds} onchange={(v) => store.patch({ menuBar: { showSeconds: v } })} /></Row>
  <Row sid="menubar.use24Hour" disabled={!s.menuBar.enabled}><Toggle checked={s.menuBar.use24Hour} onchange={(v) => store.patch({ menuBar: { use24Hour: v } })} /></Row>
  <Row sid="menubar.showBatteryPercent" disabled={!s.menuBar.enabled}><Toggle checked={s.menuBar.showBatteryPercent} onchange={(v) => store.patch({ menuBar: { showBatteryPercent: v } })} /></Row>
</Card>

<style>
  .stage { position: relative; height: 84px; overflow: hidden; }
  .wall { position: absolute; inset: 0; background: linear-gradient(120deg, #3a74e6 0%, #8556d6 55%, #f28c62 100%); }
  :root[data-theme='dark'] .wall { background: linear-gradient(120deg, #1d2c5e 0%, #4b2f86 55%, #a2503c 100%); }
  .bar {
    position: absolute; left: 0; right: 0; top: 14px; height: 28px; display: flex; align-items: center; gap: 12px; padding: 0 12px; color: #fff; font-size: 12.5px;
    background: rgba(255, 255, 255, 0.28); backdrop-filter: blur(14px) saturate(160%); border-block: 0.5px solid rgba(255, 255, 255, 0.35);
    transition: opacity var(--dur) var(--ease-out);
  }
  :root[data-theme='dark'] .bar { background: rgba(20, 20, 26, 0.5); border-color: rgba(255, 255, 255, 0.12); }
  .bar.solid { background: rgba(236, 236, 240, 0.97); backdrop-filter: none; color: #111; }
  :root[data-theme='dark'] .bar.solid { background: rgba(30, 30, 34, 0.98); color: #f2f2f4; }
  .bar.off { opacity: 0.35; }
  .logo { display: flex; }
  b { font-weight: 650; }
  .m { opacity: 0.92; }
  .sp { flex: 1; }
  .ic { display: flex; align-items: center; gap: 4px; }
  .ic i { font-style: normal; font-size: 12px; font-variant-numeric: tabular-nums; }
  .clk { font-variant-numeric: tabular-nums; white-space: pre; font-weight: 500; }
</style>
