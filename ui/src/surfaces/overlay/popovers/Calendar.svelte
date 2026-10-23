<script lang="ts">
  import './status/popover.css';
  import { onDestroy } from 'svelte';
  import { ChevronLeft, ChevronRight, Circle, CircleCheck } from '@lucide/svelte';
  import { getInit } from '$lib/ipc';
  import { hostState } from '$lib/ipc/state.svelte';
  import { currentLocale } from '$lib/i18n.svelte';
  import { defaultReminderList, type PopoverRequest, type Reminder } from '$lib/protocol/generated';
  import PopoverFrame from './PopoverFrame.svelte';
  import { openSettings } from './status/actions';
  import { buildMonthGrid, dateKey, weekdayOrder, weekStartFor } from './status/calendarGrid';
  import { t } from './status/strings';
  import Toggle from './status/Toggle.svelte';

  let { request, onclose }: { request: PopoverRequest; onclose: () => void } = $props();

  const LOCALE_TAGS: Record<string, string> = { en: 'en-US', ru: 'ru-RU' };
  const locale = $derived(LOCALE_TAGS[currentLocale()] ?? 'en-US');

  const settings = hostState('settings.changed', getInit().settings);
  const reminders = hostState('reminders.changed', defaultReminderList());
  onDestroy(() => { settings.dispose(); reminders.dispose(); });

  const use24 = $derived(settings.value.menuBar.use24Hour);

  // The clock ticks once a second; the month view is independent of it until the user navigates.
  let now = $state(new Date());
  $effect(() => {
    const id = setInterval(() => (now = new Date()), 1000);
    return () => clearInterval(id);
  });
  const today = $derived({ year: now.getFullYear(), month: now.getMonth(), day: now.getDate() });

  const start = new Date();
  let view = $state({ year: start.getFullYear(), month: start.getMonth() });
  let pickerYear = $state(start.getFullYear());
  let picking = $state(false);
  let showWeeks = $state(false);

  const weekStart = $derived(weekStartFor(locale));
  const grid = $derived(buildMonthGrid(view.year, view.month, weekStart, today));

  /** Days (YYYY-MM-DD) that have at least one reminder due; due strings are local date-times, so the date part is taken as written. */
  const reminderDays = $derived(new Set(reminders.value.items.filter((r) => r.due).map((r) => r.due!.slice(0, 10))));
  const todayKey = $derived(dateKey(today.year, today.month, today.day));
  const agenda = $derived(
    reminders.value.items
      .filter((r: Reminder) => r.due?.slice(0, 10) === todayKey)
      .slice()
      .sort((a, b) => (a.due ?? '').localeCompare(b.due ?? '')),
  );

  const fmtTime = $derived(new Intl.DateTimeFormat(locale, { hour: 'numeric', minute: '2-digit', hour12: !use24 }));
  const clockText = $derived(fmtTime.format(now));
  const longDate = $derived(new Intl.DateTimeFormat(locale, { weekday: 'long', day: 'numeric', month: 'long' }).format(now));
  const monthLabel = $derived(new Intl.DateTimeFormat(locale, { month: 'long', year: 'numeric', timeZone: 'UTC' }).format(Date.UTC(view.year, view.month, 1)));

  /** Short weekday header for a 0..6 weekday (Sunday = 0): 7 Jan 2024 was a Sunday. */
  function weekdayShort(w: number): string {
    return new Intl.DateTimeFormat(locale, { weekday: 'short', timeZone: 'UTC' }).format(Date.UTC(2024, 0, 7 + w));
  }
  function dayLabel(y: number, m: number, d: number): string {
    return new Intl.DateTimeFormat(locale, { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric', timeZone: 'UTC' }).format(Date.UTC(y, m, d));
  }
  function monthShort(m: number): string {
    return new Intl.DateTimeFormat(locale, { month: 'short', timeZone: 'UTC' }).format(Date.UTC(pickerYear, m, 1));
  }
  function timeOf(due?: string): string {
    if (!due || due.length < 16) return '';
    return fmtTime.format(new Date(due));
  }

  function shift(months: number) {
    const d = new Date(Date.UTC(view.year, view.month + months, 1));
    view = { year: d.getUTCFullYear(), month: d.getUTCMonth() };
  }
  function goToday() {
    view = { year: today.year, month: today.month };
    picking = false;
  }
  function pickMonth(m: number) {
    view = { year: pickerYear, month: m };
    picking = false;
  }
  function togglePicker() {
    picking = !picking;
    pickerYear = view.year;
  }
  const dayOf = (cell: { year: number; month: number; day: number }) => dateKey(cell.year, cell.month, cell.day);
</script>

<PopoverFrame anchor={request.anchor} placement="below-end" width={300} label={t('cal.label')}>
  <div class="sp cal-root">
    <header class="head">
      <div class="date">
        <div class="sp-title">{longDate}</div>
      </div>
      <time class="clock" datetime={now.toISOString()}>{clockText}</time>
    </header>

    <div class="sp-card cal-card">
      <div class="nav">
        <button type="button" class="sp-icon-btn" aria-label={t('cal.prevMonth')} onclick={() => shift(-1)}><ChevronLeft size={16} /></button>
        <button type="button" class="month" aria-expanded={picking} aria-label={t('cal.pickMonth')} onclick={togglePicker}>{monthLabel}</button>
        <button type="button" class="sp-icon-btn" aria-label={t('cal.nextMonth')} onclick={() => shift(1)}><ChevronRight size={16} /></button>
      </div>

      {#if picking}
        <div class="picker">
          <div class="nav">
            <button type="button" class="sp-icon-btn" aria-label={t('cal.prevYear')} onclick={() => (pickerYear -= 1)}><ChevronLeft size={15} /></button>
            <span class="year">{pickerYear}</span>
            <button type="button" class="sp-icon-btn" aria-label={t('cal.nextYear')} onclick={() => (pickerYear += 1)}><ChevronRight size={15} /></button>
          </div>
          <div class="months">
            {#each Array.from({ length: 12 }, (_, m) => m) as m (m)}
              <button
                type="button"
                class="mo"
                class:on={m === view.month && pickerYear === view.year}
                class:now={m === today.month && pickerYear === today.year}
                aria-label={new Intl.DateTimeFormat(locale, { month: 'long', timeZone: 'UTC' }).format(Date.UTC(pickerYear, m, 1))}
                onclick={() => pickMonth(m)}
              >{monthShort(m)}</button>
            {/each}
          </div>
        </div>
      {:else}
        <div class="grid" role="grid" aria-label={monthLabel} style:--cols={showWeeks ? '24px repeat(7, 1fr)' : 'repeat(7, 1fr)'}>
          <div class="row head-row" role="row">
            {#if showWeeks}<span class="wk head" role="columnheader" aria-hidden="true"></span>{/if}
            {#each weekdayOrder(weekStart) as w (w)}
              <span class="wd" role="columnheader">{weekdayShort(w)}</span>
            {/each}
          </div>
          {#each grid.rows as row, r (r)}
            <div class="row" role="row">
              {#if showWeeks}<span class="wk" role="rowheader" aria-label={t('cal.week', { n: grid.weekNumbers[r] })}>{grid.weekNumbers[r]}</span>{/if}
              {#each row as cell (dayOf(cell))}
                {@const key = dayOf(cell)}
                <span
                  role="gridcell"
                  class="day"
                  class:out={!cell.inMonth}
                  class:today={cell.isToday}
                  aria-current={cell.isToday ? 'date' : undefined}
                  aria-label={dayLabel(cell.year, cell.month, cell.day)}
                >
                  <span class="num">{cell.day}</span>
                  {#if reminderDays.has(key)}<span class="dot" aria-label={t('cal.reminderDay')}></span>{/if}
                </span>
              {/each}
            </div>
          {/each}
        </div>
      {/if}

      <div class="foot">
        <button type="button" class="today-btn" onclick={goToday}>{t('cal.today')}</button>
        <label class="wk-toggle">
          <span class="sp-small">{t('cal.weekNumbers')}</span>
          <Toggle checked={showWeeks} label={t('cal.weekNumbers')} onchange={(v) => (showWeeks = v)} />
        </label>
      </div>
    </div>

    <div class="sp-section">{t('cal.agenda')}</div>
    <div class="sp-card agenda">
      {#each agenda as r (r.id)}
        <div class="ag-row">
          <span class="check" class:done={r.done} aria-hidden="true">{#if r.done}<CircleCheck size={16} />{:else}<Circle size={16} />{/if}</span>
          <div class="sp-grow ag-text">
            <div class="sp-title" class:struck={r.done}>{r.title}</div>
            {#if timeOf(r.due)}<div class="sp-sub">{timeOf(r.due)}</div>{/if}
          </div>
          {#if r.done}<span class="sp-small">{t('cal.done')}</span>{/if}
        </div>
      {:else}
        <div class="sp-empty">{t('cal.agendaEmpty')}</div>
      {/each}
    </div>

    <div class="sp-sep"></div>
    <button type="button" class="sp-link" onclick={() => { void openSettings('ms-settings:dateandtime'); onclose(); }}>
      <span>{t('cal.settings')}</span>
    </button>
  </div>
</PopoverFrame>

<style>
  .cal-root { gap: 8px; }
  .head { display: flex; align-items: baseline; justify-content: space-between; gap: 10px; padding: 4px 8px 0; }
  .date .sp-title { font-size: 15px; }
  .clock { color: var(--text-2); font-variant-numeric: tabular-nums; font-size: 13px; white-space: nowrap; }
  .cal-card { gap: 6px; padding: 8px 8px 6px; }
  .nav { display: flex; align-items: center; justify-content: space-between; gap: 4px; }
  .month { font-weight: 600; font-size: 13px; padding: 4px 8px; border-radius: 6px; }
  .month:hover { background: var(--hover); }
  .year { font-weight: 600; font-variant-numeric: tabular-nums; }
  .grid, .picker { display: grid; gap: 2px; }
  .row { display: grid; grid-template-columns: var(--cols); align-items: center; }
  .wd { text-align: center; font-size: 10.5px; color: var(--text-2); text-transform: uppercase; padding: 2px 0; }
  .wk { text-align: center; font-size: 10px; color: var(--text-3); font-variant-numeric: tabular-nums; }
  .day {
    position: relative; height: 30px; display: grid; place-items: center; align-content: center; font-size: 12.5px;
    font-variant-numeric: tabular-nums; border-radius: 50%; margin: 1px auto; width: 30px;
  }
  .day.out { color: var(--text-3); }
  .day.today { background: var(--accent); color: #fff; font-weight: 600; }
  .dot { position: absolute; bottom: 1px; left: 50%; width: 4px; height: 4px; margin-left: -2px; border-radius: 50%; background: var(--accent); }
  .day.today .dot { background: #fff; }
  .months { display: grid; grid-template-columns: repeat(3, 1fr); gap: 4px; }
  .mo { padding: 8px 4px; border-radius: 8px; font-size: 12.5px; text-transform: capitalize; }
  .mo:hover { background: var(--hover); }
  .mo.on { background: var(--accent); color: #fff; }
  .mo.now:not(.on) { color: var(--accent); font-weight: 600; }
  .foot { display: flex; align-items: center; justify-content: space-between; gap: 8px; padding: 2px 2px 0; }
  .today-btn { font-size: 12px; padding: 4px 10px; border-radius: 999px; background: var(--sp-track); }
  .today-btn:hover { background: var(--press); }
  .wk-toggle { display: flex; align-items: center; gap: 8px; cursor: default; }
  .agenda { padding: 8px 10px; gap: 6px; }
  .ag-row { display: flex; align-items: center; gap: 9px; min-width: 0; }
  .ag-text { min-width: 0; }
  .check { color: var(--text-2); display: grid; flex: none; }
  .check.done { color: var(--sp-ok); }
  .struck { text-decoration: line-through; color: var(--text-2); }
</style>
