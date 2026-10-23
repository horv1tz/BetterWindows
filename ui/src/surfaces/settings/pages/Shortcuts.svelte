<script lang="ts">
  import { TriangleAlert } from '@lucide/svelte';
  import { t } from '../i18n';
  import { getStore } from '../store.svelte';
  import { hotkeyPatch, hotkeyRows, resetHotkeysPatch, type HotkeyId } from '../hotkeys';
  import Banner from '../components/Banner.svelte';
  import Button from '../components/Button.svelte';
  import Card from '../components/Card.svelte';
  import HotkeyRecorder from '../components/HotkeyRecorder.svelte';
  import PageHeader from '../components/PageHeader.svelte';
  import Row from '../components/Row.svelte';

  const store = getStore();
  const s = $derived(store.s);
  const rows = $derived(hotkeyRows(s, (k) => t(k)));
  const anyConflict = $derived(rows.some((r) => r.conflicts.length > 0));
  const modified = $derived(Object.keys(resetHotkeysPatch(s)).length > 0);
</script>

<PageHeader title={t('page.shortcuts')} subtitle={t('shortcuts.subtitle')} />

{#if anyConflict}
  <Banner kind="warn">{t('shortcuts.conflictBanner')}</Banner>
{/if}

<Card title={t('shortcuts.section.global')} note={t('shortcuts.note')}>
  {#snippet actions()}
    <Button size="sm" disabled={!modified} onclick={() => store.patch(resetHotkeysPatch(s))}>{t('shortcuts.restoreAll')}</Button>
  {/snippet}
  {#each rows as r (r.id)}
    <Row sid={`shortcuts.${r.id}`}>
      <div class="cell">
        <HotkeyRecorder
          value={r.value} allowEmpty={r.allowEmpty} defaultValue={r.defaultValue} others={r.others}
          onchange={(v) => store.patch(hotkeyPatch(r.id as HotkeyId, v))}
        />
        {#if r.conflicts.length}
          <div class="clash" role="status"><TriangleAlert size={12} />{t('shortcuts.sharedWith', { names: r.conflicts.join(', ') })}</div>
        {/if}
      </div>
    </Row>
  {/each}
</Card>

<style>
  .cell { display: flex; flex-direction: column; align-items: flex-end; gap: 5px; }
  .clash { display: flex; align-items: center; gap: 5px; color: var(--s-warn); font-size: 11.5px; }
</style>
