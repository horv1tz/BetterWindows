<script lang="ts">
  import type { SearchPreview, SearchResult } from '$lib/protocol/generated';
  import { calcView } from './calcFormat';
  import ResultIcon from './ResultIcon.svelte';
  import { t } from './strings';

  let { result, preview, loading }: { result: SearchResult | null; preview: SearchPreview | null; loading: boolean } = $props();

  const calc = $derived(result?.provider === 'calc' ? calcView(result) : null);
  const title = $derived(calc ? calc.answer : (result?.title ?? ''));
  const subtitle = $derived(calc ? calc.expression : (result?.subtitle ?? ''));
  const hasImage = $derived(preview?.kind === 'image' && !!preview.imageUrl);
  const hasText = $derived(preview?.kind === 'text' && !!preview.text && !calc);
  let imageFailed = $state(false);
  $effect(() => { preview?.imageUrl; imageFailed = false; });
</script>

<aside class="pane" aria-label={t('sp.preview')}>
  {#if result}
    {#key result.id}
      <div class="content">
        {#if hasImage && !imageFailed}
          <img class="photo" src={preview?.imageUrl} alt="" draggable="false" onerror={() => (imageFailed = true)} />
        {:else}
          <div class="hero"><ResultIcon result={result} size={84} /></div>
        {/if}
        <div class="name" class:big={!!calc}>{title}</div>
        {#if subtitle}<div class="where" title={subtitle}>{subtitle}</div>{/if}
        {#if hasText}
          <pre class="snippet">{preview?.text}</pre>
        {:else if loading && !calc && result.provider === 'files'}
          <div class="skeleton" aria-hidden="true"><i></i><i></i><i></i></div>
        {/if}
        <div class="kind">{t(`sp.kind.${result.provider}`)}</div>
      </div>
    {/key}
  {/if}
</aside>

<style>
  .pane {
    width: 248px; flex: none; padding: 20px 18px 16px; overflow: hidden;
    border-left: 0.5px solid var(--separator); display: flex; flex-direction: column;
    align-items: stretch;
  }
  .content { display: flex; flex-direction: column; align-items: center; gap: 8px; min-height: 0; animation: fade 160ms var(--ease-out) both; }
  @keyframes fade { from { opacity: 0; transform: translateY(3px); } to { opacity: 1; transform: none; } }
  .hero { display: grid; place-items: center; height: 96px; margin-top: 6px; filter: drop-shadow(0 6px 10px rgba(0, 0, 0, 0.22)); }
  .photo { width: 100%; height: 150px; object-fit: cover; border-radius: 10px; box-shadow: 0 1px 4px rgba(0, 0, 0, 0.3); background: var(--hover); }
  .name { font-size: 16px; font-weight: 600; text-align: center; line-height: 1.25; max-width: 100%; display: -webkit-box; -webkit-line-clamp: 2; line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; word-break: break-word; letter-spacing: -0.01em; }
  .name.big { font-size: 30px; font-weight: 500; font-variant-numeric: tabular-nums; -webkit-line-clamp: 1; line-clamp: 1; }
  .where { font-size: 12px; color: var(--text-2); text-align: center; max-width: 100%; display: -webkit-box; -webkit-line-clamp: 3; line-clamp: 3; -webkit-box-orient: vertical; overflow: hidden; overflow-wrap: anywhere; }
  .kind { margin-top: 2px; font-size: 11px; font-weight: 600; letter-spacing: 0.05em; text-transform: uppercase; color: var(--text-3); }
  .snippet {
    align-self: stretch; margin: 6px 0 0; padding: 9px 10px; max-height: 168px; overflow: hidden; border-radius: 9px;
    background: color-mix(in oklab, var(--text) 6%, transparent);
    font: 11px/1.5 'Cascadia Mono', 'Cascadia Code', Consolas, ui-monospace, monospace; color: var(--text-2); white-space: pre-wrap; word-break: break-word;
    -webkit-mask-image: linear-gradient(to bottom, #000 70%, transparent); mask-image: linear-gradient(to bottom, #000 70%, transparent);
  }
  .skeleton { align-self: stretch; display: flex; flex-direction: column; gap: 6px; margin-top: 8px; }
  .skeleton i { display: block; height: 9px; border-radius: 4px; background: var(--hover); animation: pulse 1.1s ease-in-out infinite; }
  .skeleton i:nth-child(2) { width: 82%; animation-delay: 120ms; }
  .skeleton i:nth-child(3) { width: 58%; animation-delay: 240ms; }
  @keyframes pulse { 0%, 100% { opacity: 0.45; } 50% { opacity: 1; } }
</style>
