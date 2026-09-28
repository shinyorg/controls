namespace Shiny.Controls.Office.Spreadsheet.Calc;

/// <summary>
/// XLOOKUP and XMATCH, SUBTOTAL and AGGREGATE, the *IFS aggregates, and the financial functions.
/// </summary>
/// <remarks>
/// <para>
/// Dynamic arrays are deliberately not here. UNIQUE, SORT and FILTER return a block that "spills" into
/// the cells below the formula, and that needs a grid that can hold a value it was not asked to store —
/// the spill range, its <c>#SPILL!</c> collisions and the <c>A1#</c> reference form. The engine computes
/// one value per formula cell; an XLOOKUP whose return array has several columns returns the first.
/// </para>
/// </remarks>
static class ModernFunctions
{
    public static void Register(FunctionRegistry registry)
    {
        registry.Add("XLOOKUP", 3, 6, XLookup);
        registry.Add("XMATCH", 2, 4, a =>
        {
            var index = Find(a.Checked(0), a.Value(1).Flatten().ToList(), a.IntegerOrDefault(2, 0), a.IntegerOrDefault(3, 1));
            return index < 0 ? CalcValue.Error(CellError.NotAvailable) : CalcValue.From((double)(index + 1));
        });

        registry.Add("SUBTOTAL", 2, CalcFunction.Unlimited, Subtotal);
        registry.Add("AGGREGATE", 3, CalcFunction.Unlimited, Aggregate);

        registry.Add("AVERAGEIFS", 3, CalcFunction.Unlimited, a =>
        {
            var numbers = IfsNumbers(a).ToList();
            return numbers.Count == 0 ? CalcValue.Error(CellError.Div0) : CalcValue.From(numbers.Average());
        });

        registry.Add("MAXIFS", 3, CalcFunction.Unlimited, a =>
        {
            var numbers = IfsNumbers(a).ToList();
            return CalcValue.From(numbers.Count == 0 ? 0 : numbers.Max());
        });

        registry.Add("MINIFS", 3, CalcFunction.Unlimited, a =>
        {
            var numbers = IfsNumbers(a).ToList();
            return CalcValue.From(numbers.Count == 0 ? 0 : numbers.Min());
        });

        // The dotted names Excel 2010 introduced, which newer files write in place of the old ones.
        foreach (var (alias, target) in new[] { ("STDEV.S", "STDEV"), ("STDEV.P", "STDEVP"), ("VAR.S", "VAR"), ("VAR.P", "VARP"), ("RANK.EQ", "RANK") })
        {
            if (registry.TryGet(target, out var function))
                registry.Add(function with { Name = alias });
        }

        FinancialFunctions.Register(registry);
    }

    // ---- XLOOKUP / XMATCH ----

    static CalcValue XLookup(CalcArguments a)
    {
        var needle = a.Checked(0);
        var lookup = a.AsArray(1);
        var results = a.AsArray(2);

        var keys = a.Value(1).Flatten().ToList();
        var index = Find(needle, keys, a.IntegerOrDefault(4, 0), a.IntegerOrDefault(5, 1));

        if (index < 0)
            return a.IsMissing(3) ? CalcValue.Error(CellError.NotAvailable) : a.Value(3);

        if (results is null)
            return index == 0 ? CalcValue.From(a.Scalar(2)) : CalcValue.Error(CellError.Value);

        // The return array runs the same way as the lookup array; take the row (or column) at the index.
        var vertical = lookup is null || lookup.ColumnCount == 1;
        if (vertical)
            return index < results.RowCount ? CalcValue.From(results[index, 0]) : CalcValue.Error(CellError.Value);

        return index < results.ColumnCount ? CalcValue.From(results[0, index]) : CalcValue.Error(CellError.Value);
    }

