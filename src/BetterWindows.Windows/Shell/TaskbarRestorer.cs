namespace BetterWindows.Native;

/// <summary>
/// STUB — owner: win-shell. Stateless, dependency-free, idempotent: make the native taskbar(s) visible again and reset the work area.
/// Called from crash paths, from the next start after an unclean exit, and by `BetterWindows.exe --restore-taskbar`.
/// Must work without any other service being constructed and must never throw.
/// </summary>
public static class TaskbarRestorer
{
    public static void RestoreNow() { }
}
