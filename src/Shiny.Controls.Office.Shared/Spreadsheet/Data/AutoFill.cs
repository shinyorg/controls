using System.Globalization;
using System.Text.RegularExpressions;
using Shiny.Controls.Office.Editing;
using Shiny.Controls.Office.Spreadsheet.Calc;
using Shiny.Controls.Office.Spreadsheet.Commands;

namespace Shiny.Controls.Office.Spreadsheet;

/// <summary>
/// What dragging the fill handle writes: copies, series, and rebased formulas.
/// </summary>
/// <remarks>
/// <para>
/// Each line along the fill — a column when filling down, a row when filling right — is read on its
/// own, and extended by the first rule that fits it:
/// </para>
/// <list type="bullet">
/// <item>two or more numbers extend as the straight line through them (1, 3 → 5, 7, 9);</item>
/// <item>a single date steps a day at a time;</item>
/// <item>weekday and month names continue round the week or the year, keeping their spelling;</item>
/// <item>text ending in a number counts on from it ("Q1" → "Q2");</item>
/// <item>anything else — including every formula — repeats, with formulas rebased the way a copy is.</item>
/// </list>
/// <para>
/// A single plain number repeats rather than counting, which is Excel's rule and not an oversight:
/// counting needs two to say by how much.
/// </para>
/// </remarks>
public static partial class AutoFill
{
    static readonly string[][] Lists =
    [
        ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"],
        ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"],
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"],
        ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"]
    ];

    [GeneratedRegex(@"^(.*?)(\d+)(\D*)$")]
    private static partial Regex TrailingNumber();

    /// <summary>
    /// The command that fills <paramref name="target"/> from <paramref name="source"/>, or null when the
    /// target adds nothing to the source.
    /// </summary>
    /// <param name="target">The source plus the cells to fill, extended in one direction.</param>
    /// <param name="series">False copies instead of extending — what Fill Down and Fill Right do.</param>
    public static IEditCommand<Workbook>? Build(Worksheet sheet, CellRange source, CellRange target, bool series = true)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        if (!Contains(target, source) || target == source)
            return null;

        var vertical = target.Top < source.Top || target.Bottom > source.Bottom;
        var backwards = vertical ? target.Top < source.Top : target.Left < source.Left;

        // The rectangle being written, which is the target less the source.
        var region = vertical
            ? backwards
                ? new CellRange(new CellRef(source.Left, target.Top), new CellRef(source.Right, source.Top - 1))
                : new CellRange(new CellRef(source.Left, source.Bottom + 1), new CellRef(source.Right, target.Bottom))
            : backwards
                ? new CellRange(new CellRef(target.Left, source.Top), new CellRef(source.Left - 1, source.Bottom))
                : new CellRange(new CellRef(source.Right + 1, source.Top), new CellRef(target.Right, source.Bottom));

        var cells = new List<SpreadsheetClipboardCell>();
        var lines = vertical ? source.ColumnCount : source.RowCount;
        var length = vertical ? source.RowCount : source.ColumnCount;
        var span = vertical ? region.RowCount : region.ColumnCount;

        for (var line = 0; line < lines; line++)
        {
            var sourceCells = Enumerable.Range(0, length)
                .Select(i => vertical
                    ? new CellRef(source.Left + line, source.Top + i)
                    : new CellRef(source.Left + i, source.Top + line))
                .ToList();

            var generator = series ? Plan(sheet, sourceCells) : null;

            for (var step = 0; step < span; step++)
            {
                // Position along the line, counted from the source's first cell: n, n+1... going
                // forwards, -1, -2... going backwards.
                var k = backwards ? -(step + 1) : length + step;
                var destination = vertical
                    ? new CellRef(source.Left + line, backwards ? source.Top - 1 - step : source.Bottom + 1 + step)
                    : new CellRef(backwards ? source.Left - 1 - step : source.Right + 1 + step, source.Top + line);

                var origin = sourceCells[Mod(k, length)];
                var style = sheet.GetEffectiveStyleIndex(origin);
                var formula = sheet.GetFormula(origin);

                string? text = null;
                CellValue value = CellValue.Blank;

                if (formula is not null)
                {
                    text = FormulaReferenceShifter.Translate(formula, destination.Column - origin.Column, destination.Row - origin.Row);
                }
                else if (generator is not null)
                {
                    value = generator(k);
                }
                else
                {
                    value = sheet.GetValue(origin);
                }

                if (text is null && value.IsBlank && style is null)
                    continue;

                cells.Add(new SpreadsheetClipboardCell(
                    destination.Column - region.Left,
                    destination.Row - region.Top,
                    text,
                    value,
                    style));
            }
        }

        var content = new SpreadsheetClipboardContent
        {
            SheetName = sheet.Name,
            Source = region,
            Captured = region,
            Kind = SpreadsheetClipboardKind.Cells,
            Operation = SpreadsheetClipboardOperation.Copy,
            Cells = cells,
            Bands = []
        };

