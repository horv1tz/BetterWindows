<script lang="ts">
  import { ArrowUp, ArrowDown, AppWindow, FileText, Calculator, Settings, Terminal, Globe, LayoutGrid, RotateCcw } from '@lucide/svelte';
  import type { Component } from 'svelte';
  import { defaultSettings, SearchProviderIdValues, type SearchProviderId } from '$lib/protocol/generated';
  import { t } from '../i18n';
  import { getStore } from '../store.svelte';
  import { moveItem, toggleProvider } from '../patch';
  import { previewSearchUrl, rangeOf, validateSearchUrl } from '../validate';
  import { hotkeyRows } from '../hotkeys';
  import Banner from '../components/Banner.svelte';
  import Button from '../components/Button.svelte';
  import Card from '../components/Card.svelte';
  import HotkeyRecorder from '../components/HotkeyRecorder.svelte';
  import PageHeader from '../components/PageHeader.svelte';
  import Row from '../components/Row.svelte';
  import Select from '../components/Select.svelte';
  import Stepper from '../components/Stepper.svelte';
  import TextField from '../components/TextField.svelte';
  import Toggle from '../components/Toggle.svelte';

  const placeholder = 'https://example.com/search?q={query}';
  const store = getStore();
  const s = $derived(store.s);
  const defaults = defaultSettings();
  const results = rangeOf('SpotlightSettings.maxResults', { min: 1, max: 30 });

  const ICONS: Record<SearchProviderId, Component<any>> = { apps: AppWindow, files: FileText, calc: Calculator, settings: Settings, commands: Terminal, web: Globe, windows: LayoutGrid };
  const enabled = $derived(s.spotlight.providers.filter((p): p is SearchProviderId => (SearchProviderIdValues as readonly string[]).includes(p)));
  const disabled = $derived(SearchProviderIdValues.filter((p) => !enabled.includes(p)));
  const rows = $derived([...enabled.map((id) => ({ id, on: true })), ...disabled.map((id) => ({ id, on: false }))]);

  const PRESETS = [
    { id: 'duckduckgo', url: 'https://duckduckgo.com/?q={query}' },
    { id: 'google', url: 'https://www.google.com/search?q={query}' },
    { id: 'bing', url: 'https://www.bing.com/search?q={query}' },
    { id: 'yandex', url: 'https://yandex.com/search/?text={query}' },
    { id: 'brave', url: 'https://search.brave.com/search?q={query}' },
  ];
  const presetId = $derived(PRESETS.find((p) => p.url === s.spotlight.webSearchUrl)?.id ?? 'custom');
  const urlError = (v: string): string | null => {
    const r = validateSearchUrl(v);
    return r.ok ? null : t(`spotlight.url.err.${r.reason}`);
  };
  const example = $derived(previewSearchUrl(s.spotlight.webSearchUrl, t('spotlight.url.exampleQuery')));
  const hk = $derived(hotkeyRows(s, (k) => t(k)));
  const spot = $derived(hk.find((h) => h.id === 'spotlight')!);

  function setProviders(next: string[]) { store.patch({ spotlight: { providers: next } }); }
  function webOn() { return enabled.includes('web'); }
</script>

<PageHeader title={t('page.spotlight')} subtitle={t('spotlight.subtitle')} />

<Card title={t('spotlight.section.general')}>
  <Row sid="spotlight.hotkey">
    <HotkeyRecorder
      value={s.spotlight.hotkey} allowEmpty defaultValue={defaults.spotlight.hotkey} others={spot.others}
      onchange={(v) => store.patch({ spotlight: { hotkey: v } })}
    />
  </Row>
  <Row sid="spotlight.maxResults">
    <Stepper value={s.spotlight.maxResults} min={results.min} max={results.max} onchange={(v) => store.patch({ spotlight: { maxResults: v } })} />
  </Row>
</Card>

