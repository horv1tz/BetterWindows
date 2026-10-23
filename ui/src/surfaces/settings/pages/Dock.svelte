<script lang="ts">
  import { FolderPlus, AppWindow, X, Folder } from '@lucide/svelte';
  import { getBridge } from '$lib/ipc';
  import type { DockPosition } from '$lib/protocol/generated';
  import { dyn } from '$lib/dyn';
  import AppIcon from '$lib/components/AppIcon.svelte';
  import { run } from '../actions';
  import { t, plural } from '../i18n';
  import { getStore } from '../store.svelte';
  import { rangeOf, baseName } from '../validate';
  import { pushToast } from '../toast.svelte';
  import { loadApps, appsState } from '../apps.svelte';
  import Banner from '../components/Banner.svelte';
  import Button from '../components/Button.svelte';
  import Card from '../components/Card.svelte';
  import OptionCards from '../components/OptionCards.svelte';
  import PageHeader from '../components/PageHeader.svelte';
  import Row from '../components/Row.svelte';
  import Segmented from '../components/Segmented.svelte';
  import Slider from '../components/Slider.svelte';
  import Toggle from '../components/Toggle.svelte';
  import DockPreview from '../art/DockPreview.svelte';
  import ScreenArt from '../art/ScreenArt.svelte';

  const store = getStore();
  const bridge = getBridge();
  const s = $derived(store.s);
  const size = rangeOf('DockSettings.iconSize', { min: 32, max: 128 });
  const big = rangeOf('DockSettings.magnifiedSize', { min: 32, max: 192 });

  /** live values while a slider is dragged (the preview follows, the setting is written on release) */
  let liveSize = $state<number | null>(null);
  let liveBig = $state<number | null>(null);
  const shownSize = $derived(liveSize ?? s.dock.iconSize);
  const shownBig = $derived(Math.max(shownSize, liveBig ?? s.dock.magnifiedSize));

  $effect(() => { loadApps(); });

  async function pin(kind: 'app' | 'folder') {
    const picked = await run(() => bridge.call('system.pickPath', { kind, multiple: false, title: t(kind === 'app' ? 'dock.pickApp' : 'dock.pickFolder') }));
    if (!picked.ok || picked.value.paths.length === 0) return;
    const path = picked.value.paths[0];
    const r = await run(() => bridge.call('dock.pinPath', { path }), { what: t('dock.pinFailed') });
    if (r.ok) pushToast('success', t('dock.pinned', { name: baseName(path) }));
  }
  async function unpin(id: string) {
    await run(() => bridge.call('dock.unpin', { itemId: id }), { what: t('dock.unpinFailed') });
  }

  const pins = $derived(s.dock.pins.map((p) => {
    const app = appsState.byId.get(p.id);
    const name = p.name || app?.name || baseName(p.target);
    const icon = p.kind === 'folder' ? dyn.pathIcon(p.target, 64) : (app?.iconUrl ?? dyn.appIcon(p.id, 64));
    return { ...p, name, icon };
  }));
</script>

<PageHeader title={t('page.dock')} subtitle={t('dock.subtitle')} />

