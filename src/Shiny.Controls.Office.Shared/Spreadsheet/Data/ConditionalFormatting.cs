using System.Globalization;
using System.Xml.Linq;
using Shiny.Controls.Office.Editing;
using Shiny.Controls.Office.Spreadsheet.Calc;

namespace Shiny.Controls.Office.Spreadsheet;

/// <summary>The rule kinds the model understands. Anything else in a file is kept and not drawn.</summary>
public enum ConditionalRuleType
{
    CellIs,
    ContainsText,
    NotContainsText,
    BeginsWith,
    EndsWith,
    DuplicateValues,
    UniqueValues,
    Top10,
    AboveAverage,
    DataBar,
    ColorScale,
    Expression,
    ContainsBlanks,
    NotContainsBlanks,
    Unknown
}

/// <summary>The comparisons a "cell value is" rule can make — Excel's <c>ST_ConditionalFormattingOperator</c>.</summary>
public enum ConditionalOperator
{
    LessThan,
    LessThanOrEqual,
    Equal,
    NotEqual,
    GreaterThanOrEqual,
    GreaterThan,
    Between,
    NotBetween
}

/// <summary>One point of a colour scale or end of a data bar: min, max, a number, a percent, a percentile.</summary>
public sealed record ConditionalValue(string Type, string? Value = null)
{
    public static readonly ConditionalValue Min = new("min");
    public static readonly ConditionalValue Max = new("max");
    public static ConditionalValue Percentile(double value) => new("percentile", value.ToString(CultureInfo.InvariantCulture));
}

/// <summary>One <c>&lt;cfRule&gt;</c>.</summary>
public sealed record ConditionalFormatRule(ConditionalRuleType Type)
{
    public ConditionalOperator Operator { get; init; } = ConditionalOperator.GreaterThan;

    /// <summary>The rule's formulas, without a leading <c>=</c>. Relative references are relative to the range's top-left cell.</summary>
    public IReadOnlyList<string> Formulas { get; init; } = [];

    /// <summary>The text a text rule looks for.</summary>
    public string? Text { get; init; }

    /// <summary>For a top/bottom rule: how many, or what percentage.</summary>
    public int Rank { get; init; } = 10;
    public bool Percent { get; init; }
    public bool Bottom { get; init; }

    /// <summary>For an average rule: above (true) or below.</summary>
    public bool AboveAverage { get; init; } = true;

    /// <summary>What the rule lays over a matching cell. Unused by data bars and colour scales.</summary>
    public DxfFormat? Format { get; init; }

    public int Priority { get; init; } = 1;
    public bool StopIfTrue { get; init; }

    /// <summary>A data bar's colour.</summary>
    public ArgbColor BarColor { get; init; } = new(255, 0x63, 0x8E, 0xC6);

    /// <summary>A colour scale's stops, and the colour at each — two or three of them.</summary>
    public IReadOnlyList<(ConditionalValue Point, ArgbColor Color)> Scale { get; init; } = [];

    // ---- presets ----

    public static ConditionalFormatRule CellIs(ConditionalOperator op, DxfFormat format, params string[] formulas)
        => new(ConditionalRuleType.CellIs) { Operator = op, Formulas = formulas, Format = format };

    public static ConditionalFormatRule TextContains(string text, DxfFormat format)
        => new(ConditionalRuleType.ContainsText) { Text = text, Format = format };

    public static ConditionalFormatRule Duplicates(DxfFormat format, bool unique = false)
        => new(unique ? ConditionalRuleType.UniqueValues : ConditionalRuleType.DuplicateValues) { Format = format };

    public static ConditionalFormatRule TopBottom(int rank, bool percent, bool bottom, DxfFormat format)
        => new(ConditionalRuleType.Top10) { Rank = rank, Percent = percent, Bottom = bottom, Format = format };

    public static ConditionalFormatRule Average(bool above, DxfFormat format)
        => new(ConditionalRuleType.AboveAverage) { AboveAverage = above, Format = format };

    public static ConditionalFormatRule Bar(ArgbColor color)
        => new(ConditionalRuleType.DataBar) { BarColor = color };

    public static ConditionalFormatRule TwoColorScale(ArgbColor low, ArgbColor high)
        => new(ConditionalRuleType.ColorScale) { Scale = [(ConditionalValue.Min, low), (ConditionalValue.Max, high)] };

