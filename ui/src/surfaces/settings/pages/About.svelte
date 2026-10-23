<script lang="ts">
  // About pane: version info, the read-only diagnostics report (diag.report) and quitting (system.quit has no parameters).
  import { onMount } from 'svelte';
  import { getBridge } from '$lib/ipc';
  import type { AboutInfo, DiagReport } from '$lib/protocol/generated';
  import { run } from '../actions';
  import { formatReportText, summarizeDiag } from '../format';
  import { t } from '../i18n';
  import { pushToast } from '../toast.svelte';
  import AppMark from '../art/AppMark.svelte';
  import Badge from '../components/Badge.svelte';
  import Banner from '../components/Banner.svelte';
  import Button from '../components/Button.svelte';
  import Card from '../components/Card.svelte';
  import ConfirmDialog from '../components/ConfirmDialog.svelte';
  import PageHeader from '../components/PageHeader.svelte';
  import Row from '../components/Row.svelte';

  const bridge = getBridge();
  let about: AboutInfo | null = $state(null);
  let report: DiagReport | null = $state(null);
  let running = $state(false);
  let failed = $state(false);
  let confirmQuit = $state(false);
  const summary = $derived(report ? summarizeDiag(report) : null);

  onMount(() => {
    run(() => bridge.call('system.about'), { quiet: true }).then((r) => { if (r.ok) about = r.value; });
  });

  async function diagnose() {
    running = true;
    failed = false;
    const r = await run(() => bridge.call('diag.report'), { quiet: true });
    running = false;
    if (r.ok) report = r.value;
    else { failed = true; pushToast('error', `${t('about.diag.failed')} ${r.message}`); }
  }

  async function copy() {
    if (!report) return;
    try {
      await navigator.clipboard.writeText(formatReportText(about, report));
      pushToast('success', t('about.diag.copied'));
    } catch {
      pushToast('error', t('about.diag.copyFailed'));
    }
  }

  async function quit() {
    confirmQuit = false;
    await run(() => bridge.call('system.quit'), { what: t('about.quit.label') });
  }
</script>

<PageHeader title={t('page.about')} subtitle={t('about.subtitle')} />

<Card title={t('about.section.info')}>
  <div class="hero">
    <AppMark size={56} />
    <div>
      <div class="app">BetterWindows</div>
      <div class="ver">{about ? `${t('about.version.label')} ${about.version}` : ''}</div>
    </div>
  </div>
  <Row label={t('about.version.label')}><span class="val">{about?.version ?? '—'}</span></Row>
  <Row label={t('about.build.label')}><span class="val">{about?.build ?? '—'}</span></Row>
  <Row label={t('about.os.label')}><span class="val">{about?.os ?? '—'}</span></Row>
  <Row label={t('about.webview.label')}><span class="val">{about?.webView2 ?? '—'}</span></Row>
</Card>

<Card title={t('about.section.diag')}>
  {#snippet actions()}
    {#if report}<Button size="sm" onclick={copy}>{t('about.diag.copy')}</Button>{/if}
  {/snippet}
  <Row label={t('about.diag.label')} desc={t('about.diag.desc')}>
    <Button disabled={running} busy={running} onclick={diagnose}>{running ? t('about.diag.running') : t('about.diag.run')}</Button>
  </Row>
  {#if failed}
    <div class="pad"><Banner kind="error" role="alert">{t('about.diag.failed')}</Banner></div>
  {/if}
  {#if report && summary}
    <div class="pad">
      <Banner kind={summary.failed ? 'warn' : 'ok'} role="status">{t('about.diag.summary', { passed: summary.passed, total: summary.total })}</Banner>
    </div>
    <ul class="checks" aria-label={t('about.section.diag')}>
      {#each report.checks as c, i (i)}
        <li>
          <span class="cname">{c.name}</span>
          {#if c.detail}<span class="cdetail">{c.detail}</span>{/if}
          <Badge kind={c.ok ? 'ok' : 'fail'}>{c.ok ? t('about.diag.ok') : t('about.diag.fail')}</Badge>
        </li>
      {/each}
    </ul>
  {/if}
</Card>

<Card title={t('about.section.info')}>
  <Row label={t('about.quit.label')} desc={t('about.quit.desc')}>
    <Button variant="danger" onclick={() => (confirmQuit = true)}>{t('about.quit.action')}</Button>
  </Row>
</Card>

<ConfirmDialog
  open={confirmQuit} danger title={t('about.quit.confirmTitle')} message={t('about.quit.confirmBody')} confirmLabel={t('about.quit.confirm')}
  onconfirm={quit} oncancel={() => (confirmQuit = false)}
/>

<style>
  .hero { display: flex; align-items: center; gap: 14px; padding: 14px 14px 10px; }
  .app { font-size: 17px; font-weight: 650; letter-spacing: -0.01em; }
  .ver { color: var(--text-2); font-size: 12px; }
  .val { color: var(--text-2); font-variant-numeric: tabular-nums; overflow-wrap: anywhere; text-align: right; }
  .pad { padding: 0 14px 10px; }
  .checks { list-style: none; margin: 0; padding: 0 14px 12px; display: grid; }
  .checks li { display: grid; grid-template-columns: minmax(0, 1fr) auto; align-items: center; gap: 2px 10px; padding: 8px 0; border-top: 0.5px solid var(--separator); }
  .cname { font-weight: 500; }
  .cdetail { grid-column: 1; color: var(--text-2); font-size: 11.5px; overflow-wrap: anywhere; }
  .checks li :global(.badge) { grid-column: 2; grid-row: 1 / span 2; }
</style>
