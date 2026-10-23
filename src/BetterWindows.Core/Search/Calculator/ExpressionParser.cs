using System.Globalization;
using System.Text;

namespace BetterWindows.Core.Search.Calculator;

/// <summary>The value of an evaluated expression: a finite number, with the unit symbol when the expression was a unit conversion ("5 km in mi").</summary>
public readonly record struct CalcResult(double Value, string? UnitSymbol);

/// <summary>
/// Safe arithmetic for the calculator row: a hand-written lexer and recursive-descent parser. No eval, no reflection, no loops over user data.
/// Limits (all enforced): input 256 characters, 300 tokens, nesting depth 64, 16 function arguments. Any non-finite intermediate result (division by zero,
/// overflow, sqrt of a negative number) is an error. Every failure returns false; nothing throws to the caller.
/// Grammar (lowest to highest precedence): additive (+ -) ; multiplicative (* / % mod of, implicit multiplication) ; unary (- +) ; power (^ and **, right
/// associative) ; postfix (percent, "deg") ; primary (number with optional unit, parenthesised expression, constant, function call).
/// Number literals: "1.5", "1,5" (decimal comma outside function arguments), "1,234.5" and "1 234" (group separators), "2e-3".
/// </summary>
public static class ExpressionParser
{
    public const int MaxInputLength = 256;
    public const int MaxTokens = 300;
    public const int MaxDepth = 64;
    public const int MaxArguments = 16;

