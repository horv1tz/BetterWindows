<script lang="ts">
  // Widgets pane: per-display widget list, the gallery, and the weather location used by the weather widget.
  import { onMount } from 'svelte';
  import { Battery, BellRing, Calendar, Cloud, Clock, Music, StickyNote } from '@lucide/svelte';
  import { getBridge } from '$lib/ipc';
  import { WidgetTypeValues, type WidgetInstance, type WidgetSize, type WidgetType, type WeatherLocation } from '$lib/protocol/generated';
  import { run } from '../actions';
  import { t } from '../i18n';
  import { monitors } from '../monitors.svelte';
  import { getStore } from '../store.svelte';
  import { pushToast } from '../toast.svelte';
  import { DEFAULT_WIDGET_SIZE, WIDGET_SIZES, widgetsOnMonitor } from '../widgets';
  import Banner from '../components/Banner.svelte';
  import Button from '../components/Button.svelte';
  import Card from '../components/Card.svelte';
  import MonitorTabs from '../components/MonitorTabs.svelte';
  import PageHeader from '../components/PageHeader.svelte';
  import Row from '../components/Row.svelte';
  import Segmented from '../components/Segmented.svelte';
  import Select from '../components/Select.svelte';
  import TextField from '../components/TextField.svelte';

  const bridge = getBridge();
  const store = getStore();
  const s = $derived(store.s);

  const ICONS: Record<WidgetType, typeof Clock> = {
    clock: Clock, calendar: Calendar, weather: Cloud, reminders: BellRing, media: Music, battery: Battery, notes: StickyNote,
  };

  onMount(() => { void monitors.load(); });
  let tab = $state('');
  const current = $derived(tab || monitors.primaryId);
  const onScreen = $derived(widgetsOnMonitor(s.desktop.widgets, current, monitors.primaryId));
  const sizeOptions = WIDGET_SIZES.map((v) => ({ value: v, label: t(`widgets.size.${v}`) })) as { value: WidgetSize; label: string }[];
  const sizeChoice: Record<string, WidgetSize> = $state({});

  async function add(type: WidgetType) {
    const size = sizeChoice[type] ?? DEFAULT_WIDGET_SIZE[type];
    const monitorId = current || undefined;
    const r = await run(() => bridge.call('desktop.addWidget', { type, size, monitorId }), { what: t('widgets.addFailed') });
    if (r.ok) pushToast('success', t(`widgets.type.${type}`));
  }

  async function resize(w: WidgetInstance, size: WidgetSize) {
    await run(() => bridge.call('desktop.updateWidget', { id: w.id, size }), { what: t('widgets.addFailed') });
  }

  async function remove(w: WidgetInstance) {
    const r = await run(() => bridge.call('desktop.removeWidget', { id: w.id }), { what: t('widgets.remove') });
    if (r.ok) pushToast('success', t('widgets.removed'));
  }

  // ---- weather location
  let query = $state('');
  let searching = $state(false);
  let results: WeatherLocation[] = $state([]);
  let searched = $state(false);
  let inputEl: HTMLInputElement | undefined = $state();

  async function search(q: string) {
    const text = q.trim();
    query = text;
    if (!text) return;
    searching = true;
    searched = false;
    const r = await run(() => bridge.call('weather.search', { query: text }), { quiet: true });
    searching = false;
    searched = true;
    results = r.ok ? r.value.locations : [];
    if (!r.ok) pushToast('error', `${t('widgets.weather.searchFailed')} ${r.message}`);
  }

  async function useLocation(loc: WeatherLocation) {
    const r = await run(() => bridge.call('weather.setLocation', loc), { what: t('widgets.weather.setFailed') });
    if (r.ok) { results = []; searched = false; query = ''; }
  }

  const unitOptions = $derived([{ value: 'c', label: t('widgets.weather.unit.c') }, { value: 'f', label: t('widgets.weather.unit.f') }] as { value: 'c' | 'f'; label: string }[]);
  const placeName = (l: WeatherLocation) => [l.name, l.admin, l.country].filter(Boolean).join(', ');
</script>

<PageHeader title={t('page.widgets')} subtitle={t('widgets.subtitle')} />

