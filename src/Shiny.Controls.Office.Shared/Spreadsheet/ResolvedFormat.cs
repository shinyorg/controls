namespace Shiny.Controls.Office.Spreadsheet;

public enum CellHorizontalAlignment
{
    /// <summary>Excel's default: text left, numbers right, booleans and errors centred.</summary>
    General,
    Left,
    Center,
    Right,
    Fill,
    Justify,
    CenterContinuous,
    Distributed
}

public enum CellVerticalAlignment
{
    Top,
    Center,
    Bottom,
    Justify,
    Distributed
}

/// <summary>An ARGB colour. Kept host-agnostic so the kernel never references a UI framework's colour type.</summary>
public readonly record struct ArgbColor(byte A, byte R, byte G, byte B)
{
    public static readonly ArgbColor Transparent = new(0, 0, 0, 0);
    public bool IsTransparent => this.A == 0;

    public uint ToUInt32() => ((uint)this.A << 24) | ((uint)this.R << 16) | ((uint)this.G << 8) | this.B;

    public static ArgbColor FromUInt32(uint value)
        => new((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);

    public override string ToString() => $"#{this.A:X2}{this.R:X2}{this.G:X2}{this.B:X2}";
}

/// <summary>
/// A cell's formatting, flattened from the style chain into something a renderer can use directly.
/// </summary>
public sealed record ResolvedFormat
{
    public static readonly ResolvedFormat Default = new();

    /// <summary>The Excel number format code, e.g. <c>#,##0.00</c>. Empty means General.</summary>
    public string NumberFormatCode { get; init; } = string.Empty;

    public string FontName { get; init; } = "Calibri";
    public double FontSize { get; init; } = 11;
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public bool Underline { get; init; }
    public bool Strike { get; init; }
    public ArgbColor Foreground { get; init; } = new(255, 0, 0, 0);

    /// <summary>Cell background. Transparent when no fill is applied.</summary>
    public ArgbColor Background { get; init; } = ArgbColor.Transparent;

    public CellHorizontalAlignment HorizontalAlignment { get; init; } = CellHorizontalAlignment.General;
    public CellVerticalAlignment VerticalAlignment { get; init; } = CellVerticalAlignment.Bottom;
    public bool WrapText { get; init; }
    public int Indent { get; init; }

    /// <summary>The four edges drawn around the cell. <see cref="CellBorders.None"/> when it has none.</summary>
    public CellBorders Borders { get; init; } = CellBorders.None;

    /// <summary>Resolves <see cref="CellHorizontalAlignment.General"/> against the value being shown.</summary>
    public CellHorizontalAlignment EffectiveAlignment(CellValueKind kind)
    {
        if (this.HorizontalAlignment != CellHorizontalAlignment.General)
            return this.HorizontalAlignment;

        return kind switch
        {
            CellValueKind.Number => CellHorizontalAlignment.Right,
            CellValueKind.Boolean or CellValueKind.Error => CellHorizontalAlignment.Center,
            _ => CellHorizontalAlignment.Left
        };
    }
}

/// <summary>The line styles a cell edge can be drawn in — Excel's <c>ST_BorderStyle</c>.</summary>
public enum CellBorderStyle
{
    None,
    Thin,
    Medium,
    Thick,
    Dashed,
    Dotted,
    Double,
    Hair,
    MediumDashed,
    DashDot,
    MediumDashDot,
    DashDotDot,
    MediumDashDotDot,
    SlantDashDot
}

/// <summary>One edge of a cell's border: how it is drawn and in what colour.</summary>
/// <param name="Style">The line style. <see cref="CellBorderStyle.None"/> means no edge.</param>
/// <param name="Color">The colour. Transparent means "automatic" — the sheet's ink.</param>
public readonly record struct BorderEdge(CellBorderStyle Style, ArgbColor Color)
{
    /// <summary>
    /// The value a <see cref="CellFormatChange"/> uses to take an edge away, as distinct from null,
    /// which leaves it alone.
    /// </summary>
    public static readonly BorderEdge None = new(CellBorderStyle.None, ArgbColor.Transparent);

    public static BorderEdge Thin(ArgbColor color = default) => new(CellBorderStyle.Thin, color);

    public bool IsVisible => this.Style != CellBorderStyle.None;

    /// <summary>How wide the edge paints, in device-independent pixels.</summary>
    public double Width => this.Style switch
    {
        CellBorderStyle.Medium or CellBorderStyle.MediumDashed or CellBorderStyle.MediumDashDot
            or CellBorderStyle.MediumDashDotDot or CellBorderStyle.SlantDashDot => 2,
        CellBorderStyle.Thick or CellBorderStyle.Double => 3,
        _ => 1
    };
}

/// <summary>A cell's four edges. Null means no edge on that side.</summary>
public sealed record CellBorders(BorderEdge? Left, BorderEdge? Right, BorderEdge? Top, BorderEdge? Bottom)
{
    public static readonly CellBorders None = new(null, null, null, null);

    public bool IsEmpty => this.Left is null && this.Right is null && this.Top is null && this.Bottom is null;
}
