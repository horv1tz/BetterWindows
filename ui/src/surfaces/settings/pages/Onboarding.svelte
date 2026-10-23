<script lang="ts">
  // First-run wizard: a full-window flow (welcome, taskbar, appearance, dock, desktop, shortcuts, finish). Skippable and keyboard friendly.
  // Every choice is a settings.patch applied immediately; "Finish" (or "Skip") sets general.firstRunDone and opens the General pane.
  import { ArrowLeft, ArrowRight, Check } from '@lucide/svelte';
  import { getBridge } from '$lib/ipc';
  import { DockPositionValues, TaskbarModeValues, ThemeModeValues, type DockPosition, type TaskbarMode, type ThemePreference } from '$lib/protocol/generated';
  import { hotkeyRows } from '../hotkeys';
  import { t } from '../i18n';
  import { go } from '../nav.svelte';
  import { ONBOARDING_STEPS, canGoto, gotoStep, initialOnboarding, isFirstStep, isLastStep, nextStep, prevStep, progressOf, stepOf } from '../onboarding';
  import { getStore } from '../store.svelte';
  import { pushToast } from '../toast.svelte';
  import { rangeOf } from '../validate';
  import AccentPicker from '../components/AccentPicker.svelte';
  import AppMark from '../art/AppMark.svelte';
  import Button from '../components/Button.svelte';
  import DockPreview from '../art/DockPreview.svelte';
  import Keycaps from '../components/Keycaps.svelte';
  import OptionCards from '../components/OptionCards.svelte';
  import Row from '../components/Row.svelte';
  import Segmented from '../components/Segmented.svelte';
  import Slider from '../components/Slider.svelte';
  import ThemeArt from '../art/ThemeArt.svelte';
  import Toggle from '../components/Toggle.svelte';

  const bridge = getBridge();
  const store = getStore();
  const s = $derived(store.s);
  const icon = rangeOf('DockSettings.iconSize', { min: 32, max: 128 });

  let st = $state(initialOnboarding());
  let addWidgets = $state(true);
  let finishing = $state(false);
  const step = $derived(stepOf(st));
  const restoreKey = $derived(s.hotkeys.toggleTaskbar);
  const spotlightKey = $derived(s.spotlight.hotkey);

  const modeOptions = $derived(TaskbarModeValues.map((v) => ({ value: v, label: t(`taskbar.mode.${v}`) })) as { value: TaskbarMode; label: string }[]);
  const positionOptions = $derived(DockPositionValues.map((v) => ({ value: v, label: t(`dock.position.${v}`) })) as { value: DockPosition; label: string }[]);
  const themeOptions = $derived(ThemeModeValues.map((v) => ({ value: v, label: t(`appearance.mode.${v}`) })) as { value: ThemePreference; label: string }[]);
  const allThemes: { value: ThemePreference; label: string }[] = $derived([...themeOptions, { value: 'auto', label: t('appearance.mode.auto') }]);

  function next() { if (!isLastStep(st)) st = nextStep(st); }
  function back() { if (!isFirstStep(st)) st = prevStep(st); }

  async function finish(skipped: boolean) {
    if (finishing) return;
    finishing = true;
    const ok = await store.patch({ general: { firstRunDone: true } });
    if (ok && !skipped && addWidgets && s.desktop.enabled) {
      for (const type of ['clock', 'weather'] as const) {
        await bridge.call('desktop.addWidget', { type }).catch(() => undefined);
      }
    }
    finishing = false;
    if (ok) {
      pushToast('success', t('onboarding.finish.title'));
      go('general');
    }
  }

  const STEP_TITLE = {
    welcome: 'onboarding.welcome.title', taskbar: 'onboarding.taskbar.title', appearance: 'onboarding.appearance.title',
    dock: 'onboarding.dock.title', desktop: 'onboarding.desktop.title', shortcuts: 'onboarding.shortcuts.title', finish: 'onboarding.finish.title',
  } as const;
  const STEP_BODY = {
    welcome: 'onboarding.welcome.body', taskbar: 'onboarding.taskbar.body', appearance: 'onboarding.appearance.body',
    dock: 'onboarding.dock.body', desktop: 'onboarding.desktop.body', shortcuts: 'onboarding.shortcuts.body', finish: 'onboarding.finish.body',
  } as const;
  const bodyText = $derived(
    step === 'taskbar' ? t(STEP_BODY.taskbar, { key: restoreKey.replace(/\+/g, ' + ') })
      : step === 'finish' ? t(STEP_BODY.finish, { key: spotlightKey.replace(/\+/g, ' + ') })
        : t(STEP_BODY[step]),
  );

  function onKey(e: KeyboardEvent) {
    if (e.key === 'Enter' && !(e.target instanceof HTMLButtonElement) && !(e.target instanceof HTMLSelectElement)) {
      e.preventDefault();
      if (isLastStep(st)) void finish(false); else next();
    }
  }
