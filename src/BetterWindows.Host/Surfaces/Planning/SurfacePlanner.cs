using BetterWindows.Core.Ipc;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Host.Surfaces;

/// <summary>One monitor-bound surface that should exist for the current settings and monitor layout.</summary>
/// <param name="Id">Identity assigned to the WebView (the ONLY source of the sender identity, docs/IPC.md section 14).</param>
/// <param name="Monitor">The monitor the window lives on.</param>
/// <param name="Route">Route passed to IWebViewBridge.AttachAsync: "dock", "dock/&lt;monitorId&gt;", "menubar/&lt;monitorId&gt;", "desktop/&lt;monitorId&gt;".</param>
internal sealed record PlannedSurface(SurfaceId Id, MonitorInfo Monitor, string Route)
{
    public SurfaceKind Kind => Id.Kind;
}

/// <summary>The difference between the surfaces that exist and those that should exist.</summary>
internal sealed record SurfaceDiff(IReadOnlyList<PlannedSurface> ToCreate, IReadOnlyList<SurfaceId> ToRemove, IReadOnlyList<PlannedSurface> ToKeep);

/// <summary>
/// (settings, monitors) -&gt; the desired set of monitor-bound surfaces. Pure, so every rule is unit-tested here on Linux.
///  - dock: only when settings.dock.enabled; monitor == primary -> the primary monitor; monitor == all -> every monitor. The dock of the PRIMARY monitor keeps the
///    monitor-less id (Dock, null) ("the single dock"), so switching primary &lt;-&gt; all never re-creates it; docks on other monitors are (Dock, monitorId).
///  - menubar: only when settings.menuBar.enabled; allMonitors -> every monitor else the primary one; id (Menubar, monitorId).
///  - desktop: only when settings.desktop.enabled (surface budget: the desktop WebView must not exist otherwise); one per monitor; id (Desktop, monitorId).
///  - overlay and settings are not monitor-bound plan items: the overlay exists from startup, settings on demand.
/// An empty monitor list yields an empty plan; callers keep the current windows instead of acting on it (a transient state during RDP/lock-screen switches).
/// </summary>
internal static class SurfacePlanner
{
    public static IReadOnlyList<PlannedSurface> Plan(Settings settings, IReadOnlyList<MonitorInfo> monitors)
    {
        var result = new List<PlannedSurface>();
        if (monitors.Count == 0) return result;
        var distinct = DistinctById(monitors);
        var primary = MonitorGeometry.Primary(distinct)!;

        if (settings.Dock.Enabled)
        {
            var docked = settings.Dock.Monitor == DockMonitor.All ? distinct : new[] { primary };
            foreach (var m in docked)
            {
                var isPrimary = ReferenceEquals(m, primary) || m.Id == primary.Id;
                result.Add(isPrimary
                    ? new PlannedSurface(new SurfaceId(SurfaceKind.Dock), m, "dock")
                    : new PlannedSurface(new SurfaceId(SurfaceKind.Dock, m.Id), m, "dock/" + m.Id));
            }
        }

        if (settings.MenuBar.Enabled)
        {
            var bars = settings.MenuBar.AllMonitors ? distinct : new[] { primary };
            foreach (var m in bars) result.Add(new PlannedSurface(new SurfaceId(SurfaceKind.Menubar, m.Id), m, "menubar/" + m.Id));
        }

        if (settings.Desktop.Enabled)
        {
            foreach (var m in distinct) result.Add(new PlannedSurface(new SurfaceId(SurfaceKind.Desktop, m.Id), m, "desktop/" + m.Id));
        }
        return result;
    }

    /// <summary>Surfaces to create (not present yet), to remove (no longer desired) and to keep (present and desired: re-place them, never re-create).</summary>
    public static SurfaceDiff Diff(IEnumerable<SurfaceId> existing, IReadOnlyList<PlannedSurface> desired)
    {
        var have = new HashSet<SurfaceId>(existing);
        var want = new HashSet<SurfaceId>(desired.Select(d => d.Id));
        var create = desired.Where(d => !have.Contains(d.Id)).ToList();
        var keep = desired.Where(d => have.Contains(d.Id)).ToList();
        var remove = have.Where(h => !want.Contains(h)).ToList();
        return new SurfaceDiff(create, remove, keep);
    }

    /// <summary>The monitor id a surface id stands for: its explicit monitor, or the primary monitor for the monitor-less dock.</summary>
    public static string? EffectiveMonitorId(SurfaceId id, IReadOnlyList<MonitorInfo> monitors) => id.MonitorId ?? MonitorGeometry.Primary(monitors)?.Id;

    private static IReadOnlyList<MonitorInfo> DistinctById(IReadOnlyList<MonitorInfo> monitors)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<MonitorInfo>(monitors.Count);
        foreach (var m in monitors) if (seen.Add(m.Id)) list.Add(m);
        return list;
    }
}
