<script lang="ts">
  import type { ThemePreference } from '$lib/protocol/generated';
  import { t } from '../i18n';
  import { getStore } from '../store.svelte';
  import Card from '../components/Card.svelte';
  import OptionCards from '../components/OptionCards.svelte';
  import PageHeader from '../components/PageHeader.svelte';
  import Row from '../components/Row.svelte';
  import Toggle from '../components/Toggle.svelte';
  import ThemeArt from '../art/ThemeArt.svelte';
  import AccentPicker from '../components/AccentPicker.svelte';

  const store = getStore();
  const s = $derived(store.s);
</script>

<PageHeader title={t('page.appearance')} subtitle={t('appearance.subtitle')} />

<Card title={t('appearance.section.theme')}>
  <Row sid="appearance.mode" stack>
    <OptionCards
      value={s.appearance.mode}
      options={[{ value: 'light', label: t('appearance.mode.light') }, { value: 'dark', label: t('appearance.mode.dark') }, { value: 'auto', label: t('appearance.mode.auto') }] as { value: ThemePreference; label: string }[]}
      onchange={(v) => store.patch({ appearance: { mode: v } })}
    >
      {#snippet art(v)}<ThemeArt mode={v} />{/snippet}
    </OptionCards>
  </Row>
</Card>

<Card title={t('appearance.section.accent')}>
  <Row sid="appearance.accent" stack>
    <AccentPicker value={s.appearance.accent} onchange={(v) => store.patch({ appearance: { accent: v } })} />
  </Row>
</Card>

<Card title={t('appearance.section.effects')}>
  <Row sid="appearance.reduceMotion"><Toggle checked={s.appearance.reduceMotion} onchange={(v) => store.patch({ appearance: { reduceMotion: v } })} /></Row>
  <Row sid="appearance.reduceTransparency"><Toggle checked={s.appearance.reduceTransparency} onchange={(v) => store.patch({ appearance: { reduceTransparency: v } })} /></Row>
</Card>
