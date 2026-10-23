using BetterWindows.Core.Platform;
using BetterWindows.Host.Surfaces;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;

namespace BetterWindows.Host.Interop;

/// <summary>
/// Turns a <see cref="RegionPlan"/> into a GDI region and applies it as the WINDOW REGION (SetWindowRgn). WHY a window region and not per-pixel alpha: our dock/menubar
/// windows are NOT layered (a layered window cannot coexist with a DWM system backdrop), so the only way to make the pixels outside the glass panels invisible AND
/// click-through is to clip the window itself. Resource rules (GDI handles are a process-wide quota of 10 000): every temporary region is deleted immediately; the region that
/// is passed to SetWindowRgn is OWNED BY THE SYSTEM afterwards and must not be deleted (it is only deleted by us when SetWindowRgn fails).
/// </summary>
internal static class RegionBuilder
{
    /// <summary>
    /// Builds the combined region. <paramref name="clipLocal"/> (window-local px, optional) is intersected afterwards (the auto-hiding menu bar sliding out of its monitor).
    /// Returns a null handle on failure. The caller owns the result until it is handed to SetWindowRgn.
    /// </summary>
    public static unsafe HRGN Build(RegionPlan plan, NativeRect? clipLocal = null)
    {
        var combined = HostPInvoke.CreateRectRgn(0, 0, 0, 0); // an EMPTY region: the union starts from nothing
        if (combined.Value == null) return default;
        foreach (var r in plan.Rects)
        {
            // CreateRoundRectRgn's right/bottom are inclusive-ish: it needs +1 to cover exactly [left, right) x [top, bottom). CreateRectRgn takes exclusive edges as they are.
            var tmp = r.Radius > 0
                ? HostPInvoke.CreateRoundRectRgn(r.Left, r.Top, r.Right + 1, r.Bottom + 1, r.Radius * 2, r.Radius * 2)
                : HostPInvoke.CreateRectRgn(r.Left, r.Top, r.Right, r.Bottom);
            if (tmp.Value == null) continue; // GDI quota exhausted: skip this rectangle, keep what we have
            var rc = HostPInvoke.CombineRgn(combined, combined, tmp, RGN_COMBINE_MODE.RGN_OR);
            HostPInvoke.DeleteObject(tmp);
            if (rc == GDI_REGION_TYPE.RGN_ERROR) { HostPInvoke.DeleteObject(combined); return default; }
        }
        if (clipLocal is { } clip)
        {
            var clipRgn = HostPInvoke.CreateRectRgn(clip.Left, clip.Top, clip.Right, clip.Bottom);
            if (clipRgn.Value == null) { HostPInvoke.DeleteObject(combined); return default; }
            var rc = HostPInvoke.CombineRgn(combined, combined, clipRgn, RGN_COMBINE_MODE.RGN_AND);
            HostPInvoke.DeleteObject(clipRgn);
            if (rc == GDI_REGION_TYPE.RGN_ERROR) { HostPInvoke.DeleteObject(combined); return default; }
        }
        return combined;
    }

    /// <summary>Builds and applies a plan as the window region. False on failure (the window keeps its previous region; no handle leaks).</summary>
    public static unsafe bool Apply(nint hwnd, RegionPlan plan, NativeRect? clipLocal = null)
    {
        if (hwnd == 0) return false;
        var rgn = Build(plan, clipLocal);
        if (rgn.Value == null) return false;
        // SetWindowRgn returns non-zero on success; on success the system owns rgn.
        if (HostPInvoke.SetWindowRgn(hwnd.ToHwnd(), rgn, true) != 0) return true;
        HostPInvoke.DeleteObject(rgn);
        return false;
    }

    /// <summary>Removes the window region (the whole window is visible/hit-testable again). A null region needs no cleanup.</summary>
    public static unsafe bool Clear(nint hwnd)
    {
        if (hwnd == 0) return false;
        return HostPInvoke.SetWindowRgn(hwnd.ToHwnd(), default(HRGN), true) != 0;
    }
}