    /// <summary>
    /// XMATCH's search: the index of <paramref name="needle"/> in <paramref name="values"/>, or -1.
    /// </summary>
    /// <param name="matchMode">0 exact, -1 exact or next smaller, 1 exact or next larger, 2 wildcard.</param>
    /// <param name="searchMode">1 first-to-last, -1 last-to-first; 2 and -2 (binary) are searched linearly.</param>
    internal static int Find(CellValue needle, IReadOnlyList<CellValue> values, int matchMode, int searchMode)
    {
        var order = searchMode < 0
            ? Enumerable.Range(0, values.Count).Reverse()
            : Enumerable.Range(0, values.Count);

        if (matchMode == 2)
        {
            var wildcard = ConditionalFunctions.Parse(needle);
            foreach (var i in order)
            {
                if (wildcard(values[i]))
                    return i;
            }

            return -1;
        }

        var best = -1;
        foreach (var i in order)
        {
            var candidate = values[i];
            if (candidate.IsBlank)
                continue;

            if (Coercion.EqualityWithBlank(candidate, needle, out var equal) ? equal : Equal(candidate, needle))
                return i;

            if (matchMode == 0 || !Comparable(candidate, needle))
                continue;

            var comparison = Coercion.Compare(candidate, needle);
            if (matchMode == -1 && comparison < 0 && (best < 0 || Coercion.Compare(candidate, values[best]) > 0))
                best = i;
            else if (matchMode == 1 && comparison > 0 && (best < 0 || Coercion.Compare(candidate, values[best]) < 0))
                best = i;
        }

        return best;

        static bool Equal(CellValue x, CellValue y)
            => x.Kind == y.Kind && (x.Kind == CellValueKind.Text
                ? string.Equals(x.AsText(), y.AsText(), StringComparison.OrdinalIgnoreCase)
                : x == y);

        static bool Comparable(CellValue x, CellValue y)
            => (x.Kind == CellValueKind.Number) == (y.Kind == CellValueKind.Number);
    }

    // ---- SUBTOTAL / AGGREGATE ----

    /// <summary>
    /// The values a SUBTOTAL or AGGREGATE argument contributes, read cell by cell so the ones to skip —
    /// other subtotals, hidden rows, errors — can be told apart from the rest.
    /// </summary>
    static IEnumerable<CellValue> Cells(CalcArguments a, int index, bool skipHidden, bool skipErrors)
    {
        var context = a.Context;

        IEnumerable<(string? Sheet, CellRef Cell)> Addresses()
        {
            switch (a.Node(index))
            {
                case RangeNode range:
                    foreach (var cell in range.Range.Cells())
                        yield return (range.Sheet, cell);

                    break;

                case ReferenceNode reference:
                    yield return (reference.Sheet, reference.Cell);
                    break;
            }
        }

        var node = a.Node(index);
        if (node is not (RangeNode or ReferenceNode))
        {
            foreach (var value in a.Value(index).Flatten())
            {
                if (skipErrors && value.IsError)
                    continue;

                yield return value;
            }

            yield break;
        }

        foreach (var (sheet, cell) in Addresses())
        {
            if (skipHidden && context.IsRowHidden(sheet, cell.Row))
                continue;

            // A subtotal inside the range is already counted by the rows it totals; counting it again is
            // exactly the double-count SUBTOTAL exists to avoid.
            if (context.GetFormula(sheet, cell) is { } formula &&
                (formula.Contains("SUBTOTAL(", StringComparison.OrdinalIgnoreCase) || formula.Contains("AGGREGATE(", StringComparison.OrdinalIgnoreCase)))
                continue;

            var value = context.GetValue(sheet, cell);
            if (skipErrors && value.IsError)
                continue;

            yield return value;
        }
    }

    static CalcValue Subtotal(CalcArguments a)
    {
        var code = a.Integer(0);
        var skipHidden = code > 100;
        var function = skipHidden ? code - 100 : code;

        if (function is < 1 or > 11)
            return CalcValue.Error(CellError.Value);

        var values = new List<CellValue>();
        for (var i = 1; i < a.Count; i++)
            values.AddRange(Cells(a, i, skipHidden, skipErrors: false));

        return Apply(function, values, k: null);
    }