    /// <summary>
    /// Evaluates a complete expression. Returns false for anything that is not a complete, finite expression that performs an operation: bare numbers
    /// ("2024"), units without a conversion ("5 km") and malformed input are not calculator results.
    /// </summary>
    public static bool TryEvaluate(string? input, out CalcResult result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(input) || input.Length > MaxInputLength) return false;
        try
        {
            var tokens = Lexer.Tokenize(input);
            result = new Parser(tokens).ParseTop();
            return true;
        }
        catch (Exception ex) when (ex is CalcException or FormatException or OverflowException or ArgumentException)
        {
            // Expected failures only: anything else is a bug and must surface in tests rather than being swallowed here.
            return false;
        }
    }

    /// <summary>Thrown inside the parser; never escapes <see cref="TryEvaluate"/>.</summary>
    internal sealed class CalcException(string message) : Exception(message);

    internal enum TokenKind { Number, Identifier, Operator, LParen, RParen, Comma, End }

    internal readonly record struct Token(TokenKind Kind, string Text, double Number = 0);

    /// <summary>Lexer: turns text into tokens. Commas are decimal separators or group separators at top level, and argument separators inside function calls.</summary>
    internal static class Lexer
    {
        private static readonly HashSet<string> Functions = new(StringComparer.Ordinal)
        {
            "sqrt", "cbrt", "abs", "round", "floor", "ceil", "sin", "cos", "tan", "asin", "acos", "atan",
            "ln", "log", "log10", "log2", "exp", "min", "max", "pow", "mod",
        };

        /// <summary>Known function names (used to tell a call "sin(" from a unit or constant).</summary>
        public static bool IsFunction(string name) => Functions.Contains(name);

        public static List<Token> Tokenize(string s)
        {
            var list = new List<Token>();
            var parens = new Stack<bool>(); // true = this '(' opens a function call
            int i = 0, n = s.Length;
            while (i < n)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c) || c == '=' || c == '?') { i++; continue; }
                if (list.Count >= MaxTokens) throw new CalcException("too long");

                bool inFunction = parens.Count > 0 && parens.Peek();
                if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < n && char.IsAsciiDigit(s[i + 1])))
                {
                    list.Add(ReadNumber(s, ref i, inFunction));
                    continue;
                }
                if (c == '°') { list.Add(new Token(TokenKind.Identifier, "deg")); i++; continue; }
                if (char.IsLetter(c) || c == '_')
                {
                    int start = i;
                    while (i < n && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                    var name = s[start..i].ToLowerInvariant();
                    if (name == "π") name = "pi";
                    list.Add(new Token(TokenKind.Identifier, name));
                    continue;
                }

                switch (c)
                {
                    case '+': list.Add(Op("+")); break;
                    case '-' or '−' or '–': list.Add(Op("-")); break;
                    case '*' when i + 1 < n && s[i + 1] == '*':
                        list.Add(Op("^")); i += 2; continue;
                    case '*' or '×' or '·': list.Add(Op("*")); break;
                    case '/' or '÷': list.Add(Op("/")); break;
                    case '^': list.Add(Op("^")); break;
                    case '%': list.Add(Op("%")); break;
                    case '(':
                    {
                        bool isCall = list.Count > 0 && list[^1].Kind == TokenKind.Identifier && IsFunction(list[^1].Text);
                        parens.Push(isCall);
                        list.Add(new Token(TokenKind.LParen, "("));
                        break;
                    }
                    case ')':
                        if (parens.Count == 0) throw new CalcException("unbalanced parenthesis");
                        parens.Pop();
                        list.Add(new Token(TokenKind.RParen, ")"));
                        break;
                    case ',' or ';':
                        list.Add(new Token(TokenKind.Comma, c.ToString()));
                        break;
                    default:
                        throw new CalcException("unexpected character");
                }
                i++;
            }
            if (parens.Count != 0) throw new CalcException("unbalanced parenthesis");
            list.Add(new Token(TokenKind.End, ""));
            return list;
        }

        private static Token Op(string text) => new(TokenKind.Operator, text);

        private static bool IsGroupSeparator(char c, bool inFunction) =>
            c == ' ' || c == ' ' || c == ' ' || (c == ',' && !inFunction);

        private static bool IsDigitAt(string s, int index) => index < s.Length && char.IsAsciiDigit(s[index]);

        /// <summary>
        /// Reads one number literal. Group separators (space, NBSP, thin space, or a comma outside function calls) are accepted only when exactly three digits
        /// follow and no digit after them, so "1,234" is 1234 but "1,23" is 1.23 and "2 3" is two tokens.
        /// </summary>
        private static Token ReadNumber(string s, ref int i, bool inFunction)
        {
            int n = s.Length;
            var sb = new StringBuilder();
            while (IsDigitAt(s, i)) sb.Append(s[i++]);

            while (sb.Length > 0 && i < n && IsGroupSeparator(s[i], inFunction) && IsDigitAt(s, i + 1) && IsDigitAt(s, i + 2) && IsDigitAt(s, i + 3) && !IsDigitAt(s, i + 4))
            {
                i++;
                for (int k = 0; k < 3; k++) sb.Append(s[i++]);
            }

            if (i < n && (s[i] == '.' || (s[i] == ',' && !inFunction)) && IsDigitAt(s, i + 1))
            {
                sb.Append('.');
                i++;
                while (IsDigitAt(s, i)) sb.Append(s[i++]);
            }

            if (i < n && (s[i] == 'e' || s[i] == 'E'))
            {
                int j = i + 1;
                var exp = new StringBuilder("e");
                if (j < n && (s[j] == '+' || s[j] == '-')) exp.Append(s[j++]);
                if (IsDigitAt(s, j))
                {
                    while (IsDigitAt(s, j)) exp.Append(s[j++]);
                    sb.Append(exp);
                    i = j;
                }
            }

            if (sb.Length > 400) throw new CalcException("number too long");
            var value = double.Parse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
            if (!double.IsFinite(value)) throw new CalcException("number out of range");
            return new Token(TokenKind.Number, sb.ToString(), value);
        }
    }

    /// <summary>Recursive-descent parser and evaluator in one pass.</summary>
    private sealed class Parser(List<Token> tokens)
    {
        /// <summary>A value during evaluation. Percent literals keep their raw number (10 for "10%") until normalised.</summary>
        private readonly record struct Val(double V, Unit? Unit = null, bool Percent = false);

        private readonly List<Token> _tokens = tokens;
        private int _pos;
        private int _depth;
        /// <summary>Number of operations performed; a result needs at least one (bare numbers and bare units are not results).</summary>
        private int _ops;

        private static readonly Dictionary<string, double> Constants = new(StringComparer.Ordinal)
        {
            ["pi"] = Math.PI, ["e"] = Math.E, ["tau"] = Math.Tau,
        };

        public CalcResult ParseTop()
        {
            var v = ParseAdditive();
            var next = Peek();
            if (next.Kind == TokenKind.Identifier && next.Text is "in" or "to" or "в")
            {
                Advance();
                var target = ExpectUnit();
                if (v.Unit is null) throw new CalcException("conversion needs a unit");
                v = Convert(v, target);
                _ops++;
            }
            if (Peek().Kind != TokenKind.End) throw new CalcException("unexpected token");
            if (_ops == 0) throw new CalcException("not an operation");
            return new CalcResult(Finite(Num(v)), v.Unit?.Symbol);
        }

        private static double Num(Val v) => v.Percent ? v.V / 100.0 : v.V;

        private static double Finite(double d) => double.IsFinite(d) ? d : throw new CalcException("not a finite number");

        private Token Peek() => _tokens[_pos];

        private Token PeekAt(int offset) => _pos + offset < _tokens.Count ? _tokens[_pos + offset] : _tokens[^1];

        /// <summary>Consumes the current token; the End token is never consumed, so Peek stays valid.</summary>
        private Token Advance()
        {
            var t = _tokens[_pos];
            if (t.Kind != TokenKind.End) _pos++;
            return t;
        }

        private bool IsOperator(string text) => Peek() is { Kind: TokenKind.Operator } t && t.Text == text;

        private void Enter()
        {
            if (++_depth > MaxDepth) throw new CalcException("too deeply nested");
        }

        private void Leave() => _depth--;

        private Unit ExpectUnit()
        {
            var t = Peek();
            if (t.Kind != TokenKind.Identifier || !UnitTable.TryGet(t.Text, out var unit)) throw new CalcException("unknown unit");
            Advance();
            return unit;
        }

        private static Val Convert(Val v, Unit to)
        {
            var from = v.Unit ?? throw new CalcException("no unit to convert");
            if (from.Dimension != to.Dimension) throw new CalcException("incompatible units");
            double baseValue = Num(v) * from.Scale + from.Offset;
            return new Val(Finite((baseValue - to.Offset) / to.Scale), to);
        }

        // additive := term (('+' | '-') term)*
        private Val ParseAdditive()
        {
            var left = ParseTerm();
            while (IsOperator("+") || IsOperator("-"))
            {
                string op = Advance().Text;
                var right = ParseTerm();
                _ops++;
                if (right.Percent)
                {
                    // "100 + 10%" means 100 plus 10 percent of 100.
                    if (left.Unit is not null) throw new CalcException("percent of a unit");
                    double baseValue = Num(left);
                    double delta = right.V / 100.0 * baseValue;
                    left = new Val(Finite(op == "+" ? baseValue + delta : baseValue - delta));
                }
                else
                {
                    left = Combine(left, right, op);
                }
            }
            return left;
        }

        // Adding is only defined for the same unit (or for plain numbers).
        private static Val Combine(Val l, Val r, string op)
        {
            if (l.Unit is not null || r.Unit is not null)
            {
                if (l.Unit is null || r.Unit is null || !ReferenceEquals(l.Unit, r.Unit)) throw new CalcException("unit mismatch");
                return new Val(Finite(op == "+" ? Num(l) + Num(r) : Num(l) - Num(r)), l.Unit);
            }
            return new Val(Finite(op == "+" ? Num(l) + Num(r) : Num(l) - Num(r)));
        }

        // term := unary (('*' | '/' | '%' | 'mod' | 'of' | implicit) unary)*
        private Val ParseTerm()
        {
            var left = ParseUnary();
            while (true)
            {
                var t = Peek();
                if (t.Kind == TokenKind.Operator && t.Text is "*" or "/" or "%")
                {
                    Advance();
                    var right = ParseUnary();
                    _ops++;
                    left = t.Text switch
                    {
                        "*" => Scale(left, right, Num(left) * Num(right)),
                        "/" => Scale(left, right, Divide(Num(left), Num(right))),
                        _ => Scale(left, right, Modulo(Num(left), Num(right))),
                    };
                }
                else if (t.Kind == TokenKind.Identifier && t.Text == "mod" && PeekAt(1).Kind != TokenKind.LParen)
                {
                    Advance();
                    var right = ParseUnary();
                    _ops++;
                    left = Scale(left, right, Modulo(Num(left), Num(right)));
                }
                else if (t.Kind == TokenKind.Identifier && t.Text == "of")
                {
                    // "20% of 50": the percent is already normalised by Num().
                    Advance();
                    var right = ParseUnary();
                    _ops++;
                    left = Scale(left, right, Num(left) * Num(right));
                }
                else if (t.Kind == TokenKind.LParen || (t.Kind == TokenKind.Identifier && StartsImplicitFactor(t, 1)))
                {
                    // Implicit multiplication: "2(3+4)", "2pi", "3sqrt(16)".
                    var right = ParseUnary();
                    _ops++;
                    left = Scale(left, right, Num(left) * Num(right));
                }
                else
                {
                    return left;
                }
            }
        }

        private bool StartsImplicitFactor(Token t, int offset)
        {
            if (t.Kind != TokenKind.Identifier) return false;
            if (Constants.ContainsKey(t.Text)) return true;
            return Lexer.IsFunction(t.Text) && PeekAt(offset).Kind == TokenKind.LParen;
        }

        /// <summary>Multiplying or dividing keeps a unit when exactly one side has one ("5 km * 2"); two units are an error.</summary>
        private static Val Scale(Val left, Val right, double value)
        {
            if (left.Unit is not null && right.Unit is not null) throw new CalcException("unit times unit");
            return new Val(Finite(value), left.Unit ?? right.Unit);
        }

        private static double Divide(double a, double b)
        {
            if (b == 0) throw new CalcException("division by zero");
            return a / b;
        }

        private static double Modulo(double a, double b)
        {
            if (b == 0) throw new CalcException("division by zero");
            return a - b * Math.Floor(a / b);
        }

        // unary := ('-' | '+') unary | power
        private Val ParseUnary()
        {
            if (IsOperator("-"))
            {
                Advance();
                Enter();
                var v = ParseUnary();
                Leave();
                return new Val(-Num(v), v.Unit);
            }
            if (IsOperator("+"))
            {
                Advance();
                return ParseUnary();
            }
            return ParsePower();
        }

        // power := postfix ('^' unary)?   (right associative; the exponent may carry a sign: 2^-1)
        private Val ParsePower()
        {
            var b = ParsePostfix();
            if (!IsOperator("^")) return b;
            Advance();
            Enter();
            var e = ParseUnary();
            Leave();
            _ops++;
            if (b.Unit is not null || e.Unit is not null) throw new CalcException("power of a unit");
            return new Val(Finite(Math.Pow(Num(b), Num(e))));
        }

        // postfix := primary ('deg' | '%')?
        private Val ParsePostfix()
        {
            var v = ParsePrimary();
            if (Peek() is { Kind: TokenKind.Identifier, Text: "deg" })
            {
                Advance();
                if (v.Unit is not null) throw new CalcException("degrees of a unit");
                _ops++;
                return new Val(Finite(Num(v) * Math.PI / 180.0));
            }
            if (IsOperator("%") && IsPercentPostfix(PeekAt(1)))
            {
                Advance();
                if (v.Unit is not null) throw new CalcException("percent of a unit");
                _ops++;
                return new Val(v.V, null, true); // raw percent number; normalised by Num()
            }
            return v;
        }

        /// <summary>A '%' is a percent sign when nothing that needs an operand follows it; otherwise it is the modulo operator ("10 % 3").</summary>
        private static bool IsPercentPostfix(Token next) => next.Kind switch
        {
            TokenKind.End or TokenKind.RParen or TokenKind.Comma => true,
            TokenKind.Operator => next.Text is "+" or "-" or "*" or "/" or "^",
            TokenKind.Identifier => next.Text == "of",
            _ => false,
        };

        // primary := number [unit] | '(' additive ')' | constant | function '(' args ')'
        private Val ParsePrimary()
        {
            var t = Peek();
            switch (t.Kind)
            {
                case TokenKind.Number:
                {
                    Advance();
                    return new Val(t.Number, ConsumeUnitAfterNumber());
                }
                case TokenKind.LParen:
                {
                    Advance();
                    Enter();
                    var inner = ParseAdditive();
                    Leave();
                    if (Peek().Kind != TokenKind.RParen) throw new CalcException("expected )");
                    Advance();
                    return inner;
                }
                case TokenKind.Identifier when PeekAt(1).Kind == TokenKind.LParen:
                    return ParseCall();
                case TokenKind.Identifier when Constants.TryGetValue(t.Text, out var constant):
                    Advance();
                    _ops++;
                    return new Val(constant);
                default:
                    throw new CalcException("unexpected token");
            }
        }

        /// <summary>"5 km" attaches the unit to the number. "in" is the conversion keyword when a unit follows it ("5 km in mi"), otherwise inches ("5 in").</summary>
        private Unit? ConsumeUnitAfterNumber()
        {
            var t = Peek();
            if (t.Kind != TokenKind.Identifier || !UnitTable.TryGet(t.Text, out var unit)) return null;
            if (t.Text == "in" && PeekAt(1).Kind == TokenKind.Identifier && UnitTable.TryGet(PeekAt(1).Text, out _)) return null;
            Advance();
            return unit;
        }

        private Val ParseCall()
        {
            var name = Advance().Text;
            Advance(); // '('
            Enter();
            var args = new List<Val>();
            if (Peek().Kind != TokenKind.RParen)
            {
                while (true)
                {
                    if (args.Count >= MaxArguments) throw new CalcException("too many arguments");
                    args.Add(ParseAdditive());
                    if (Peek().Kind != TokenKind.Comma) break;
                    Advance();
                }
            }
            if (Peek().Kind != TokenKind.RParen) throw new CalcException("expected )");
            Advance();
            Leave();
            _ops++;
            return Apply(name, args);
        }

        private static Val Apply(string name, List<Val> args)
        {
            foreach (var a in args) if (a.Unit is not null) throw new CalcException("function of a unit");
            var x = args.Select(Num).ToArray();

            double result;
            switch (name)
            {
                case "min" when x.Length >= 1: result = x.Min(); break;
                case "max" when x.Length >= 1: result = x.Max(); break;
                case "round" when x.Length == 1: result = Math.Round(x[0], MidpointRounding.AwayFromZero); break;
                case "round" when x.Length == 2:
                {
                    double places = Math.Round(x[1]);
                    if (places < 0 || places > 15) throw new CalcException("rounding places out of range");
                    result = Math.Round(x[0], (int)places, MidpointRounding.AwayFromZero);
                    break;
                }
                case "pow" when x.Length == 2: result = Math.Pow(x[0], x[1]); break;
                case "mod" when x.Length == 2: result = Modulo(x[0], x[1]); break;
                case "sqrt" when x.Length == 1: result = Math.Sqrt(x[0]); break;
                case "cbrt" when x.Length == 1: result = Math.Cbrt(x[0]); break;
                case "abs" when x.Length == 1: result = Math.Abs(x[0]); break;
                case "floor" when x.Length == 1: result = Math.Floor(x[0]); break;
                case "ceil" when x.Length == 1: result = Math.Ceiling(x[0]); break;
                case "sin" when x.Length == 1: result = Math.Sin(x[0]); break;
                case "cos" when x.Length == 1: result = Math.Cos(x[0]); break;
                case "tan" when x.Length == 1: result = Math.Tan(x[0]); break;
                case "asin" when x.Length == 1: result = Math.Asin(x[0]); break;
                case "acos" when x.Length == 1: result = Math.Acos(x[0]); break;
                case "atan" when x.Length == 1: result = Math.Atan(x[0]); break;
                case "ln" when x.Length == 1: result = Math.Log(x[0]); break;
                case "log" or "log10" when x.Length == 1: result = Math.Log10(x[0]); break;
                case "log2" when x.Length == 1: result = Math.Log2(x[0]); break;
                case "exp" when x.Length == 1: result = Math.Exp(x[0]); break;
                default: throw new CalcException("unknown function or wrong argument count");
            }
            return new Val(Finite(result));
        }
    }
}