</script>

<svelte:window onkeydown={onKey} />

<section class="wiz" aria-labelledby="wiz-title">
  <header class="top">
    <ol class="dots" aria-label={t('onboarding.progress')}>
      {#each ONBOARDING_STEPS as id, i (id)}
        <li>
          <button
            type="button"
            class="dot"
            class:on={i === st.index}
            class:done={i < st.index}
            disabled={!canGoto(st, i)}
            aria-label={t('onboarding.step', { n: i + 1, total: ONBOARDING_STEPS.length })}
            aria-current={i === st.index ? 'step' : undefined}
            onclick={() => (st = gotoStep(st, i))}
          ></button>
        </li>
      {/each}
    </ol>
    <div class="bar" aria-hidden="true"><i style:width="{progressOf(st) * 100}%"></i></div>
  </header>

  <div class="body" aria-live="polite">
    <div class="art" class:wide={step === 'dock'} aria-hidden="true">
      {#if step === 'welcome'}
        <AppMark size={96} />
      {:else if step === 'taskbar'}
        <div class="hint-art">{#each [0, 1, 2] as _, i (i)}<i></i>{/each}</div>
      {:else if step === 'appearance'}
        <ThemeArt mode={s.appearance.mode} width={200} />
      {:else if step === 'dock'}
        <DockPreview position={s.dock.position} iconSize={s.dock.iconSize} magnification={s.dock.magnification} magnifiedSize={s.dock.magnifiedSize} autoHide={s.dock.autoHide} />
      {:else if step === 'desktop'}
        <span class="desk-glyph"><Check size={28} /></span>
      {:else if step === 'shortcuts'}
        <span class="desk-glyph">⌨</span>
      {:else}
        <span class="done-glyph"><Check size={40} strokeWidth={2.5} /></span>
      {/if}
    </div>

    <h1 id="wiz-title">{t(STEP_TITLE[step])}</h1>
    <p class="lead">{bodyText}</p>

    <div class="controls">
      {#if step === 'taskbar'}
        <Segmented value={s.taskbar.mode} options={modeOptions} ariaLabel={t('taskbar.mode.label')} onchange={(v) => store.patch({ taskbar: { mode: v } })} />
      {:else if step === 'appearance'}
        <OptionCards options={allThemes} value={s.appearance.mode} ariaLabel={t('appearance.mode.label')} onchange={(v) => store.patch({ appearance: { mode: v } })}>
          {#snippet art(v)}<ThemeArt mode={v} width={120} />{/snippet}
        </OptionCards>
        <AccentPicker value={s.appearance.accent} onchange={(v) => store.patch({ appearance: { accent: v } })} />
      {:else if step === 'dock'}
        <Segmented value={s.dock.position} options={positionOptions} ariaLabel={t('dock.position.label')} onchange={(v) => store.patch({ dock: { position: v } })} />
        <Row sid="dock.iconSize">
          <Slider value={s.dock.iconSize} min={icon.min} max={icon.max} step={4} onchange={(v) => store.patch({ dock: { iconSize: v } })} />
        </Row>
      {:else if step === 'desktop'}
        <div class="list">
          <Row label={t('onboarding.desktop.enable')}>
            <Toggle checked={s.desktop.enabled} onchange={(v) => store.patch({ desktop: { enabled: v } })} />
          </Row>
          <Row label={t('onboarding.desktop.widgets')} disabled={!s.desktop.enabled}>
            <Toggle checked={addWidgets} disabled={!s.desktop.enabled} onchange={(v) => (addWidgets = v)} />
          </Row>
        </div>
      {:else if step === 'shortcuts'}
        <div class="list">
          {#each hotkeyRows(s, (k) => t(k)) as r (r.id)}
            <Row label={t(`shortcuts.${r.id}.label`)}>
              <Keycaps hotkey={r.value} />
            </Row>
          {/each}
        </div>
      {/if}
    </div>
  </div>

  <footer class="foot">
    <Button variant="ghost" onclick={() => finish(true)} disabled={finishing}>{t('onboarding.skip')}</Button>
    <div class="nav">
      {#if !isFirstStep(st)}
        <Button onclick={back}>{#snippet children()}<ArrowLeft size={13} />{t('onboarding.back')}{/snippet}</Button>
      {/if}
      {#if isLastStep(st)}
        <Button variant="primary" busy={finishing} onclick={() => finish(false)}>{t('onboarding.finish')}</Button>
      {:else}
        <Button variant="primary" onclick={next}>{#snippet children()}{t('onboarding.next')}<ArrowRight size={13} />{/snippet}</Button>
      {/if}
    </div>
  </footer>
</section>

<style>
  .wiz { position: absolute; inset: 0; display: flex; flex-direction: column; min-height: 0; color: var(--text); background: var(--s-window); }
  .top { padding: 22px 28px 0; display: grid; gap: 10px; }
  .dots { display: flex; justify-content: center; gap: 10px; list-style: none; margin: 0; padding: 0; }
  .dot { width: 9px; height: 9px; border-radius: 50%; border: 0; padding: 0; background: var(--s-track); }
  .dot.done { background: color-mix(in srgb, var(--accent) 55%, transparent); }
  .dot.on { background: var(--accent); box-shadow: 0 0 0 3px color-mix(in srgb, var(--accent) 25%, transparent); }
  .dot:disabled { opacity: 0.5; }
  .bar { height: 2px; border-radius: 2px; background: var(--s-track); overflow: hidden; }
  .bar i { display: block; height: 100%; background: var(--accent); transition: width var(--dur) var(--ease-out); }
  .body { flex: 1; min-height: 0; overflow: auto; display: flex; flex-direction: column; align-items: center; text-align: center; padding: 22px 28px 12px; }
  .art { display: grid; place-items: center; min-height: 120px; margin-bottom: 14px; }
  /* the dock preview is positioned absolutely inside a fluid stage, so it needs the full column width */
  .art.wide { display: block; width: 100%; max-width: 520px; }
  .art.wide :global(.stage) { width: 100%; }
  h1 { margin: 0; font-size: 22px; font-weight: 650; letter-spacing: -0.015em; }
  .lead { margin: 8px auto 0; max-width: 460px; color: var(--text-2); font-size: 13.5px; line-height: 1.5; text-wrap: pretty; }
  .controls { width: 100%; max-width: 520px; margin-top: 18px; display: grid; gap: 14px; justify-items: center; }
  .controls :global(.row) { width: 100%; }
  .list { width: 100%; background: var(--s-card); border: 0.5px solid var(--s-card-border); border-radius: 10px; text-align: left; overflow: hidden; }
  .hint-art { display: grid; gap: 8px; width: 150px; }
  .hint-art i { display: block; height: 16px; border-radius: 6px; background: var(--s-seg); }
  .hint-art i:nth-child(2) { width: 70%; background: color-mix(in srgb, var(--accent) 45%, transparent); }
  .desk-glyph, .done-glyph {
    display: grid; place-items: center; width: 64px; height: 64px; border-radius: 18px; font-size: 30px; color: #fff;
    background: linear-gradient(150deg, var(--accent), color-mix(in srgb, var(--accent) 60%, #000));
  }
  .done-glyph { width: 84px; height: 84px; border-radius: 50%; }
  .foot { display: flex; align-items: center; justify-content: space-between; gap: 10px; padding: 14px 28px 22px; border-top: 0.5px solid var(--separator); }
  .nav { display: flex; gap: 8px; }
</style>
