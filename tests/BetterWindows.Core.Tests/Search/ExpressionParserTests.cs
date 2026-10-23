using BetterWindows.Core.Search.Calculator;
using Xunit;

namespace BetterWindows.Core.Tests.Search;

public class ExpressionParserTests
{
    private static void AssertValue(string expr, double expected)
    {
        Assert.True(ExpressionParser.TryEvaluate(expr, out var r), $"'{expr}' should evaluate");
        Assert.Null(r.UnitSymbol);
        Assert.True(Math.Abs(r.Value - expected) <= 1e-6 * Math.Max(1, Math.Abs(expected)), $"'{expr}' = {r.Value}, expected {expected}");
    }

    [Theory]
    // basic arithmetic and precedence
    [InlineData("2+2", 4)]
    [InlineData("2 + 2", 4)]
    [InlineData("10-4", 6)]
    [InlineData("3*4", 12)]
    [InlineData("3×4", 12)]
    [InlineData("3·4", 12)]
    [InlineData("8/4", 2)]
    [InlineData("8÷4", 2)]
    [InlineData("2+3*4", 14)]
    [InlineData("(2+3)*4", 20)]
    [InlineData("2*(3+4)", 14)]
    [InlineData("((1+2))*((3))", 9)]
    [InlineData("10-2-3", 5)]
    [InlineData("100/10/5", 2)]
    [InlineData("7/2", 3.5)]
    [InlineData("0.1+0.2", 0.3)]
    [InlineData("1/4+1/4", 0.5)]
    [InlineData("-3+5", 2)]
    [InlineData("5--3", 8)]
    [InlineData("5+-3", 2)]
    [InlineData("2*-3", -6)]
    [InlineData("3+-+-2", 5)]
    // powers
    [InlineData("2^10", 1024)]
    [InlineData("2**10", 1024)]
    [InlineData("2^3^2", 512)]
    [InlineData("-2^2", -4)]
    [InlineData("(-2)^2", 4)]
    [InlineData("2^-1", 0.5)]
    [InlineData("4^0.5", 2)]
    [InlineData("2^3*2", 16)]
    [InlineData("10^-2", 0.01)]
    // modulo
    [InlineData("10%3", 1)]
    [InlineData("10 % 3", 1)]
    [InlineData("17 mod 5", 2)]
    [InlineData("-7 mod 3", 2)]
    [InlineData("mod(10,4)", 2)]
    // implicit multiplication
    [InlineData("2(3+4)", 14)]
    [InlineData("(1+2)(3+4)", 21)]
    [InlineData("2pi", 6.283185307179586)]
    [InlineData("pi*2", 6.283185307179586)]
    [InlineData("2e", 5.43656365691809)]
    [InlineData("2sqrt(16)", 8)]
    // constants and functions
    [InlineData("e^2", 7.3890560989306495)]
    [InlineData("π", 3.141592653589793)]
    [InlineData("tau/2", 3.141592653589793)]
    [InlineData("sqrt(16)", 4)]
    [InlineData("sqrt(2)^2", 2)]
    [InlineData("cbrt(27)", 3)]
    [InlineData("abs(-5)", 5)]
    [InlineData("abs(3-10)", 7)]
    [InlineData("round(2.5)", 3)]
    [InlineData("round(-2.5)", -3)]
    [InlineData("round(2.5678, 2)", 2.57)]
    [InlineData("round(1234, 0)", 1234)]
    [InlineData("floor(-2.5)", -3)]
    [InlineData("floor(2.9)", 2)]
    [InlineData("ceil(2.1)", 3)]
    [InlineData("ceil(-2.1)", -2)]
    [InlineData("min(3,1,2)", 1)]
    [InlineData("max(3;7)", 7)]
    [InlineData("max(3, 7, 5)", 7)]
    [InlineData("pow(2,8)", 256)]
    [InlineData("pow(2;0.5)", 1.4142135623730951)]
    [InlineData("ln(e)", 1)]
    [InlineData("log(1000)", 3)]
    [InlineData("log10(100)", 2)]
    [InlineData("log2(8)", 3)]
    [InlineData("exp(0)", 1)]
    [InlineData("sin(0)", 0)]
    [InlineData("cos(0)", 1)]
    [InlineData("tan(0)", 0)]
    [InlineData("asin(1)", 1.5707963267948966)]
    [InlineData("acos(1)", 0)]
    [InlineData("atan(1)*4", 3.141592653589793)]
    [InlineData("sin(pi/2)", 1)]
    [InlineData("mod(-7,3)", 2)]
    // degrees
    [InlineData("sin(30 deg)", 0.5)]
    [InlineData("cos(60°)", 0.5)]
    [InlineData("180 deg", 3.141592653589793)]
    [InlineData("sin(90°)", 1)]
    // percent forms
    [InlineData("100 + 10%", 110)]
    [InlineData("100 - 15%", 85)]
    [InlineData("200 + 50%", 300)]
    [InlineData("20% of 50", 10)]
    [InlineData("50% * 4", 2)]
    [InlineData("200 * 10%", 20)]
    [InlineData("10%", 0.1)]
    [InlineData("(100 + 10)%", 1.1)]
    // number formats
    [InlineData("1,5 + 1", 2.5)]
    [InlineData("1.5 + 1", 2.5)]
    [InlineData("1,500 + 1", 1501)]
    [InlineData("1,234,567 + 0", 1234567)]
    [InlineData("1 000 + 1", 1001)]
    [InlineData("1 000 000 / 2", 500000)]
    [InlineData("1 000 * 2", 2000)]
    [InlineData("2.5e3 + 0", 2500)]
    [InlineData("1e3+1", 1001)]
    [InlineData("1E-3 * 1", 0.001)]
    [InlineData("2,5*2", 5)]
    [InlineData(".5 + .25", 0.75)]
    [InlineData("1,23 + 1,77", 3)]
    [InlineData("1/3", 0.3333333333333333)]
    [InlineData("= 2+2", 4)]
    [InlineData("2+2=", 4)]
    [InlineData("2 + 2 ", 4)]
    [InlineData("8 mod 3 + 1", 3)]
    [InlineData("2^(1/2)*2^(1/2)", 2)]
    [InlineData("10 * (5 - 2) ^ 2 / 3", 30)]
    public void Evaluates_valid_expressions(string expr, double expected) => AssertValue(expr, expected);