        return new CompositeCommand<Workbook>(series ? "Auto Fill" : "Fill", [new PasteClipboardCommand(content, sheet.Name, region.TopLeft)]);
    }

    static bool Contains(CellRange outer, CellRange inner)
        => outer.Left <= inner.Left && outer.Right >= inner.Right && outer.Top <= inner.Top && outer.Bottom >= inner.Bottom;

    static int Mod(int value, int modulus) => ((value % modulus) + modulus) % modulus;

    /// <summary>
    /// The value a line produces at position <c>k</c>, or null when the line just repeats.
    /// </summary>
    static Func<int, CellValue>? Plan(Worksheet sheet, IReadOnlyList<CellRef> line)
    {
        if (line.Any(cell => sheet.GetFormula(cell) is not null))
            return null;

        var values = line.Select(sheet.GetValue).ToList();
        if (values.All(x => x.IsBlank))
            return null;

        // Numbers: the least-squares line through them, which for two values is simply the step
        // between them - and for 1, 2, 4 is Excel's "trend" rather than a guess at a pattern.
        if (values.All(x => x.Kind == CellValueKind.Number))
        {
            if (values.Count == 1)
            {
                var only = values[0].AsNumber();
                var code = sheet.Workbook.Styles.Resolve(sheet.GetEffectiveStyleIndex(line[0])).NumberFormatCode;
                return IsDateFormat(code) ? k => CellValue.FromNumber(only + k) : null;
            }

            var (intercept, slope) = Fit(values.Select(x => x.AsNumber()).ToList());
            return k => CellValue.FromNumber(Math.Round(intercept + slope * k, 10));
        }

        if (values.All(x => x.Kind == CellValueKind.Text))
        {
            var texts = values.Select(x => x.AsText()).ToList();

            foreach (var list in Lists)
            {
                var indexes = texts.Select(t => Array.FindIndex(list, x => string.Equals(x, t.Trim(), StringComparison.OrdinalIgnoreCase))).ToList();
                if (indexes.Any(x => x < 0))
                    continue;

                var step = indexes.Count > 1 ? indexes[1] - indexes[0] : 1;
                var casing = Casing(texts[0]);
                return k => CellValue.FromText(ApplyCasing(list[Mod(indexes[0] + step * k, list.Length)], casing));
            }

            var matches = texts.Select(t => TrailingNumber().Match(t)).ToList();
            if (matches.All(m => m.Success) && matches.All(m => m.Groups[1].Value == matches[0].Groups[1].Value && m.Groups[3].Value == matches[0].Groups[3].Value))
            {
                var numbers = matches.Select(m => long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)).ToList();
                var step = numbers.Count > 1 ? numbers[1] - numbers[0] : 1;
                var width = matches[0].Groups[2].Value.Length;
                var prefix = matches[0].Groups[1].Value;
                var suffix = matches[0].Groups[3].Value;

                return k =>
                {
                    var n = numbers[0] + step * k;
                    var digits = Math.Abs(n).ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
                    return CellValue.FromText(prefix + (n < 0 ? "-" : string.Empty) + digits + suffix);
                };
            }
        }

        return null;
    }

    static (double Intercept, double Slope) Fit(IReadOnlyList<double> values)
    {
        var n = values.Count;
        var meanX = (n - 1) / 2d;
        var meanY = values.Average();

        double numerator = 0, denominator = 0;
        for (var i = 0; i < n; i++)
        {
            numerator += (i - meanX) * (values[i] - meanY);
            denominator += (i - meanX) * (i - meanX);
        }

        var slope = denominator == 0 ? 0 : numerator / denominator;
        return (meanY - slope * meanX, slope);
    }

    enum TextCasing { Title, Upper, Lower }

    static TextCasing Casing(string text)
        => text.All(c => !char.IsLetter(c) || char.IsUpper(c)) ? TextCasing.Upper
            : text.All(c => !char.IsLetter(c) || char.IsLower(c)) ? TextCasing.Lower
            : TextCasing.Title;

    static string ApplyCasing(string text, TextCasing casing) => casing switch
    {
        TextCasing.Upper => text.ToUpperInvariant(),
        TextCasing.Lower => text.ToLowerInvariant(),
        _ => text
    };

    /// <summary>
    /// Whether a number format shows a date or time, which is what makes a single value count by days.
    /// </summary>
    /// <remarks>
    /// The day, month, year and hour tokens counted only outside quoted literals and square brackets —
    /// <c>[Red]</c> and <c>"days"</c> both contain letters that are not date parts.
    /// </remarks>
    public static bool IsDateFormat(string? code)
    {
        if (string.IsNullOrEmpty(code) || code == "General")
            return false;

        var quoted = false;
        var bracket = false;

        foreach (var c in code)
        {
            switch (c)
            {
                case '"':
                    quoted = !quoted;
                    continue;
                case '[' when !quoted:
                    bracket = true;
                    continue;
                case ']' when !quoted:
                    bracket = false;
                    continue;
            }

            if (quoted || bracket)
                continue;

            if (c is 'd' or 'D' or 'y' or 'Y' or 'm' or 'M' or 'h' or 'H')
                return true;
        }

        return false;
    }
}
