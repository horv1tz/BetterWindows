/**
 * Best-effort display name for an app id when the active-app record does not (or no longer) match it:
 *   "exe:c:\program files\foo\foo.exe" -> "foo",  "aumid:Microsoft.WindowsNotepad_8wekyb3d8bbwe!App" -> "WindowsNotepad".
 */
export function nameFromAppId(appId: string): string {
  const rest = appId.replace(/^(exe|aumid|dir):/i, '');
  if (/^exe:/i.test(appId)) {
    const file = rest.split(/[\\/]/).pop() ?? rest;
    return file.replace(/\.exe$/i, '');
  }
  const [pkg] = rest.split('!');
  const family = pkg.split('_')[0];
  const last = family.split('.').pop() ?? family;
  return last || rest;
}
