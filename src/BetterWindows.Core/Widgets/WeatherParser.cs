using System.Globalization;
using System.Text.Json;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Widgets;

/// <summary>
/// Turns Open-Meteo JSON into protocol snapshots. Tolerant by design: the service is third-party, so a missing/renamed/null member degrades that value (or, when the
/// current conditions are missing, the whole answer) instead of throwing. Temperatures are always Celsius (the request asks for it; the UI converts), wind is km/h.
/// </summary>
public static class WeatherParser
{
    public const int HourlyCount = 24;
    public const int DailyCount = 7;

    /// <summary>
    /// Parses a forecast response. Returns null when the document has no usable current temperature (an error body, HTML, an unrelated JSON).
    /// <paramref name="locationName"/> and <paramref name="updatedAtIso"/> are stamped on the result.
    /// </summary>
    public static WeatherSnapshot? ParseForecast(string json, string locationName, string updatedAtIso)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.True) return null;
            if (!root.TryGetProperty("current", out var cur) || cur.ValueKind != JsonValueKind.Object) return null;
            if (!TryNumber(cur, "temperature_2m", out var temp)) return null;

            var curTime = Str(cur, "time");
            return new WeatherSnapshot
            {
                Available = true,
                Location = locationName,
                TempC = temp,
                ApparentC = TryNumber(cur, "apparent_temperature", out var app) ? app : temp,
                Code = TryNumber(cur, "weather_code", out var code) ? (int)Math.Round(code) : 0,
                IsDay = !TryNumber(cur, "is_day", out var day) || day >= 0.5,
                Humidity = TryNumber(cur, "relative_humidity_2m", out var hum) ? (int)Math.Round(hum) : null,
                WindKph = TryNumber(cur, "wind_speed_10m", out var wind) ? wind : null,
                Hourly = ParseHourly(root, curTime),
                Daily = ParseDaily(root),
                UpdatedAt = updatedAtIso,
            };
        }
        catch (JsonException) { return null; }
    }

    /// <summary>The next 24 hourly points starting at the current hour (the response may carry a whole week).</summary>
    private static IReadOnlyList<WeatherHour> ParseHourly(JsonElement root, string? currentTime)
    {
        if (!root.TryGetProperty("hourly", out var h) || h.ValueKind != JsonValueKind.Object) return Array.Empty<WeatherHour>();
        if (!h.TryGetProperty("time", out var times) || times.ValueKind != JsonValueKind.Array) return Array.Empty<WeatherHour>();
        h.TryGetProperty("temperature_2m", out var temps);
        h.TryGetProperty("weather_code", out var codes);

        // ISO local times of one format compare correctly as strings; the current time is cut to the hour ("2026-10-23T14:15" -> "2026-10-23T14:00").
        var from = currentTime is { Length: >= 13 } ? currentTime[..13] + ":00" : null;
        var list = new List<WeatherHour>(HourlyCount);
        var i = 0;
        foreach (var t in times.EnumerateArray())
        {
            var idx = i++;
            if (t.ValueKind != JsonValueKind.String) continue;
            var ts = t.GetString()!;
            if (from is not null && string.CompareOrdinal(ts, from) < 0) continue;
            if (!At(temps, idx, out var temp)) continue;
            list.Add(new WeatherHour { Time = ts, TempC = temp, Code = At(codes, idx, out var c) ? (int)Math.Round(c) : 0 });
            if (list.Count == HourlyCount) break;
        }
        return list;
    }

    private static IReadOnlyList<WeatherDay> ParseDaily(JsonElement root)
    {
        if (!root.TryGetProperty("daily", out var d) || d.ValueKind != JsonValueKind.Object) return Array.Empty<WeatherDay>();
        if (!d.TryGetProperty("time", out var dates) || dates.ValueKind != JsonValueKind.Array) return Array.Empty<WeatherDay>();
        d.TryGetProperty("temperature_2m_max", out var highs);
        d.TryGetProperty("temperature_2m_min", out var lows);
        d.TryGetProperty("weather_code", out var codes);

        var list = new List<WeatherDay>(DailyCount);
        var i = 0;
        foreach (var t in dates.EnumerateArray())
        {
            var idx = i++;
            if (t.ValueKind != JsonValueKind.String) continue;
            if (!At(highs, idx, out var hi) || !At(lows, idx, out var lo)) continue;
            list.Add(new WeatherDay { Date = t.GetString()!, HighC = hi, LowC = lo, Code = At(codes, idx, out var c) ? (int)Math.Round(c) : 0 });
            if (list.Count == DailyCount) break;
        }
        return list;
    }

    /// <summary>Parses a geocoding response ("results" is absent when nothing matched). Entries without a name or coordinates are skipped.</summary>
    public static IReadOnlyList<WeatherLocation>? ParseLocations(string json, int max)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.True) return null;
            if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) return Array.Empty<WeatherLocation>();
            var list = new List<WeatherLocation>();
            foreach (var r in results.EnumerateArray())
            {
                if (r.ValueKind != JsonValueKind.Object) continue;
                var name = Str(r, "name");
                if (string.IsNullOrWhiteSpace(name) || !TryNumber(r, "latitude", out var lat) || !TryNumber(r, "longitude", out var lon)) continue;
                if (lat is < -90 or > 90 || lon is < -180 or > 180) continue;
                list.Add(new WeatherLocation { Name = name!, Country = NullIfEmpty(Str(r, "country")), Admin = NullIfEmpty(Str(r, "admin1")), Latitude = lat, Longitude = lon });
                if (list.Count == max) break;
            }
            return list;
        }
        catch (JsonException) { return null; }
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string? Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool TryNumber(JsonElement o, string name, out double value)
    {
        value = 0;
        if (!o.TryGetProperty(name, out var v)) return false;
        return Number(v, out value);
    }

    private static bool At(JsonElement array, int index, out double value)
    {
        value = 0;
        if (array.ValueKind != JsonValueKind.Array || index >= array.GetArrayLength()) return false;
        return Number(array[index], out value);
    }

    private static bool Number(JsonElement v, out double value)
    {
        value = 0;
        if (v.ValueKind == JsonValueKind.Number) return v.TryGetDouble(out value) && double.IsFinite(value);
        if (v.ValueKind == JsonValueKind.String) return double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
        return false;
    }
}