    static CalcValue Aggregate(CalcArguments a)
    {
        var function = a.Integer(0);
        var options = a.Integer(1);

        if (function is < 1 or > 19 || options is < 0 or > 7)
            return CalcValue.Error(CellError.Value);

        var skipHidden = options is 1 or 3 or 5 or 7;
        var skipErrors = options is 2 or 3 or 6 or 7;

        // The array forms (14-19) take the array and then k; the rest take any number of references.
        if (function >= 14)
        {
            var values = Cells(a, 2, skipHidden, skipErrors).ToList();
            var k = a.Number(3);
            return Apply(function, values, k);
        }

        var all = new List<CellValue>();
        for (var i = 2; i < a.Count; i++)
            all.AddRange(Cells(a, i, skipHidden, skipErrors));

        return Apply(function, all, k: null);
    }

    /// <summary>The function numbers SUBTOTAL and AGGREGATE share, applied to a list of cell values.</summary>
    static CalcValue Apply(int function, IReadOnlyList<CellValue> values, double? k)
    {
        foreach (var value in values)
        {
            if (value.IsError && function is not (2 or 3))
                return CalcValue.From(value);
        }

        var numbers = values.Where(x => x.Kind == CellValueKind.Number).Select(x => x.AsNumber()).ToList();

        double Variance(bool sample)
        {
            var divisor = sample ? numbers.Count - 1 : numbers.Count;
            if (divisor <= 0)
                throw new CalcErrorException(CellError.Div0);

            var mean = numbers.Average();
            return numbers.Sum(x => (x - mean) * (x - mean)) / divisor;
        }

        try
        {
            return function switch
            {
                1 => numbers.Count == 0 ? CalcValue.Error(CellError.Div0) : CalcValue.From(numbers.Average()),
                2 => CalcValue.From((double)numbers.Count),
                3 => CalcValue.From((double)values.Count(x => !x.IsBlank)),
                4 => CalcValue.From(numbers.Count == 0 ? 0 : numbers.Max()),
                5 => CalcValue.From(numbers.Count == 0 ? 0 : numbers.Min()),
                6 => CalcValue.From(numbers.Count == 0 ? 0 : numbers.Aggregate(1d, (x, y) => x * y)),
                7 => CalcValue.From(Math.Sqrt(Variance(sample: true))),
                8 => CalcValue.From(Math.Sqrt(Variance(sample: false))),
                9 => CalcValue.From(numbers.Sum()),
                10 => CalcValue.From(Variance(sample: true)),
                11 => CalcValue.From(Variance(sample: false)),
                12 => numbers.Count == 0 ? CalcValue.Error(CellError.Num) : CalcValue.From(Percentile(numbers, 0.5, inclusive: true)),
                13 => Mode(numbers),
                14 => Nth(numbers, (int)(k ?? 1), largest: true),
                15 => Nth(numbers, (int)(k ?? 1), largest: false),
                16 => CalcValue.From(Percentile(numbers, k ?? 0, inclusive: true)),
                17 => CalcValue.From(Percentile(numbers, (k ?? 0) / 4, inclusive: true)),
                18 => CalcValue.From(Percentile(numbers, k ?? 0, inclusive: false)),
                19 => CalcValue.From(Percentile(numbers, (k ?? 0) / 4, inclusive: false)),
                _ => CalcValue.Error(CellError.Value)
            };
        }
        catch (CalcErrorException ex)
        {
            return CalcValue.Error(ex.Error);
        }
    }

    static CalcValue Mode(List<double> numbers)
    {
        var groups = numbers.GroupBy(x => x).Where(g => g.Count() > 1).ToList();
        if (groups.Count == 0)
            return CalcValue.Error(CellError.NotAvailable);

        var top = groups.Max(g => g.Count());

        // Ties go to the value that appears first, as Excel's MODE does.
        return CalcValue.From(numbers.First(x => groups.Any(g => g.Key == x && g.Count() == top)));
    }

    static CalcValue Nth(List<double> numbers, int k, bool largest)
    {
        if (k < 1 || k > numbers.Count)
            return CalcValue.Error(CellError.Num);

        var sorted = numbers.OrderBy(x => x).ToList();
        return CalcValue.From(largest ? sorted[^k] : sorted[k - 1]);
    }

