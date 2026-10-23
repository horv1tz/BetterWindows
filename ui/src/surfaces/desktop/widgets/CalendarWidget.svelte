<script lang="ts">
  import { defaultWeekStart, monthGrid, msUntilMidnight, weekdayHeaders } from '../calendarLogic';
  import { locale } from '../shared.svelte';
  import { t } from '../strings';
  import type { WidgetProps } from './registry';

  let { config, size }: WidgetProps = $props();

  const weekNumbers = $derived(config.weekNumbers === true);

  let now = $state(new Date());
  $effect(() => {
    let timer = 0;
    const tick = () => { now = new Date(); timer = window.setTimeout(tick, msUntilMidnight(now)); };
    timer = window.setTimeout(tick, msUntilMidnight(now));
    return () => clearTimeout(timer);
  });

  const today = $derived({ year: now.getFullYear(), month: now.getMonth(), day: now.getDate() });
  const weekStart = $derived(defaultWeekStart(locale()));
  const grid = $derived(monthGrid(today.year, today.month, weekStart, today));
  const headers = $derived(weekdayHeaders(locale(), weekStart, 'narrow'));
  const month = $derived(new Intl.DateTimeFormat(locale(), { month: 'long' }).format(now));
  const monthYear = $derived(new Intl.DateTimeFormat(locale(), { month: 'long', year: 'numeric' }).format(now));
  const weekday = $derived(new Intl.DateTimeFormat(locale(), { weekday: 'long' }).format(now));
  const showNumbers = $derived(weekNumbers && size !== 'small');
</script>

<div class="cal {size}" role="group" aria-label="{t('w.name.calendar')}: {monthYear}">
  {#if size === 'small'}
    <div class="hero">
      <div class="weekday">{weekday}</div>
      <div class="day">{today.day}</div>
      <div class="monthname">{month}</div>
    </div>
  {:else}
    {#if size === 'medium'}
      <div class="hero side">
        <div class="weekday">{weekday}</div>
        <div class="day">{today.day}</div>
        <div class="monthname">{month}</div>
      </div>
    {/if}
    <div class="monthgrid" role="table" aria-label={monthYear}>
      {#if size === 'large'}<div class="title">{monthYear}</div>{/if}
      <div class="row head" role="row" class:with-wk={showNumbers}>
        {#if showNumbers}<span class="wk" role="columnheader">{t('cal.week')}</span>{/if}
        {#each headers as h, i (i)}<span role="columnheader" class="cell">{h}</span>{/each}
      </div>
      {#each grid.weeks as week, wi (wi)}
        <div class="row" role="row" class:with-wk={showNumbers}>
          {#if showNumbers}<span class="wk" role="rowheader">{grid.weekNumbers[wi]}</span>{/if}
          {#each week as d (d.key)}
            <span role="cell" class="cell" class:out={!d.inMonth} class:weekend={d.weekend && d.inMonth} class:today={d.today} aria-current={d.today ? 'date' : undefined} data-key={d.key}>
              <b>{d.day}</b>
            </span>
          {/each}
        </div>
      {/each}
    </div>
  {/if}
</div>

<style>
  .cal { position: absolute; inset: 0; padding: 14px; display: flex; gap: 14px; }
  .hero { display: flex; flex-direction: column; justify-content: center; }
  .cal.small .hero { flex: 1; align-items: flex-start; justify-content: space-between; padding: 2px 4px; }
  .weekday { font-size: 12px; font-weight: 700; letter-spacing: 0.04em; text-transform: uppercase; color: #ff453a; }
  .day { font-size: 76px; font-weight: 300; line-height: 1; letter-spacing: -0.03em; font-variant-numeric: tabular-nums; }
  .cal.small .day { font-size: 84px; align-self: center; margin: -6px 0; }
  .monthname { font-size: 15px; font-weight: 600; color: var(--text-2); text-transform: capitalize; }
  .cal.small .monthname { align-self: flex-start; }
  .hero.side { width: 128px; flex: none; justify-content: center; padding-left: 6px; border-right: 0.5px solid var(--separator); padding-right: 6px; }
  .hero.side .day { font-size: 64px; margin: 4px 0; }
  .monthgrid { flex: 1; display: flex; flex-direction: column; justify-content: space-between; min-width: 0; font-variant-numeric: tabular-nums; }
  .title { font-size: 20px; font-weight: 700; text-transform: capitalize; padding: 2px 4px 8px; }
  .row { display: grid; grid-template-columns: repeat(7, 1fr); flex: 1; align-items: center; text-align: center; }
  .row.with-wk { grid-template-columns: 26px repeat(7, 1fr); }
  .head { flex: 0 0 auto; font-size: 10.5px; font-weight: 600; color: var(--text-3); text-transform: uppercase; padding-bottom: 2px; }
  .cell { display: grid; place-items: center; }
  .cell b { display: grid; place-items: center; width: 24px; height: 24px; border-radius: 50%; font-weight: 500; font-size: 12.5px; }
  .cal.large .cell b { width: 36px; height: 36px; font-size: 16px; }
  .cal.large .head { font-size: 12px; }
  .cell.out b { color: var(--text-3); font-weight: 400; }
  .cell.weekend b { color: var(--text-2); }
  .cell.today b { background: #ff453a; color: #fff; font-weight: 700; box-shadow: 0 1px 4px rgba(255, 69, 58, 0.45); }
  .wk { color: var(--text-3); font-size: 10.5px; font-weight: 500; }
  .cal.large .wk { font-size: 12px; }
</style>
