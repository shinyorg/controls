using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Shiny.Controls.Office.Packaging;
using Fill = DocumentFormat.OpenXml.Spreadsheet.Fill;
using Font = DocumentFormat.OpenXml.Spreadsheet.Font;

namespace Shiny.Controls.Office.Spreadsheet;

/// <summary>
/// Flattens a cell's style index into a <see cref="ResolvedFormat"/>.
/// </summary>
/// <remarks>
/// Results are cached per style index. A sheet of 100k cells typically uses a few dozen distinct
/// formats, so resolving on every paint would repeat the same chain walk thousands of times per frame.
/// </remarks>
public sealed class StyleResolver
{
    readonly WorkbookPart workbookPart;
    readonly IUnsupportedFeatureSink unsupported;
    readonly Dictionary<uint, ResolvedFormat> cache = new();
    readonly Dictionary<uint, string> customNumberFormats = new();
    readonly List<ArgbColor> themeColors = new();
    readonly Dictionary<string, ExcelNumberFormat.NumberFormat> formatCache = new(StringComparer.Ordinal);

    internal StyleResolver(WorkbookPart workbookPart, IUnsupportedFeatureSink unsupported)
    {
        this.unsupported = unsupported;
        this.workbookPart = workbookPart;

        foreach (var format in this.Stylesheet?.NumberingFormats?.Elements<NumberingFormat>() ?? Enumerable.Empty<NumberingFormat>())
        {
            if (format.NumberFormatId?.Value is { } id && format.FormatCode?.Value is { } code)
                this.customNumberFormats[id] = code;
        }

        this.LoadThemeColors(workbookPart);
    }

    /// <summary>
    /// The styles part's root, read through the part rather than captured.
    /// </summary>
    /// <remarks>
    /// A workbook can arrive with no styles part at all, and <see cref="StyleWriter"/> adds one the
    /// first time anything is formatted. Holding the element from construction would leave this
    /// resolver looking at null forever, so every index the writer then hands out would resolve to
    /// the default format and the formatting would simply not appear.
    /// </remarks>
    Stylesheet? Stylesheet => this.workbookPart.WorkbookStylesPart?.Stylesheet;

    /// <summary>
    /// Teaches the resolver a number format the <see cref="StyleWriter"/> has just added to the file.
    /// </summary>
    /// <remarks>
    /// The custom formats are read once, when the workbook is opened. A cell given a brand-new
    /// numFmtId after that resolves against a table that has never heard of it and falls back to
    /// General - so applying a currency format would appear to do nothing until the file was closed
    /// and reopened.
    /// </remarks>
    internal void RegisterNumberFormat(uint id, string code) => this.customNumberFormats[id] = code;

    /// <summary>
    /// The id Excel already reserves for a format code, or null when the code needs a custom entry.
    /// </summary>
    /// <remarks>
    /// Reusing a built-in id keeps the file closer to what Excel itself writes, and avoids adding a
    /// numFmts entry for something like <c>0.00%</c> that every reader already knows.
    /// </remarks>
    internal static uint? BuiltInNumberFormatId(string code)
    {
        foreach (var (id, builtIn) in BuiltInNumberFormats)
        {
            if (string.Equals(builtIn, code, StringComparison.Ordinal))
                return id;
        }

        return null;
    }

    public ResolvedFormat Resolve(uint? styleIndex)
    {
        if (styleIndex is not { } index)
            return ResolvedFormat.Default;

        if (this.cache.TryGetValue(index, out var cached))
            return cached;

        var resolved = this.Build(index);
        this.cache[index] = resolved;
        return resolved;
    }

