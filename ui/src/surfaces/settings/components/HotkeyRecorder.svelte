<script lang="ts">
  import { Pencil, RotateCcw, Check, X } from '@lucide/svelte';
  import { canonicalHotkey, hotkeyParts, initialRecorder, isReservedHotkey, parseHotkey, reduceRecorder, sameHotkey, type RecorderEvent, type RecorderState } from '../hotkey';
  import { getRowContext } from '../rowContext';
  import { t } from '../i18n';
  import Keycaps from './Keycaps.svelte';
  import TextField from './TextField.svelte';

  interface Other { id: string; value: string; name: string }
  let { value, onchange, allowEmpty = true, others = [], defaultValue, label }: {
    value: string; onchange: (v: string) => void; allowEmpty?: boolean; others?: readonly Other[]; defaultValue?: string; label?: string;
  } = $props();
  const row = getRowContext();

  let rec = $state<RecorderState>(initialRecorder());
  let editing = $state(false);
  let pill: HTMLButtonElement | undefined = $state();
  let textInput: HTMLInputElement | undefined = $state();
  const ctx = $derived({ allowEmpty, others: others.map((o) => ({ id: o.id, value: o.value })) });
  const recording = $derived(rec.phase === 'recording');
  const dirty = $derived(defaultValue !== undefined && !sameHotkey(value, defaultValue) && !(value === '' && defaultValue === ''));

  function send(ev: RecorderEvent) {
    rec = reduceRecorder(rec, ev, ctx);
    const o = rec.outcome;
    if (!o) return;
    rec = { ...rec, outcome: undefined };
    if (o.kind === 'set') onchange(o.value);
    else if (o.kind === 'clear') onchange('');
    if (o.kind !== 'cancel' || o.reason === 'escape') queueMicrotask(() => pill?.focus());
  }

  const asEvent = (e: KeyboardEvent, type: 'keydown' | 'keyup'): RecorderEvent => ({ type, code: e.code, ctrl: e.ctrlKey, alt: e.altKey, shift: e.shiftKey, meta: e.metaKey });

  function down(e: KeyboardEvent) {
    if (!recording) return;
    if (e.repeat) { e.preventDefault(); return; }
    const before = rec;
    send(asEvent(e, 'keydown'));
    // Tab leaves the control like normal; every other key is swallowed so the browser/WebView does not act on it
    const tab = before.phase === 'recording' && e.code === 'Tab' && !e.ctrlKey && !e.altKey && !e.shiftKey && !e.metaKey;
    if (!tab) { e.preventDefault(); e.stopPropagation(); }
  }
  function up(e: KeyboardEvent) {
    if (!recording) return;
    e.preventDefault();
    send(asEvent(e, 'keyup'));
  }

  function start() { if (!(disabled)) send({ type: 'start' }); }
  const disabled = $derived(!!row?.().disabled);

  const errorText = $derived.by(() => {
    if (!rec.error) return '';
    const other = others.find((o) => o.id === rec.conflictWith)?.name ?? '';
    return t(`hotkey.err.${rec.error}`, { name: other, keys: rec.attempt ?? '' });
  });

  // ---- text fallback (Win-key combinations never reach a web page, so offer typing them) ----
  function validateText(text: string): string | null {
    const c = canonicalHotkey(text);
    if (c === '') return allowEmpty ? null : t('hotkey.err.emptyNotAllowed');
    if (c === undefined) {
      const r = parseHotkey(text);
      return t(`hotkey.parse.${r.ok ? 'syntax' : r.reason}`);
    }
    if (isReservedHotkey(c)) return t('hotkey.err.reserved', { keys: c });
    const clash = others.find((o) => sameHotkey(o.value, c));
    if (clash) return t('hotkey.err.conflict', { name: clash.name });
    return null;
  }
  function commitText(text: string) {
    const c = canonicalHotkey(text);
    if (c !== undefined) onchange(c);
    editing = false;
  }
  $effect(() => { if (editing) queueMicrotask(() => textInput?.focus()); });
</script>