    static double Percentile(List<double> numbers, double p, bool inclusive)
    {
        if (numbers.Count == 0 || p < 0 || p > 1)
            throw new CalcErrorException(CellError.Num);

        var sorted = numbers.OrderBy(x => x).ToList();
        var n = sorted.Count;
        var rank = inclusive ? p * (n - 1) : p * (n + 1) - 1;

        if (rank < 0 || rank > n - 1)
            throw new CalcErrorException(CellError.Num);

        var low = (int)Math.Floor(rank);
        var high = Math.Min(n - 1, low + 1);
        return sorted[low] + (sorted[high] - sorted[low]) * (rank - low);
    }

    /// <summary>The numbers of the first range whose rows satisfy every (range, criterion) pair after it.</summary>
    static IEnumerable<double> IfsNumbers(CalcArguments a)
    {
        var values = a.Value(0).Flatten().ToList();

        foreach (var index in ConditionalFunctions.MatchingIndexes(a, 1))
        {
            if (index >= values.Count)
                continue;

            var value = values[index];
            if (value.IsError)
                throw new CalcErrorException(value.AsError());

            if (value.Kind == CellValueKind.Number)
                yield return value.AsNumber();
        }
    }
}

/// <summary>Time value of money: the annuity functions, NPV and IRR.</summary>
/// <remarks>
/// Sign convention as Excel's: money paid out is negative, money received positive — so PMT on a loan
/// taken (a positive present value) comes back negative.
/// </remarks>
static class FinancialFunctions
{
    public static void Register(FunctionRegistry registry)
    {
        registry.Add("PMT", 3, 5, a => Result(Pmt(a.Number(0), a.Number(1), a.Number(2), a.NumberOrDefault(3, 0), a.NumberOrDefault(4, 0))));
        registry.Add("FV", 3, 5, a => Result(Fv(a.Number(0), a.Number(1), a.Number(2), a.NumberOrDefault(3, 0), a.NumberOrDefault(4, 0))));
        registry.Add("PV", 3, 5, a => Result(Pv(a.Number(0), a.Number(1), a.Number(2), a.NumberOrDefault(3, 0), a.NumberOrDefault(4, 0))));
        registry.Add("NPER", 3, 5, a => Result(Nper(a.Number(0), a.Number(1), a.Number(2), a.NumberOrDefault(3, 0), a.NumberOrDefault(4, 0))));
        registry.Add("RATE", 3, 6, a => Result(Rate(a.Number(0), a.Number(1), a.Number(2), a.NumberOrDefault(3, 0), a.NumberOrDefault(4, 0), a.NumberOrDefault(5, 0.1))));

        registry.Add("IPMT", 4, 6, a =>
        {
            var (rate, per, nper, pv, fv, type) = (a.Number(0), a.Number(1), a.Number(2), a.Number(3), a.NumberOrDefault(4, 0), a.NumberOrDefault(5, 0));
            return Result(Ipmt(rate, per, nper, pv, fv, type));
        });

        registry.Add("PPMT", 4, 6, a =>
        {
            var (rate, per, nper, pv, fv, type) = (a.Number(0), a.Number(1), a.Number(2), a.Number(3), a.NumberOrDefault(4, 0), a.NumberOrDefault(5, 0));
            return Result(Pmt(rate, nper, pv, fv, type) - Ipmt(rate, per, nper, pv, fv, type));
        });

        registry.Add("SLN", 3, 3, a =>
        {
            var life = a.Number(2);
            return life == 0 ? CalcValue.Error(CellError.Div0) : CalcValue.From((a.Number(0) - a.Number(1)) / life);
        });

        registry.Add("NPV", 2, CalcFunction.Unlimited, a =>
        {
            var rate = a.Number(0);
            var total = 0d;
            var period = 1;

            for (var i = 1; i < a.Count; i++)
            {
                foreach (var value in a.Value(i).Flatten())
                {
                    if (value.IsError)
                        return CalcValue.From(value);

                    // In a range only numbers count; a literal argument is coerced.
                    if (value.Kind != CellValueKind.Number)
                    {
                        if (a.AsArray(i) is not null || !Coercion.TryToNumber(value, out _, out _))
                            continue;
                    }

                    Coercion.TryToNumber(value, out var number, out _);
                    total += number / Math.Pow(1 + rate, period++);
                }
            }

            return CalcValue.From(total);
        });

        registry.Add("IRR", 1, 2, a =>
        {
            var flows = a.Value(0).Flatten().Where(x => x.Kind == CellValueKind.Number).Select(x => x.AsNumber()).ToList();
            if (!flows.Any(x => x > 0) || !flows.Any(x => x < 0))
                return CalcValue.Error(CellError.Num);

            var rate = a.NumberOrDefault(1, 0.1);
            for (var iteration = 0; iteration < 100; iteration++)
            {
                double npv = 0, derivative = 0;
                for (var t = 0; t < flows.Count; t++)
                {
                    var factor = Math.Pow(1 + rate, t);
                    npv += flows[t] / factor;
                    derivative -= t * flows[t] / (factor * (1 + rate));
                }

                if (derivative == 0)
                    break;

                var next = rate - npv / derivative;
                if (Math.Abs(next - rate) < 1e-10)
                    return CalcValue.From(next);

                rate = next;
            }

            return CalcValue.Error(CellError.Num);
        });
    }

