namespace BetterWindows.Core.Search.Calculator;

/// <summary>Physical quantity a unit measures. Only units of the same dimension convert into each other.</summary>
public enum Dimension { Length, Mass, Volume, Time, Information, Temperature }

/// <summary>
/// A unit of a dimension. Base value = input * Scale + Offset (Offset is only non-zero for temperatures, which are affine).
/// </summary>
public sealed record Unit(string Symbol, Dimension Dimension, double Scale, double Offset = 0);

/// <summary>The unit words the calculator understands (lower-case keys, typed after a number: "5 km", "72 f"). Information units are binary (1 KB = 1024 B), like Windows.</summary>
public static class UnitTable
{
    private const double Fahrenheit = 5.0 / 9.0;
    private const double KelvinOfZeroFahrenheitOffset = 273.15 - 32 * Fahrenheit;

    private static readonly Dictionary<string, Unit> ByKey = new(StringComparer.Ordinal)
    {
        ["mm"] = new("mm", Dimension.Length, 0.001),
        ["cm"] = new("cm", Dimension.Length, 0.01),
        ["m"] = new("m", Dimension.Length, 1),
        ["km"] = new("km", Dimension.Length, 1000),
        ["in"] = new("in", Dimension.Length, 0.0254),
        ["ft"] = new("ft", Dimension.Length, 0.3048),
        ["yd"] = new("yd", Dimension.Length, 0.9144),
        ["mi"] = new("mi", Dimension.Length, 1609.344),

        ["mg"] = new("mg", Dimension.Mass, 0.000001),
        ["g"] = new("g", Dimension.Mass, 0.001),
        ["kg"] = new("kg", Dimension.Mass, 1),
        ["lb"] = new("lb", Dimension.Mass, 0.45359237),
        ["oz"] = new("oz", Dimension.Mass, 0.028349523125),

        ["ml"] = new("ml", Dimension.Volume, 0.001),
        ["l"] = new("l", Dimension.Volume, 1),
        ["gal"] = new("gal", Dimension.Volume, 3.785411784),

        ["ms"] = new("ms", Dimension.Time, 0.001),
        ["s"] = new("s", Dimension.Time, 1),
        ["min"] = new("min", Dimension.Time, 60),
        ["h"] = new("h", Dimension.Time, 3600),
        ["d"] = new("d", Dimension.Time, 86400),

        ["b"] = new("B", Dimension.Information, 1),
        ["kb"] = new("KB", Dimension.Information, 1024),
        ["mb"] = new("MB", Dimension.Information, 1024.0 * 1024),
        ["gb"] = new("GB", Dimension.Information, 1024.0 * 1024 * 1024),
        ["tb"] = new("TB", Dimension.Information, 1024.0 * 1024 * 1024 * 1024),

        ["c"] = new("°C", Dimension.Temperature, 1, 273.15),
        ["f"] = new("°F", Dimension.Temperature, Fahrenheit, KelvinOfZeroFahrenheitOffset),
        ["k"] = new("K", Dimension.Temperature, 1, 0),
    };

    /// <summary>Looks up a unit by its lower-case key.</summary>
    public static bool TryGet(string key, out Unit unit)
    {
        if (ByKey.TryGetValue(key, out var found))
        {
            unit = found;
            return true;
        }
        unit = null!;
        return false;
    }
}
