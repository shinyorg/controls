namespace Shiny.Controls.Office.Spreadsheet;

/// <summary>
/// A partial edit to a cell's formatting: the properties to change, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// Every property is nullable, and null means "leave whatever is there". That is the whole point of
/// the type. A toolbar applies its change to a selection whose cells are formatted differently from
/// one another — bolding a range that mixes red text and blue text must leave both colours alone —
/// so the change cannot be expressed as a <see cref="ResolvedFormat"/> to assign; it has to be a
/// delta each cell's own format is folded into.
/// </para>
/// <para>
/// <see cref="Background"/> uses <see cref="ArgbColor.Transparent"/> rather than null to mean "no
/// fill", because null already means "don't touch the fill" and removing a highlight has to be
/// expressible.
/// </para>
/// </remarks>
public sealed record CellFormatChange
{
    /// <summary>A change that does nothing. Useful as the seed of a <c>with</c> chain.</summary>
    public static readonly CellFormatChange None = new();

    public bool? Bold { get; init; }
    public bool? Italic { get; init; }
    public bool? Underline { get; init; }
    public bool? Strike { get; init; }

    public string? FontName { get; init; }
    public double? FontSize { get; init; }

    public ArgbColor? Foreground { get; init; }

    /// <summary>The cell fill. <see cref="ArgbColor.Transparent"/> removes it; null leaves it alone.</summary>
    public ArgbColor? Background { get; init; }

    public CellHorizontalAlignment? HorizontalAlignment { get; init; }
    public CellVerticalAlignment? VerticalAlignment { get; init; }

    public bool? WrapText { get; init; }
    public int? Indent { get; init; }

    /// <summary>An Excel number format code, or the empty string for General.</summary>
    public string? NumberFormatCode { get; init; }

    /// <summary>The left edge. <see cref="BorderEdge.None"/> removes it; null leaves it alone.</summary>
    public BorderEdge? BorderLeft { get; init; }

    /// <inheritdoc cref="BorderLeft"/>
    public BorderEdge? BorderRight { get; init; }

    /// <inheritdoc cref="BorderLeft"/>
    public BorderEdge? BorderTop { get; init; }

    /// <inheritdoc cref="BorderLeft"/>
    public BorderEdge? BorderBottom { get; init; }

    bool TouchesBorders => this.BorderLeft is not null || this.BorderRight is not null || this.BorderTop is not null || this.BorderBottom is not null;

    CellBorders ApplyBorders(CellBorders borders)
    {
        if (!this.TouchesBorders)
            return borders;

        static BorderEdge? Edge(BorderEdge? change, BorderEdge? current)
            => change is { } edge ? (edge.IsVisible ? edge : null) : current;

        var result = new CellBorders(
            Edge(this.BorderLeft, borders.Left),
            Edge(this.BorderRight, borders.Right),
            Edge(this.BorderTop, borders.Top),
            Edge(this.BorderBottom, borders.Bottom));

        return result.IsEmpty ? CellBorders.None : result;
    }

    /// <summary>
    /// Replaces the whole format rather than merging into it. Set by <see cref="Clear"/>.
    /// </summary>
    /// <remarks>
    /// Clearing formatting is not expressible as a delta: a delta with every property set would still
    /// have to name a value for each, and "the default" is exactly what <see cref="ResolvedFormat"/>
    /// already is. This flag says to start from the default instead of from the cell.
    /// </remarks>
    public bool ResetFirst { get; init; }

    /// <summary>Strips every cell to the default format.</summary>
    public static CellFormatChange Clear { get; } = new() { ResetFirst = true };

    /// <summary>True when applying this would change nothing at all.</summary>
    public bool IsEmpty =>
        !this.ResetFirst &&
        this.Bold is null && this.Italic is null && this.Underline is null && this.Strike is null &&
        this.FontName is null && this.FontSize is null &&
        this.Foreground is null && this.Background is null &&
        this.HorizontalAlignment is null && this.VerticalAlignment is null &&
        this.WrapText is null && this.Indent is null &&
        this.NumberFormatCode is null &&
        !this.TouchesBorders;

    /// <summary>Folds this change into an existing format.</summary>
    public ResolvedFormat ApplyTo(ResolvedFormat format)
    {
        var start = this.ResetFirst ? ResolvedFormat.Default : format;

        return start with
        {
            Bold = this.Bold ?? start.Bold,
            Italic = this.Italic ?? start.Italic,
            Underline = this.Underline ?? start.Underline,
            Strike = this.Strike ?? start.Strike,
            FontName = this.FontName ?? start.FontName,
            FontSize = this.FontSize ?? start.FontSize,
            Foreground = this.Foreground ?? start.Foreground,
            Background = this.Background ?? start.Background,
            HorizontalAlignment = this.HorizontalAlignment ?? start.HorizontalAlignment,
            VerticalAlignment = this.VerticalAlignment ?? start.VerticalAlignment,
            WrapText = this.WrapText ?? start.WrapText,

            // Indent is clamped rather than validated: Excel's own limit is 250, and a toolbar's
            // "decrease indent" on an unindented cell would otherwise write -1 and be rejected on save.
            Indent = Math.Clamp(this.Indent ?? start.Indent, 0, 250),
            NumberFormatCode = this.NumberFormatCode ?? start.NumberFormatCode,
            Borders = this.ApplyBorders(start.Borders)
        };
    }
}
