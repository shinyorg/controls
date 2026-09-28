namespace Shiny.Controls.Office.Spreadsheet;

/// <summary>
/// A differential format: the handful of properties a conditional-format rule or a table style lays
/// over a cell, each one null when the rule leaves it alone.
/// </summary>
/// <remarks>
/// Not a <see cref="ResolvedFormat"/>. A rule that says "red fill" says nothing about the font, and
/// modelling it as a whole format would have it reset every cell it matched to Calibri 11.
/// </remarks>
public sealed record DxfFormat
{
    public static readonly DxfFormat Empty = new();

    public bool? Bold { get; init; }
    public bool? Italic { get; init; }
    public bool? Underline { get; init; }
    public bool? Strike { get; init; }
    public ArgbColor? Foreground { get; init; }
    public ArgbColor? Background { get; init; }
    public string? NumberFormatCode { get; init; }
    public CellBorders? Borders { get; init; }

    public bool IsEmpty => this == Empty;

    /// <summary>Lays this over a resolved format.</summary>
    public ResolvedFormat ApplyTo(ResolvedFormat format) => format with
    {
        Bold = this.Bold ?? format.Bold,
        Italic = this.Italic ?? format.Italic,
        Underline = this.Underline ?? format.Underline,
        Strike = this.Strike ?? format.Strike,
        Foreground = this.Foreground ?? format.Foreground,
        Background = this.Background ?? format.Background,
        NumberFormatCode = this.NumberFormatCode ?? format.NumberFormatCode,
        Borders = this.Borders is { IsEmpty: false } borders ? borders : format.Borders
    };

    // ---- the presets Excel's Highlight Cells Rules offer ----

    /// <summary>Light red fill with dark red text — Excel's default highlight.</summary>
    public static readonly DxfFormat LightRedFill = new()
    {
        Background = new ArgbColor(255, 0xFF, 0xC7, 0xCE),
        Foreground = new ArgbColor(255, 0x9C, 0x00, 0x06)
    };

    /// <summary>Yellow fill with dark yellow text.</summary>
    public static readonly DxfFormat YellowFill = new()
    {
        Background = new ArgbColor(255, 0xFF, 0xEB, 0x9C),
        Foreground = new ArgbColor(255, 0x9C, 0x57, 0x00)
    };

    /// <summary>Green fill with dark green text.</summary>
    public static readonly DxfFormat GreenFill = new()
    {
        Background = new ArgbColor(255, 0xC6, 0xEF, 0xCE),
        Foreground = new ArgbColor(255, 0x00, 0x61, 0x00)
    };

    /// <summary>Light red fill only.</summary>
    public static readonly DxfFormat LightRedFillOnly = new() { Background = new ArgbColor(255, 0xFF, 0xC7, 0xCE) };

    /// <summary>Red text only.</summary>
    public static readonly DxfFormat RedText = new() { Foreground = new ArgbColor(255, 0x9C, 0x00, 0x06) };

    /// <summary>The presets by the names Excel's dialog lists them under.</summary>
    public static IReadOnlyList<(string Name, DxfFormat Format)> Presets { get; } =
    [
        ("Light Red Fill with Dark Red Text", LightRedFill),
        ("Yellow Fill with Dark Yellow Text", YellowFill),
        ("Green Fill with Dark Green Text", GreenFill),
        ("Light Red Fill", LightRedFillOnly),
        ("Red Text", RedText)
    ];
}
