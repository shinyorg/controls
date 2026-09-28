using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Fill = DocumentFormat.OpenXml.Spreadsheet.Fill;
using Font = DocumentFormat.OpenXml.Spreadsheet.Font;

namespace Shiny.Controls.Office.Spreadsheet;

/// <summary>
/// The other half of <see cref="StyleResolver"/>: turns a <see cref="ResolvedFormat"/> back into a
/// style index the file can carry.
/// </summary>
/// <remarks>
/// <para>
/// A cell does not store its formatting. It stores one number, an index into <c>cellXfs</c>, and that
/// entry indexes in turn into <c>fonts</c>, <c>fills</c>, <c>borders</c> and <c>numFmts</c>. So
/// bolding one cell is not "set bold on the cell" — it is find or create a font that matches the
/// cell's current one plus bold, find or create a cell format pointing at it, and write that index.
/// </para>
/// <para>
/// Everything is <em>interned</em>: identical formats resolve to the same index. Without that, bolding
/// a thousand cells would append a thousand identical font and cell-format entries, and the styles
/// part would grow without bound across a session of ordinary editing.
/// </para>
/// <para>
/// Entries are only ever appended, never rewritten. Every existing index therefore keeps meaning
/// exactly what it meant — which is what lets a workbook opened from disk be formatted without
/// disturbing the cells nobody touched.
/// </para>
/// </remarks>
public sealed class StyleWriter
{
    /// <summary>The first number-format id Excel leaves to the document. 0-163 are reserved.</summary>
    const uint FirstCustomNumberFormatId = 164;

    readonly WorkbookPart workbookPart;
    readonly StyleResolver resolver;
    readonly Action onChanged;
    readonly Dictionary<string, uint> internedFormats = new(StringComparer.Ordinal);

    Stylesheet? stylesheet;

    internal StyleWriter(WorkbookPart workbookPart, StyleResolver resolver, Action onChanged)
    {
        this.workbookPart = workbookPart;
        this.resolver = resolver;
        this.onChanged = onChanged;
        this.stylesheet = workbookPart.WorkbookStylesPart?.Stylesheet;
    }

    /// <summary>
    /// The index of a <c>cellXf</c> describing <paramref name="format"/>, creating one if the file has
    /// none.
    /// </summary>
    public uint Intern(ResolvedFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);

        // The default format is index 0 in every file Excel writes, and a cell with no style attribute
        // already renders that way - so it is worth short-circuiting rather than interning.
        if (format == ResolvedFormat.Default)
            return 0;

        var key = Key(format);
        if (this.internedFormats.TryGetValue(key, out var cached))
            return cached;

        var sheet = this.EnsureStylesheet();

        var numberFormatId = this.NumberFormatId(sheet, format.NumberFormatCode);
        var fontId = FontId(sheet, format);
        var fillId = FillId(sheet, format);