<div class="rec">
  {#if editing}
    <div class="edit">
      <TextField
        value={value} oncommit={commitText} validate={validateText} placeholder="Ctrl+Alt+K" mono width={200} ariaLabel={label}
        bind:inputEl={textInput}
      />
      <button type="button" class="icon" title={t('common.cancel')} aria-label={t('common.cancel')} onclick={() => (editing = false)}><X size={14} /></button>
    </div>
  {:else}
    <div class="line">
      <button
        bind:this={pill} type="button" class="pill" class:recording class:bad={!!rec.error} {disabled}
        aria-label={label}
        aria-labelledby={label ? undefined : row?.().labelId}
        aria-describedby={row?.().descId}
        aria-pressed={recording}
        onclick={() => (recording ? send({ type: 'cancel' }) : start())}
        onkeydown={down} onkeyup={up} onblur={() => send({ type: 'blur' })}
      >
        {#if recording}
          {@const held = (['ctrl', 'alt', 'shift', 'win'] as const).filter((k) => rec.held[k]).map((k) => ({ ctrl: 'Ctrl', alt: 'Alt', shift: 'Shift', win: 'Win' })[k])}
          <span class="dot" aria-hidden="true"></span>
          {#if held.length}<Keycaps parts={held} />{:else}<span class="prompt">{t('hotkey.pressKeys')}</span>{/if}
        {:else if value}
          <Keycaps hotkey={value} />
        {:else}
          <span class="unset">{t('hotkey.notSet')}</span>
        {/if}
      </button>
      {#if !recording}
        <button type="button" class="icon" title={t('hotkey.editText')} aria-label={t('hotkey.editText')} {disabled} onclick={() => (editing = true)}><Pencil size={13} /></button>
        {#if dirty}
          <button type="button" class="icon" title={t('hotkey.reset')} aria-label={t('hotkey.reset')} {disabled} onclick={() => onchange(defaultValue ?? '')}><RotateCcw size={13} /></button>
        {/if}
      {:else}
        <span class="hint">{t('hotkey.escToCancel')}</span>
      {/if}
    </div>
    {#if errorText}<div class="msg" role="alert">{errorText}</div>{/if}
  {/if}
</div>

<style>
  .rec { display: flex; flex-direction: column; align-items: flex-end; gap: 5px; min-width: 0; }
  .line, .edit { display: flex; align-items: center; gap: 6px; }
  .pill {
    display: inline-flex; align-items: center; gap: 8px; min-width: 128px; height: 28px; padding: 0 8px; border-radius: 7px; justify-content: center;
    background: var(--s-field); border: 0.5px solid var(--s-field-border); box-shadow: var(--s-btn-shadow);
  }
  .pill:hover:not(:disabled):not(.recording) { background: var(--s-btn-hover); }
  .pill.recording { border-color: var(--accent); box-shadow: 0 0 0 3px color-mix(in srgb, var(--accent) 30%, transparent); justify-content: flex-start; min-width: 190px; }
  .pill.bad { border-color: var(--s-danger); box-shadow: 0 0 0 3px color-mix(in srgb, var(--s-danger) 28%, transparent); }
  .pill:disabled { opacity: 0.5; }
  .dot { width: 8px; height: 8px; border-radius: 50%; background: var(--s-danger); animation: blink 1.1s ease-in-out infinite; flex: none; }
  .prompt, .unset { color: var(--text-2); }
  .hint { font-size: 11.5px; color: var(--text-2); white-space: nowrap; }
  .icon { width: 24px; height: 24px; display: grid; place-items: center; padding: 0; border: 0; border-radius: 6px; background: none; color: var(--text-2); }
  .icon:hover:not(:disabled) { background: var(--hover); color: var(--text); }
  .icon:disabled { opacity: 0.4; }
  .msg { color: var(--s-danger); font-size: 11.5px; line-height: 1.3; max-width: 280px; text-align: right; }
  @keyframes blink { 0%, 100% { opacity: 1; } 50% { opacity: 0.25; } }
</style>