    public static ConditionalFormatRule ThreeColorScale(ArgbColor low, ArgbColor mid, ArgbColor high)
        => new(ConditionalRuleType.ColorScale) { Scale = [(ConditionalValue.Min, low), (ConditionalValue.Percentile(50), mid), (ConditionalValue.Max, high)] };
}

/// <summary>One <c>&lt;conditionalFormatting&gt;</c> block: the ranges it covers and the rules over them.</summary>
public sealed record ConditionalFormat(IReadOnlyList<CellRange> Ranges, IReadOnlyList<ConditionalFormatRule> Rules)
{
    public bool Covers(CellRef cell) => this.Ranges.Any(x => x.Contains(cell));

    public CellRef Origin => this.Ranges.Count == 0
        ? default
        : new CellRef(this.Ranges.Min(x => x.Left), this.Ranges.Min(x => x.Top));
}

/// <summary>The data bar a cell is drawn with.</summary>
/// <param name="Fraction">How much of the cell's width the bar fills, 0 to 1.</param>
public readonly record struct DataBarFill(double Fraction, ArgbColor Color);

/// <summary>What conditional formatting does to one cell.</summary>
public sealed record ConditionalResult(DxfFormat? Overlay, ArgbColor? ScaleFill, DataBarFill? Bar)
{
    public static readonly ConditionalResult None = new(null, null, null);

    public bool IsEmpty => this.Overlay is null && this.ScaleFill is null && this.Bar is null;
}

/// <summary>
/// Reads, writes and evaluates conditional formatting.
/// </summary>
/// <remarks>
/// <para>
/// Evaluation needs the whole range, not just the cell: "top 10", "above average", a colour scale and a
/// data bar are all relative to the other values the rule covers. Those statistics are computed once per
/// rule per workbook revision and every cell is then a lookup — a painter asking cell by cell would
/// otherwise rescan the range once per visible cell per frame.
/// </para>
/// </remarks>
public static class ConditionalFormatting
{
    static readonly XNamespace Main = SheetXml.MainNamespace;

    // ---- reading ----

    public static ConditionalFormat? Parse(string xml, StyleResolver styles)
    {
        var element = XElement.Parse(xml);
        var ranges = ParseSqref(element.Attribute("sqref")?.Value);
        if (ranges.Count == 0)
            return null;

        var rules = element.Elements(Main + "cfRule").Select(x => ParseRule(x, styles)).ToList();
        return new ConditionalFormat(ranges, rules);
    }