        var borderId = BorderId(sheet, format.Borders);
        var index = this.CellFormatId(sheet, format, numberFormatId, fontId, fillId, borderId);
        this.internedFormats[key] = index;
        return index;
    }

    /// <summary>
    /// A stable string identifying a format, so identical ones intern to the same index.
    /// </summary>
    /// <remarks>
    /// Joined on a control character rather than on anything printable: a number format code is
    /// arbitrary text, and a separator that can appear inside one lets two different formats collide.
    /// </remarks>
    static string Key(ResolvedFormat format) => string.Join(
        '\u0001',
        format.NumberFormatCode,
        format.FontName,
        format.FontSize.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        format.Bold ? "1" : "0",
        format.Italic ? "1" : "0",
        format.Underline ? "1" : "0",
        format.Strike ? "1" : "0",
        format.Foreground.ToUInt32().ToString(),
        format.Background.ToUInt32().ToString(),
        ((int)format.HorizontalAlignment).ToString(),
        ((int)format.VerticalAlignment).ToString(),
        format.WrapText ? "1" : "0",
        format.Indent.ToString(),
        BorderKey(format.Borders));

    /// <summary>
    /// The styles part, created if the file has none.
    /// </summary>
    /// <remarks>
    /// A workbook can legitimately arrive without one — Excel writes styles.xml always, other producers
    /// do not — and every index written below would then dangle. The minimum Excel accepts is a default
    /// font, the two fills it requires (none and gray125), a default border and a default cell format.
    /// </remarks>
    Stylesheet EnsureStylesheet()
    {
        if (this.stylesheet is not null)
            return this.stylesheet;

        var part = this.workbookPart.WorkbookStylesPart ?? this.workbookPart.AddNewPart<WorkbookStylesPart>();
        part.Stylesheet ??= new Stylesheet(
            new Fonts(new Font(new FontSize { Val = 11d }, new FontName { Val = "Calibri" })) { Count = 1u },
            new Fills(
                new Fill(new PatternFill { PatternType = PatternValues.None }),
                new Fill(new PatternFill { PatternType = PatternValues.Gray125 })) { Count = 2u },
            new Borders(new Border()) { Count = 1u },
            new CellFormats(new CellFormat { NumberFormatId = 0u, FontId = 0u, FillId = 0u, BorderId = 0u }) { Count = 1u });

        this.onChanged();
        this.stylesheet = part.Stylesheet;
        return this.stylesheet;
    }

    // ---- number formats ----

    uint NumberFormatId(Stylesheet sheet, string code)
    {
        if (string.IsNullOrEmpty(code) || code == "General")
            return 0u;

        if (StyleResolver.BuiltInNumberFormatId(code) is { } builtIn)
            return builtIn;

        var formats = sheet.NumberingFormats ??= new NumberingFormats();

        var highest = FirstCustomNumberFormatId - 1;
        foreach (var format in formats.Elements<NumberingFormat>())
        {
            if (string.Equals(format.FormatCode?.Value, code, StringComparison.Ordinal) && format.NumberFormatId?.Value is { } existing)
                return existing;

            highest = Math.Max(highest, format.NumberFormatId?.Value ?? 0u);
        }

        var id = highest + 1;
        formats.AppendChild(new NumberingFormat { NumberFormatId = id, FormatCode = code });
        formats.Count = (uint)formats.Elements<NumberingFormat>().Count();

        // The resolver read the number formats when the workbook was opened. Without this the cell gets
        // its new numFmtId, resolves to an unknown id, and renders as General - the format silently
        // does nothing until the file is closed and reopened.
        this.resolver.RegisterNumberFormat(id, code);
        this.onChanged();
        return id;
    }

    // ---- fonts ----

    static uint FontId(Stylesheet sheet, ResolvedFormat format)
    {
        var fonts = sheet.Fonts ??= new Fonts();
        var index = 0u;

        foreach (var font in fonts.Elements<Font>())
        {
            if (Matches(font, format))
                return index;

            index++;
        }

        var created = new Font();

        if (format.Bold)
            created.AppendChild(new Bold());

        if (format.Italic)
            created.AppendChild(new Italic());

        if (format.Strike)
            created.AppendChild(new Strike());

        if (format.Underline)
            created.AppendChild(new Underline { Val = UnderlineValues.Single });

        created.AppendChild(new FontSize { Val = format.FontSize });

        if (format.Foreground != ResolvedFormat.Default.Foreground)
            created.AppendChild(new DocumentFormat.OpenXml.Spreadsheet.Color { Rgb = HexBinaryOf(format.Foreground) });

        created.AppendChild(new FontName { Val = format.FontName });

        fonts.AppendChild(created);
        fonts.Count = (uint)fonts.Elements<Font>().Count();
        return index;
    }

    /// <summary>
    /// Whether an existing font entry is exactly the one <paramref name="format"/> asks for.
    /// </summary>
    /// <remarks>
    /// A font whose colour is a theme or indexed reference never matches, even when it resolves to the
    /// same pixels. Reusing one would quietly repoint the cell at the theme, so that a later theme
    /// change moved a colour the user picked explicitly.
    /// </remarks>
    static bool Matches(Font font, ResolvedFormat format)
    {
        if ((font.FontName?.Val?.Value ?? ResolvedFormat.Default.FontName) != format.FontName)
            return false;

        if (Math.Abs((font.FontSize?.Val?.Value ?? ResolvedFormat.Default.FontSize) - format.FontSize) > 0.001)
            return false;

        if (IsOn(font.Bold) != format.Bold || IsOn(font.Italic) != format.Italic || IsOn(font.Strike) != format.Strike)
            return false;

        var underlined = font.Underline is not null && font.Underline.Val?.Value != UnderlineValues.None;
        if (underlined != format.Underline)
            return false;

        // Anything the writer never emits - outline, shadow, condense, vertical alignment, a scheme -
        // makes the entry something other than what is being asked for.
        if (font.Outline is not null || font.Shadow is not null || font.Condense is not null ||
            font.Extend is not null || font.VerticalTextAlignment is not null || font.FontScheme is not null ||
            font.FontFamilyNumbering is not null || font.FontCharSet is not null)
        {
            return false;
        }

        if (font.Color is null)
            return format.Foreground == ResolvedFormat.Default.Foreground;

        if (font.Color.Rgb?.Value is not { } hex || font.Color.Tint?.Value is not (null or 0))
            return false;

        return TryParseHex(hex, out var color) && color == format.Foreground;
    }

    static bool IsOn(BooleanPropertyType? property)
        => property is not null && (property.Val is null || property.Val.Value);

    // ---- fills ----

    static uint FillId(Stylesheet sheet, ResolvedFormat format)
    {
        // Index 0 is the "no fill" entry every file has, and it is what a cell with no highlight wants.
        if (format.Background.IsTransparent)
            return 0u;

        var fills = sheet.Fills ??= new Fills();
        var index = 0u;

        foreach (var fill in fills.Elements<Fill>())
        {
            if (Matches(fill, format.Background))
                return index;

            index++;
        }

        fills.AppendChild(new Fill(new PatternFill(
            new ForegroundColor { Rgb = HexBinaryOf(format.Background) },

            // Excel writes bgColor="64" (the system background sentinel) alongside every solid fill it
            // creates, and some readers - Numbers among them - render the pattern wrong without it.
            new BackgroundColor { Indexed = 64u })
        { PatternType = PatternValues.Solid }));

        fills.Count = (uint)fills.Elements<Fill>().Count();
        return index;
    }

    static bool Matches(Fill fill, ArgbColor background)
    {
        if (fill.PatternFill is not { } pattern || pattern.PatternType?.Value != PatternValues.Solid)
            return false;

        if (pattern.ForegroundColor?.Rgb?.Value is not { } hex || pattern.ForegroundColor.Tint?.Value is not (null or 0))
            return false;

        return TryParseHex(hex, out var color) && color == background;
    }

    // ---- borders ----

    static string BorderKey(CellBorders borders)
    {
        if (borders.IsEmpty)
            return string.Empty;

        return string.Join('|', Edge(borders.Left), Edge(borders.Right), Edge(borders.Top), Edge(borders.Bottom));

        static string Edge(BorderEdge? edge)
            => edge is { } e ? $"{(int)e.Style}:{e.Color.ToUInt32()}" : "-";
    }

    /// <summary>
    /// The index of a <c>&lt;border&gt;</c> matching <paramref name="borders"/>, appending one if there is
    /// none.
    /// </summary>
    /// <remarks>
    /// Index 0 is the empty border every file carries, and is what a cell with no edges points at. The
    /// four edges are written in the schema's order — left, right, top, bottom, diagonal — and the
    /// diagonal is written empty, as Excel does, because some readers treat a border without one as
    /// malformed.
    /// </remarks>
    static uint BorderId(Stylesheet sheet, CellBorders borders)
    {
        if (borders.IsEmpty)
            return 0u;

        var list = sheet.Borders ??= new Borders(new Border()) { Count = 1u };
        var index = 0u;

        foreach (var border in list.Elements<Border>())
        {
            if (Matches(border, borders))
                return index;

            index++;
        }

        var created = new Border(
            EdgeElement(new LeftBorder(), borders.Left),
            EdgeElement(new RightBorder(), borders.Right),
            EdgeElement(new TopBorder(), borders.Top),
            EdgeElement(new BottomBorder(), borders.Bottom),
            new DiagonalBorder());

        list.AppendChild(created);
        list.Count = (uint)list.Elements<Border>().Count();
        return index;
    }

    static T EdgeElement<T>(T element, BorderEdge? edge) where T : BorderPropertiesType
    {
        if (edge is not { IsVisible: true } visible)
            return element;

        element.Style = new EnumValue<BorderStyleValues> { InnerText = StyleResolver.BorderStyleText(visible.Style) };

        element.Color = visible.Color.IsTransparent
            ? new DocumentFormat.OpenXml.Spreadsheet.Color { Auto = true }
            : new DocumentFormat.OpenXml.Spreadsheet.Color { Rgb = HexBinaryOf(visible.Color) };

        return element;
    }

    static bool Matches(Border border, CellBorders borders)
    {
        if (border.DiagonalUp is not null || border.DiagonalDown is not null || border.Outline is not null)
            return false;

        if (border.DiagonalBorder?.Style is not null)
            return false;

        return Same(border.LeftBorder, borders.Left) &&
               Same(border.RightBorder, borders.Right) &&
               Same(border.TopBorder, borders.Top) &&
               Same(border.BottomBorder, borders.Bottom);

        static bool Same(BorderPropertiesType? element, BorderEdge? edge)
        {
            var style = StyleResolver.BorderStyleOf(element?.Style?.InnerText);
            if (edge is not { IsVisible: true } visible)
                return style == CellBorderStyle.None;

            if (style != visible.Style)
                return false;

            var color = element!.Color;
            if (visible.Color.IsTransparent)
                return color is null || color.Auto?.Value == true;

            return color?.Rgb?.Value is { } hex && color.Tint is null && TryParseHex(hex, out var parsed) && parsed == visible.Color;
        }
    }

    // ---- differential formats ----

    /// <summary>
    /// The index of a <c>&lt;dxf&gt;</c> for <paramref name="format"/>, appending one when the file has none
    /// that matches. What a conditional-format rule's <c>dxfId</c> points at.
    /// </summary>
    public uint InternDifferential(DxfFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);

        var sheet = this.EnsureStylesheet();
        var created = BuildDxf(format);
        var xml = created.OuterXml;

        var dxfs = sheet.DifferentialFormats;
        if (dxfs is null)
        {
            // The typed setter places it in CT_Stylesheet's sequence - after cellStyles, before
            // tableStyles - which an append would not.
            dxfs = new DifferentialFormats { Count = 0u };
            sheet.DifferentialFormats = dxfs;
        }

        var index = 0u;
        foreach (var existing in dxfs.Elements<DifferentialFormat>())
        {
            if (existing.OuterXml == xml)
                return index;

            index++;
        }

        dxfs.AppendChild(created);
        dxfs.Count = (uint)dxfs.Elements<DifferentialFormat>().Count();
        this.onChanged();
        return index;
    }

    static DifferentialFormat BuildDxf(DxfFormat format)
    {
        var dxf = new DifferentialFormat();

        if (format.Bold is not null || format.Italic is not null || format.Strike is not null ||
            format.Underline is not null || format.Foreground is not null)
        {
            var font = new Font();
            if (format.Bold is { } bold)
                font.AppendChild(new Bold { Val = bold ? null : BooleanValue.FromBoolean(false) });

            if (format.Italic is { } italic)
                font.AppendChild(new Italic { Val = italic ? null : BooleanValue.FromBoolean(false) });

            if (format.Strike is { } strike)
                font.AppendChild(new Strike { Val = strike ? null : BooleanValue.FromBoolean(false) });

            if (format.Underline is { } underline)
                font.AppendChild(new Underline { Val = underline ? UnderlineValues.Single : UnderlineValues.None });

            if (format.Foreground is { } color)
                font.AppendChild(new DocumentFormat.OpenXml.Spreadsheet.Color { Rgb = HexBinaryOf(color) });

            dxf.Font = font;
        }

        if (format.NumberFormatCode is { } code)
            dxf.NumberingFormat = new NumberingFormat { NumberFormatId = 164u, FormatCode = code };

        if (format.Background is { } background)
        {
            // In a dxf the visible colour of a solid fill is bgColor - the reverse of a cell fill.
            dxf.Fill = new Fill(new PatternFill(new BackgroundColor { Rgb = HexBinaryOf(background) }));
        }

        if (format.Borders is { IsEmpty: false } borders)
        {
            dxf.Border = new Border(
                EdgeElement(new LeftBorder(), borders.Left),
                EdgeElement(new RightBorder(), borders.Right),
                EdgeElement(new TopBorder(), borders.Top),
                EdgeElement(new BottomBorder(), borders.Bottom));
        }

        return dxf;
    }

    // ---- cell formats ----

    uint CellFormatId(Stylesheet sheet, ResolvedFormat format, uint numberFormatId, uint fontId, uint fillId, uint borderId)
    {
        var formats = sheet.CellFormats ??= new CellFormats();
        var index = 0u;

        foreach (var candidate in formats.Elements<CellFormat>())
        {
            if (Matches(candidate, format, numberFormatId, fontId, fillId, borderId))
                return index;

            index++;
        }

        var created = new CellFormat
        {
            NumberFormatId = numberFormatId,
            FontId = fontId,
            FillId = fillId,
            BorderId = borderId,
            ApplyBorder = borderId != 0 ? BooleanValue.FromBoolean(true) : null,
            ApplyNumberFormat = numberFormatId != 0,
            ApplyFont = true,
            ApplyFill = fillId != 0
        };

        if (NeedsAlignment(format))
        {
            created.ApplyAlignment = true;
            created.Alignment = AlignmentOf(format);
        }

        formats.AppendChild(created);
        formats.Count = (uint)formats.Elements<CellFormat>().Count();
        this.onChanged();
        return index;
    }

    static bool Matches(CellFormat candidate, ResolvedFormat format, uint numberFormatId, uint fontId, uint fillId, uint borderId)
    {
        if ((candidate.NumberFormatId?.Value ?? 0u) != numberFormatId ||
            (candidate.FontId?.Value ?? 0u) != fontId ||
            (candidate.FillId?.Value ?? 0u) != fillId ||
            (candidate.BorderId?.Value ?? 0u) != borderId)
        {
            return false;
        }

        // A named-style cell format or a quote-prefixed one means more than its indices say, so it is
        // never the entry to reuse even when every index lines up.
        if (candidate.FormatId?.Value is not (null or 0u) || candidate.QuotePrefix?.Value == true || candidate.PivotButton?.Value == true)
            return false;

        if (candidate.Protection is not null)
            return false;

        var alignment = candidate.Alignment;
        if (alignment is null)
            return !NeedsAlignment(format);

        // Rotation, shrink-to-fit and reading order are carried by files this writer never produced;
        // an entry with any of them is not the plain alignment being asked for.
        if (alignment.TextRotation is not null || alignment.ShrinkToFit is not null ||
            alignment.ReadingOrder is not null || alignment.JustifyLastLine is not null)
        {
            return false;
        }

        var target = AlignmentOf(format);
        return alignment.Horizontal?.Value == target.Horizontal?.Value &&
               alignment.Vertical?.Value == target.Vertical?.Value &&
               (alignment.WrapText?.Value ?? false) == format.WrapText &&
               (alignment.Indent?.Value ?? 0u) == (uint)format.Indent;
    }

    static bool NeedsAlignment(ResolvedFormat format)
        => format.HorizontalAlignment != CellHorizontalAlignment.General ||
           format.VerticalAlignment != CellVerticalAlignment.Bottom ||
           format.WrapText ||
           format.Indent > 0;

    static Alignment AlignmentOf(ResolvedFormat format)
    {
        var alignment = new Alignment();

        // General and Bottom are the schema defaults; writing them explicitly is noise Excel does not
        // produce, and it would stop an entry matching the ones it does.
        if (format.HorizontalAlignment != CellHorizontalAlignment.General)
            alignment.Horizontal = HorizontalOf(format.HorizontalAlignment);

        if (format.VerticalAlignment != CellVerticalAlignment.Bottom)
            alignment.Vertical = VerticalOf(format.VerticalAlignment);

        if (format.WrapText)
            alignment.WrapText = true;

        if (format.Indent > 0)
            alignment.Indent = (uint)format.Indent;

        return alignment;
    }

    static HorizontalAlignmentValues HorizontalOf(CellHorizontalAlignment alignment) => alignment switch
    {
        CellHorizontalAlignment.Left => HorizontalAlignmentValues.Left,
        CellHorizontalAlignment.Center => HorizontalAlignmentValues.Center,
        CellHorizontalAlignment.Right => HorizontalAlignmentValues.Right,
        CellHorizontalAlignment.Fill => HorizontalAlignmentValues.Fill,
        CellHorizontalAlignment.Justify => HorizontalAlignmentValues.Justify,
        CellHorizontalAlignment.CenterContinuous => HorizontalAlignmentValues.CenterContinuous,
        CellHorizontalAlignment.Distributed => HorizontalAlignmentValues.Distributed,
        _ => HorizontalAlignmentValues.General
    };

    static VerticalAlignmentValues VerticalOf(CellVerticalAlignment alignment) => alignment switch
    {
        CellVerticalAlignment.Top => VerticalAlignmentValues.Top,
        CellVerticalAlignment.Center => VerticalAlignmentValues.Center,
        CellVerticalAlignment.Justify => VerticalAlignmentValues.Justify,
        CellVerticalAlignment.Distributed => VerticalAlignmentValues.Distributed,
        _ => VerticalAlignmentValues.Bottom
    };

    static HexBinaryValue HexBinaryOf(ArgbColor color)
        => HexBinaryValue.FromString($"{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}");

    static bool TryParseHex(string hex, out ArgbColor color)
    {
        color = default;
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
}
