using System.Globalization;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;

namespace BetterWindows.Core.Widgets;

/// <summary>Validation and time rules of reminders (UI input is untrusted). Due times are ISO-8601 LOCAL date-times, parsed with the invariant culture.</summary>
public static class ReminderRules
{
    public const int MaxTitleChars = 500, MaxNotesChars = 4000, MaxListChars = 100, MaxItems = 2000;
    private const int MaxDueChars = 40;
    private static readonly string[] DueFormats = { "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF" };

    /// <summary>Parses "2026-10-23T14:30" / "...:30:15" / "...:30:15.5" (no offset, no 'Z': it is the user's local wall-clock time). Years outside 1970..2999 are rejected.</summary>
    public static bool TryParseDue(string? s, out DateTime local)
    {
        local = default;
        if (string.IsNullOrEmpty(s) || s.Length > MaxDueChars) return false;
        if (!DateTime.TryParseExact(s, DueFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) return false;
        if (dt.Year < 1970 || dt.Year > 2999) return false;
        local = DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
        return true;
    }

    /// <summary>The canonical stored form: minutes when the seconds are zero (what a datetime-local input produces), otherwise with seconds. Fractions are dropped.</summary>
    public static string FormatDue(DateTime local) =>
        local.ToString(local.Second == 0 ? "yyyy-MM-dd'T'HH:mm" : "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>The UTC instant of a local wall-clock time in <paramref name="zone"/>: a time that does not exist (spring-forward gap) moves to the next hour; an ambiguous one takes the first occurrence.</summary>
    public static DateTimeOffset ToUtc(DateTime local, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        try
        {
            if (zone.IsInvalidTime(unspecified)) unspecified = unspecified.AddHours(1);
            return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(unspecified, zone), TimeSpan.Zero);
        }
        catch (ArgumentException) { return new DateTimeOffset(DateTime.SpecifyKind(unspecified, DateTimeKind.Utc)); }
    }

    /// <summary>Single-line text: trimmed, 1..max chars, no control characters.</summary>
    public static string RequireLine(string? s, int max, string what)
    {
        var t = (s ?? "").Trim();
        if (t.Length == 0) throw ProtocolException.BadRequest($"{what} must not be empty");
        if (t.Length > max) throw ProtocolException.BadRequest($"{what} is longer than {max} characters");
        foreach (var c in t) if (c < 0x20 || c == 0x7f) throw ProtocolException.BadRequest($"{what} contains control characters");
        return t;
    }

    /// <summary>Optional single-line text: null/empty/whitespace = none.</summary>
    public static string? OptionalLine(string? s, int max, string what) => string.IsNullOrWhiteSpace(s) ? null : RequireLine(s, max, what);

    /// <summary>Optional multi-line text (newlines and tabs allowed); null/empty/whitespace = none.</summary>
    public static string? OptionalNotes(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (s.Length > MaxNotesChars) throw ProtocolException.BadRequest($"notes are longer than {MaxNotesChars} characters");
        foreach (var c in s) if ((c < 0x20 && c != '\n' && c != '\r' && c != '\t') || c == 0x7f) throw ProtocolException.BadRequest("notes contain control characters");
        return s;
    }

    /// <summary>Optional due: null/empty = none; otherwise a valid local date-time in its canonical form.</summary>
    public static string? OptionalDue(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        if (!TryParseDue(s, out var local)) throw ProtocolException.BadRequest("due must be an ISO-8601 local date-time like 2026-10-23T14:30");
        return FormatDue(local);
    }

    public static bool IsValidId(string? id) => id is { Length: 36 } && Guid.TryParseExact(id, "D", out _);
}
