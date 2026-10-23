using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;

namespace BetterWindows.Host.Surfaces;

/// <summary>
/// Reason-stacked surface visibility (ISurfaceManager.SetSurfacesVisible): a surface is visible only while NO reason hides it.
/// A hide is an entry (reason, kind?, monitorId?) where null means "all"; a show with the same reason removes every entry of that reason that lies WITHIN the scope of the show
/// (so show(null, null, r) clears everything of reason r, show(Dock, null, r) clears only dock entries, a narrower hide survives a broader show of another reason).
/// Automatic fullscreen suppression uses the same registry with the reason "fullscreen:&lt;monitorId&gt;". Not thread-safe by itself (UI thread only).
/// </summary>
internal sealed class VisibilityRegistry
{
    private readonly record struct Entry(string Reason, SurfaceKind? Kind, string? MonitorId);

    private readonly HashSet<Entry> _entries = new();

    public const int MaxReasonLength = 64;
    public const int MaxEntries = 256;

    public int Count => _entries.Count;

    /// <summary>Applies a hide (visible == false) or show. Returns true when the set of entries changed.</summary>
    public bool Set(SurfaceKind? kind, string? monitorId, bool visible, string? reason)
    {
        var r = NormalizeReason(reason);
        var mon = string.IsNullOrEmpty(monitorId) ? null : InputPolicy.Truncate(monitorId, 64);
        if (!visible) return _entries.Count < MaxEntries && _entries.Add(new Entry(r, kind, mon));
        return _entries.RemoveWhere(e => e.Reason == r && (kind is null || e.Kind == kind) && (mon is null || e.MonitorId == mon)) > 0;
    }

    /// <summary>True when no entry hides a surface of this kind on this (effective) monitor.</summary>
    public bool IsVisible(SurfaceKind kind, string? effectiveMonitorId)
    {
        foreach (var e in _entries)
        {
            if (e.Kind is not null && e.Kind != kind) continue;
            if (e.MonitorId is not null && !string.Equals(e.MonitorId, effectiveMonitorId, StringComparison.Ordinal)) continue;
            return false;
        }
        return true;
    }

    /// <summary>The reasons that currently hide this surface, sorted (diagnostics and the surface.visibility reason).</summary>
    public IReadOnlyList<string> Reasons(SurfaceKind kind, string? effectiveMonitorId)
    {
        var list = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var e in _entries)
        {
            if (e.Kind is not null && e.Kind != kind) continue;
            if (e.MonitorId is not null && !string.Equals(e.MonitorId, effectiveMonitorId, StringComparison.Ordinal)) continue;
            list.Add(e.Reason);
        }
        return list.ToArray();
    }

    /// <summary>Drops every entry whose reason starts with <paramref name="prefix"/> (monitors disappeared: their fullscreen entries must not linger).</summary>
    public bool RemoveReasonsWithPrefix(string prefix) => _entries.RemoveWhere(e => e.Reason.StartsWith(prefix, StringComparison.Ordinal)) > 0;

    private static string NormalizeReason(string? reason)
    {
        var r = InputPolicy.Truncate(reason, MaxReasonLength);
        return r.Length == 0 ? "unspecified" : r;
    }
}

/// <summary>Automatic fullscreen suppression (owned by the Host): fullscreen on monitor M hides the dock and the menu bar of M under the reason "fullscreen:M".</summary>
internal static class FullscreenSuppression
{
    public const string ReasonPrefix = "fullscreen:";

    public static string Reason(string monitorId) => ReasonPrefix + monitorId;

    /// <summary>
    /// Brings the registry in line with the set of monitors that currently have a fullscreen app in front. Monitors that left the set are un-hidden, new ones are hidden
    /// (dock + menubar only: the overlay, desktop and settings are never suppressed). Returns true when anything changed.
    /// </summary>
    public static bool Apply(VisibilityRegistry registry, ISet<string> previous, IReadOnlyCollection<string> current)
    {
        var changed = false;
        var now = new HashSet<string>(current, StringComparer.Ordinal);
        foreach (var m in previous.ToArray())
        {
            if (now.Contains(m)) continue;
            changed |= registry.Set(null, m, true, Reason(m));
            previous.Remove(m);
        }
        foreach (var m in now)
        {
            if (previous.Contains(m)) continue;
            changed |= registry.Set(SurfaceKind.Dock, m, false, Reason(m));
            changed |= registry.Set(SurfaceKind.Menubar, m, false, Reason(m));
            previous.Add(m);
        }
        return changed;
    }
}
