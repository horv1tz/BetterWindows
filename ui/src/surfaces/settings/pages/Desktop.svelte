<script lang="ts">
  // Desktop pane: the desktop layer (wallpaper per display, icon settings, shortcuts). Every control is a settings.patch or a desktop.* call.
  import { onMount } from 'svelte';
  import { getBridge } from '$lib/ipc';
  import { WallpaperFitValues, type WallpaperFit } from '$lib/protocol/generated';
  import { run } from '../actions';
  import { baseName } from '../validate';
  import { t } from '../i18n';
  import { monitors } from '../monitors.svelte';
  import { wallpaperEntryPatch } from '../patch';
  import { getStore } from '../store.svelte';
  import { pushToast } from '../toast.svelte';
  import Banner from '../components/Banner.svelte';
  import Button from '../components/Button.svelte';
  import Card from '../components/Card.svelte';
  import MonitorTabs from '../components/MonitorTabs.svelte';
  import PageHeader from '../components/PageHeader.svelte';
  import Row from '../components/Row.svelte';
  import Segmented from '../components/Segmented.svelte';
  import Slider from '../components/Slider.svelte';
  import Toggle from '../components/Toggle.svelte';
  import ScreenArt from '../art/ScreenArt.svelte';
  import { rangeOf } from '../validate';

  const bridge = getBridge();
  const store = getStore();
  const s = $derived(store.s);
  const on = $derived(s.desktop.enabled);
  const size = rangeOf('DesktopSettings.iconSize', { min: 32, max: 128 });

  /** 'all' or a monitor id; the picture and the shortcut target follow this choice. */
  let tab = $state('all');
  onMount(() => { void monitors.load(); });
  const target = $derived(tab === 'all' ? undefined : tab);
  const pictureFor = $derived(tab === 'all' ? s.desktop.wallpaperPath : (s.desktop.wallpapers[tab] ?? s.desktop.wallpaperPath));
  const ownPicture = $derived(tab !== 'all' && tab in s.desktop.wallpapers);

  const fitOptions = $derived(WallpaperFitValues.map((v) => ({ value: v, label: t(`desktop.fit.${v}`) })) as { value: WallpaperFit; label: string }[]);

  let busy = $state(false);

  async function choosePicture() {
    const picked = await run(() => bridge.call('system.pickPath', { kind: 'image', multiple: false, title: t('desktop.wallpaper.choose') }));
    if (!picked.ok || picked.value.paths.length === 0) return;
    // the host persists the picture and broadcasts settings.changed, which refreshes the view
    await run(() => bridge.call('desktop.setWallpaper', { path: picked.value.paths[0], monitorId: target }), { what: t('desktop.wallpaper.failed') });
  }

  async function resetPicture() {
    if (tab === 'all') return;
    await store.patch(wallpaperEntryPatch(tab, null));
  }

  async function addShortcut(kind: 'file' | 'folder') {
    const picked = await run(() => bridge.call('system.pickPath', { kind, multiple: false }));
    if (!picked.ok || picked.value.paths.length === 0) return;
    const path = picked.value.paths[0];
    busy = true;
    const r = await run(() => bridge.call('desktop.addShortcut', { path, monitorId: target }), { what: t('desktop.shortcuts.addFailed') });
    busy = false;
    if (r.ok) pushToast('success', t('desktop.shortcuts.added', { name: r.value.name || baseName(path) }));
  }

  async function removeShortcut(id: string) {
    await run(() => bridge.call('desktop.removeShortcut', { id }), { what: t('desktop.shortcuts.removeFailed') });
  }
</script>

<PageHeader title={t('page.desktop')} subtitle={t('desktop.subtitle')} />

