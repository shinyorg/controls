using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Shiny.Controls.Office.Editing;
using D = DocumentFormat.OpenXml.Drawing;
using PSlide = DocumentFormat.OpenXml.Presentation.Slide;

namespace Shiny.Controls.Office.Presentation;

/// <summary>What the Header &amp; Footer dialog turns on.</summary>
public sealed record SlideHeaderFooter
{
    /// <summary>Show the date placeholder.</summary>
    public bool DateAndTime { get; init; }

    /// <summary>A fixed date to show; null for one that updates each time the deck is shown.</summary>
    public string? FixedDate { get; init; }

    /// <summary>The auto-updating date's format (a .NET format string); the field PowerPoint writes is <c>datetime1</c>.</summary>
    public string DateFormat { get; init; } = "d";

    public bool SlideNumber { get; init; }

    public bool Footer { get; init; }

    public string FooterText { get; init; } = string.Empty;

    /// <summary>Leave the title slide bare, as PowerPoint's "Don't show on title slide" does.</summary>
    public bool NotOnTitleSlide { get; init; }

    /// <summary>What a slide currently shows — for filling the dialog in.</summary>
    public static SlideHeaderFooter Of(Slide slide) => new()
    {
        DateAndTime = slide.Shapes.Any(x => x.IsEditable && x.PlaceholderType == "dt"),
        SlideNumber = slide.Shapes.Any(x => x.IsEditable && x.PlaceholderType == "sldNum"),
        Footer = slide.Shapes.Any(x => x.IsEditable && x.PlaceholderType == "ftr"),
        FooterText = slide.Shapes.FirstOrDefault(x => x.IsEditable && x.PlaceholderType == "ftr")?.Text?.PlainText ?? string.Empty
    };
}

/// <summary>The fields a caret can insert — Insert ▸ Slide Number, Insert ▸ Date &amp; Time.</summary>
public enum SlideFieldKind
{
    SlideNumber,
    DateTime
}

/// <summary>
/// Puts the date, slide number and footer placeholders on slides, or takes them off.
/// </summary>
/// <remarks>
/// <para>
/// These are placeholders like any other — <c>dt</c>, <c>sldNum</c> and <c>ftr</c> — whose position and
/// style come from the layout (or the master, when the layout has none). A slide shows one only if it
/// carries it, which is why New Slide leaves them off and this puts them on per slide, the way
/// PowerPoint's dialog does for "Apply to All".
/// </para>
/// <para>
/// The slide number is a real <c>a:fld type="slidenum"</c>, cached with the right number, so PowerPoint
/// renumbers it when slides move; an auto-updating date is <c>type="datetime1"</c> likewise.
/// </para>
/// </remarks>
public sealed record ApplyHeaderFooterCommand(IReadOnlyList<int> Slides, SlideHeaderFooter Settings) : SlideCommand
{
    public override string Name => "Header & Footer";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        var inverses = new List<IEditCommand<SlideDeck>>();

        foreach (var index in this.Slides.Distinct())
        {
            if (context.PartAt(index) is not { Slide: { } slide } part || context.TreeAt(index) is not { } tree)
                continue;

            inverses.Add(RestoreSlideTreeCommand.Capture(context, index, this.Name));

            var isTitle = slide.CommonSlideData?.ShapeTree?.Descendants<PlaceholderShape>()
                .Any(x => x.Type?.Value == PlaceholderValues.CenteredTitle) == true;
            var bare = this.Settings.NotOnTitleSlide && isTitle;

            Set(tree, part, "dt", !bare && this.Settings.DateAndTime, index);
            Set(tree, part, "sldNum", !bare && this.Settings.SlideNumber, index);
            Set(tree, part, "ftr", !bare && this.Settings.Footer, index);
            context.Reproject(index);
        }

