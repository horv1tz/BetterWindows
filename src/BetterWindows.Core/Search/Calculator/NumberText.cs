using System.Globalization;

namespace BetterWindows.Core.Search.Calculator;

/// <summary>
/// Formats calculator results for a UI locale. English: "1,234.5"; Russian: "1 234,5" with a no-break space as the group separator.
/// Values are rounded to 10 decimal places (so 0.1 + 0.2 shows 0.3) and switch to exponent notation from 10^15 upwards.
/// </summary>
public static class NumberText
{
    private const double ExponentThreshold = 1e15;

    /// <summary>Formats a finite value for the locale ("en" or "ru"; anything else uses English).</summary>
    public static string Format(double value, string locale)
    {
        if (!double.IsFinite(value)) return "";
        var nfi = FormatFor(locale);
        var rounded = Math.Round(value, 10, MidpointRounding.AwayFromZero);
        if (rounded == 0) rounded = 0; // drops the sign of -0
        return Math.Abs(rounded) >= ExponentThreshold
            ? rounded.ToString("0.###########E+0", nfi)
            : rounded.ToString("#,##0.##########", nfi);
    }

    private static NumberFormatInfo FormatFor(string locale)
    {
        var nfi = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        if (string.Equals(locale, "ru", StringComparison.OrdinalIgnoreCase))
        {
            nfi.NumberDecimalSeparator = ",";
            nfi.NumberGroupSeparator = " ";
        }
        else
        {
            nfi.NumberDecimalSeparator = ".";
            nfi.NumberGroupSeparator = ",";
        }
        return nfi;
    }
}