    /// <summary>Formats a value for display using the cell's number format.</summary>
    public string Format(CellValue value, ResolvedFormat format)
    {
        switch (value.Kind)
        {
            case CellValueKind.Blank:
                return string.Empty;

            case CellValueKind.Error:
                return CellValue.ErrorText(value.AsError());

            case CellValueKind.Boolean:
                return value.AsBoolean() ? "TRUE" : "FALSE";
        }

        var code = format.NumberFormatCode;
        if (string.IsNullOrEmpty(code) || code == "General")
        {
            return value.Kind == CellValueKind.Text
                ? value.AsText()
                : FormatGeneral(value.AsNumber());
        }

        if (!this.formatCache.TryGetValue(code, out var numberFormat))
        {
            try
            {
                numberFormat = new ExcelNumberFormat.NumberFormat(code);
            }
            catch (Exception ex)
            {
                this.unsupported.Report(new UnsupportedFeature("styles", "Number format", UnsupportedSeverity.NotRendered, $"{code}: {ex.Message}"));
                numberFormat = null!;
            }

            this.formatCache[code] = numberFormat;
        }

        if (numberFormat is null || !numberFormat.IsValid)
            return value.Kind == CellValueKind.Text ? value.AsText() : FormatGeneral(value.AsNumber());

        var boxed = value.Kind == CellValueKind.Text ? value.AsText() : (object)value.AsNumber();
        return numberFormat.Format(boxed, System.Globalization.CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// Excel's General format: up to 11 significant digits, switching to scientific when the value will
    /// not fit. This is an approximation of a notoriously fiddly rule, not a reproduction of it.
    /// </summary>
    static string FormatGeneral(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return CellValue.ErrorText(CellError.Num);

        if (value == 0)
            return "0";

        var magnitude = Math.Abs(value);
        if (magnitude >= 1e11 || magnitude < 1e-10)
            return value.ToString("0.#####E+00", System.Globalization.CultureInfo.CurrentCulture);

        return value.ToString("0.###########", System.Globalization.CultureInfo.CurrentCulture);
    }

    ResolvedFormat Build(uint styleIndex)
    {
        var cellFormats = this.Stylesheet?.CellFormats;
        if (cellFormats is null || styleIndex >= cellFormats.Count())
            return ResolvedFormat.Default;

        if (cellFormats.ElementAt((int)styleIndex) is not CellFormat cellFormat)
            return ResolvedFormat.Default;

        var result = ResolvedFormat.Default;

        if (cellFormat.NumberFormatId?.Value is { } numberFormatId)
            result = result with { NumberFormatCode = this.NumberFormatCode(numberFormatId) };

        // ApplyFont/ApplyFill are hints, not gates: Excel itself writes cells with the flag absent but
        // the index meaningful, so the index is honoured whenever it is present.
        if (cellFormat.FontId?.Value is { } fontId)
            result = this.ApplyFont(result, fontId);

        if (cellFormat.FillId?.Value is { } fillId)
            result = this.ApplyFill(result, fillId);

        if (cellFormat.Alignment is { } alignment)
            result = ApplyAlignment(result, alignment);

        if (cellFormat.BorderId?.Value is { } borderId and > 0)
            result = this.ApplyBorder(result, borderId);

        return result;
    }

    ResolvedFormat ApplyBorder(ResolvedFormat format, uint borderId)
    {
        var borders = this.Stylesheet?.Borders;
        if (borders is null || borderId >= borders.Count() || borders.ElementAt((int)borderId) is not Border border)
            return format;

        var result = new CellBorders(
            this.EdgeOf(border.LeftBorder),
            this.EdgeOf(border.RightBorder),
            this.EdgeOf(border.TopBorder),
            this.EdgeOf(border.BottomBorder));

        return result.IsEmpty ? format : format with { Borders = result };
    }

    BorderEdge? EdgeOf(BorderPropertiesType? edge)
    {
        // The attribute's own text, not the typed value's ToString(): OpenXml v3's enums are record
        // structs whose ToString() names the type, so a string match against it never succeeds.
        var style = BorderStyleOf(edge?.Style?.InnerText);
        if (style == CellBorderStyle.None)
            return null;

        var color = this.ResolveColor(edge!.Color) ?? ArgbColor.Transparent;
        return new BorderEdge(style, color);
    }

    /// <summary>ST_BorderStyle text to the model's enum. Unknown text reads as no edge.</summary>
    internal static CellBorderStyle BorderStyleOf(string? text) => text switch
    {
        "thin" => CellBorderStyle.Thin,
        "medium" => CellBorderStyle.Medium,
        "thick" => CellBorderStyle.Thick,
        "dashed" => CellBorderStyle.Dashed,
        "dotted" => CellBorderStyle.Dotted,
        "double" => CellBorderStyle.Double,
        "hair" => CellBorderStyle.Hair,
        "mediumDashed" => CellBorderStyle.MediumDashed,
        "dashDot" => CellBorderStyle.DashDot,
        "mediumDashDot" => CellBorderStyle.MediumDashDot,
        "dashDotDot" => CellBorderStyle.DashDotDot,
        "mediumDashDotDot" => CellBorderStyle.MediumDashDotDot,
        "slantDashDot" => CellBorderStyle.SlantDashDot,
        _ => CellBorderStyle.None
    };

    /// <summary>The model's enum back to ST_BorderStyle text.</summary>
    internal static string BorderStyleText(CellBorderStyle style) => style switch
    {
        CellBorderStyle.Thin => "thin",
        CellBorderStyle.Medium => "medium",
        CellBorderStyle.Thick => "thick",
        CellBorderStyle.Dashed => "dashed",
        CellBorderStyle.Dotted => "dotted",
        CellBorderStyle.Double => "double",
        CellBorderStyle.Hair => "hair",
        CellBorderStyle.MediumDashed => "mediumDashed",
        CellBorderStyle.DashDot => "dashDot",
        CellBorderStyle.MediumDashDot => "mediumDashDot",
        CellBorderStyle.DashDotDot => "dashDotDot",
        CellBorderStyle.MediumDashDotDot => "mediumDashDotDot",
        CellBorderStyle.SlantDashDot => "slantDashDot",
        _ => "none"
    };

    /// <summary>
    /// A theme colour by Excel's index: 0/1 background and text, 2/3 the second pair, 4-9 accents one to
    /// six, 10/11 the hyperlink colours. Office's defaults stand in when the workbook has no theme.
    /// </summary>
    public ArgbColor ThemeColor(int index)
    {
        if (index >= 0 && index < this.themeColors.Count)
            return this.themeColors[index];

        uint[] office =
        [
            0xFFFFFFFF, 0xFF000000, 0xFFE7E6E6, 0xFF44546A, 0xFF4472C4, 0xFFED7D31,
            0xFFA5A5A5, 0xFFFFC000, 0xFF5B9BD5, 0xFF70AD47, 0xFF0563C1, 0xFF954F72
        ];

        return ArgbColor.FromUInt32(office[Math.Clamp(index, 0, office.Length - 1)]);
    }

    /// <summary>Resolves an OOXML colour element — rgb, theme with tint, or indexed — against this workbook.</summary>
    internal ArgbColor? ColorOf(ColorType? color) => this.ResolveColor(color);

    /// <summary>
    /// A differential format — what a conditional-format rule or a table style lays over a cell — read
    /// from <c>&lt;dxfs&gt;</c>.
    /// </summary>
    public DxfFormat ResolveDifferential(uint dxfId)
    {
        var dxfs = this.Stylesheet?.DifferentialFormats;
        if (dxfs is null || dxfId >= dxfs.Count())
            return DxfFormat.Empty;

        if (dxfs.ElementAt((int)dxfId) is not DocumentFormat.OpenXml.Spreadsheet.DifferentialFormat dxf)
            return DxfFormat.Empty;

        var result = DxfFormat.Empty;

        if (dxf.Font is { } font)
        {
            result = result with
            {
                Bold = font.GetFirstChild<Bold>() is { } b ? (b.Val is null || b.Val.Value) : null,
                Italic = font.GetFirstChild<Italic>() is { } i ? (i.Val is null || i.Val.Value) : null,
                Strike = font.GetFirstChild<Strike>() is { } s ? (s.Val is null || s.Val.Value) : null,
                Underline = font.GetFirstChild<Underline>() is { } u ? u.Val?.InnerText != "none" : null,
                Foreground = this.ResolveColor(font.GetFirstChild<DocumentFormat.OpenXml.Spreadsheet.Color>())
            };
        }

        if (dxf.Fill?.PatternFill is { } pattern)
        {
            // A dxf fill is the opposite way round to a cell fill: the visible colour of a solid dxf is
            // bgColor, and Excel writes fgColor alongside it only sometimes.
            var color = this.ResolveColor(pattern.BackgroundColor) ?? this.ResolveColor(pattern.ForegroundColor);
            if (color is { } fill)
                result = result with { Background = fill };
        }

        if (dxf.NumberingFormat?.FormatCode?.Value is { } code)
            result = result with { NumberFormatCode = code };

        if (dxf.Border is { } border)
        {
            result = result with
            {
                Borders = new CellBorders(
                    this.EdgeOf(border.LeftBorder),
                    this.EdgeOf(border.RightBorder),
                    this.EdgeOf(border.TopBorder),
                    this.EdgeOf(border.BottomBorder))
            };
        }

        return result;
    }

    /// <summary>Forgets every resolved style, after the stylesheet has been rewritten underneath it.</summary>
    internal void Invalidate() => this.cache.Clear();

    string NumberFormatCode(uint id)
    {
        if (this.customNumberFormats.TryGetValue(id, out var custom))
            return custom;

        return BuiltInNumberFormats.TryGetValue(id, out var builtIn) ? builtIn : string.Empty;
    }

    ResolvedFormat ApplyFont(ResolvedFormat format, uint fontId)
    {
        var fonts = this.Stylesheet?.Fonts;
        if (fonts is null || fontId >= fonts.Count() || fonts.ElementAt((int)fontId) is not Font font)
            return format;

        return format with
        {
            FontName = font.FontName?.Val?.Value ?? format.FontName,
            FontSize = font.FontSize?.Val?.Value ?? format.FontSize,
            Bold = font.Bold is not null && (font.Bold.Val is null || font.Bold.Val.Value),
            Italic = font.Italic is not null && (font.Italic.Val is null || font.Italic.Val.Value),
            Underline = font.Underline is not null && font.Underline.Val?.Value != UnderlineValues.None,
            Strike = font.Strike is not null && (font.Strike.Val is null || font.Strike.Val.Value),
            Foreground = this.ResolveColor(font.Color) ?? format.Foreground
        };
    }

    ResolvedFormat ApplyFill(ResolvedFormat format, uint fillId)
    {
        var fills = this.Stylesheet?.Fills;
        if (fills is null || fillId >= fills.Count() || fills.ElementAt((int)fillId) is not Fill fill)
            return format;

        var pattern = fill.PatternFill;
        if (pattern is null)
            return format;

        var type = pattern.PatternType?.Value ?? PatternValues.None;
        if (type == PatternValues.None)
            return format;

        if (type != PatternValues.Solid)
        {
            // Hatch patterns render as their background colour rather than as the pattern itself.
            this.unsupported.Report(new UnsupportedFeature("styles", "Pattern fill", UnsupportedSeverity.NotRendered, type.ToString()));
        }

        // In a solid fill it is fgColor that carries the visible colour, not bgColor.
        var color = this.ResolveColor(pattern.ForegroundColor) ?? this.ResolveColor(pattern.BackgroundColor);
        return color is null ? format : format with { Background = color.Value };
    }

    static ResolvedFormat ApplyAlignment(ResolvedFormat format, Alignment alignment)
    {
        var horizontal = alignment.Horizontal?.Value switch
        {
            var v when v == HorizontalAlignmentValues.Left => CellHorizontalAlignment.Left,
            var v when v == HorizontalAlignmentValues.Center => CellHorizontalAlignment.Center,
            var v when v == HorizontalAlignmentValues.Right => CellHorizontalAlignment.Right,
            var v when v == HorizontalAlignmentValues.Fill => CellHorizontalAlignment.Fill,
            var v when v == HorizontalAlignmentValues.Justify => CellHorizontalAlignment.Justify,
            var v when v == HorizontalAlignmentValues.CenterContinuous => CellHorizontalAlignment.CenterContinuous,
            var v when v == HorizontalAlignmentValues.Distributed => CellHorizontalAlignment.Distributed,
            _ => CellHorizontalAlignment.General
        };

        var vertical = alignment.Vertical?.Value switch
        {
            var v when v == VerticalAlignmentValues.Top => CellVerticalAlignment.Top,
            var v when v == VerticalAlignmentValues.Center => CellVerticalAlignment.Center,
            var v when v == VerticalAlignmentValues.Justify => CellVerticalAlignment.Justify,
            var v when v == VerticalAlignmentValues.Distributed => CellVerticalAlignment.Distributed,
            _ => CellVerticalAlignment.Bottom
        };

        return format with
        {
            HorizontalAlignment = horizontal,
            VerticalAlignment = vertical,
            WrapText = alignment.WrapText?.Value ?? false,
            Indent = (int)(alignment.Indent?.Value ?? 0)
        };
    }

    ArgbColor? ResolveColor(ColorType? color)
    {
        if (color is null)
            return null;

        if (color.Rgb?.Value is { } hex && TryParseHex(hex, out var parsed))
            return ApplyTint(parsed, color.Tint?.Value ?? 0);

        if (color.Theme?.Value is { } themeIndex && themeIndex < this.themeColors.Count)
            return ApplyTint(this.themeColors[(int)themeIndex], color.Tint?.Value ?? 0);

        if (color.Indexed?.Value is { } indexed)
        {
            if (indexed < (uint)IndexedPalette.Length)
                return ApplyTint(ArgbColor.FromUInt32(IndexedPalette[indexed]), color.Tint?.Value ?? 0);

            // 64/65 are the "system foreground/background" sentinels — deliberately left to the theme.
            return null;
        }

        return null;
    }

    void LoadThemeColors(WorkbookPart workbookPart)
    {
        var scheme = workbookPart.ThemePart?.Theme?.ThemeElements?.ColorScheme;
        if (scheme is null)
            return;

        // Excel's theme indices are not the document order of clrScheme: the first two pairs are
        // swapped, because lt1/dk1 map to background1/text1.
        var ordered = new DocumentFormat.OpenXml.Drawing.Color2Type?[]
        {
            scheme.Light1Color, scheme.Dark1Color, scheme.Light2Color, scheme.Dark2Color,
            scheme.Accent1Color, scheme.Accent2Color, scheme.Accent3Color,
            scheme.Accent4Color, scheme.Accent5Color, scheme.Accent6Color,
            scheme.Hyperlink, scheme.FollowedHyperlinkColor
        };

        foreach (var entry in ordered)
        {
            if (entry?.RgbColorModelHex?.Val?.Value is { } hex && TryParseHex(hex, out var color))
                this.themeColors.Add(color);
            else if (entry?.SystemColor?.LastColor?.Value is { } system && TryParseHex(system, out var systemColor))
                this.themeColors.Add(systemColor);
            else
                this.themeColors.Add(new ArgbColor(255, 0, 0, 0));
        }
    }

    static bool TryParseHex(string hex, out ArgbColor color)
    {
        color = default;
        if (string.IsNullOrEmpty(hex))
            return false;

        var span = hex.AsSpan().TrimStart('#');
        if (span.Length == 6)
        {
            if (!uint.TryParse(span, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
                return false;

            color = ArgbColor.FromUInt32(0xFF000000u | rgb);
            return true;
        }

        if (span.Length == 8)
        {
            if (!uint.TryParse(span, System.Globalization.NumberStyles.HexNumber, null, out var argb))
                return false;

            color = ArgbColor.FromUInt32(argb);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Applies Excel's tint, which lightens toward white for positive values and darkens toward black
    /// for negative ones. This is what "Accent1, Lighter 40%" actually is in the file.
    /// </summary>
    static ArgbColor ApplyTint(ArgbColor color, double tint)
    {
        if (tint == 0)
            return color;

        static byte Scale(byte channel, double tint)
        {
            var value = channel / 255d;
            value = tint > 0
                ? value * (1 - tint) + tint
                : value * (1 + tint);

            return (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
        }

        return color with { R = Scale(color.R, tint), G = Scale(color.G, tint), B = Scale(color.B, tint) };
    }

    /// <summary>The number formats Excel defines implicitly, which never appear in the file.</summary>
    static readonly Dictionary<uint, string> BuiltInNumberFormats = new()
    {
        [0] = "General",
        [1] = "0",
        [2] = "0.00",
        [3] = "#,##0",
        [4] = "#,##0.00",
        [9] = "0%",
        [10] = "0.00%",
        [11] = "0.00E+00",
        [12] = "# ?/?",
        [13] = "# ??/??",
        [14] = "mm-dd-yy",
        [15] = "d-mmm-yy",
        [16] = "d-mmm",
        [17] = "mmm-yy",
        [18] = "h:mm AM/PM",
        [19] = "h:mm:ss AM/PM",
        [20] = "h:mm",
        [21] = "h:mm:ss",
        [22] = "m/d/yy h:mm",
        [37] = "#,##0 ;(#,##0)",
        [38] = "#,##0 ;[Red](#,##0)",
        [39] = "#,##0.00;(#,##0.00)",
        [40] = "#,##0.00;[Red](#,##0.00)",
        [45] = "mm:ss",
        [46] = "[h]:mm:ss",
        [47] = "mmss.0",
        [48] = "##0.0E+0",
        [49] = "@"
    };

    /// <summary>The legacy 56-colour indexed palette, still referenced by files saved from older Excel.</summary>
    static readonly uint[] IndexedPalette =
    [
        0xFF000000, 0xFFFFFFFF, 0xFFFF0000, 0xFF00FF00, 0xFF0000FF, 0xFFFFFF00, 0xFFFF00FF, 0xFF00FFFF,
        0xFF000000, 0xFFFFFFFF, 0xFFFF0000, 0xFF00FF00, 0xFF0000FF, 0xFFFFFF00, 0xFFFF00FF, 0xFF00FFFF,
        0xFF800000, 0xFF008000, 0xFF000080, 0xFF808000, 0xFF800080, 0xFF008080, 0xFFC0C0C0, 0xFF808080,
        0xFF9999FF, 0xFF993366, 0xFFFFFFCC, 0xFFCCFFFF, 0xFF660066, 0xFFFF8080, 0xFF0066CC, 0xFFCCCCFF,
        0xFF000080, 0xFFFF00FF, 0xFFFFFF00, 0xFF00FFFF, 0xFF800080, 0xFF800000, 0xFF008080, 0xFF0000FF,
        0xFF00CCFF, 0xFFCCFFFF, 0xFFCCFFCC, 0xFFFFFF99, 0xFF99CCFF, 0xFFFF99CC, 0xFFCC99FF, 0xFFFFCC99,
        0xFF3366FF, 0xFF33CCCC, 0xFF99CC00, 0xFFFFCC00, 0xFFFF9900, 0xFFFF6600, 0xFF666699, 0xFF969696,
        0xFF003366, 0xFF339966, 0xFF003300, 0xFF333300, 0xFF993300, 0xFF993366, 0xFF333399, 0xFF333333
    ];
}
