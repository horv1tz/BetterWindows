import type { SearchResult } from '$lib/protocol/generated';

/**
 * The calculator result of the host is `title = "= 84"` or `"84"`, `subtitle = "12*(3+4)"` (expression) — the exact wording is the
 * engine's, so be liberal: take the expression from the subtitle (or the inline preview text "expr = value") and the answer from the title.
 */
export interface CalcView { expression: string; answer: string }

export function calcView(r: SearchResult): CalcView {
  const title = r.title.trim();
  const sub = (r.subtitle ?? '').trim();
  const previewText = r.preview?.kind === 'text' ? (r.preview.text ?? '').trim() : '';
  let answer = title.replace(/^=\s*/, '');
  let expression = sub;
  if (!expression && previewText.includes('=')) {
    const at = previewText.lastIndexOf('=');
    expression = previewText.slice(0, at).trim();
    if (!answer) answer = previewText.slice(at + 1).trim();
  }
  if (!answer && previewText) answer = previewText.replace(/^.*=\s*/, '');
  return { expression, answer };
}
