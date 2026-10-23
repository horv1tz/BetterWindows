<script lang="ts">
  import { flip } from 'svelte/animate';
  import { Plus, Check } from '@lucide/svelte';
  import { getBridge } from '$lib/ipc';
  import type { Reminder } from '$lib/protocol/generated';
  import { locale, use24Hour, useHostState } from '../shared.svelte';
  import { cleanTitle, filterByList, formatDue, isOverdue, sortReminders } from '../remindersLogic';
  import { t } from '../strings';
  import type { WidgetProps } from './registry';

  let { config, size }: WidgetProps = $props();

  const rems = useHostState('reminders.changed', { items: [] as Reminder[] });
  const rows = $derived({ small: 2, medium: 3, large: 7 }[size]);
  let now = $state(new Date());
  $effect(() => { const id = setInterval(() => (now = new Date()), 30_000); return () => clearInterval(id); });

  // optimistic checkbox state until the host's reminders.changed confirms it
  let pending = $state<Record<string, boolean>>({});
  let failed = $state(false);
  const items = $derived(
    sortReminders(filterByList(rems.value.items, typeof config.list === 'string' ? config.list : undefined).map((r) => (r.id in pending ? { ...r, done: pending[r.id] } : r))),
  );
  const open = $derived(items.filter((r) => !r.done));
  const shown = $derived(items.slice(0, rows));
  const hidden = $derived(Math.max(0, items.length - shown.length));
  const labels = $derived({ today: t('rem.today'), tomorrow: t('rem.tomorrow'), yesterday: t('rem.yesterday') });

  // reminders.due flashes the row
  let flash = $state<string | null>(null);
  $effect(() => getBridge().on('reminders.due', (r) => { flash = r.id; setTimeout(() => { if (flash === r.id) flash = null; }, 4500); }));

  async function toggle(r: Reminder) {
    const done = !r.done;
    pending = { ...pending, [r.id]: done };
    failed = false;
    try {
      await getBridge().call('reminders.update', { id: r.id, done });
    } catch {
      failed = true;
      setTimeout(() => (failed = false), 3500);
    } finally {
      const { [r.id]: _gone, ...rest } = pending;
      setTimeout(() => { pending = rest; }, 400);
    }
  }

  let draft = $state('');
  let adding = $state(false);
  async function add(e: SubmitEvent) {
    e.preventDefault();
    const title = cleanTitle(draft);
    if (!title || adding) return;
    adding = true;
    try {
      await getBridge().call('reminders.add', { title, ...(typeof config.list === 'string' && config.list ? { list: config.list } : {}) });
      draft = '';
    } catch {
      failed = true;
      setTimeout(() => (failed = false), 3500);
    } finally { adding = false; }
  }
</script>