<Card title={t('spotlight.section.providers')} note={t('spotlight.providers.note')}>
  <Row sid="spotlight.providers" />
  {#each rows as r, i (r.id)}
    {@const Icon = ICONS[r.id]}
    <Row label={t(`provider.${r.id}`)} desc={t(`provider.${r.id}.desc`)}>
      {#snippet lead()}<span class="pico" class:off={!r.on}><Icon size={16} strokeWidth={1.9} /></span>{/snippet}
      {#if r.on}
        <button type="button" class="mv" aria-label={t('spotlight.moveUp', { name: t(`provider.${r.id}`) })} title={t('spotlight.moveUp', { name: t(`provider.${r.id}`) })} disabled={i === 0} onclick={() => setProviders(moveItem(enabled, i, i - 1))}><ArrowUp size={14} /></button>
        <button type="button" class="mv" aria-label={t('spotlight.moveDown', { name: t(`provider.${r.id}`) })} title={t('spotlight.moveDown', { name: t(`provider.${r.id}`) })} disabled={i === enabled.length - 1} onclick={() => setProviders(moveItem(enabled, i, i + 1))}><ArrowDown size={14} /></button>
      {/if}
      <Toggle checked={r.on} label={t(`provider.${r.id}`)} onchange={(v) => setProviders(toggleProvider(enabled, r.id, v))} />
    </Row>
  {/each}
</Card>

<Card title={t('spotlight.section.web')}>
  {#if !webOn()}<div class="warnrow"><Banner kind="info">{t('spotlight.web.disabledNote')}</Banner></div>{/if}
  <Row sid="spotlight.webEngine">
    <Select
      value={presetId} width={170} disabled={!webOn()}
      options={[...PRESETS.map((p) => ({ value: p.id, label: t(`engine.${p.id}`) })), { value: 'custom', label: t('engine.custom'), disabled: true }]}
      onchange={(v) => { const p = PRESETS.find((x) => x.id === v); if (p) store.patch({ spotlight: { webSearchUrl: p.url } }); }}
    />
  </Row>
  <Row sid="spotlight.webSearchUrl" stack disabled={!webOn()}>
    <TextField
      value={s.spotlight.webSearchUrl} mono width="100%" validate={urlError} maxlength={512} disabled={!webOn()} {placeholder}
      oncommit={(v) => store.patch({ spotlight: { webSearchUrl: v.trim() } })}
    >
      {#snippet trailing()}
        {#if s.spotlight.webSearchUrl !== defaults.spotlight.webSearchUrl}
          <button type="button" class="mv" title={t('common.reset')} aria-label={t('common.reset')} onclick={() => store.patch({ spotlight: { webSearchUrl: defaults.spotlight.webSearchUrl } })}><RotateCcw size={13} /></button>
        {/if}
      {/snippet}
    </TextField>
    {#snippet below()}
      <div class="example"><span>{t('spotlight.url.example')}</span> <code>{example}</code></div>
    {/snippet}
  </Row>
</Card>

<style>
  .pico { width: 28px; height: 28px; display: grid; place-items: center; border-radius: 7px; background: color-mix(in srgb, var(--accent) 16%, transparent); color: var(--accent); }
  .pico.off { background: var(--s-seg); color: var(--text-3); }
  .mv { width: 24px; height: 24px; display: grid; place-items: center; padding: 0; border: 0; border-radius: 6px; background: none; color: var(--text-2); }
  .mv:hover:not(:disabled) { background: var(--hover); color: var(--text); }
  .mv:disabled { opacity: 0.3; }
  .warnrow { padding: 0 12px 4px; }
  .warnrow :global(.banner) { margin-top: 10px; }
  .example { margin-top: -2px; color: var(--text-2); font-size: 11.5px; line-height: 1.4; overflow-wrap: anywhere; }
  .example code { font-family: ui-monospace, 'Cascadia Mono', Consolas, monospace; font-size: 11px; color: var(--text); user-select: text; }
</style>
