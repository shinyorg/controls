using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shiny.Controls.Office.Shapes;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;

namespace Shiny.Controls.Office.Notebook;

// The on-disk shape of a notebook. Deliberately separate from the model records rather than
// serialising those directly: the model carries computed members, a null OOXML element on the text
// bodies it borrows from the slide side, and byte arrays that belong in the package as files rather
// than as base64 in a manifest. A DTO layer is also what lets the format version independently of the
// types the editor works in.

sealed class NotebookDto
{
    public int Version { get; set; } = NotebookPackage.FormatVersion;
    public string Title { get; set; } = "Notebook";
    public List<SectionDto> Sections { get; set; } = new();
}

sealed class SectionDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Color { get; set; }
    public List<PageRefDto> Pages { get; set; } = new();
}

sealed class PageRefDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset Modified { get; set; }
    public string Rule { get; set; } = nameof(PageRule.Blank);
    public double RuleSpacing { get; set; } = 24;
    public string? RuleColor { get; set; }
    public string? Background { get; set; }
    public double MinWidth { get; set; } = 1100;
    public double MinHeight { get; set; } = 850;
}

sealed class PageDto
{
    public List<ItemDto> Items { get; set; } = new();
}

sealed class ItemDto
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = nameof(NoteItemKind.Text);
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
    public double Rotation { get; set; }
    public string? Geometry { get; set; }
    public FillDto? Fill { get; set; }
    public OutlineDto? Outline { get; set; }
    public TextBodyDto? Text { get; set; }

    /// <summary>The media entry holding the picture bytes, relative to the package root.</summary>
    public string? Media { get; set; }

    public string? MediaContentType { get; set; }
    public StrokeDto? Stroke { get; set; }
    public double CornerRadius { get; set; } = 0.16;
    public bool AutoHeight { get; set; }
    public bool Locked { get; set; }
}

sealed class FillDto
{
    public string? Solid { get; set; }
    public List<GradientStopDto>? Stops { get; set; }
    public double Angle { get; set; }
}

sealed class GradientStopDto
{
    public double Pos { get; set; }
    public string Color { get; set; } = "#FF000000";
}

sealed class OutlineDto
{
    public string Color { get; set; } = "#FF000000";
    public double Width { get; set; } = 1;
    public bool Dashed { get; set; }
}

sealed class TextBodyDto
{
    public List<ParagraphDto> Paragraphs { get; set; } = new();
    public string Anchor { get; set; } = nameof(TextAnchor.Top);
    public double InsetLeft { get; set; } = 9.6;
    public double InsetRight { get; set; } = 9.6;
    public double InsetTop { get; set; } = 4.8;
    public double InsetBottom { get; set; } = 4.8;
    public bool WordWrap { get; set; } = true;
}

sealed class ParagraphDto
{
    public List<RunDto> Runs { get; set; } = new();
    public string Alignment { get; set; } = nameof(TextAlignment.Left);
    public int Level { get; set; }
    public string? Bullet { get; set; }
    public string List { get; set; } = nameof(ListStyle.None);
    public double SpaceBefore { get; set; }
    public double SpaceAfter { get; set; }
    public double LineSpacing { get; set; } = 1.0;
}

sealed class RunDto
{
    public string Text { get; set; } = string.Empty;
    public bool Break { get; set; }
    public string? Font { get; set; }
    public double Size { get; set; } = 11;
    public bool B { get; set; }
    public bool I { get; set; }
    public string U { get; set; } = nameof(UnderlineStyle.None);
    public bool S { get; set; }
    public string Color { get; set; } = "#FF000000";
    public string? Highlight { get; set; }
    public string? Link { get; set; }
    public double BaselineShift { get; set; }
    public double SizeScale { get; set; } = 1;
}

sealed class StrokeDto
{
    /// <summary>
    /// Points flattened to x, y, pressure triples.
    /// </summary>
    /// <remarks>
    /// A stroke is hundreds of points and a page is hundreds of strokes, so the object-per-point form
    /// spends most of a notebook's bytes on repeating the property names <c>x</c>, <c>y</c> and
    /// <c>pressure</c>. Flat is roughly a fifth of the size and parses in one pass.
    /// </remarks>
    public List<double> P { get; set; } = new();

    public string Color { get; set; } = "#FF1A1A1A";
    public double Width { get; set; } = 2;
    public string Tool { get; set; } = nameof(InkTool.Pen);
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(NotebookDto))]
[JsonSerializable(typeof(PageDto))]
partial class NotebookJsonContext : JsonSerializerContext;

/// <summary>Maps between the model records and their on-disk form.</summary>
static class NotebookMapping
{
    public static string ToHex(ArgbColor color)
        => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    public static string? ToHex(ArgbColor? color) => color is { } c ? ToHex(c) : null;