        inverses.Reverse();
        return inverses.Count == 0 ? new NoOpSlideCommand() : new CompositeCommand<SlideDeck>(this.Name, inverses);
    }

    void Set(ShapeTree tree, SlidePart part, string type, bool on, int index)
    {
        var existing = tree.Elements<Shape>().FirstOrDefault(x => OoxmlUnits.EnumAttribute(x.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape, "type") == type);

        if (!on)
        {
            existing?.Remove();
            return;
        }

        var shape = existing;
        if (shape is null)
        {
            var template = Template(part, type);
            if (template?.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape is not { } source)
                return;

            var placeholder = (PlaceholderShape)source.CloneNode(true);
            placeholder.HasCustomPrompt = null;

            shape = new Shape(
                new NonVisualShapeProperties(
                    new NonVisualDrawingProperties
                    {
                        Id = SlideObjects.NextShapeId(tree),
                        Name = type switch { "dt" => "Date Placeholder", "sldNum" => "Slide Number Placeholder", _ => "Footer Placeholder" }
                    },
                    new NonVisualShapeDrawingProperties(new D.ShapeLocks { NoGrouping = true }),
                    new ApplicationNonVisualDrawingProperties(placeholder)),
                new ShapeProperties(),
                new TextBody(new D.BodyProperties(), new D.ListStyle()));

            tree.AppendChild(shape);
        }

        var body = shape.TextBody ??= new TextBody(new D.BodyProperties(), new D.ListStyle());
        foreach (var paragraph in body.Elements<D.Paragraph>().ToList())
            paragraph.Remove();

        var content = new D.Paragraph();
        switch (type)
        {
            case "sldNum":
                content.AppendChild(SlideFields.Build(SlideFieldKind.SlideNumber, index + 1, null));
                break;

            case "dt" when this.Settings.FixedDate is { Length: > 0 } fixedDate:
                content.AppendChild(new D.Run(new D.RunProperties { Language = "en-US" }, new D.Text(fixedDate)));
                break;

            case "dt":
                content.AppendChild(SlideFields.Build(SlideFieldKind.DateTime, index + 1, this.Settings.DateFormat));
                break;

            default:
                if (this.Settings.FooterText.Length > 0)
                    content.AppendChild(new D.Run(new D.RunProperties { Language = "en-US" }, new D.Text(this.Settings.FooterText)));
                break;
        }

        content.AppendChild(new D.EndParagraphRunProperties { Language = "en-US" });
        body.AppendChild(content);
    }

    /// <summary>The layout's placeholder of a type, or the master's when the layout has none.</summary>
    static Shape? Template(SlidePart part, string type)
    {
        static Shape? In(OpenXmlElement? tree, string type)
            => tree?.Descendants<Shape>().FirstOrDefault(x => OoxmlUnits.EnumAttribute(x.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape, "type") == type);

        return In(part.SlideLayoutPart?.SlideLayout?.CommonSlideData?.ShapeTree, type)
            ?? In(part.SlideLayoutPart?.SlideMasterPart?.SlideMaster?.CommonSlideData?.ShapeTree, type);
    }
}

/// <summary>Building the field runs.</summary>
static class SlideFields
{
    public static D.Field Build(SlideFieldKind kind, int slideNumber, string? dateFormat)
    {
        var field = new D.Field { Id = "{" + Guid.NewGuid().ToString().ToUpperInvariant() + "}", Type = kind == SlideFieldKind.SlideNumber ? "slidenum" : "datetime1" };
        field.AppendChild(new D.RunProperties { Language = "en-US" });
        field.AppendChild(new D.Text(kind == SlideFieldKind.SlideNumber
            ? slideNumber.ToString(CultureInfo.InvariantCulture)
            : DateTime.Now.ToString(dateFormat ?? "d", CultureInfo.CurrentCulture)));

        return field;
    }
}

/// <summary>Inserts a slide-number or date field at a caret.</summary>
public sealed record InsertSlideFieldCommand(SlidePosition At, SlideFieldKind Kind, string? DateFormat = null) : SlideCommand
{
    public override string Name => this.Kind == SlideFieldKind.SlideNumber ? "Insert slide number" : "Insert date";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        var restore = CaptureShape(context, this.At.Slide, this.At.Shape);
        if (TextAt(context, this.At)?.Paragraphs.ElementAtOrDefault(this.At.Paragraph)?.Element is not { } paragraph)
            return new NoOpSlideCommand();

        // Split at the caret, then put the field between the halves.
        var tail = ShapeTextEditor.Split(paragraph, this.At.Offset);
        paragraph.InsertAfterSelf(tail);
        var field = SlideFields.Build(this.Kind, this.At.Slide + 1, this.DateFormat);

        var properties = paragraph.Elements<D.Run>().LastOrDefault()?.RunProperties?.CloneNode(true) as D.RunProperties;
        if (properties is not null)
            field.RunProperties = properties;

        var mark = paragraph.GetFirstChild<D.EndParagraphRunProperties>();
        if (mark is not null)
            paragraph.InsertBefore(field, mark);
        else
            paragraph.AppendChild(field);

        ShapeTextEditor.Merge(paragraph, tail);
        context.Reproject(this.At.Slide);
        return restore;
    }
}