{#if !s.desktop.enabled}
  <Banner kind="info">{t('widgets.disabledNote')}</Banner>
{/if}

<Card title={t('widgets.section.onDesktop')}>
  {#snippet actions()}
    {#if monitors.multiple}
      <Select
        value={current}
        width={170}
        ariaLabel={t('widgets.monitor.label')}
        options={monitors.list.map((m, i) => ({ value: m.id, label: m.name || t('monitor.n', { n: i + 1 }) }))}
        onchange={(v) => (tab = v)}
      />
    {/if}
  {/snippet}
  {#if onScreen.length === 0}
    <Row label={t('widgets.empty')} />
  {/if}
  {#each onScreen as w (w.id)}
    {@const Icon = ICONS[w.type]}
    <Row label={t(`widgets.type.${w.type}`)}>
      <div class="ctl">
        <Segmented value={w.size} options={sizeOptions} ariaLabel={t('widgets.size.label')} compact onchange={(v) => resize(w, v)} />
        <Button size="sm" variant="ghost" ariaLabel={`${t('widgets.remove')} ${t(`widgets.type.${w.type}`)}`} onclick={() => remove(w)}>{t('widgets.remove')}</Button>
      </div>
      {#snippet lead()}<span class="glyph" aria-hidden="true"><Icon size={15} /></span>{/snippet}
    </Row>
  {/each}
</Card>

<Card title={t('widgets.section.gallery')}>
  {#snippet actions()}
    {#if monitors.multiple}
      <MonitorTabs monitors={monitors.list} value={current} onchange={(id) => (tab = id)} ariaLabel={t('widgets.monitor.label')} />
    {/if}
  {/snippet}
  <div class="gallery">
    {#each WidgetTypeValues as type (type)}
      {@const Icon = ICONS[type]}
      <div class="tile">
        <div class="thumb" aria-hidden="true"><Icon size={26} strokeWidth={1.6} /></div>
        <div class="meta">
          <div class="name">{t(`widgets.type.${type}`)}</div>
          <Segmented
            compact
            value={sizeChoice[type] ?? DEFAULT_WIDGET_SIZE[type]}
            options={sizeOptions}
            ariaLabel={`${t(`widgets.type.${type}`)} · ${t('widgets.size.label')}`}
            onchange={(v) => (sizeChoice[type] = v)}
          />
          <Button size="sm" block onclick={() => add(type)} disabled={!s.desktop.enabled}>{t('widgets.add')}</Button>
        </div>
      </div>
    {/each}
  </div>
</Card>

<Card title={t('widgets.section.weather')}>
  <Row sid="widgets.weather" stack>
    <div class="weather">
      <div class="now">
        {s.weather.locationName ? t('widgets.weather.current', { name: s.weather.locationName }) : t('widgets.weather.unset')}
      </div>
      <TextField
        value={query}
        placeholder={t('widgets.weather.placeholder')}
        ariaLabel={t('widgets.weather.placeholder')}
        maxlength={256}
        width="100%"
        oncommit={(v) => search(v)}
        bind:inputEl
      >
        {#snippet trailing()}<Button size="sm" disabled={searching} onclick={() => search(inputEl?.value ?? query)}>{t('widgets.weather.search')}</Button>{/snippet}
      </TextField>
      {#if searching}<p class="hint" role="status">{t('widgets.weather.searching')}</p>{/if}
      {#if searched && results.length === 0 && !searching}<p class="hint" role="status">{t('widgets.weather.none')}</p>{/if}
      {#if results.length}
        <ul class="places">
          {#each results as loc, i (i)}
            <li>
              <span class="place">{placeName(loc)}</span>
              <Button size="sm" onclick={() => useLocation(loc)}>{t('widgets.weather.use')}</Button>
            </li>
          {/each}
        </ul>
      {/if}
    </div>
  </Row>
  <Row label={t('widgets.weather.unit.label')}>
    <Segmented value={s.weather.unit} options={unitOptions} ariaLabel={t('widgets.weather.unit.label')} compact onchange={(v) => store.patch({ weather: { unit: v } })} />
  </Row>
</Card>

<style>
  .ctl { display: flex; align-items: center; gap: 8px; }
  .glyph { display: grid; place-items: center; width: 26px; height: 26px; border-radius: 7px; background: var(--s-seg); color: var(--text-2); }
  .gallery { display: grid; grid-template-columns: repeat(auto-fill, minmax(190px, 1fr)); gap: 10px; padding: 12px 14px 14px; }
  .tile { display: grid; gap: 8px; padding: 8px; border-radius: 10px; background: var(--s-seg); }
  .thumb {
    display: grid; place-items: center; height: 74px; border-radius: 8px; color: #fff;
    background: linear-gradient(150deg, #5a8dfa, #7d5be0); box-shadow: inset 0 0 0 0.5px rgba(255, 255, 255, 0.2);
  }
  .meta { display: grid; gap: 6px; }
  .name { font-weight: 550; font-size: 12.5px; }
  .weather { display: grid; gap: 8px; width: 100%; }
  .now { font-weight: 500; }
  .hint { margin: 0; color: var(--text-2); font-size: 11.5px; }
  .places { list-style: none; margin: 0; padding: 0; display: grid; gap: 4px; }
  .places li { display: flex; align-items: center; justify-content: space-between; gap: 8px; padding: 4px 0; border-top: 0.5px solid var(--separator); }
  .place { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; min-width: 0; }
</style>