    [Theory]
    [InlineData("5 km in mi", 3.106855961187, "mi")]
    [InlineData("5 km to m", 5000, "m")]
    [InlineData("5 km в m", 5000, "m")]
    [InlineData("72 f to c", 22.22222222222222, "°C")]
    [InlineData("100 c to f", 212, "°F")]
    [InlineData("0 c to k", 273.15, "K")]
    [InlineData("300 k in c", 26.85, "°C")]
    [InlineData("1 kg to g", 1000, "g")]
    [InlineData("1 lb in kg", 0.45359237, "kg")]
    [InlineData("2 gb in mb", 2048, "MB")]
    [InlineData("1 tb to gb", 1024, "GB")]
    [InlineData("1 h to min", 60, "min")]
    [InlineData("90 min to h", 1.5, "h")]
    [InlineData("1 mi to km", 1.609344, "km")]
    [InlineData("12 in to cm", 30.48, "cm")]
    [InlineData("3 ft in in", 36, "in")]
    [InlineData("1 l to ml", 1000, "ml")]
    [InlineData("1 gal in l", 3.785411784, "l")]
    [InlineData("1000 ms to s", 1, "s")]
    [InlineData("5 km + 3 km", 8, "km")]
    [InlineData("5 km * 2", 10, "km")]
    [InlineData("2 * 5 km", 10, "km")]
    [InlineData("5 km - 1 km in m", 4000, "m")]
    [InlineData("2 mb to kb", 2048, "KB")]
    public void Converts_between_units_of_one_dimension(string expr, double expected, string symbol)
    {
        Assert.True(ExpressionParser.TryEvaluate(expr, out var r), $"'{expr}' should evaluate");
        Assert.Equal(symbol, r.UnitSymbol);
        Assert.True(Math.Abs(r.Value - expected) <= 1e-6 * Math.Max(1, Math.Abs(expected)), $"'{expr}' = {r.Value}, expected {expected}");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("=")]
    [InlineData("2024")]
    [InlineData("-5")]
    [InlineData("(2024)")]
    [InlineData("5 km")]
    [InlineData("5 in")]
    [InlineData("12 f")]
    [InlineData("abc")]
    [InlineData("hello world")]
    [InlineData("2+")]
    [InlineData("*5")]
    [InlineData("(2+3")]
    [InlineData("2+3)")]
    [InlineData("1 + )")]
    [InlineData("%")]
    [InlineData("20%%")]
    [InlineData("3 $ 4")]
    [InlineData("3!")]
    [InlineData("2 3")]
    [InlineData("foo(3)")]
    [InlineData("unknown + 1")]
    [InlineData("min()")]
    [InlineData("round(1,2,3)")]
    [InlineData("round(1, 99)")]
    [InlineData("sin()")]
    [InlineData("1/0")]
    [InlineData("0/0")]
    [InlineData("5 mod 0")]
    [InlineData("sqrt(-1)")]
    [InlineData("ln(0)")]
    [InlineData("(-8)^(1/3)")]
    [InlineData("10^400")]
    [InlineData("9^9^9")]
    [InlineData("1e400")]
    [InlineData("5 km in kg")]
    [InlineData("5 km + 3")]
    [InlineData("5 km / 2 km")]
    [InlineData("5 km in")]
    [InlineData("5 km to")]
    [InlineData("5 km in mi in")]
    [InlineData("5 km ^ 2")]
    [InlineData("sin(5 km)")]
    [InlineData("5 km %")]
    [InlineData("(3 km)2 + 1 km + 1 g")]
    public void Rejects_invalid_or_non_operations(string expr)
    {
        Assert.False(ExpressionParser.TryEvaluate(expr, out _));
    }

