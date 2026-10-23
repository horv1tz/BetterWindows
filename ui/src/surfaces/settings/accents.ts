/** Accent colour presets (original palette) and helpers. 'system' = follow the Windows accent colour (resolved by the host). */
export interface AccentPreset { id: string; hex: string }

export const ACCENTS: readonly AccentPreset[] = [
  { id: 'blue', hex: '#0a84ff' },
  { id: 'purple', hex: '#a550f0' },
  { id: 'pink', hex: '#ff4f87' },
  { id: 'red', hex: '#ff453a' },
  { id: 'orange', hex: '#ff9500' },
  { id: 'yellow', hex: '#ffcc00' },
  { id: 'green', hex: '#30c860' },
  { id: 'graphite', hex: '#8a8f98' },
];

/** Which swatch a stored accent value belongs to: 'system', a preset id, or 'custom'. */
export function accentChoice(value: string): string {
  if (value === 'system') return 'system';
  const v = value.toLowerCase().slice(0, 7);
  return ACCENTS.find((a) => a.hex === v)?.id ?? 'custom';
}

/** Text colour that stays readable on top of `hex` (WCAG relative luminance). */
export function contrastOn(hex: string): '#000000' | '#ffffff' {
  const m = /^#?([0-9a-f]{6})/i.exec(hex);
  if (!m) return '#ffffff';
  const n = parseInt(m[1], 16);
  const lin = (c: number) => { const s = c / 255; return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4; };
  const L = 0.2126 * lin((n >> 16) & 255) + 0.7152 * lin((n >> 8) & 255) + 0.0722 * lin(n & 255);
  // white text needs contrast >= 3 against the accent (large/bold UI text); otherwise use black
  return (1.05) / (L + 0.05) >= 3 ? '#ffffff' : '#000000';
}
