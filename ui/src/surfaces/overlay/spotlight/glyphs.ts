import type { SearchProviderId } from '$lib/protocol/generated';

export type FileGlyph = 'image' | 'sheet' | 'slides' | 'code' | 'archive' | 'audio' | 'video' | 'folder' | 'text' | 'file';

const EXT: Record<string, FileGlyph> = {
  png: 'image', jpg: 'image', jpeg: 'image', gif: 'image', webp: 'image', bmp: 'image', svg: 'image', heic: 'image', psd: 'image', ico: 'image',
  xls: 'sheet', xlsx: 'sheet', csv: 'sheet', ods: 'sheet',
  ppt: 'slides', pptx: 'slides', odp: 'slides', key: 'slides',
  js: 'code', ts: 'code', json: 'code', cs: 'code', py: 'code', html: 'code', css: 'code', xml: 'code', yml: 'code', yaml: 'code', ps1: 'code', sh: 'code', xmp: 'code',
  zip: 'archive', rar: 'archive', '7z': 'archive', tar: 'archive', gz: 'archive',
  mp3: 'audio', wav: 'audio', flac: 'audio', ogg: 'audio', m4a: 'audio',
  mp4: 'video', mkv: 'video', mov: 'video', avi: 'video', webm: 'video',
  txt: 'text', md: 'text', doc: 'text', docx: 'text', rtf: 'text', pdf: 'text', odt: 'text', log: 'text',
};

/** Glyph for a file result chosen from the extension of its title ("Downloads" without an extension is a folder). */
export function fileGlyph(title: string): FileGlyph {
  const dot = title.lastIndexOf('.');
  if (dot < 0 || dot === title.length - 1) return 'folder';
  return EXT[title.slice(dot + 1).toLowerCase()] ?? 'file';
}

/** Tint (CSS gradient stops) used by the fallback tile of each provider. */
export const PROVIDER_TINT: Record<SearchProviderId, [string, string]> = {
  apps: ['#4f8cff', '#2f5fd0'],
  files: ['#7a8aa0', '#56657b'],
  calc: ['#ffb340', '#f2780c'],
  settings: ['#9aa0aa', '#6b7280'],
  commands: ['#4a4a52', '#232328'],
  web: ['#3ea0ff', '#1469d6'],
  windows: ['#6d7cff', '#4a4fd6'],
};
