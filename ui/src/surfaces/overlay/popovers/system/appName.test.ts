import { describe, expect, it } from 'vitest';
import { nameFromAppId } from './appName';

describe('nameFromAppId', () => {
  it('uses the exe file name without extension', () => {
    expect(nameFromAppId('exe:c:\\program files\\foo\\Foo Bar.exe')).toBe('Foo Bar');
    expect(nameFromAppId('exe:c:/tools/vim.EXE')).toBe('vim');
  });
  it('uses the last segment of the package family name for AUMIDs', () => {
    expect(nameFromAppId('aumid:Microsoft.WindowsNotepad_8wekyb3d8bbwe!App')).toBe('WindowsNotepad');
    expect(nameFromAppId('aumid:Discord')).toBe('Discord');
  });
  it('returns something non-empty for odd ids', () => {
    expect(nameFromAppId('whatever')).toBe('whatever');
  });
});