    public static IReadOnlyList<CellRange> ParseSqref(string? sqref)
    {
        var ranges = new List<CellRange>();
        foreach (var part in (sqref ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (CellRange.TryParse(part, out var range))
                ranges.Add(range);
        }

        return ranges;
    }

    static ConditionalFormatRule ParseRule(XElement rule, StyleResolver styles)
    {
        var type = rule.Attribute("type")?.Value switch
        {
            "cellIs" => ConditionalRuleType.CellIs,
            "containsText" => ConditionalRuleType.ContainsText,
            "notContainsText" => ConditionalRuleType.NotContainsText,
            "beginsWith" => ConditionalRuleType.BeginsWith,
            "endsWith" => ConditionalRuleType.EndsWith,
            "duplicateValues" => ConditionalRuleType.DuplicateValues,
            "uniqueValues" => ConditionalRuleType.UniqueValues,
            "top10" => ConditionalRuleType.Top10,
            "aboveAverage" => ConditionalRuleType.AboveAverage,
            "dataBar" => ConditionalRuleType.DataBar,
            "colorScale" => ConditionalRuleType.ColorScale,
            "expression" => ConditionalRuleType.Expression,
            "containsBlanks" => ConditionalRuleType.ContainsBlanks,
            "notContainsBlanks" => ConditionalRuleType.NotContainsBlanks,
            _ => ConditionalRuleType.Unknown
        };

        var result = new ConditionalFormatRule(type)
        {
            Operator = rule.Attribute("operator")?.Value switch
            {
                "lessThan" => ConditionalOperator.LessThan,
                "lessThanOrEqual" => ConditionalOperator.LessThanOrEqual,
                "equal" => ConditionalOperator.Equal,
                "notEqual" => ConditionalOperator.NotEqual,
                "greaterThanOrEqual" => ConditionalOperator.GreaterThanOrEqual,
                "between" => ConditionalOperator.Between,
                "notBetween" => ConditionalOperator.NotBetween,
                _ => ConditionalOperator.GreaterThan
            },
            Formulas = rule.Elements(Main + "formula").Select(x => x.Value).ToList(),
            Text = rule.Attribute("text")?.Value,
            Rank = int.TryParse(rule.Attribute("rank")?.Value, out var rank) ? rank : 10,
            Percent = IsTrue(rule.Attribute("percent")),
            Bottom = IsTrue(rule.Attribute("bottom")),
            AboveAverage = rule.Attribute("aboveAverage")?.Value is not ("0" or "false"),
            Priority = int.TryParse(rule.Attribute("priority")?.Value, out var priority) ? priority : 1,
            StopIfTrue = IsTrue(rule.Attribute("stopIfTrue")),
            Format = uint.TryParse(rule.Attribute("dxfId")?.Value, out var dxfId) ? styles.ResolveDifferential(dxfId) : null
        };

        if (rule.Element(Main + "dataBar") is { } bar && bar.Element(Main + "color") is { } barColor)
            result = result with { BarColor = ParseColor(barColor, styles) ?? result.BarColor };

        if (rule.Element(Main + "colorScale") is { } scale)
        {
            var points = scale.Elements(Main + "cfvo").Select(x => new ConditionalValue(x.Attribute("type")?.Value ?? "min", x.Attribute("val")?.Value)).ToList();
            var colors = scale.Elements(Main + "color").Select(x => ParseColor(x, styles) ?? new ArgbColor(255, 255, 255, 255)).ToList();
            result = result with { Scale = points.Zip(colors, (p, c) => (p, c)).ToList() };
        }

        return result;
    }

    static bool IsTrue(XAttribute? attribute) => attribute?.Value is "1" or "true";

    static ArgbColor? ParseColor(XElement color, StyleResolver styles)
    {
        var typed = new DocumentFormat.OpenXml.Spreadsheet.Color();
        if (color.Attribute("rgb")?.Value is { } rgb)
            typed.Rgb = rgb;

        if (uint.TryParse(color.Attribute("theme")?.Value, out var theme))
            typed.Theme = theme;

        if (double.TryParse(color.Attribute("tint")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var tint))
            typed.Tint = tint;

        if (uint.TryParse(color.Attribute("indexed")?.Value, out var indexed))
            typed.Indexed = indexed;

        return styles.ColorOf(typed);
    }

    // ---- writing ----

    /// <summary>
    /// The <c>&lt;conditionalFormatting&gt;</c> element for one rule over <paramref name="ranges"/>.
    /// </summary>
    /// <param name="dxfId">The rule's format, interned into <c>&lt;dxfs&gt;</c> by the caller.</param>
    public static string ToXml(IReadOnlyList<CellRange> ranges, ConditionalFormatRule rule, uint? dxfId)
    {
        var origin = new CellRef(ranges.Min(x => x.Left), ranges.Min(x => x.Top)).ToString();

        var element = new XElement(Main + "cfRule");
        string type = rule.Type switch
        {
            ConditionalRuleType.CellIs => "cellIs",
            ConditionalRuleType.ContainsText => "containsText",
            ConditionalRuleType.NotContainsText => "notContainsText",
            ConditionalRuleType.BeginsWith => "beginsWith",
            ConditionalRuleType.EndsWith => "endsWith",
            ConditionalRuleType.DuplicateValues => "duplicateValues",
            ConditionalRuleType.UniqueValues => "uniqueValues",
            ConditionalRuleType.Top10 => "top10",
            ConditionalRuleType.AboveAverage => "aboveAverage",
            ConditionalRuleType.DataBar => "dataBar",
            ConditionalRuleType.ColorScale => "colorScale",
            ConditionalRuleType.ContainsBlanks => "containsBlanks",
            ConditionalRuleType.NotContainsBlanks => "notContainsBlanks",
            _ => "expression"
        };

        element.Add(new XAttribute("type", type));

        if (dxfId is { } id)
            element.Add(new XAttribute("dxfId", id));

        element.Add(new XAttribute("priority", Math.Max(1, rule.Priority)));

        if (rule.StopIfTrue)
            element.Add(new XAttribute("stopIfTrue", "1"));

        switch (rule.Type)
        {
            case ConditionalRuleType.CellIs:
                element.Add(new XAttribute("operator", OperatorText(rule.Operator)));
                foreach (var formula in rule.Formulas)
                    element.Add(new XElement(Main + "formula", formula));

                break;

            case ConditionalRuleType.ContainsText:
            case ConditionalRuleType.NotContainsText:
            case ConditionalRuleType.BeginsWith:
            case ConditionalRuleType.EndsWith:
                var text = rule.Text ?? string.Empty;
                var quoted = "\"" + text.Replace("\"", "\"\"") + "\"";
                var (op, formula1) = rule.Type switch
                {
                    ConditionalRuleType.NotContainsText => ("notContains", $"ISERROR(SEARCH({quoted},{origin}))"),
                    ConditionalRuleType.BeginsWith => ("beginsWith", $"LEFT({origin},LEN({quoted}))={quoted}"),
                    ConditionalRuleType.EndsWith => ("endsWith", $"RIGHT({origin},LEN({quoted}))={quoted}"),
                    _ => ("containsText", $"NOT(ISERROR(SEARCH({quoted},{origin})))")
                };

                element.Add(new XAttribute("operator", op), new XAttribute("text", text), new XElement(Main + "formula", formula1));
                break;

            case ConditionalRuleType.Top10:
                element.Add(new XAttribute("rank", rule.Rank));
                if (rule.Percent)
                    element.Add(new XAttribute("percent", "1"));

                if (rule.Bottom)
                    element.Add(new XAttribute("bottom", "1"));

                break;

            case ConditionalRuleType.AboveAverage:
                if (!rule.AboveAverage)
                    element.Add(new XAttribute("aboveAverage", "0"));

                break;

            case ConditionalRuleType.ContainsBlanks:
                element.Add(new XElement(Main + "formula", $"LEN(TRIM({origin}))=0"));
                break;

            case ConditionalRuleType.NotContainsBlanks:
                element.Add(new XElement(Main + "formula", $"LEN(TRIM({origin}))>0"));
                break;

            case ConditionalRuleType.Expression:
                foreach (var formula in rule.Formulas)
                    element.Add(new XElement(Main + "formula", formula));

                break;

            case ConditionalRuleType.DataBar:
                element.Add(new XElement(Main + "dataBar",
                    new XElement(Main + "cfvo", new XAttribute("type", "min")),
                    new XElement(Main + "cfvo", new XAttribute("type", "max")),
                    new XElement(Main + "color", new XAttribute("rgb", Hex(rule.BarColor)))));
                break;

            case ConditionalRuleType.ColorScale:
                var scale = new XElement(Main + "colorScale");
                foreach (var (point, _) in rule.Scale)
                {
                    var cfvo = new XElement(Main + "cfvo", new XAttribute("type", point.Type));
                    if (point.Value is not null)
                        cfvo.Add(new XAttribute("val", point.Value));

                    scale.Add(cfvo);
                }

                foreach (var (_, color) in rule.Scale)
                    scale.Add(new XElement(Main + "color", new XAttribute("rgb", Hex(color))));

                element.Add(scale);
                break;
        }

        var block = new XElement(Main + "conditionalFormatting",
            new XAttribute("sqref", string.Join(' ', ranges.Select(x => x.ToString()))),
            element);

        return block.ToString(SaveOptions.DisableFormatting);
    }

    static string OperatorText(ConditionalOperator op) => op switch
    {
        ConditionalOperator.LessThan => "lessThan",
        ConditionalOperator.LessThanOrEqual => "lessThanOrEqual",
        ConditionalOperator.Equal => "equal",
        ConditionalOperator.NotEqual => "notEqual",
        ConditionalOperator.GreaterThanOrEqual => "greaterThanOrEqual",
        ConditionalOperator.Between => "between",
        ConditionalOperator.NotBetween => "notBetween",
        _ => "greaterThan"
    };

    static string Hex(ArgbColor color) => $"{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>
    /// The sheet's blocks with a new rule added at the top of the priority order — where Excel puts a
    /// rule just created, so it wins over the ones that were there.
    /// </summary>
    /// <remarks>
    /// The existing blocks are edited in place — only their <c>priority</c> attributes move — so rules
    /// this model does not understand, and extensions it cannot read, come through unchanged.
    /// </remarks>
    public static IReadOnlyList<string> WithRule(IReadOnlyList<string> existing, IReadOnlyList<CellRange> ranges, ConditionalFormatRule rule, uint? dxfId)
    {
        var result = new List<string>(existing.Count + 1);

        foreach (var fragment in existing)
        {
            var element = XElement.Parse(fragment);
            foreach (var cfRule in element.Elements().Where(x => x.Name.LocalName == "cfRule"))
            {
                if (int.TryParse(cfRule.Attribute("priority")?.Value, out var priority))
                    cfRule.SetAttributeValue("priority", priority + 1);
            }

            result.Add(element.ToString(SaveOptions.DisableFormatting));
        }

        result.Add(ToXml(ranges, rule with { Priority = 1 }, dxfId));
        return result;
    }

    /// <summary>
    /// The sheet's blocks with <paramref name="area"/> cut out of every one of them — what Clear Rules
    /// from Selected Cells does. A block left covering nothing is dropped.
    /// </summary>
    public static IReadOnlyList<string> WithoutArea(IReadOnlyList<string> existing, CellRange area)
    {
        var result = new List<string>();

        foreach (var fragment in existing)
        {
            var element = XElement.Parse(fragment);
            var ranges = ParseSqref(element.Attribute("sqref")?.Value);
            var remaining = ranges.SelectMany(x => Subtract(x, area)).ToList();

            if (remaining.Count == 0)
                continue;

            if (remaining.Count != ranges.Count || !remaining.SequenceEqual(ranges))
                element.SetAttributeValue("sqref", string.Join(' ', remaining.Select(x => x.ToString())));

            result.Add(ranges.SequenceEqual(remaining) ? fragment : element.ToString(SaveOptions.DisableFormatting));
        }

        return result;
    }

    /// <summary><paramref name="range"/> less <paramref name="hole"/>, as up to four rectangles.</summary>
    public static IEnumerable<CellRange> Subtract(CellRange range, CellRange hole)
    {
        if (!range.Intersects(hole))
        {
            yield return range;
            yield break;
        }

        // Above and below span the full width; left and right fill the band in between.
        if (hole.Top > range.Top)
            yield return new CellRange(range.TopLeft, new CellRef(range.Right, hole.Top - 1));

        if (hole.Bottom < range.Bottom)
            yield return new CellRange(new CellRef(range.Left, hole.Bottom + 1), range.BottomRight);

        var top = Math.Max(range.Top, hole.Top);
        var bottom = Math.Min(range.Bottom, hole.Bottom);

        if (hole.Left > range.Left)
            yield return new CellRange(new CellRef(range.Left, top), new CellRef(hole.Left - 1, bottom));

        if (hole.Right < range.Right)
            yield return new CellRange(new CellRef(hole.Right + 1, top), new CellRef(range.Right, bottom));
    }

    // ---- evaluation ----

    /// <summary>
    /// Evaluates a sheet's rules, caching per-rule statistics against the workbook revision.
    /// </summary>
    public sealed class Evaluator
    {
        readonly Worksheet sheet;
        readonly IReadOnlyList<ConditionalFormat> formats;
        readonly Dictionary<(int Format, int Rule), RuleStats> stats = new();
        readonly Dictionary<CellRef, ConditionalResult> results = new();

        internal Evaluator(Worksheet sheet, IReadOnlyList<ConditionalFormat> formats)
        {
            this.sheet = sheet;
            this.formats = formats;
        }

        public bool IsEmpty => this.formats.Count == 0;

        public ConditionalResult Evaluate(CellRef cell)
        {
            if (this.formats.Count == 0)
                return ConditionalResult.None;

            cell = cell.Relative();
            if (this.results.TryGetValue(cell, out var cached))
                return cached;

            var result = this.Compute(cell);
            this.results[cell] = result;
            return result;
        }

        ConditionalResult Compute(CellRef cell)
        {
            var matches = new List<(int Priority, bool Stop, ConditionalFormatRule Rule, int FormatIndex, int RuleIndex)>();

            for (var f = 0; f < this.formats.Count; f++)
            {
                var format = this.formats[f];
                if (!format.Covers(cell))
                    continue;

                for (var r = 0; r < format.Rules.Count; r++)
                    matches.Add((format.Rules[r].Priority, format.Rules[r].StopIfTrue, format.Rules[r], f, r));
            }

            if (matches.Count == 0)
                return ConditionalResult.None;

            DxfFormat? overlay = null;
            ArgbColor? scale = null;
            DataBarFill? bar = null;

            // Highest priority (lowest number) first: the first rule to set a property keeps it.
            foreach (var match in matches.OrderBy(x => x.Priority))
            {
                var rule = match.Rule;
                var value = this.sheet.GetDisplayValue(cell);
                var stats = this.StatsFor(match.FormatIndex, match.RuleIndex);

                switch (rule.Type)
                {
                    case ConditionalRuleType.DataBar:
                        if (bar is null && value.Kind == CellValueKind.Number && stats.Numbers.Count > 0)
                        {
                            var span = stats.Max - stats.Min;
                            var fraction = span <= 0 ? 1 : (value.AsNumber() - stats.Min) / span;

                            // Excel's bars never shrink to nothing: the smallest value still shows a sliver.
                            bar = new DataBarFill(0.1 + 0.9 * Math.Clamp(fraction, 0, 1), rule.BarColor);
                        }

                        continue;

                    case ConditionalRuleType.ColorScale:
                        if (scale is null && value.Kind == CellValueKind.Number && rule.Scale.Count >= 2 && stats.Numbers.Count > 0)
                            scale = ScaleColor(rule, stats, value.AsNumber());

                        continue;
                }

                if (rule.Format is not { } dxf || !this.Matches(rule, format: this.formats[match.FormatIndex], cell, value, stats))
                    continue;

                overlay = overlay is null ? dxf : Merge(overlay, dxf);

                if (match.Stop)
                    break;
            }

            return overlay is null && scale is null && bar is null ? ConditionalResult.None : new ConditionalResult(overlay, scale, bar);
        }

        /// <summary>Folds a lower-priority rule under one that already matched: gaps only.</summary>
        static DxfFormat Merge(DxfFormat winner, DxfFormat under) => winner with
        {
            Bold = winner.Bold ?? under.Bold,
            Italic = winner.Italic ?? under.Italic,
            Underline = winner.Underline ?? under.Underline,
            Strike = winner.Strike ?? under.Strike,
            Foreground = winner.Foreground ?? under.Foreground,
            Background = winner.Background ?? under.Background,
            NumberFormatCode = winner.NumberFormatCode ?? under.NumberFormatCode,
            Borders = winner.Borders ?? under.Borders
        };

        bool Matches(ConditionalFormatRule rule, ConditionalFormat format, CellRef cell, CellValue value, RuleStats stats)
        {
            var text = value.IsBlank ? string.Empty : Coercion.ToText(value);

            switch (rule.Type)
            {
                case ConditionalRuleType.CellIs:
                    return this.CellIs(rule, format, cell, value);

                case ConditionalRuleType.ContainsText:
                    return text.Contains(rule.Text ?? string.Empty, StringComparison.CurrentCultureIgnoreCase);

                case ConditionalRuleType.NotContainsText:
                    return !text.Contains(rule.Text ?? string.Empty, StringComparison.CurrentCultureIgnoreCase);

                case ConditionalRuleType.BeginsWith:
                    return text.StartsWith(rule.Text ?? string.Empty, StringComparison.CurrentCultureIgnoreCase);

                case ConditionalRuleType.EndsWith:
                    return text.EndsWith(rule.Text ?? string.Empty, StringComparison.CurrentCultureIgnoreCase);

                case ConditionalRuleType.DuplicateValues:
                    return text.Length > 0 && stats.Counts.GetValueOrDefault(text) > 1;

                case ConditionalRuleType.UniqueValues:
                    return text.Length > 0 && stats.Counts.GetValueOrDefault(text) == 1;

                case ConditionalRuleType.Top10:
                    if (value.Kind != CellValueKind.Number || stats.Numbers.Count == 0)
                        return false;

                    var count = rule.Percent
                        ? Math.Max(1, (int)Math.Floor(stats.Numbers.Count * rule.Rank / 100d))
                        : Math.Min(rule.Rank, stats.Numbers.Count);

                    // Numbers are sorted ascending; the threshold is the n-th from whichever end.
                    var threshold = rule.Bottom ? stats.Numbers[count - 1] : stats.Numbers[^count];
                    return rule.Bottom ? value.AsNumber() <= threshold : value.AsNumber() >= threshold;

                case ConditionalRuleType.AboveAverage:
                    if (value.Kind != CellValueKind.Number || stats.Numbers.Count == 0)
                        return false;

                    return rule.AboveAverage ? value.AsNumber() > stats.Average : value.AsNumber() < stats.Average;

                case ConditionalRuleType.ContainsBlanks:
                    return string.IsNullOrWhiteSpace(text);

                case ConditionalRuleType.NotContainsBlanks:
                    return !string.IsNullOrWhiteSpace(text);

                case ConditionalRuleType.Expression:
                    if (rule.Formulas.Count == 0)
                        return false;

                    var result = this.EvaluateRelative(rule.Formulas[0], format, cell);
                    return Coercion.TryToBoolean(result, out var truth, out _) && truth;

                default:
                    return false;
            }
        }

        bool CellIs(ConditionalFormatRule rule, ConditionalFormat format, CellRef cell, CellValue value)
        {
            if (rule.Formulas.Count == 0)
                return false;

            var first = this.EvaluateRelative(rule.Formulas[0], format, cell);
            var second = rule.Formulas.Count > 1 ? this.EvaluateRelative(rule.Formulas[1], format, cell) : CellValue.Blank;

            if (value.IsBlank)
                value = CellValue.FromNumber(0);

            int Cmp(CellValue other) => Coercion.Compare(value, other.IsBlank ? CellValue.FromNumber(0) : other);

            return rule.Operator switch
            {
                ConditionalOperator.LessThan => Cmp(first) < 0,
                ConditionalOperator.LessThanOrEqual => Cmp(first) <= 0,
                ConditionalOperator.Equal => Cmp(first) == 0,
                ConditionalOperator.NotEqual => Cmp(first) != 0,
                ConditionalOperator.GreaterThanOrEqual => Cmp(first) >= 0,
                ConditionalOperator.GreaterThan => Cmp(first) > 0,
                ConditionalOperator.Between => Between(),
                ConditionalOperator.NotBetween => !Between(),
                _ => false
            };

            bool Between()
            {
                var low = Cmp(first) >= 0 && Cmp(second) <= 0;
                var high = Cmp(second) >= 0 && Cmp(first) <= 0;
                return low || high;
            }
        }

        /// <summary>A rule formula, evaluated as if it had been filled from the range's top-left to this cell.</summary>
        CellValue EvaluateRelative(string formula, ConditionalFormat format, CellRef cell)
        {
            var origin = format.Origin;
            var shifted = FormulaReferenceShifter.Translate(formula, cell.Column - origin.Column, cell.Row - origin.Row);
            return this.sheet.Workbook.Evaluate(shifted, this.sheet.Name, cell);
        }

        static ArgbColor ScaleColor(ConditionalFormatRule rule, RuleStats stats, double number)
        {
            var points = rule.Scale.Select(x => (Value: PointValue(x.Point, stats), x.Color)).ToList();

            if (number <= points[0].Value)
                return points[0].Color;

            for (var i = 1; i < points.Count; i++)
            {
                if (number <= points[i].Value)
                {
                    var span = points[i].Value - points[i - 1].Value;
                    var t = span <= 0 ? 1 : (number - points[i - 1].Value) / span;
                    return Lerp(points[i - 1].Color, points[i].Color, t);
                }
            }

            return points[^1].Color;
        }

        static double PointValue(ConditionalValue point, RuleStats stats)
        {
            double.TryParse(point.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number);

            return point.Type switch
            {
                "min" => stats.Min,
                "max" => stats.Max,
                "percent" => stats.Min + (stats.Max - stats.Min) * number / 100d,
                "percentile" => Percentile(stats.Numbers, number / 100d),
                "num" => number,
                _ => stats.Min
            };
        }

        static double Percentile(IReadOnlyList<double> sorted, double p)
        {
            if (sorted.Count == 0)
                return 0;

            var position = (sorted.Count - 1) * Math.Clamp(p, 0, 1);
            var low = (int)Math.Floor(position);
            var high = (int)Math.Ceiling(position);
            return sorted[low] + (sorted[high] - sorted[low]) * (position - low);
        }

        static ArgbColor Lerp(ArgbColor a, ArgbColor b, double t)
        {
            byte Mix(byte x, byte y) => (byte)Math.Round(x + (y - x) * Math.Clamp(t, 0, 1));
            return new ArgbColor(255, Mix(a.R, b.R), Mix(a.G, b.G), Mix(a.B, b.B));
        }

        RuleStats StatsFor(int formatIndex, int ruleIndex)
        {
            if (this.stats.TryGetValue((formatIndex, ruleIndex), out var cached))
                return cached;

            var numbers = new List<double>();
            var counts = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
            var used = this.sheet.UsedRange;

            foreach (var range in this.formats[formatIndex].Ranges)
            {
                if (used is not { } u || !u.Intersects(range))
                    continue;

                var clipped = new CellRange(
                    new CellRef(Math.Max(range.Left, u.Left), Math.Max(range.Top, u.Top)),
                    new CellRef(Math.Min(range.Right, u.Right), Math.Min(range.Bottom, u.Bottom)));

                foreach (var cell in clipped.Cells())
                {
                    var value = this.sheet.GetDisplayValue(cell);
                    if (value.IsBlank)
                        continue;

                    if (value.Kind == CellValueKind.Number)
                        numbers.Add(value.AsNumber());

                    var text = Coercion.ToText(value);
                    counts[text] = counts.GetValueOrDefault(text) + 1;
                }
            }

            numbers.Sort();
            var result = new RuleStats(
                numbers,
                counts,
                numbers.Count == 0 ? 0 : numbers[0],
                numbers.Count == 0 ? 0 : numbers[^1],
                numbers.Count == 0 ? 0 : numbers.Average());

            this.stats[(formatIndex, ruleIndex)] = result;
            return result;
        }

        sealed record RuleStats(List<double> Numbers, Dictionary<string, int> Counts, double Min, double Max, double Average);
    }
}

/// <summary>The data bar colours and colour scales Excel's galleries offer.</summary>
public static class ConditionalPresets
{
    public static IReadOnlyList<(string Name, ArgbColor Color)> DataBars { get; } =
    [
        ("Blue Data Bar", new ArgbColor(255, 0x63, 0x8E, 0xC6)),
        ("Green Data Bar", new ArgbColor(255, 0x63, 0xC3, 0x84)),
        ("Red Data Bar", new ArgbColor(255, 0xFF, 0x55, 0x5A)),
        ("Orange Data Bar", new ArgbColor(255, 0xFF, 0xB6, 0x28)),
        ("Light Blue Data Bar", new ArgbColor(255, 0x00, 0x8A, 0xEF)),
        ("Purple Data Bar", new ArgbColor(255, 0xD6, 0x00, 0x7B))
    ];

    static readonly ArgbColor Green = new(255, 0x63, 0xBE, 0x7B);
    static readonly ArgbColor Yellow = new(255, 0xFF, 0xEB, 0x84);
    static readonly ArgbColor Red = new(255, 0xF8, 0x69, 0x6B);
    static readonly ArgbColor White = new(255, 0xFC, 0xFC, 0xFF);
    static readonly ArgbColor Blue = new(255, 0x5A, 0x8A, 0xC6);

    public static IReadOnlyList<(string Name, ConditionalFormatRule Rule)> ColorScales { get; } =
    [
        ("Green - Yellow - Red", ConditionalFormatRule.ThreeColorScale(Red, Yellow, Green)),
        ("Red - Yellow - Green", ConditionalFormatRule.ThreeColorScale(Green, Yellow, Red)),
        ("Green - White - Red", ConditionalFormatRule.ThreeColorScale(Red, White, Green)),
        ("Blue - White - Red", ConditionalFormatRule.ThreeColorScale(Red, White, Blue)),
        ("Green - White", ConditionalFormatRule.TwoColorScale(White, Green)),
        ("White - Red", ConditionalFormatRule.TwoColorScale(White, Red))
    ];
}
