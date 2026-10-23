using System.Text.Json.Nodes;
using BetterWindows.Core.Desktop;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Widgets;

/// <summary>
/// Pure read-modify-write operations on <c>settings.desktop.widgets</c>, run inside <c>ISettingsService.UpdateAsync</c>. Positions are DIPs relative to the widget's
/// monitor (the desktop surface covers the whole monitor); a widget whose monitor was unplugged lives on the primary monitor.
/// </summary>
public static class WidgetOps
{
    public static int MaxWidgets => SettingsLimits.All.TryGetValue("DesktopSettings.widgets", out var l) && l.MaxItems is { } m ? m : 100;

    public static string NewId() => "w-" + Guid.NewGuid().ToString("N");

    /// <summary>The rectangle a widget occupies (its size class at its stored position).</summary>
    public static DipRect RectOf(WidgetInstance w)
    {
        var (width, height) = WidgetMetrics.SizeDip(w.Size);
        return new DipRect(w.X, w.Y, width, height);
    }

    /// <summary>Widgets shown on <paramref name="monitor"/>.</summary>
    public static IEnumerable<WidgetInstance> On(Settings s, IReadOnlyList<MonitorInfo> monitors, MonitorInfo monitor) =>
        s.Desktop.Widgets.Where(w => MonitorGeometry.Effective(monitors, w.MonitorId)?.Id == monitor.Id);

    /// <summary>The widget as it must be DISPLAYED: kept inside the work area (not snapped, so the stored layout is left alone) after resolution/monitor changes.</summary>
    public static WidgetInstance ForDisplay(WidgetInstance w, MonitorInfo monitor)
    {
        var (width, height) = WidgetMetrics.SizeDip(w.Size);
        var (x, y) = WidgetPlacement.Clamp(MonitorGeometry.WorkAreaDip(monitor), w.X, w.Y, width, height, snap: false);
        return x == w.X && y == w.Y ? w : w with { X = x, Y = y };
    }

    /// <summary>Adds a widget. Explicit (x,y) are clamped/snapped; absent ones are auto-placed on free space of the target monitor. <c>null</c> widget = the limit was reached.</summary>
    public static (Settings Settings, WidgetInstance? Widget) Add(Settings s, IReadOnlyList<MonitorInfo> monitors, MonitorInfo monitor, string? storedMonitorId,
        WidgetType type, WidgetSize? size, double? x, double? y, string id)
    {
        if (s.Desktop.Widgets.Count >= MaxWidgets) return (s, null);
        var sz = size ?? WidgetMetrics.DefaultSize(type);
        var (width, height) = WidgetMetrics.SizeDip(sz);
        var area = MonitorGeometry.WorkAreaDip(monitor);
        var snap = s.Desktop.GridSnap;
        (double X, double Y) pos;
        if (x is { } px && y is { } py) pos = WidgetPlacement.Clamp(area, px, py, width, height, snap);
        else
        {
            var existing = On(s, monitors, monitor).Select(w => RectOf(ForDisplay(w, monitor))).ToArray();
            pos = WidgetPlacement.AutoPlace(area, width, height, existing, snap);
        }
        var widget = new WidgetInstance { Id = id, Type = type, Size = sz, X = pos.X, Y = pos.Y, Config = WidgetMetrics.DefaultConfig(type), MonitorId = storedMonitorId };
        return (s with { Desktop = s.Desktop with { Widgets = s.Desktop.Widgets.Append(widget).ToArray() } }, widget);
    }

    public static (Settings Settings, WidgetInstance? Widget) Move(Settings s, IReadOnlyList<MonitorInfo> monitors, string id, double x, double y)
    {
        var w = s.Desktop.Widgets.FirstOrDefault(w => w.Id == id);
        var monitor = w is null ? null : MonitorGeometry.Effective(monitors, w.MonitorId);
        if (w is null || monitor is null) return (s, null);
        var (width, height) = WidgetMetrics.SizeDip(w.Size);
        var (nx, ny) = WidgetPlacement.Clamp(MonitorGeometry.WorkAreaDip(monitor), x, y, width, height, s.Desktop.GridSnap);
        var moved = w with { X = nx, Y = ny };
        return (Replace(s, moved), moved);
    }

    /// <summary>
    /// Changes the size class and/or REPLACES the whole config (docs: not merged). A bigger widget is pulled back inside the work area. <c>null</c> widget = unknown id.
    /// </summary>
    public static (Settings Settings, WidgetInstance? Widget) Update(Settings s, IReadOnlyList<MonitorInfo> monitors, string id, WidgetSize? size, JsonObject? config)
    {
        var w = s.Desktop.Widgets.FirstOrDefault(w => w.Id == id);
        if (w is null) return (s, null);
        var updated = w;
        if (size is { } sz && sz != w.Size)
        {
            updated = updated with { Size = sz };
            if (MonitorGeometry.Effective(monitors, w.MonitorId) is { } monitor)
            {
                var (width, height) = WidgetMetrics.SizeDip(sz);
                var (nx, ny) = WidgetPlacement.Clamp(MonitorGeometry.WorkAreaDip(monitor), w.X, w.Y, width, height, s.Desktop.GridSnap);
                updated = updated with { X = nx, Y = ny };
            }
        }
        if (config is not null) updated = updated with { Config = config };
        return (Replace(s, updated), updated);
    }

    public static (Settings Settings, bool Found) Remove(Settings s, string id)
    {
        var keep = s.Desktop.Widgets.Where(w => w.Id != id).ToArray();
        if (keep.Length == s.Desktop.Widgets.Count) return (s, false);
        return (s with { Desktop = s.Desktop with { Widgets = keep } }, true);
    }

    private static Settings Replace(Settings s, WidgetInstance updated) =>
        s with { Desktop = s.Desktop with { Widgets = s.Desktop.Widgets.Select(w => w.Id == updated.Id ? updated : w).ToArray() } };
}