    /// <summary>
    /// Parses <c>#AARRGGBB</c>, and tolerates <c>#RRGGBB</c> and a missing hash.
    /// </summary>
    /// <remarks>
    /// Lenient because these files are meant to be hand-editable. A colour that fails to parse comes
    /// back null rather than throwing, so one bad swatch costs a highlight rather than the notebook.
    /// </remarks>
    public static ArgbColor? ParseColor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var span = text.AsSpan().Trim();
        if (span.Length > 0 && span[0] == '#')
            span = span[1..];

        if (span.Length == 6)
        {
            return uint.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)
                ? ArgbColor.FromUInt32(rgb | 0xFF000000)
                : null;
        }

        if (span.Length != 8)
            return null;

        return uint.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb)
            ? ArgbColor.FromUInt32(argb)
            : null;
    }

    static TEnum ParseEnum<TEnum>(string? text, TEnum fallback) where TEnum : struct, Enum
        => Enum.TryParse<TEnum>(text, ignoreCase: true, out var value) ? value : fallback;

    // ---- write ----

    public static PageRefDto ToRef(NotebookPage page) => new()
    {
        Id = page.Id,
        Title = page.Title,
        Created = page.Created,
        Modified = page.Modified,
        Rule = page.Rule.ToString(),
        RuleSpacing = page.RuleSpacing,
        RuleColor = ToHex(page.RuleColor),
        Background = ToHex(page.Background),
        MinWidth = page.MinWidth,
        MinHeight = page.MinHeight
    };

    public static ItemDto ToDto(NoteItem item, string? mediaPath) => new()
    {
        Id = item.Id,
        Kind = item.Kind.ToString(),
        X = item.X,
        Y = item.Y,
        W = item.Width,
        H = item.Height,
        Rotation = item.Rotation,
        Geometry = item.Kind == NoteItemKind.Shape ? item.Geometry.ToString() : null,
        Fill = ToDto(item.Fill),
        Outline = item.Outline is { } o ? new OutlineDto { Color = ToHex(o.Color), Width = o.Width, Dashed = o.Dashed } : null,
        Text = ToDto(item.Text),
        Media = mediaPath,
        MediaContentType = item.ImageContentType,
        Stroke = ToDto(item.Stroke),
        CornerRadius = item.CornerRadius,
        AutoHeight = item.AutoHeight,
        Locked = item.Locked
    };

    static FillDto? ToDto(ShapeFill fill)
    {
        if (fill.IsEmpty)
            return null;

        return new FillDto
        {
            Solid = ToHex(fill.Solid),
            Angle = fill.GradientAngle,
            Stops = fill.GradientStops.Count == 0
                ? null
                : [.. fill.GradientStops.Select(s => new GradientStopDto { Pos = s.Position, Color = ToHex(s.Color) })]
        };
    }

    static StrokeDto? ToDto(InkStroke? stroke)
    {
        if (stroke is null)
            return null;

        var points = new List<double>(stroke.Points.Count * 3);
        foreach (var point in stroke.Points)
        {
            points.Add(Math.Round(point.X, 2));
            points.Add(Math.Round(point.Y, 2));
            points.Add(Math.Round(point.Pressure, 3));
        }

        return new StrokeDto
        {
            P = points,
            Color = ToHex(stroke.Color),
            Width = stroke.Width,
            Tool = stroke.Tool.ToString()
        };
    }

    static TextBodyDto? ToDto(ShapeTextBody? body)
    {
        if (body is null)
            return null;

        return new TextBodyDto
        {
            Anchor = body.Anchor.ToString(),
            InsetLeft = body.InsetLeft,
            InsetRight = body.InsetRight,
            InsetTop = body.InsetTop,
            InsetBottom = body.InsetBottom,
            WordWrap = body.WordWrap,
            Paragraphs = [.. body.Paragraphs.Select(p => new ParagraphDto
            {
                Alignment = p.Alignment.ToString(),
                Level = p.Level,
                Bullet = p.Bullet,
                List = p.List.ToString(),
                SpaceBefore = p.SpaceBefore,
                SpaceAfter = p.SpaceAfter,
                LineSpacing = p.LineSpacing,
                Runs = [.. p.Runs.Select(ToDto)]
            })]
        };
    }

    static RunDto ToDto(StyledRun run) => new()
    {
        Text = run.Text,
        Break = run.IsBreak,
        Font = run.Style.FontFamily,
        Size = run.Style.FontSize,
        B = run.Style.Bold,
        I = run.Style.Italic,
        U = run.Style.Underline.ToString(),
        S = run.Style.Strike,
        Color = ToHex(run.Style.Color),
        Highlight = ToHex(run.Style.Highlight),
        Link = run.Style.Link,
        BaselineShift = run.Style.BaselineShift,
        SizeScale = run.Style.SizeScale <= 0 ? 1 : run.Style.SizeScale
    };

    // ---- read ----

    public static NotebookPage FromDto(PageRefDto dto)
    {
        var page = new NotebookPage(
            string.IsNullOrWhiteSpace(dto.Id) ? NotebookDocument.NewId() : dto.Id,
            dto.Title)
        {
            Created = dto.Created == default ? DateTimeOffset.UtcNow : dto.Created,
            Modified = dto.Modified == default ? DateTimeOffset.UtcNow : dto.Modified,
            Rule = ParseEnum(dto.Rule, PageRule.Blank),
            RuleSpacing = dto.RuleSpacing <= 0 ? 24 : dto.RuleSpacing,
            RuleColor = ParseColor(dto.RuleColor),
            Background = ParseColor(dto.Background),
            MinWidth = dto.MinWidth <= 0 ? 1100 : dto.MinWidth,
            MinHeight = dto.MinHeight <= 0 ? 850 : dto.MinHeight
        };

        return page;
    }

    public static NoteItem FromDto(ItemDto dto, byte[]? media) => new()
    {
        Id = string.IsNullOrWhiteSpace(dto.Id) ? NotebookDocument.NewId() : dto.Id,
        Kind = ParseEnum(dto.Kind, NoteItemKind.Text),
        X = dto.X,
        Y = dto.Y,
        Width = dto.W,
        Height = dto.H,
        Rotation = dto.Rotation,
        Geometry = ParseEnum(dto.Geometry, ShapeGeometry.Rectangle),
        Fill = FromDto(dto.Fill),
        Outline = dto.Outline is { } o
            ? new ShapeOutline(ParseColor(o.Color) ?? new ArgbColor(255, 0, 0, 0), o.Width, o.Dashed)
            : null,
        Text = FromDto(dto.Text),
        Image = media,
        ImageContentType = dto.MediaContentType,
        Stroke = FromDto(dto.Stroke),
        CornerRadius = dto.CornerRadius,
        AutoHeight = dto.AutoHeight,
        Locked = dto.Locked
    };

    static ShapeFill FromDto(FillDto? dto)
    {
        if (dto is null)
            return ShapeFill.None;

        return new ShapeFill
        {
            Solid = ParseColor(dto.Solid),
            GradientAngle = dto.Angle,
            GradientStops = dto.Stops is null
                ? []
                : [.. dto.Stops.Select(s => (s.Pos, ParseColor(s.Color) ?? ArgbColor.Transparent))]
        };
    }

    static InkStroke? FromDto(StrokeDto? dto)
    {
        if (dto is null)
            return null;

        // Truncating rather than throwing on a partial triple: a stroke with a lost tail is still the
        // stroke the user drew, and refusing the file over three missing numbers is not.
        var count = dto.P.Count / 3;
        var points = new InkPoint[count];
        for (var i = 0; i < count; i++)
            points[i] = new InkPoint(dto.P[i * 3], dto.P[i * 3 + 1], dto.P[i * 3 + 2]);

        return new InkStroke
        {
            Points = points,
            Color = ParseColor(dto.Color) ?? new ArgbColor(255, 0x1A, 0x1A, 0x1A),
            Width = dto.Width <= 0 ? 2 : dto.Width,
            Tool = ParseEnum(dto.Tool, InkTool.Pen)
        };
    }

    static ShapeTextBody? FromDto(TextBodyDto? dto)
    {
        if (dto is null)
            return null;

        return new ShapeTextBody([.. dto.Paragraphs.Select(FromDto)])
        {
            Anchor = ParseEnum(dto.Anchor, TextAnchor.Top),
            InsetLeft = dto.InsetLeft,
            InsetRight = dto.InsetRight,
            InsetTop = dto.InsetTop,
            InsetBottom = dto.InsetBottom,
            WordWrap = dto.WordWrap
        };
    }

    static ShapeParagraph FromDto(ParagraphDto dto)
        => new([.. dto.Runs.Select(FromDto)])
        {
            Alignment = ParseEnum(dto.Alignment, TextAlignment.Left),
            Level = dto.Level,
            Bullet = dto.Bullet,
            List = ParseEnum(dto.List, ListStyle.None),
            SpaceBefore = dto.SpaceBefore,
            SpaceAfter = dto.SpaceAfter,
            LineSpacing = dto.LineSpacing <= 0 ? 1 : dto.LineSpacing
        };

    static StyledRun FromDto(RunDto dto)
        => new(dto.Text, new TextStyle
        {
            FontFamily = string.IsNullOrWhiteSpace(dto.Font) ? "Calibri" : dto.Font,
            FontSize = dto.Size <= 0 ? 11 : dto.Size,
            Bold = dto.B,
            Italic = dto.I,
            Underline = ParseEnum(dto.U, UnderlineStyle.None),
            Strike = dto.S,
            Color = ParseColor(dto.Color) ?? new ArgbColor(255, 0, 0, 0),
            Highlight = ParseColor(dto.Highlight),
            Link = dto.Link,
            BaselineShift = dto.BaselineShift,
            SizeScale = dto.SizeScale <= 0 ? 1 : dto.SizeScale
        })
        { IsBreak = dto.Break };

    public static readonly JsonSerializerOptions Options = new(NotebookJsonContext.Default.Options);
}