{#if !s.dock.enabled}
  <Banner kind="info">{t('dock.disabledNote')}</Banner>
{/if}

<Card title={t('dock.section.general')}>
  <Row sid="dock.enabled"><Toggle checked={s.dock.enabled} onchange={(v) => store.patch({ dock: { enabled: v } })} /></Row>
  <Row sid="dock.position" stack disabled={!s.dock.enabled}>
    <OptionCards
      value={s.dock.position} disabled={!s.dock.enabled}
      options={[{ value: 'bottom', label: t('dock.position.bottom') }, { value: 'left', label: t('dock.position.left') }, { value: 'right', label: t('dock.position.right') }] as { value: DockPosition; label: string }[]}
      onchange={(v) => store.patch({ dock: { position: v } })}
    >
      {#snippet art(v)}<ScreenArt width={104} aspect={1.6} dock={v} items={4} />{/snippet}
    </OptionCards>
  </Row>
  <Row sid="dock.monitor" disabled={!s.dock.enabled}>
    <Segmented
      value={s.dock.monitor}
      options={[{ value: 'primary', label: t('dock.monitor.primary') }, { value: 'all', label: t('dock.monitor.all') }]}
      onchange={(v) => store.patch({ dock: { monitor: v } })}
    />
  </Row>
</Card>

<Card title={t('dock.section.size')}>
  <div class="preview"><DockPreview position={s.dock.position} iconSize={shownSize} magnification={s.dock.magnification} magnifiedSize={shownBig} indicators={s.dock.showIndicators} autoHide={s.dock.autoHide} /></div>
  <Row sid="dock.iconSize" disabled={!s.dock.enabled}>
    <Slider
      value={s.dock.iconSize} min={size.min} max={size.max} step={2} format={(v) => `${v} px`}
      oninput={(v) => (liveSize = v)} onchange={(v) => { liveSize = null; store.patch({ dock: { iconSize: v } }); }}
    />
  </Row>
  <Row sid="dock.magnification" disabled={!s.dock.enabled}><Toggle checked={s.dock.magnification} onchange={(v) => store.patch({ dock: { magnification: v } })} /></Row>
  <Row sid="dock.magnifiedSize" disabled={!s.dock.enabled || !s.dock.magnification}>
    <Slider
      value={Math.max(s.dock.iconSize, s.dock.magnifiedSize)} min={Math.max(size.min, shownSize)} max={big.max} step={2} format={(v) => `${v} px`}
      oninput={(v) => (liveBig = v)} onchange={(v) => { liveBig = null; store.patch({ dock: { magnifiedSize: v } }); }}
    />
  </Row>
</Card>

<Card title={t('dock.section.behavior')}>
  <Row sid="dock.autoHide" disabled={!s.dock.enabled}><Toggle checked={s.dock.autoHide} onchange={(v) => store.patch({ dock: { autoHide: v } })} /></Row>
  <Row sid="dock.reserveSpace" disabled={!s.dock.enabled || s.dock.autoHide}><Toggle checked={s.dock.reserveSpace && !s.dock.autoHide} onchange={(v) => store.patch({ dock: { reserveSpace: v } })} /></Row>
  <Row sid="dock.showIndicators" disabled={!s.dock.enabled}><Toggle checked={s.dock.showIndicators} onchange={(v) => store.patch({ dock: { showIndicators: v } })} /></Row>
  <Row sid="dock.bounceOnLaunch" disabled={!s.dock.enabled}><Toggle checked={s.dock.bounceOnLaunch} onchange={(v) => store.patch({ dock: { bounceOnLaunch: v } })} /></Row>
</Card>

<Card title={t('dock.section.pinned')} note={t('dock.pinned.note')}>
  {#snippet actions()}
    <Button size="sm" onclick={() => pin('app')}><AppWindow size={13} />{t('dock.addApp')}</Button>
    <Button size="sm" onclick={() => pin('folder')}><FolderPlus size={13} />{t('dock.addFolder')}</Button>
  {/snippet}
  {#if pins.length === 0}
    <div class="empty">{t('dock.pinned.empty')}</div>
  {:else}
    <ul class="pins" aria-label={t('dock.section.pinned')}>
      {#each pins as p (p.id)}
        <li class="pin">
          <span class="pico">{#if p.kind === 'folder'}<Folder size={18} strokeWidth={1.8} />{:else}<AppIcon src={p.icon} name={p.name} size={26} />{/if}</span>
          <span class="pname">{p.name}</span>
          <span class="pkind">{p.kind === 'folder' ? t('dock.kind.folder') : t('dock.kind.app')}</span>
          <button type="button" class="rm" aria-label={t('dock.unpin', { name: p.name })} title={t('dock.unpin', { name: p.name })} onclick={() => unpin(p.id)}><X size={14} /></button>
        </li>
      {/each}
    </ul>
    <div class="count">{plural(pins.length, { one: t('dock.count.one', { n: pins.length }), few: t('dock.count.few', { n: pins.length }), many: t('dock.count.many', { n: pins.length }), other: t('dock.count.other', { n: pins.length }) })}</div>
  {/if}
</Card>

<style>
  .preview { padding: 12px 12px 4px; }
  .empty { padding: 18px 14px; color: var(--text-2); text-align: center; }
  .pins { list-style: none; margin: 0; padding: 0; }
  .pin { position: relative; display: flex; align-items: center; gap: 10px; padding: 6px 10px 6px 14px; min-height: 40px; }
  .pin + .pin::before { content: ''; position: absolute; top: 0; left: 14px; right: 0; border-top: 0.5px solid var(--separator); }
  .pico { width: 28px; height: 28px; display: grid; place-items: center; flex: none; color: var(--accent); }
  .pname { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-weight: 500; }
  .pkind { color: var(--text-2); font-size: 11.5px; }
  .rm { width: 24px; height: 24px; display: grid; place-items: center; padding: 0; border: 0; border-radius: 6px; background: none; color: var(--text-2); }
  .rm:hover { background: var(--hover); color: var(--s-danger); }
  .count { padding: 8px 14px; border-top: 0.5px solid var(--separator); color: var(--text-2); font-size: 11.5px; }
</style>