    [Fact]
    public void Null_input_is_rejected()
    {
        Assert.False(ExpressionParser.TryEvaluate(null, out _));
    }

    [Fact]
    public void Inputs_over_the_length_limit_are_rejected()
    {
        var expr = string.Concat(Enumerable.Repeat("1+", ExpressionParser.MaxInputLength / 2 + 10)) + "1";
        Assert.True(expr.Length > ExpressionParser.MaxInputLength);
        Assert.False(ExpressionParser.TryEvaluate(expr, out _));
    }

    [Fact]
    public void Too_many_tokens_are_rejected()
    {
        var expr = string.Concat(Enumerable.Repeat("1+", 200)) + "1";
        Assert.False(ExpressionParser.TryEvaluate(expr, out _));
    }

    [Fact]
    public void Deep_nesting_is_rejected_without_a_stack_overflow()
    {
        var deep = new string('(', 100) + "1" + new string(')', 100);
        Assert.False(ExpressionParser.TryEvaluate(deep, out _));
        var deepUnary = new string('-', 100) + "1+1";
        Assert.False(ExpressionParser.TryEvaluate(deepUnary, out _));
    }

    [Fact]
    public void Moderate_nesting_still_evaluates()
    {
        var expr = new string('(', 20) + "1" + new string(')', 20) + "+1";
        AssertValue(expr, 2);
    }

    [Fact]
    public void Too_many_function_arguments_are_rejected()
    {
        var args = string.Join(",", Enumerable.Repeat("1", ExpressionParser.MaxArguments + 1));
        Assert.False(ExpressionParser.TryEvaluate($"min({args})", out _));
    }

    [Fact]
    public void Numbers_with_too_many_digits_are_rejected()
    {
        var digits = "1" + new string('0', 450);
        Assert.False(ExpressionParser.TryEvaluate(digits + "+1", out _));
    }

    [Fact]
    public void Exponent_overflow_is_an_error_not_infinity()
    {
        Assert.False(ExpressionParser.TryEvaluate("2^2000", out _));
        Assert.False(ExpressionParser.TryEvaluate("10^10^10", out _));
    }

    [Fact]
    public void Results_are_always_finite()
    {
        var exprs = new[] { "1/3", "2^0.5", "pi", "e^-700", "exp(700)", "1e308*10", "tan(pi/2)" };
        foreach (var e in exprs)
        {
            if (ExpressionParser.TryEvaluate(e, out var r)) Assert.True(double.IsFinite(r.Value), e);
        }
    }

    [Fact]
    public void Commas_inside_function_calls_separate_arguments_not_decimals()
    {
        AssertValue("min(2,3)", 2);
        AssertValue("min(2,3.5)", 2);
        AssertValue("max(1,5) + 1,5", 6.5);
    }

    [Fact]
    public void A_bare_decimal_comma_at_top_level_is_a_decimal_point()
    {
        AssertValue("(2,5)*2", 5);
    }
}
