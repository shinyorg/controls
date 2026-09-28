using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Shiny.Controls.Office.Spreadsheet.Calc;

/// <summary>
/// The <c>*IF</c> family and the criteria language they share.
/// </summary>
/// <remarks>
/// A criterion is not a value — <c>"&gt;5"</c>, <c>"&lt;&gt;"</c>, <c>"app*"</c> and <c>"5"</c> are four
/// different things, and the wildcard forms use <c>*</c> and <c>?</c> rather than regex. Sharing one
/// matcher across SUMIF/COUNTIF/AVERAGEIF is what keeps them consistent with each other.
/// </remarks>
static class ConditionalFunctions
{
    public static void Register(FunctionRegistry registry)
    {
        registry.Add("COUNTIF", 2, 2, a =>
        {
            var range = a.Value(0).Flatten().ToList();
            var predicate = Parse(a.Scalar(1));
            return CalcValue.From((double)range.Count(predicate));
        });

        registry.Add("COUNTIFS", 2, CalcFunction.Unlimited, a =>
        {
            var count = 0;
            foreach (var index in MatchingIndexes(a, 0))
                count++;

            return CalcValue.From((double)count);
        });

        registry.Add("AVERAGEIF", 2, 3, a =>
        {
            var (values, total, count) = Accumulate(a);
            _ = values;
            return count == 0 ? CalcValue.Error(CellError.Div0) : CalcValue.From(total / count);
        });
    }

    public static CalcValue SumIf(CalcArguments a)
    {
        var (_, total, _) = Accumulate(a);
        return CalcValue.From(total);
    }

    public static CalcValue SumIfs(CalcArguments a)
    {
        var sumRange = a.Value(0).Flatten().ToList();
        var total = 0d;

        foreach (var index in MatchingIndexes(a, 1))
        {
            if (index >= sumRange.Count)
                continue;

            var value = sumRange[index];
            if (value.IsError)
                throw new CalcErrorException(value.AsError());

            if (value.Kind == CellValueKind.Number)
                total += value.AsNumber();
        }

        return CalcValue.From(total);
    }

    /// <summary>SUMIF/AVERAGEIF share a shape: criteria range, criterion, then an optional separate value range.</summary>
    static (List<CellValue> Values, double Total, int Count) Accumulate(CalcArguments a)
    {
        var criteriaRange = a.Value(0).Flatten().ToList();
        var predicate = Parse(a.Scalar(1));
        var valueRange = a.IsMissing(2) ? criteriaRange : a.Value(2).Flatten().ToList();

        var total = 0d;
        var count = 0;

        for (var i = 0; i < criteriaRange.Count; i++)
        {
            if (!predicate(criteriaRange[i]))
                continue;

            if (i >= valueRange.Count)
                continue;

            var value = valueRange[i];
            if (value.IsError)
                throw new CalcErrorException(value.AsError());

            if (value.Kind != CellValueKind.Number)
                continue;

            total += value.AsNumber();
            count++;
        }

        return (valueRange, total, count);
    }

    /// <summary>Indexes satisfying every (range, criterion) pair starting at <paramref name="start"/>.</summary>
    internal static IEnumerable<int> MatchingIndexes(CalcArguments a, int start)
    {
        var ranges = new List<List<CellValue>>();
        var predicates = new List<Func<CellValue, bool>>();

        for (var i = start; i + 1 < a.Count; i += 2)
        {
            ranges.Add(a.Value(i).Flatten().ToList());
            predicates.Add(Parse(a.Scalar(i + 1)));
        }

        if (ranges.Count == 0)
            yield break;

        var length = ranges[0].Count;
        if (ranges.Any(x => x.Count != length))
            throw new CalcErrorException(CellError.Value);

        for (var i = 0; i < length; i++)
        {
            var matched = true;
            for (var r = 0; r < ranges.Count; r++)
            {
                if (predicates[r](ranges[r][i]))
                    continue;

                matched = false;
                break;
            }

            if (matched)
                yield return i;
        }
    }

    /// <summary>Compiles a criterion into a predicate.</summary>
    public static Func<CellValue, bool> Parse(CellValue criterion)
    {
        if (criterion.IsError)
        {
            var error = criterion.AsError();
            return value => value.IsError && value.AsError() == error;
        }

        if (criterion.Kind != CellValueKind.Text)
            return value => Equal(value, criterion);

        var text = criterion.AsText();
        var (op, operand) = SplitOperator(text);

        if (op == "=" && operand.Length == 0)
            return value => value.IsBlank;

        if (op == "<>" && operand.Length == 0)
            return value => !value.IsBlank;

        // A numeric operand compares numerically; anything else compares as text.
        var isNumber = double.TryParse(operand, NumberStyles.Float, CultureInfo.CurrentCulture, out var number) ||
                       double.TryParse(operand, NumberStyles.Float, CultureInfo.InvariantCulture, out number);

        var target = isNumber ? CellValue.FromNumber(number) : CellValue.FromText(operand);

        if (op is "=" or "<>" && !isNumber && ContainsWildcard(operand))
        {
            var pattern = WildcardToRegex(operand);
            var wanted = op == "=";
            return value => (value.Kind == CellValueKind.Text && pattern.IsMatch(value.AsText())) == wanted;
        }

        return op switch
        {
            "=" => value => Equal(value, target),
            "<>" => value => !Equal(value, target),
            ">" => value => Comparable(value, target) && Coercion.Compare(value, target) > 0,
            ">=" => value => Comparable(value, target) && Coercion.Compare(value, target) >= 0,
            "<" => value => Comparable(value, target) && Coercion.Compare(value, target) < 0,
            "<=" => value => Comparable(value, target) && Coercion.Compare(value, target) <= 0,
            _ => value => Equal(value, target)
        };
    }

    static bool Comparable(CellValue value, CellValue target)
        => !value.IsBlank && !value.IsError && value.Kind == target.Kind;

    static bool Equal(CellValue value, CellValue target)
    {
        if (Coercion.EqualityWithBlank(value, target, out var areEqual))
            return areEqual;

        if (value.Kind == CellValueKind.Text && target.Kind == CellValueKind.Text)
            return string.Equals(value.AsText(), target.AsText(), StringComparison.OrdinalIgnoreCase);

        if (value.IsError || target.IsError)
            return value.IsError && target.IsError && value.AsError() == target.AsError();

        return value.Kind == target.Kind && Coercion.Compare(value, target) == 0;
    }

    static (string Operator, string Operand) SplitOperator(string text)
    {
        foreach (var op in new[] { "<=", ">=", "<>" })
        {
            if (text.StartsWith(op, StringComparison.Ordinal))
                return (op, text[op.Length..]);
        }

        if (text.Length > 0 && text[0] is '<' or '>' or '=')
            return (text[..1], text[1..]);

        return ("=", text);
    }

    static bool ContainsWildcard(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '~')
            {
                i++;
                continue;
            }

            if (text[i] is '*' or '?')
                return true;
        }

        return false;
    }

    /// <summary>Translates Excel wildcards to a regex. <c>~</c> escapes the next wildcard character.</summary>
    public static Regex WildcardToRegex(string pattern)
    {
        var builder = new StringBuilder("^");
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '~' && i + 1 < pattern.Length && pattern[i + 1] is '*' or '?' or '~')
            {
                builder.Append(Regex.Escape(pattern[i + 1].ToString()));
                i++;
                continue;
            }

            builder.Append(c switch
            {
                '*' => ".*",
                '?' => ".",
                _ => Regex.Escape(c.ToString())
            });
        }

        builder.Append('$');
        return new Regex(builder.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
