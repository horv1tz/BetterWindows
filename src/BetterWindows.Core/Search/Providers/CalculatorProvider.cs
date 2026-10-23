using BetterWindows.Core.Protocol;
using BetterWindows.Core.Search.Calculator;
using BetterWindows.Core.Services;

namespace BetterWindows.Core.Search.Providers;

/// <summary>
/// The calculator row: when the query is a complete expression that performs an operation ("2+2", "20% of 50", "5 km in mi") it yields one result titled
/// "= 4" whose primary action copies the value. Pure; no I/O.
/// </summary>
public sealed class CalculatorProvider : ISearchProvider
{
    public SearchProviderId Id => SearchProviderId.Calc;
    public TimeSpan Timeout => TimeSpan.FromMilliseconds(200);

    public Task<IReadOnlyList<SearchHit>> QueryAsync(SearchQuery query, CancellationToken ct)
    {
        if (!ExpressionParser.TryEvaluate(query.Text, out var result)) return Task.FromResult<IReadOnlyList<SearchHit>>(Array.Empty<SearchHit>());

        var number = NumberText.Format(result.Value, query.Locale);
        var shown = result.UnitSymbol is null ? number : number + " " + result.UnitSymbol;
        var expression = InputPolicy.Truncate(query.Text.Trim(), 120);
        var hit = new SearchHit(
            Id,
            Title: "= " + shown,
            Subtitle: expression,
            IconUrl: null,
            Score: 1.0,
            Actions: new[] { new HitAction("copy", Loc.Pick(query.Locale, "Copy result", "Скопировать результат"), true, new CopyTextOperation(shown)) },
            Preview: new SearchPreview { Kind = SearchPreviewKind.Text, Text = InputPolicy.Truncate(expression + " = " + shown, 200) });
        return Task.FromResult<IReadOnlyList<SearchHit>>(new[] { hit });
    }
}
