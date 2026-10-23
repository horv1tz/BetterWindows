namespace BetterWindows.Native.Interop;

/// <summary>One top-level window considered when looking for the wallpaper host, in Z order (topmost first).</summary>
/// <param name="Handle">HWND value.</param>
/// <param name="ClassName">Window class name.</param>
/// <param name="HostsDefView">True when the window has a SHELLDLL_DefView child (the desktop icon view).</param>
internal readonly record struct DesktopCandidate(nint Handle, string ClassName, bool HostsDefView);

/// <summary>
/// Pure rules for choosing the WorkerW that the desktop layer is parented to. Cross-platform and unit-tested, so the Windows-specific
/// window walk in <see cref="DesktopShellNative"/> stays a thin feed of candidates.
/// </summary>
internal static class DesktopLayerSelection
{
    /// <summary>Class name of the wallpaper host windows.</summary>
    public const string WorkerWClass = "WorkerW";

    /// <summary>
    /// Returns the WorkerW to parent the layer to, or 0. The classic wallpaper layout puts the icon view (DefView) in one WorkerW and the
    /// wallpaper-only WorkerW directly BELOW it in Z order, so that one is preferred. Otherwise the first wallpaper-only WorkerW anywhere
    /// (layouts where the icon view lives under Progman or under another WorkerW, e.g. Windows 11 24H2). 0 when none exists.
    /// </summary>
    public static nint ChooseWorkerW(IReadOnlyList<DesktopCandidate> zOrderTopFirst)
    {
        ArgumentNullException.ThrowIfNull(zOrderTopFirst);

        int host = -1;
        for (int i = 0; i < zOrderTopFirst.Count; i++)
        {
            if (zOrderTopFirst[i].HostsDefView)
            {
                host = i;
                break;
            }
        }

        for (int i = host + 1; host >= 0 && i < zOrderTopFirst.Count; i++)
        {
            if (IsWallpaperWorkerW(zOrderTopFirst[i]))
            {
                return zOrderTopFirst[i].Handle;
            }
        }

        for (int i = 0; i < zOrderTopFirst.Count; i++)
        {
            if (IsWallpaperWorkerW(zOrderTopFirst[i]))
            {
                return zOrderTopFirst[i].Handle;
            }
        }

        return 0;
    }

    private static bool IsWallpaperWorkerW(DesktopCandidate c) =>
        !c.HostsDefView && c.Handle != 0 && string.Equals(c.ClassName, WorkerWClass, StringComparison.Ordinal);
}
