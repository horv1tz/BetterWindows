<script lang="ts">
  import { hotkeyParts, keycapGlyph } from '../hotkey';
  import { t, has } from '../i18n';
  let { hotkey, parts, dim = false, size = 'md' }: { hotkey?: string; parts?: readonly string[]; dim?: boolean; size?: 'md' | 'sm' } = $props();
  const tokens = $derived(parts ?? hotkeyParts(hotkey ?? ''));
  const name = (tok: string) => (has(`key.${tok}`) ? t(`key.${tok}`) : keycapGlyph(tok));
</script>

<span class="caps" class:dim class:sm={size === 'sm'}>
  {#each tokens as tok, i (i)}<kbd>{name(tok)}</kbd>{/each}
</span>

<style>
  .caps { display: inline-flex; align-items: center; gap: 4px; }
  kbd {
    display: inline-grid; place-items: center; min-width: 24px; height: 22px; padding: 0 7px; border-radius: 5px; font-size: 12px; font-weight: 500; color: var(--text);
    background: var(--s-key); box-shadow: 0 1px 0 var(--s-key-edge), 0 0 0 0.5px var(--s-card-border); white-space: nowrap;
  }
  .sm kbd { min-width: 20px; height: 19px; padding: 0 5px; font-size: 11px; border-radius: 4px; }
  .dim kbd { color: var(--text-2); }
</style>
