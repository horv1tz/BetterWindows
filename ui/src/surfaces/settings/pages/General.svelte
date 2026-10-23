<script lang="ts">
  import { getBridge } from '$lib/ipc';
  import type { TaskbarMode } from '$lib/protocol/generated';
  import { run } from '../actions';
  import { formatHotkey, parseHotkey } from '../hotkey';
  import { t } from '../i18n';
  import { go } from '../nav.svelte';
  import { getStore } from '../store.svelte';
  import { pushToast } from '../toast.svelte';
  import Card from '../components/Card.svelte';
  import ConfirmDialog from '../components/ConfirmDialog.svelte';
  import PageHeader from '../components/PageHeader.svelte';
  import Row from '../components/Row.svelte';
  import Segmented from '../components/Segmented.svelte';
  import Select from '../components/Select.svelte';
  import Toggle from '../components/Toggle.svelte';
  import Button from '../components/Button.svelte';
  import Banner from '../components/Banner.svelte';

  const store = getStore();
  const bridge = getBridge();
  const s = $derived(store.s);

  const FLAGS = ['internalVirtualDesktops', 'trayHosting', 'windowChrome', 'globalMenu', 'liveThumbnails'] as const;
  let confirmReset = $state(false);
  let taskbarShown = $state(false);

  const restoreKey = $derived.by(() => {
    const p = parseHotkey(s.hotkeys.toggleTaskbar);
    return p.ok ? formatHotkey(p.value) : s.hotkeys.toggleTaskbar;
  });

  async function toggleTaskbar() {
    const next = !taskbarShown;
    const r = await run(() => bridge.call('system.showTaskbar', { show: next }), { what: t('general.taskbar.show.label') });
    if (r.ok) taskbarShown = next;
  }
  async function doReset() {
    confirmReset = false;
    if (await store.resetAll()) pushToast('success', t('general.reset.done'));
  }
  async function resetLaunchpad() {
    if (await store.patch({ launchpad: { folders: [], order: [], hidden: [] } })) pushToast('success', t('general.launchpad.done'));
  }
</script>

<PageHeader title={t('page.general')} subtitle={t('general.subtitle')} />

<Card title={t('general.section.startup')}>
  <Row sid="general.autostart"><Toggle checked={s.general.autostart} onchange={(v) => store.patch({ general: { autostart: v } })} /></Row>
  <Row sid="general.language">
    <Select
      value={s.general.language} width={170}
      options={[{ value: 'auto', label: t('general.language.auto') }, { value: 'en', label: 'English' }, { value: 'ru', label: 'Русский' }]}
      onchange={(v) => store.patch({ general: { language: v } })}
    />
  </Row>
  <Row sid="general.checkUpdates"><Toggle checked={s.general.checkUpdates} onchange={(v) => store.patch({ general: { checkUpdates: v } })} /></Row>
</Card>

<Card title={t('general.section.taskbar')}>
  <Row sid="taskbar.mode" stack>
    <Segmented
      value={s.taskbar.mode}
      options={[{ value: 'hidden', label: t('taskbar.mode.hidden') }, { value: 'autoHide', label: t('taskbar.mode.autoHide') }, { value: 'visible', label: t('taskbar.mode.visible') }] as { value: TaskbarMode; label: string }[]}
      onchange={(v) => store.patch({ taskbar: { mode: v } })}
    />
    {#snippet below()}
      <p class="explain" aria-live="polite">{t(`taskbar.explain.${s.taskbar.mode}`, { key: restoreKey })}</p>
    {/snippet}
  </Row>
  <Row sid="general.taskbar.show">
    <Button onclick={toggleTaskbar}>{taskbarShown ? t('general.taskbar.hideAgain') : t('general.taskbar.show.action')}</Button>
  </Row>
</Card>

<Card title={t('general.section.setup')}>
  <Row sid="general.setup"><Button onclick={() => go('onboarding')}>{t('general.setup.action')}</Button></Row>
  <Row sid="general.launchpad"><Button onclick={resetLaunchpad}>{t('general.launchpad.action')}</Button></Row>
  <Row sid="general.reset"><Button variant="danger" onclick={() => (confirmReset = true)}>{t('general.reset.action')}</Button></Row>
</Card>

<Card title={t('general.section.experimental')} note={t('general.experimental.note')}>
  {#each FLAGS as flag (flag)}
    <Row sid={`general.features.${flag}`}>
      <Toggle checked={s.general.features[flag]} onchange={(v) => store.patch({ general: { features: { [flag]: v } } })} />
    </Row>
  {/each}
</Card>

<ConfirmDialog
  open={confirmReset} danger title={t('general.reset.confirmTitle')} message={t('general.reset.confirmBody')} confirmLabel={t('general.reset.confirm')}
  onconfirm={doReset} oncancel={() => (confirmReset = false)}
/>

<style>
  .explain { margin: 2px 0 0; color: var(--text-2); font-size: 12px; line-height: 1.45; text-wrap: pretty; }
</style>