/// <summary>A slide as the Outline view shows it: its title and its body lines with their levels.</summary>
public sealed record SlideOutlineEntry(int Slide, string Title, IReadOnlyList<(int Level, string Text)> Body)
{
    /// <summary>Reads one slide's outline from its title and first body placeholder.</summary>
    public static SlideOutlineEntry Of(Slide slide, int index)
    {
        var title = slide.Shapes.FirstOrDefault(x => x.IsEditable && x.PlaceholderType is "title" or "ctrTitle");
        var body = slide.Shapes.FirstOrDefault(x => x.IsEditable && x.PlaceholderType is "body" or "obj" or "subTitle");

        return new SlideOutlineEntry(
            index,
            title?.Text?.PlainText.Replace(Environment.NewLine, " ", StringComparison.Ordinal) ?? string.Empty,
            body?.Text?.Paragraphs.Select(p => (p.Level, p.PlainText)).Where(x => x.PlainText.Length > 0).ToList() ?? []);
    }

    /// <summary>The body as outline text: one line per paragraph, a tab per level.</summary>
    public string BodyText => string.Join("\n", this.Body.Select(x => new string('\t', x.Level) + x.Text));

    /// <summary>Parses outline text back into levelled lines — leading tabs are the level.</summary>
    public static IReadOnlyList<(int Level, string Text)> ParseBody(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => (Level: Math.Min(8, line.TakeWhile(c => c == '\t').Count()), Text: line.TrimStart('\t')))
            .Where(x => x.Text.Length > 0)
            .ToList();
}

/// <summary>
/// Rewrites a slide's title and body from the Outline view.
/// </summary>
/// <remarks>
/// Paragraphs are rebuilt, but each keeps the formatting of the paragraph it replaces (or of the last
/// one), so retyping a heading in the outline does not strip its bold. A slide without the placeholder
/// the outline is writing into gets nothing — the outline shows only what is there.
/// </remarks>
public sealed record SetSlideOutlineCommand(int Slide, string? Title, IReadOnlyList<(int Level, string Text)>? Body) : SlideCommand
{
    public override string Name => "Outline";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (context.Slides.ElementAtOrDefault(this.Slide) is not { } slide || context.TreeAt(this.Slide) is null)
            return new NoOpSlideCommand();

        var inverse = RestoreSlideTreeCommand.Capture(context, this.Slide, this.Name);
        var changed = false;

        if (this.Title is not null &&
            slide.Shapes.FirstOrDefault(x => x.IsEditable && x.PlaceholderType is "title" or "ctrTitle")?.Text?.Element is { } titleBody)
        {
            Rewrite(titleBody, [(0, this.Title)]);
            changed = true;
        }

        if (this.Body is not null &&
            slide.Shapes.FirstOrDefault(x => x.IsEditable && x.PlaceholderType is "body" or "obj" or "subTitle")?.Text?.Element is { } body)
        {
            Rewrite(body, this.Body);
            changed = true;
        }

        if (!changed)
            return new NoOpSlideCommand();

        context.Reproject(this.Slide);
        return inverse;
    }

    static void Rewrite(OpenXmlCompositeElement body, IReadOnlyList<(int Level, string Text)> lines)
    {
        var old = body.Elements<D.Paragraph>().ToList();
        var template = old.FirstOrDefault();

        foreach (var paragraph in old)
            paragraph.Remove();

        if (lines.Count == 0)
            lines = [(0, string.Empty)];

        for (var i = 0; i < lines.Count; i++)
        {
            var source = old.ElementAtOrDefault(i) ?? old.LastOrDefault() ?? template;
            var paragraph = new D.Paragraph();

            var properties = source?.ParagraphProperties?.CloneNode(true) as D.ParagraphProperties ?? new D.ParagraphProperties();
            properties.Level = lines[i].Level == 0 ? null : lines[i].Level;
            if (properties.HasAttributes || properties.HasChildren)
                paragraph.AppendChild(properties);

            var runProperties = source?.Elements<D.Run>().FirstOrDefault()?.RunProperties?.CloneNode(true) as D.RunProperties
                ?? new D.RunProperties { Language = "en-US" };

            if (lines[i].Text.Length > 0)
                paragraph.AppendChild(new D.Run(runProperties, new D.Text(lines[i].Text)));

            paragraph.AppendChild(source?.GetFirstChild<D.EndParagraphRunProperties>()?.CloneNode(true) as D.EndParagraphRunProperties
                ?? new D.EndParagraphRunProperties { Language = "en-US" });

            body.AppendChild(paragraph);
        }
    }
}
