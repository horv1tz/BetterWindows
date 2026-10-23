using BetterWindows.Core.Desktop;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using Microsoft.Extensions.Logging;

namespace BetterWindows.Core.MissionControl;

/// <summary>
/// Builds the <see cref="McSnapshot"/> for ONE monitor from the tracker's consistent window snapshot. Pure apart from the two virtual-desktop queries it is given.
/// Rules: windows on other virtual desktops are included with their desktop id (they are real, running windows); windows hidden by their app (suspended UWP) are not; with
/// virtual desktops unsupported only the current desktop exists, so shell-cloaked windows are left out; a window belongs to the monitor that contains its CENTRE and its
/// bounds are DIPs relative to THAT monitor (converted with its scale); a minimised window has no meaningful position, so it is shown on whichever monitor is asked for,
/// as a centred placeholder rectangle.
/// </summary>
public static class McSnapshotBuilder
{
    /// <summary>Requested thumbnail width (the resource provider rounds it to one of its buckets).</summary>
    public const int ThumbWidth = 480;
    public const int IconSize = 128;
    public const int MaxTitleChars = 512;

    /// <summary>Win32 parks minimised windows at about (-32000,-32000); anything that far off-screen has no real position.</summary>
    private const double OffscreenLimit = -30000;

    public static McSnapshot Build(WindowSnapshot windows, IReadOnlyList<MonitorInfo> monitors, MonitorInfo target, IVirtualDesktopService desktops, int rev, ILogger? log = null)
    {
        var supported = SafeSupported(desktops);
        var list = new List<McWindow>();
        foreach (var tw in windows.Windows)
        {
            var raw = tw.Raw;
            if (raw.IsOwnProcess) continue;
            if (raw.IsCloaked && !raw.IsCloakedByShell) continue; // hidden by its own app (suspended UWP)
            if (raw.IsCloakedByShell && !supported) continue;     // another desktop exists only if desktops are supported

            if (!TryPlace(raw, monitors, target, out var bounds)) continue;
            list.Add(new McWindow
            {
                Handle = Hwnd.ToWire(tw.Handle),
                Title = InputPolicy.Truncate(raw.Title, MaxTitleChars),
                AppId = tw.AppId,
                AppName = tw.AppName,
                IconUrl = IconUrlOf(tw),
                ThumbUrl = DynUrls.WindowThumb(tw.Handle, ThumbWidth, rev),
                DesktopId = SafeDesktopId(desktops, tw.Handle, log),
                Bounds = bounds,
                Minimized = raw.IsMinimized,
                Active = windows.Foreground is { } fg && fg.Handle == tw.Handle,
                MonitorId = target.Id,
            });
        }
        return new McSnapshot { Desktops = SafeDesktops(desktops, log), DesktopsSupported = supported, Windows = list, Rev = rev };
    }

    /// <summary>The icon of a window: its app's icon when the app id is a real one (aumid:/exe:), else the window's own icon.</summary>
    public static string IconUrlOf(TrackedWindow tw) =>
        tw.AppId.StartsWith("aumid:", StringComparison.Ordinal) || tw.AppId.StartsWith("exe:", StringComparison.Ordinal)
            ? DynUrls.AppIcon(tw.AppId, IconSize)
            : DynUrls.WindowIcon(tw.Handle, IconSize);

    /// <summary>True (with DIP bounds relative to <paramref name="target"/>) when the window is shown on <paramref name="target"/>.</summary>
    public static bool TryPlace(RawWindow raw, IReadOnlyList<MonitorInfo> monitors, MonitorInfo target, out Rect bounds)
    {
        var b = raw.Bounds;
        var scale = MonitorGeometry.ScaleOf(target);
        if (IsOffscreen(b))
        {
            // No known position (minimised): a centred placeholder at 60% of the monitor.
            var area = MonitorGeometry.BoundsDip(target);
            var w = Math.Round(area.Width * 0.6);
            var h = Math.Round(area.Height * 0.6);
            bounds = new Rect { X = Math.Round((area.Width - w) / 2), Y = Math.Round((area.Height - h) / 2), Width = w, Height = h };
            return true;
        }

        var cx = b.Left + b.Width / 2.0;
        var cy = b.Top + b.Height / 2.0;
        var owner = MonitorGeometry.MonitorOfPoint(monitors, cx, cy);
        if (owner is null || owner.Id != target.Id) { bounds = new Rect(); return false; }
        bounds = new Rect
        {
            X = Math.Round((b.Left - owner.BoundsPx.X) / scale, 2),
            Y = Math.Round((b.Top - owner.BoundsPx.Y) / scale, 2),
            Width = Math.Round(b.Width / scale, 2),
            Height = Math.Round(b.Height / scale, 2),
        };
        return true;
    }

    private static bool IsOffscreen(NativeRect b) => b.Left <= OffscreenLimit || b.Top <= OffscreenLimit || b.Width <= 0 || b.Height <= 0;

    private static bool SafeSupported(IVirtualDesktopService d)
    {
        try { return d.IsSupported; } catch (Exception) { return false; }
    }

    private static string? SafeDesktopId(IVirtualDesktopService d, long handle, ILogger? log)
    {
        try { return d.GetWindowDesktopId(handle); }
        catch (Exception ex) { log?.LogDebug(ex, "GetWindowDesktopId({Handle}) failed", handle); return null; }
    }

    private static IReadOnlyList<DesktopInfo> SafeDesktops(IVirtualDesktopService d, ILogger? log)
    {
        try { return d.GetDesktops().ToArray(); }
        catch (Exception ex) { log?.LogDebug(ex, "GetDesktops failed"); return Array.Empty<DesktopInfo>(); }
    }
}