<Card title={t('desktop.section.layer')}>
  <Row sid="desktop.enabled"><Toggle checked={on} onchange={(v) => store.patch({ desktop: { enabled: v } })} /></Row>
  <Row sid="desktop.hideNativeIcons" disabled={!on}>
    <Toggle checked={s.desktop.hideNativeIcons} onchange={(v) => store.patch({ desktop: { hideNativeIcons: v } })} />
  </Row>
  <Row sid="desktop.showDesktopFolder" disabled={!on}>
    <Toggle checked={s.desktop.showDesktopFolder} onchange={(v) => store.patch({ desktop: { showDesktopFolder: v } })} />
  </Row>
  {#if on && s.desktop.hideNativeIcons && !s.desktop.showDesktopFolder}
    <div class="warn"><Banner kind="warn">{t('desktop.showDesktopFolder.warn')}</Banner></div>
  {/if}
</Card>
{#if !on}<p class="note">{t('desktop.disabledNote')}</p>{/if}

<div class="pane-body" class:off={!on} inert={!on} aria-disabled={!on}>
  <Card title={t('desktop.section.wallpaper')}>
    {#if monitors.multiple}
      <div class="tabs"><MonitorTabs monitors={monitors.list} value={tab} includeAll onchange={(id) => (tab = id)} ariaLabel={t('desktop.monitor.label')} /></div>
    {/if}
    <Row sid="desktop.wallpaper" stack>
      <div class="pic">
        <ScreenArt width={220} aspect={1.6} dock="none" />
        <div class="pic-info">
          <div class="path" title={pictureFor ?? ''}>{pictureFor ? baseName(pictureFor) : t('desktop.wallpaper.default')}</div>
          <div class="pic-actions">
            <Button onclick={choosePicture} disabled={!on}>{t('desktop.wallpaper.choose')}</Button>
            {#if ownPicture}<Button size="sm" variant="ghost" onclick={resetPicture}>{t('desktop.wallpaper.reset')}</Button>{/if}
          </div>
        </div>
      </div>
    </Row>
    <Row sid="desktop.fit">
      <Segmented value={s.desktop.wallpaperFit} options={fitOptions} onchange={(v) => store.patch({ desktop: { wallpaperFit: v } })} />
    </Row>
  </Card>

  <Card title={t('desktop.section.icons')}>
    <Row sid="desktop.iconSize">
      <Slider value={s.desktop.iconSize} min={size.min} max={size.max} step={4} onchange={(v) => store.patch({ desktop: { iconSize: v } })} />
    </Row>
    <Row sid="desktop.gridSnap"><Toggle checked={s.desktop.gridSnap} onchange={(v) => store.patch({ desktop: { gridSnap: v } })} /></Row>
  </Card>

  <Card title={t('desktop.section.shortcuts')}>
    {#snippet actions()}
      <Button size="sm" disabled={busy} onclick={() => addShortcut('file')}>{t('desktop.shortcuts.addFile')}</Button>
      <Button size="sm" disabled={busy} onclick={() => addShortcut('folder')}>{t('desktop.shortcuts.addFolder')}</Button>
    {/snippet}
    {#if s.desktop.shortcuts.length === 0}
      <Row label={t('desktop.shortcuts.empty')} />
    {/if}
    {#each s.desktop.shortcuts as sc (sc.id)}
      <Row label={sc.name || baseName(sc.target)} desc={sc.target}>
        <Button size="sm" variant="ghost" ariaLabel={t('desktop.shortcuts.remove', { name: sc.name || baseName(sc.target) })} onclick={() => removeShortcut(sc.id)}>
          {t('common.remove')}
        </Button>
      </Row>
    {/each}
  </Card>
</div>

<style>
  .pane-body { transition: opacity var(--dur-fast) var(--ease-out); }
  .pane-body.off { opacity: 0.55; }
  .warn { padding: 0 14px 12px; }
  .note { margin: 8px 6px 0; color: var(--text-2); font-size: 11.5px; }
  .tabs { padding: 12px 14px 0; }
  .pic { display: flex; align-items: center; gap: 16px; flex-wrap: wrap; }
  .pic-info { display: grid; gap: 8px; min-width: 0; }
  .path { font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; max-width: 260px; }
  .pic-actions { display: flex; gap: 6px; flex-wrap: wrap; }
</style>
