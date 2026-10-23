using System.Text.Json.Nodes;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Overlay;

/// <summary>
/// Validation of <c>overlay.open</c> (the UI is untrusted, docs/IPC.md section 14). Pure: monitor ids are checked against the list passed in.
/// Rules: the mode is a defined value; a popover is required iff mode == popover; its kind is defined; the anchor is finite and within sane DIP bounds; itemId is a bounded
/// printable string; data is bounded (and for appMenu an object with an optional string appId); monitorId, if present, is one of the known monitors.
/// </summary>
public static class OverlayRequestValidator
{
    /// <summary>Largest absolute DIP coordinate/size accepted for an anchor (an 8K screen at 100 % is 7680 DIPs wide).</summary>
    public const double MaxAnchorExtent = 16384;
    public const int MaxItemIdChars = 4200;
    public const int MaxDataChars = 4096;
    public const int MaxMonitorIdChars = 64;

    /// <summary>Returns the request to hand to the surface manager (same values, validated) or throws a <see cref="ProtocolException"/> (badRequest).</summary>
    public static OverlayRequest Validate(OverlayRequest request, IReadOnlyList<MonitorInfo> monitors)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Mode)) throw ProtocolException.BadRequest("unknown overlay mode");

        if (request.Mode == OverlayMode.Popover)
        {
            if (request.Popover is null) throw ProtocolException.BadRequest("mode 'popover' requires a popover");
            ValidatePopover(request.Popover);
        }
        else if (request.Popover is not null) throw ProtocolException.BadRequest($"mode '{request.Mode}' takes no popover");

        if (request.MonitorId is not null)
        {
            if (request.MonitorId.Length is 0 or > MaxMonitorIdChars || !monitors.Any(m => string.Equals(m.Id, request.MonitorId, StringComparison.Ordinal)))
                throw ProtocolException.BadRequest("unknown monitorId");
        }
        return request;
    }

    private static void ValidatePopover(PopoverRequest p)
    {
        if (!Enum.IsDefined(p.Kind)) throw ProtocolException.BadRequest("unknown popover kind");
        var a = p.Anchor ?? throw ProtocolException.BadRequest("popover anchor is required");
        if (!IsCoordinate(a.X) || !IsCoordinate(a.Y) || !IsExtent(a.Width) || !IsExtent(a.Height))
            throw ProtocolException.BadRequest("popover anchor must be finite and within screen bounds");

        if (p.ItemId is not null && !IsPrintable(p.ItemId, MaxItemIdChars)) throw ProtocolException.BadRequest("invalid popover itemId");

        if (p.Data is null) return;
        string json;
        try { json = p.Data.ToJsonString(); }
        catch (Exception) { throw ProtocolException.BadRequest("invalid popover data"); }
        if (json.Length > MaxDataChars) throw ProtocolException.BadRequest("popover data is too large");

        if (p.Kind == PopoverKind.AppMenu)
        {
            if (p.Data is not JsonObject obj) throw ProtocolException.BadRequest("appMenu data must be an object");
            if (obj["appId"] is { } id && !(id is JsonValue v && v.TryGetValue<string>(out var s) && IsPrintable(s, MaxItemIdChars)))
                throw ProtocolException.BadRequest("appMenu data.appId must be a string");
        }
    }

    private static bool IsCoordinate(double v) => double.IsFinite(v) && Math.Abs(v) <= MaxAnchorExtent;
    private static bool IsExtent(double v) => double.IsFinite(v) && v >= 0 && v <= MaxAnchorExtent;

    private static bool IsPrintable(string s, int max)
    {
        if (s.Length > max) return false;
        foreach (var c in s) if (c < 0x20 || c == 0x7f) return false;
        return true;
    }
}