    static CalcValue Result(double value)
        => double.IsNaN(value) || double.IsInfinity(value) ? CalcValue.Error(CellError.Num) : CalcValue.From(value);

    internal static double Pmt(double rate, double nper, double pv, double fv, double type)
    {
        if (rate == 0)
            return -(pv + fv) / nper;

        var growth = Math.Pow(1 + rate, nper);
        return -(rate * (pv * growth + fv)) / ((1 + rate * (type != 0 ? 1 : 0)) * (growth - 1));
    }

    internal static double Fv(double rate, double nper, double pmt, double pv, double type)
    {
        if (rate == 0)
            return -(pv + pmt * nper);

        var growth = Math.Pow(1 + rate, nper);
        return -(pv * growth + pmt * (1 + rate * (type != 0 ? 1 : 0)) * (growth - 1) / rate);
    }

    internal static double Pv(double rate, double nper, double pmt, double fv, double type)
    {
        if (rate == 0)
            return -(fv + pmt * nper);

        var growth = Math.Pow(1 + rate, nper);
        return -(fv + pmt * (1 + rate * (type != 0 ? 1 : 0)) * (growth - 1) / rate) / growth;
    }

    internal static double Nper(double rate, double pmt, double pv, double fv, double type)
    {
        if (rate == 0)
            return pmt == 0 ? double.NaN : -(pv + fv) / pmt;

        var adjusted = pmt * (1 + rate * (type != 0 ? 1 : 0)) / rate;
        return Math.Log((adjusted - fv) / (adjusted + pv)) / Math.Log(1 + rate);
    }

    static double Ipmt(double rate, double per, double nper, double pv, double fv, double type)
    {
        if (per < 1 || per > nper)
            return double.NaN;

        var pmt = Pmt(rate, nper, pv, fv, type);

        if (type != 0 && per == 1)
            return 0;

        // Interest is charged on the balance left after the previous period.
        var balance = Fv(rate, type != 0 ? per - 2 : per - 1, pmt, pv, type);
        if (type != 0)
            balance -= pmt;

        return balance * rate;
    }

    /// <summary>RATE by Newton's method from the guess, falling back to bisection when it wanders.</summary>
    internal static double Rate(double nper, double pmt, double pv, double fv, double type, double guess)
    {
        double F(double r) => r == 0
            ? pv + pmt * nper + fv
            : pv * Math.Pow(1 + r, nper) + pmt * (1 + r * (type != 0 ? 1 : 0)) * (Math.Pow(1 + r, nper) - 1) / r + fv;

        var rate = guess;
        for (var i = 0; i < 100; i++)
        {
            var value = F(rate);
            var step = 1e-7;
            var slope = (F(rate + step) - value) / step;

            if (slope == 0 || double.IsNaN(slope))
                break;

            var next = rate - value / slope;
            if (Math.Abs(next - rate) < 1e-10)
                return next;

            rate = next;
        }

        return double.NaN;
    }
}
