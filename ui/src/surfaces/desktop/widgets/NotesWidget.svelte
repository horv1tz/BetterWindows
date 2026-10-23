<script lang="ts">
  import { onDestroy } from 'svelte';
  import NotebookPen from '@lucide/svelte/icons/notebook-pen';
  import { createDebouncer } from '../debounce';
  import { t } from '../strings';
  import type { WidgetProps } from './registry';

  let { config, saveConfig, size }: WidgetProps = $props();

  const hostText = $derived(typeof config.text === 'string' ? config.text : '');
  let text = $state('');
  let focused = $state(false);
  let lastSent: string | null = null;
  let initialised = false;

  // Persist 600 ms after the last keystroke; desktop.updateWidget REPLACES the config, so the other keys are carried along.
  const save = createDebouncer<string>((value) => {
    lastSent = value;
    saveConfig({ ...config, text: value });
  }, 600);
  onDestroy(() => save.flush());

  // Adopt the host's text unless the user is editing or has unsent changes (our own echo must not move the caret).
  $effect(() => {
    const incoming = hostText;
    if (!initialised) { initialised = true; text = incoming; return; }
    if (focused || save.pending) return;
    if (incoming !== text && incoming !== lastSent) text = incoming;
  });

  function oninput(e: Event) {
    text = (e.currentTarget as HTMLTextAreaElement).value;
    save.push(text);
  }
</script>

<div class="notes {size}" role="group" aria-label={t('notes.title')}>
  <header><NotebookPen size={14} strokeWidth={2.2} /><h3>{t('notes.title')}</h3></header>
  <textarea
    value={text} {oninput} onfocus={() => (focused = true)} onblur={() => { focused = false; save.flush(); }}
    placeholder={t('notes.placeholder')} aria-label={t('notes.label')} spellcheck="false" maxlength="20000" data-nodrag
  ></textarea>
</div>

<style>
  .notes { position: absolute; inset: 0; display: flex; flex-direction: column; background: color-mix(in oklab, #ffcc33 15%, transparent); }
  header { display: flex; align-items: center; gap: 6px; padding: 11px 14px 4px; color: #b78600; }
  :global(:root[data-theme='dark']) header { color: #ffd60a; }
  h3 { margin: 0; font-size: 13px; font-weight: 700; }
  textarea {
    flex: 1; min-height: 0; resize: none; border: 0; outline: none; background: none; color: var(--text);
    font: inherit; font-size: 13px; line-height: 1.45; padding: 4px 14px 12px; user-select: text; cursor: text;
  }
  textarea::placeholder { color: var(--text-3); }
  .notes.small textarea { font-size: 12.5px; }
</style>
