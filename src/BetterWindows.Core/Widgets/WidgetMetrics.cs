using System.Text.Json.Nodes;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Widgets;

/// <summary>
/// Sizes and defaults of the desktop widgets. The three size classes follow the macOS widget family (small = square, medium = 2:1, large = 2x2 of small) so the UI can
/// render every widget type in the same slots; Core needs them to keep widgets inside the work area and to find free space. All values are DIPs.
/// </summary>
public static class WidgetMetrics
{
    /// <summary>Widgets snap to this grid (DIPs) when settings.desktop.gridSnap is on.</summary>
    public const double SnapGrid = 8;
    /// <summary>Largest accepted widget config (serialised UTF-8 JSON), docs/IPC.md section 12.</summary>
    public const int MaxConfigBytes = 16 * 1024;
    /// <summary>First auto-placement position (DIPs) and the per-widget cascade step, as documented on <c>AddWidgetParams.x</c>.</summary>
    public const double CascadeStartX = 48, CascadeStartY = 80, CascadeStep = 24;

    public static (double Width, double Height) SizeDip(WidgetSize size) => size switch
    {
        WidgetSize.Small => (160, 160),
        WidgetSize.Large => (340, 340),
        _ => (340, 160),
    };

    public static WidgetSize DefaultSize(WidgetType type) => type switch
    {
        WidgetType.Clock => WidgetSize.Small,
        WidgetType.Battery => WidgetSize.Small,
        _ => WidgetSize.Medium,
    };

    /// <summary>The initial config object of a new widget (see the schemas on <c>WidgetInstance.config</c>).</summary>
    public static JsonObject DefaultConfig(WidgetType type) => type switch
    {
        WidgetType.Clock => new JsonObject { ["style"] = "digital", ["seconds"] = false },
        WidgetType.Calendar => new JsonObject { ["weekNumbers"] = false },
        WidgetType.Notes => new JsonObject { ["text"] = "" },
        _ => new JsonObject(),
    };

    /// <summary>
    /// Validates a UI-supplied widget config: it must be a JSON object whose compact UTF-8 form is at most <see cref="MaxConfigBytes"/>. Returns a detached deep copy
    /// (a JsonNode can only have one parent; the original belongs to the request).
    /// </summary>
    public static JsonObject ValidateConfig(JsonNode? config)
    {
        if (config is not JsonObject obj) throw ProtocolException.BadRequest("config must be a JSON object");
        string json;
        try { json = obj.ToJsonString(); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { throw ProtocolException.BadRequest("config is not valid JSON"); }
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxConfigBytes) throw ProtocolException.BadRequest($"config is larger than {MaxConfigBytes / 1024} KB");
        return (JsonObject)obj.DeepClone();
    }
}