<div class="rem {size}" role="group" aria-label={t('rem.title')}>
  <header>
    <h3>{t('rem.title')}</h3>
    <span class="count" aria-live="polite">{open.length}</span>
  </header>

  {#if items.length === 0}
    <div class="empty"><Check size={22} strokeWidth={2} /><b>{t('rem.empty')}</b>{#if size !== 'small'}<span>{t('rem.emptyHint')}</span>{/if}</div>
  {:else}
    <ul>
      {#each shown as r (r.id)}
        {@const overdue = isOverdue(r, now)}
        <li class:done={r.done} class:overdue class:flash={flash === r.id} animate:flip={{ duration: document.documentElement.dataset.reduceMotion === 'true' ? 0 : 240 }}>
          <button type="button" class="check" role="checkbox" aria-checked={r.done} aria-label={r.done ? t('rem.uncomplete', { title: r.title }) : t('rem.complete', { title: r.title })} onclick={() => toggle(r)}>
            <span class="ring" aria-hidden="true">{#if r.done}<Check size={11} strokeWidth={3.4} />{/if}</span>
          </button>
          <span class="body">
            <span class="title">{r.title}</span>
            {#if r.due && !r.done}<span class="due" title={overdue ? t('rem.overdue') : undefined}>{formatDue(r.due, now, locale(), use24Hour(), labels)}</span>{/if}
          </span>
        </li>
      {/each}
    </ul>
    {#if hidden > 0}<div class="more">{t('rem.more', { n: hidden })}</div>{/if}
  {/if}

  <form class="add" onsubmit={add}>
    <Plus size={14} strokeWidth={2.4} />
    <input type="text" placeholder={t('rem.add')} aria-label={t('rem.addLabel')} maxlength="200" bind:value={draft} disabled={adding} autocomplete="off" spellcheck="false" />
  </form>
  {#if failed}<div class="toast" role="alert">{t('rem.failed')}</div>{/if}
</div>

<style>
  .rem { position: absolute; inset: 0; padding: 12px 14px 10px; display: flex; flex-direction: column; }
  header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 4px; }
  h3 { margin: 0; font-size: 13px; font-weight: 700; color: var(--accent); }
  .count { font-size: 24px; font-weight: 300; line-height: 1; color: var(--text); font-variant-numeric: tabular-nums; }
  ul { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; flex: 1; min-height: 0; overflow: hidden; }
  li { display: flex; align-items: flex-start; gap: 8px; padding: 4px 0; border-bottom: 0.5px solid var(--separator); border-radius: 0; transition: background 600ms ease; }
  li:last-child { border-bottom: 0; }
  .rem.large li { padding: 7px 0; }
  .check { flex: none; width: 18px; height: 18px; margin-top: 1px; padding: 0; border: 0; background: none; border-radius: 50%; cursor: default; }
  .ring { display: grid; place-items: center; width: 17px; height: 17px; border-radius: 50%; border: 1.5px solid var(--text-3); color: #fff; transition: background var(--dur-fast) ease, border-color var(--dur-fast) ease; }
  .check:hover .ring { border-color: var(--accent); }
  .check:focus-visible { outline: 2px solid var(--accent); outline-offset: 1px; }
  li.done .ring { background: var(--accent); border-color: var(--accent); }
  li.overdue .ring { border-color: #ff453a; }
  .body { display: flex; flex-direction: column; min-width: 0; flex: 1; }
  .title { font-size: 13px; line-height: 1.25; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  li.done .title { color: var(--text-3); text-decoration: line-through; }
  .rem.medium .body { flex-direction: row; align-items: baseline; justify-content: space-between; gap: 12px; }
  .rem.medium .due { flex: none; }
  .rem.medium li { align-items: baseline; }
  .rem.medium .check { align-self: center; margin-top: 0; }
  .due { font-size: 11px; color: var(--text-2); font-variant-numeric: tabular-nums; }
  li.overdue .due { color: #ff453a; font-weight: 600; }
  li.flash { background: color-mix(in oklab, var(--accent) 20%, transparent); }
  .more { font-size: 11px; color: var(--text-2); padding: 2px 0 0 26px; }
  .empty { flex: 1; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 3px; color: var(--text-2); font-size: 12px; text-align: center; }
  .empty b { font-size: 13px; color: var(--text); }
  .add { display: flex; align-items: center; gap: 6px; margin-top: 4px; padding: 0 8px; height: 28px; border-radius: 8px; color: var(--text-2); background: var(--hover); transition: background var(--dur-fast) ease; }
  .add:focus-within { background: var(--press); color: var(--text); }
  input { flex: 1; min-width: 0; height: 100%; border: 0; outline: none; background: none; color: var(--text); font: inherit; }
  input::placeholder { color: var(--text-2); }
  .toast { position: absolute; left: 12px; right: 12px; bottom: 44px; text-align: center; font-size: 11.5px; padding: 5px 8px; border-radius: 8px; background: #ff453a; color: #fff; animation: fade 160ms ease; }
  @keyframes fade { from { opacity: 0; } to { opacity: 1; } }
</style>
