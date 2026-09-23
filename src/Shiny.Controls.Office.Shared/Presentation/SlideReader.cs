using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Shiny.Controls.Office.Packaging;
using Shiny.Controls.Office.Shapes;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;
using D = DocumentFormat.OpenXml.Drawing;

namespace Shiny.Controls.Office.Presentation;

/// <summary>
/// Reads one slide, resolving every shape through its layout and master.
/// </summary>
sealed class SlideReader
{
    readonly SlidePart part;
    readonly IUnsupportedFeatureSink unsupported;
    readonly DrawingReader drawing;
    readonly SlideLayoutPart? layout;
    readonly SlideMasterPart? master;

    public SlideReader(SlidePart part, IUnsupportedFeatureSink unsupported)
    {
        this.part = part;
        this.unsupported = unsupported;
        this.layout = part.SlideLayoutPart;
        this.master = this.layout?.SlideMasterPart;

        var themePart = this.master?.ThemePart;
        this.drawing = new DrawingReader(ThemeColors.From(themePart));
    }

    public Slide Read(int number)
    {
        var shapes = new List<SlideShape>();

        // Layout and master shapes paint underneath the slide's own, and only the non-placeholder ones:
        // a placeholder on the master is a template for the slide's content, not content itself.
        if (this.master?.SlideMaster?.CommonSlideData?.ShapeTree is { } masterTree && this.ShowsMasterShapes())
            shapes.AddRange(this.ReadTree(masterTree, decorativeOnly: true));

        if (this.layout?.SlideLayout?.CommonSlideData?.ShapeTree is { } layoutTree)
            shapes.AddRange(this.ReadTree(layoutTree, decorativeOnly: true));

        if (this.part.Slide?.CommonSlideData?.ShapeTree is { } tree)
            shapes.AddRange(this.ReadTree(tree, decorativeOnly: false));

        var title = this.part.Slide?.CommonSlideData?.ShapeTree?
            .Descendants<Shape>()
            .FirstOrDefault(IsTitlePlaceholder)?
            .TextBody?.InnerText;

        return new Slide
        {
            Number = number,
            Shapes = shapes,
            Background = this.ReadBackground(),
            Title = string.IsNullOrWhiteSpace(title) ? null : title,
            Notes = this.ReadNotes()
        };
    }

    bool ShowsMasterShapes() => this.layout?.SlideLayout?.ShowMasterShapes?.Value ?? true;

    static bool IsTitlePlaceholder(Shape shape)
    {
        var type = shape.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?
            .PlaceholderShape?.Type?.Value;

        return type == PlaceholderValues.Title || type == PlaceholderValues.CenteredTitle;
    }

    IEnumerable<SlideShape> ReadTree(OpenXmlElement tree, bool decorativeOnly)
        => this.ReadTree(tree, decorativeOnly, ChildSpace.Identity, null);

    IEnumerable<SlideShape> ReadTree(OpenXmlElement tree, bool decorativeOnly, ChildSpace space, OpenXmlElement? group)
        => this.ReadTreeLocal(tree, decorativeOnly, space, group);

    /// <summary>
    /// Maps a shape read in a group's child space into slide space, remembering the mapping so a move
    /// can be written back in the group's units.
    /// </summary>
    static SlideShape Place(SlideShape shape, ChildSpace space, OpenXmlElement? group)
    {
        if (group is null)
            return shape;

        var (x, y, w, h) = space.ToSlide(shape.X, shape.Y, shape.Width, shape.Height);
        return shape with { X = x, Y = y, Width = w, Height = h, Space = space, Group = group };
    }

    IEnumerable<SlideShape> ReadTreeLocal(OpenXmlElement tree, bool decorativeOnly, ChildSpace space, OpenXmlElement? parentGroup)
    {
        // decorativeOnly means this tree is a layout or master, whose shapes belong to every slide
        // using it rather than to this one - so nothing read from here is editable.
        var editable = !decorativeOnly;

        foreach (var element in tree.ChildElements)
        {
            switch (element)
            {
                case Shape shape:
                    // On a layout or master, only non-placeholder shapes are decoration to inherit;
                    // placeholders there are templates that the slide fills in.
                    if (decorativeOnly && shape.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape is not null)
                        continue;

                    var read = this.ReadShape(shape);
                    if (read is not null)
                        yield return Place(read with { IsEditable = editable, Element = editable ? shape : null }, space, parentGroup);

                    break;

                case Picture picture:
                    var image = this.ReadPicture(picture);
                    if (image is not null)
                        yield return Place(image with { IsEditable = editable, Element = editable ? picture : null }, space, parentGroup);

                    break;

                case GraphicFrame frame:
                    var table = this.ReadTable(frame);
                    if (table is not null)
                        yield return Place(table with { IsEditable = editable, Element = editable ? frame : null }, space, parentGroup);

                    break;

                case GroupShape group:
                    // A group has a coordinate space of its own: its children are written in
                    // chOff/chExt units and drawn scaled into off/ext. Each child is mapped into
                    // slide space and keeps the mapping, so a move can be written back in the
                    // group's units.
                    var groupTransform = group.GroupShapeProperties?.TransformGroup;
                    var inner = ChildSpaceOf(groupTransform).Then(space);

                    foreach (var child in this.ReadTree(group, decorativeOnly, inner, group))
                        yield return child with { IsEditable = editable && child.IsEditable, Element = editable ? child.Element : null };

                    // The group's own entry comes after its children, so a click finds it first.
                    if (groupTransform?.Offset is { } offset && groupTransform.Extents is { } extents)
                    {
                        var (gx, gy, gw, gh) = space.ToSlide(
                            OoxmlUnits.EmuToPixels(offset.X?.Value ?? 0),
                            OoxmlUnits.EmuToPixels(offset.Y?.Value ?? 0),
                            OoxmlUnits.EmuToPixels(extents.Cx?.Value ?? 0),
                            OoxmlUnits.EmuToPixels(extents.Cy?.Value ?? 0));

                        yield return new SlideShape
                        {
                            X = gx,
                            Y = gy,
                            Width = gw,
                            Height = gh,
                            Geometry = ShapeGeometry.None,
                            IsGroup = true,
                            IsEditable = editable,
                            Element = editable ? group : null,
                            Group = parentGroup,
                            Space = space,
                            Name = group.NonVisualGroupShapeProperties?.NonVisualDrawingProperties?.Name?.Value
                        };
                    }

                    break;

                case ConnectionShape connection:
                    var line = this.ReadConnection(connection);
                    if (line is not null)
                        yield return Place(line with { IsEditable = editable, Element = editable ? connection : null }, space, parentGroup);

                    break;
            }
        }
    }

    SlideShape? ReadShape(Shape shape)
    {
        var placeholder = shape.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape;
        var inherited = placeholder is null ? null : this.FindInheritedPlaceholder(placeholder);

        var transform = shape.ShapeProperties?.Transform2D ?? inherited?.ShapeProperties?.Transform2D;
        if (transform is null)
            return null;

        var offset = transform.Offset;
        var extents = transform.Extents;
        if (offset is null || extents is null)
            return null;

        var preset = OoxmlUnits.EnumAttribute(shape.ShapeProperties?.GetFirstChild<D.PresetGeometry>(), "prst")
            ?? OoxmlUnits.EnumAttribute(inherited?.ShapeProperties?.GetFirstChild<D.PresetGeometry>(), "prst");

        if (!DrawingReader.IsKnownGeometry(preset))
        {
            this.unsupported.Report(new UnsupportedFeature(
                "slide", "Preset shape", UnsupportedSeverity.NotRendered,
                $"'{preset}' is drawn as a rectangle."));
        }

        var fill = this.drawing.ReadFill(shape.ShapeProperties);
        if (fill.IsEmpty && inherited is not null)
            fill = this.drawing.ReadFill(inherited.ShapeProperties);

        var custom = shape.ShapeProperties?.GetFirstChild<D.CustomGeometry>() is not null;
        if (custom)
        {
            this.unsupported.Report(new UnsupportedFeature(
                "slide", "Custom geometry", UnsupportedSeverity.NotRendered,
                "Freeform shapes are drawn as their bounding rectangle."));
        }

        var text = this.ReadTextBody(shape.TextBody, placeholder, inherited);

        return new SlideShape
        {
            X = OoxmlUnits.EmuToPixels(offset.X?.Value ?? 0),
            Y = OoxmlUnits.EmuToPixels(offset.Y?.Value ?? 0),
            Width = OoxmlUnits.EmuToPixels(extents.Cx?.Value ?? 0),
            Height = OoxmlUnits.EmuToPixels(extents.Cy?.Value ?? 0),
            Prompt = this.ReadPrompt(shape.TextBody, text, placeholder, inherited),
            Geometry = shape.ShapeProperties?.GetFirstChild<D.PresetGeometry>() is null && inherited is null && !custom
                ? ShapeGeometry.None
                : DrawingReader.MapGeometry(preset),
            Fill = fill,
            Outline = this.drawing.ReadOutline(shape.ShapeProperties) ?? this.drawing.ReadOutline(inherited?.ShapeProperties),
            Text = text,
            Rotation = transform.Rotation?.Value is { } rotation ? OoxmlUnits.AngleToDegrees(rotation) : 0,
            FlipHorizontal = transform.HorizontalFlip?.Value ?? false,
            FlipVertical = transform.VerticalFlip?.Value ?? false,
            Name = shape.NonVisualShapeProperties?.NonVisualDrawingProperties?.Name?.Value
        };
    }

    /// <summary>The grey the placeholder prompts are drawn in, PowerPoint's own.</summary>
    static readonly ArgbColor PromptInk = new(255, 0x80, 0x80, 0x80);

    /// <summary>
    /// The "Click to add …" text an empty placeholder shows while it is being edited.
    /// </summary>
    /// <remarks>
    /// Laid out by reading the placeholder's first paragraph again with the prompt appended as a run,
    /// so the prompt takes exactly the size, font, alignment and bullet the user's first keystroke
    /// will — a title prompt is title-sized and a body prompt carries its bullet. A layout that wrote
    /// its own prompt (<c>hasCustomPrompt</c>) is quoted instead.
    /// </remarks>
    ShapeTextBody? ReadPrompt(TextBody? body, ShapeTextBody? text, PlaceholderShape? placeholder, Shape? inherited)
    {
        if (placeholder is null || body is null || text is null || text.PlainText.Trim().Length > 0)
            return null;

        var custom = placeholder.HasCustomPrompt?.Value == true ||
            inherited?.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape?.HasCustomPrompt?.Value == true
                ? inherited?.TextBody?.InnerText
                : null;

        var type = placeholder.Type?.Value;
        var prompt = !string.IsNullOrWhiteSpace(custom) ? custom
            : type == PlaceholderValues.Title || type == PlaceholderValues.CenteredTitle ? "Click to add title"
            : type == PlaceholderValues.SubTitle ? "Click to add subtitle"
            : type is null || type == PlaceholderValues.Body || type == PlaceholderValues.Object ? "Click to add text"
            : null;

        if (prompt is null || body.Elements<D.Paragraph>().FirstOrDefault() is not { } first)
            return null;

        // Read, never written: the run goes after a:endParaRPr, which the schema forbids, but this
        // element never leaves the method.
        var synthetic = (D.Paragraph)first.CloneNode(true);
        synthetic.Append(new D.Run(new D.Text(prompt)));

        var paragraph = this.ReadParagraph(synthetic, this.ResolveListStyle(placeholder, inherited), new ShapeNumbering());
        paragraph = paragraph with
        {
            Runs = paragraph.Runs.Select(x => x with { Style = x.Style with { Color = PromptInk } }).ToList()
        };

        return text with { Paragraphs = [paragraph] };
    }

    /// <summary>
    /// Finds the layout (then master) placeholder a slide placeholder inherits from.
    /// </summary>
    /// <remarks>
    /// Matching is by index first and type second. Index is the reliable key when a layout has two
    /// body placeholders; type is the fallback for the single title or body case where the slide omits
    /// the index entirely.
    /// </remarks>
    Shape? FindInheritedPlaceholder(PlaceholderShape placeholder)
    {
        var index = placeholder.Index?.Value;
        var type = placeholder.Type?.Value;

        return Find(this.layout?.SlideLayout?.CommonSlideData?.ShapeTree)
            ?? Find(this.master?.SlideMaster?.CommonSlideData?.ShapeTree);

        Shape? Find(OpenXmlElement? tree)
        {
            if (tree is null)
                return null;

            var candidates = tree.Descendants<Shape>()
                .Where(x => x.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape is not null)
                .ToList();

            if (index is not null)
            {
                var byIndex = candidates.FirstOrDefault(x =>
                    x.NonVisualShapeProperties!.ApplicationNonVisualDrawingProperties!.PlaceholderShape!.Index?.Value == index);

                if (byIndex is not null)
                    return byIndex;
            }

            if (type is null)
                return null;

            return candidates.FirstOrDefault(x =>
            {
                var candidateType = x.NonVisualShapeProperties!.ApplicationNonVisualDrawingProperties!.PlaceholderShape!.Type?.Value;

                // A slide's "title" matches a layout's "ctrTitle" and vice versa.
                if (candidateType == type)
                    return true;

                var titles = new[] { PlaceholderValues.Title, PlaceholderValues.CenteredTitle };
                return titles.Contains(type.Value) && candidateType is not null && titles.Contains(candidateType.Value);
            });
        }
    }

    /// <remarks>
    /// Takes the element rather than a typed body because a table cell's <c>a:txBody</c> and a
    /// shape's <c>p:txBody</c> are two types with one shape. Reading the cell's own element — not a
    /// copy re-wrapped as the other type, which is what this used to do — is what lets an edit to a
    /// cell's paragraphs reach the file.
    /// </remarks>
    ShapeTextBody? ReadTextBody(OpenXmlCompositeElement? body, PlaceholderShape? placeholder, Shape? inherited)
    {
        if (body is null)
            return null;

        var paragraphs = new List<ShapeParagraph>();
        OpenXmlCompositeElement? listStyle = this.ResolveListStyle(placeholder, inherited);

        // One counter set per text body. A numbered list is a property of the shape it is in — two
        // bulleted placeholders on the same slide each start at one, and a counter shared across the
        // slide would have the second one carry on from the first.
        var numbering = new ShapeNumbering();

        foreach (var paragraph in body.Elements<D.Paragraph>())
            paragraphs.Add(this.ReadParagraph(paragraph, listStyle, numbering) with { Element = paragraph });

        if (paragraphs.Count == 0)
            return null;

        var properties = body.GetFirstChild<D.BodyProperties>();
        var normalAutofit = properties?.GetFirstChild<D.NormalAutoFit>();

        return new ShapeTextBody(paragraphs)
        {
            Element = body,
            Anchor = properties?.Anchor?.Value switch
            {
                var v when v == D.TextAnchoringTypeValues.Center => TextAnchor.Middle,
                var v when v == D.TextAnchoringTypeValues.Bottom => TextAnchor.Bottom,
                _ => TextAnchor.Top
            },
            InsetLeft = properties?.LeftInset?.Value is { } l ? OoxmlUnits.EmuToPixels(l) : 9.6,
            InsetRight = properties?.RightInset?.Value is { } r ? OoxmlUnits.EmuToPixels(r) : 9.6,
            InsetTop = properties?.TopInset?.Value is { } t ? OoxmlUnits.EmuToPixels(t) : 4.8,
            InsetBottom = properties?.BottomInset?.Value is { } b ? OoxmlUnits.EmuToPixels(b) : 4.8,
            WordWrap = properties?.Wrap?.Value != D.TextWrappingValues.None,

            // PowerPoint stores the shrink factor it computed; recomputing it would need its exact
            // font metrics, so the recorded value is honoured instead.
            FontScale = normalAutofit?.FontScale?.Value is { } scale ? scale / 100000d : 1.0,
            LineSpaceReduction = normalAutofit?.LineSpaceReduction?.Value is { } reduction ? reduction / 100000d : 0
        };
    }

    /// <summary>The list style that supplies default run formatting per outline level.</summary>
    OpenXmlCompositeElement? ResolveListStyle(PlaceholderShape? placeholder, Shape? inherited)
    {
        if (inherited?.TextBody?.ListStyle is { } fromLayout && fromLayout.HasChildren)
            return fromLayout;

        var master = this.master?.SlideMaster;
        if (master is null)
            return null;

        // A shape that is not a placeholder is not a list. Falling back to the master's body style
        // hands it the body bullet, so every plain text box grows a bullet it never asked for - and
        // the space reserved for that bullet also throws off centred text.
        if (placeholder?.Type?.Value is not { } type)
            return master.TextStyles?.OtherStyle;

        if (type == PlaceholderValues.Title || type == PlaceholderValues.CenteredTitle)
            return master.TextStyles?.TitleStyle;

        if (type == PlaceholderValues.Body || type == PlaceholderValues.SubTitle)
            return master.TextStyles?.BodyStyle;

        return master.TextStyles?.OtherStyle;
    }

    ShapeParagraph ReadParagraph(D.Paragraph paragraph, OpenXmlCompositeElement? listStyle, ShapeNumbering numbering)
    {
        var properties = paragraph.ParagraphProperties;
        var level = properties?.Level?.Value ?? 0;

        // Level defaults come from the list style; the paragraph's own properties override them.
        var levelDefaults = LevelProperties(listStyle, level);
        var style = this.ReadRunStyleDefaults(levelDefaults);

        var runs = new List<StyledRun>();
        foreach (var child in paragraph.ChildElements)
        {
            switch (child)
            {
                case D.Run run:
                    runs.Add(new StyledRun(run.Text?.Text ?? string.Empty, this.ApplyRunProperties(style, run.RunProperties)));
                    break;

                case D.Break:
                    runs.Add(new StyledRun(string.Empty, style) { IsBreak = true });
                    break;

                case D.Field field:
                    // Slide numbers and dates render as whatever text PowerPoint last cached.
                    runs.Add(new StyledRun(field.Text?.Text ?? string.Empty, this.ApplyRunProperties(style, field.RunProperties)));
                    break;
            }
        }

        var alignment = (properties?.Alignment?.Value ?? levelDefaults?.Alignment?.Value) switch
        {
            var v when v == D.TextAlignmentTypeValues.Center => TextAlignment.Center,
            var v when v == D.TextAlignmentTypeValues.Right => TextAlignment.Right,
            var v when v == D.TextAlignmentTypeValues.Justified => TextAlignment.Justify,
            _ => TextAlignment.Left
        };

        var (listStyle_, bullet) = ReadBullet(properties, levelDefaults, level, numbering);

        return new ShapeParagraph(runs)
        {
            Level = level,
            Alignment = alignment,
            List = listStyle_,
            Bullet = bullet,
            SpaceBefore = SpacingOf(properties?.SpaceBefore ?? levelDefaults?.SpaceBefore),
            SpaceAfter = SpacingOf(properties?.SpaceAfter ?? levelDefaults?.SpaceAfter),
            LineSpacing = LineSpacingOf(properties?.LineSpacing ?? levelDefaults?.LineSpacing)
        };
    }

    /// <summary>
    /// The level definition for an outline level. The master's title/body/other styles and a shape's own
    /// list style are different element types with identical children, so this reaches for the child by
    /// type rather than through a typed property that only one of them has.
    /// </summary>
    static D.TextParagraphPropertiesType? LevelProperties(OpenXmlCompositeElement? listStyle, int level) => level switch
    {
        0 => listStyle?.GetFirstChild<D.Level1ParagraphProperties>(),
        1 => listStyle?.GetFirstChild<D.Level2ParagraphProperties>(),
        2 => listStyle?.GetFirstChild<D.Level3ParagraphProperties>(),
        3 => listStyle?.GetFirstChild<D.Level4ParagraphProperties>(),
        4 => listStyle?.GetFirstChild<D.Level5ParagraphProperties>(),
        5 => listStyle?.GetFirstChild<D.Level6ParagraphProperties>(),
        6 => listStyle?.GetFirstChild<D.Level7ParagraphProperties>(),
        7 => listStyle?.GetFirstChild<D.Level8ParagraphProperties>(),
        _ => listStyle?.GetFirstChild<D.Level9ParagraphProperties>()
    };

    TextStyle ReadRunStyleDefaults(D.TextParagraphPropertiesType? levelProperties)
    {
        var style = TextStyle.Default with { FontSize = OoxmlUnits.PointsToPixels(18) };
        return levelProperties?.GetFirstChild<D.DefaultRunProperties>() is { } defaults
            ? this.ApplyRunProperties(style, defaults)
            : style;
    }

    /// <summary>
    /// Applies DrawingML run properties.
    /// </summary>
    /// <remarks>
    /// Reads through <see cref="D.TextCharacterPropertiesType"/>, the base every run-property element
    /// shares. Reaching for attributes by name instead throws for elements that do not declare them,
    /// which is not something a viewer should die on.
    /// </remarks>
    TextStyle ApplyRunProperties(TextStyle style, OpenXmlElement? properties)
    {
        if (properties is null)
            return style;

        if (properties is D.TextCharacterPropertiesType typed)
        {
            if (typed.FontSize?.Value is { } size)
                style = style with { FontSize = OoxmlUnits.HundredthPointsToPixels(size) };

            if (typed.Bold?.Value is { } bold)
                style = style with { Bold = bold };

            if (typed.Italic?.Value is { } italic)
                style = style with { Italic = italic };

            if (typed.Underline?.Value is { } underline && underline != D.TextUnderlineValues.None)
                style = style with { Underline = UnderlineStyle.Single };

            if (typed.Strike?.Value is { } strike && strike != D.TextStrikeValues.NoStrike)
                style = style with { Strike = true };
        }

        if (this.drawing.ReadColor(properties.GetFirstChild<D.SolidFill>()) is { } color)
            style = style with { Color = color };

        // a:highlight wraps a colour choice of its own rather than carrying one as an attribute, which
        // is why it goes through the same reader as a solid fill instead of being parsed here.
        if (properties.GetFirstChild<D.Highlight>() is { } highlight)
            style = style with { Highlight = this.drawing.ReadColor(highlight) };

        // A '+' prefix means "the theme's major or minor font", which is resolved by the font scheme
        // rather than being a family name in its own right.
        if (properties.GetFirstChild<D.LatinFont>()?.Typeface?.Value is { } typeface && !typeface.StartsWith('+'))
            style = style with { FontFamily = typeface };

        return style;
    }

    /// <summary>
    /// Resolves the mark in front of a paragraph, and what kind of list that makes it.
    /// </summary>
    /// <remarks>
    /// The paragraph's own properties win outright: <c>a:buNone</c> on the paragraph turns off a
    /// bullet the layout supplies, and neither one falls through to the other. An auto-numbered
    /// paragraph is the only case that needs more than the element in front of it — its number comes
    /// from <paramref name="numbering"/>, which has been walking the body's paragraphs in order.
    /// </remarks>
    static (ListStyle Style, string? Bullet) ReadBullet(
        D.ParagraphProperties? properties,
        D.TextParagraphPropertiesType? defaults,
        int level,
        ShapeNumbering numbering)
    {
        OpenXmlElement? source = properties;
        if (source?.GetFirstChild<D.NoBullet>() is not null)
            return (ListStyle.None, null);

        if (source?.GetFirstChild<D.CharacterBullet>() is { } character)
            return (ListStyle.Bullet, MapBullet(character.Char?.Value));

        if (source?.GetFirstChild<D.AutoNumberedBullet>() is { } auto)
            return (ListStyle.Numbered, AutoNumber(auto, level, numbering));

        if (defaults?.GetFirstChild<D.NoBullet>() is not null)
            return (ListStyle.None, null);

        if (defaults?.GetFirstChild<D.CharacterBullet>() is { } inherited)
            return (ListStyle.Bullet, MapBullet(inherited.Char?.Value));

        if (defaults?.GetFirstChild<D.AutoNumberedBullet>() is { } inheritedAuto)
            return (ListStyle.Numbered, AutoNumber(inheritedAuto, level, numbering));

        return (ListStyle.None, null);
    }

    /// <summary>Advances the counter for a level and renders it in the paragraph's own scheme.</summary>
    static string AutoNumber(D.AutoNumberedBullet bullet, int level, ShapeNumbering numbering)
        => ShapeNumbering.Render(
            numbering.Next(level, bullet.StartAt?.Value ?? 1),
            bullet.Type?.Value ?? D.TextAutoNumberSchemeValues.ArabicPeriod);

    /// <summary>Symbol-font bullet code points mapped onto glyphs a text font can actually draw.</summary>
    static string MapBullet(string? glyph) => glyph switch
    {
        null or "" => "\u2022",
        "\uF0B7" => "\u2022",   // Symbol bullet
        "\uF0A7" => "\u25AA",   // Wingdings square
        "\uF076" => "\u25C6",   // Wingdings diamond
        "\uF0D8" => "\u25B8",   // Wingdings arrowhead
        "o" => "\u25E6",
        _ => glyph
    };

    static double SpacingOf(D.SpaceBefore? spacing) => SpacingOf((OpenXmlElement?)spacing);

    static double SpacingOf(D.SpaceAfter? spacing) => SpacingOf((OpenXmlElement?)spacing);

    static double SpacingOf(OpenXmlElement? spacing)
    {
        // Points here are in hundredths, unlike everywhere else in the format.
        if (spacing?.GetFirstChild<D.SpacingPoints>()?.Val?.Value is { } points)
            return OoxmlUnits.PointsToPixels(points / 100d);

        return 0;
    }

    static double LineSpacingOf(D.LineSpacing? spacing)
    {
        if (spacing?.GetFirstChild<D.SpacingPercent>()?.Val?.Value is { } percent)
            return Math.Max(0.5, percent / 100000d);

        if (spacing?.GetFirstChild<D.SpacingPoints>()?.Val?.Value is { } points)
            return Math.Max(0.5, OoxmlUnits.PointsToPixels(points / 100d) / 24d);

        return 1.0;
    }

    SlideShape? ReadPicture(Picture picture)
    {
        var transform = picture.ShapeProperties?.Transform2D;
        var offset = transform?.Offset;
        var extents = transform?.Extents;
        if (offset is null || extents is null)
            return null;

        var blip = picture.BlipFill?.Blip;
        if (blip?.Embed?.Value is not { } relationshipId)
            return null;

        if (this.part.GetPartById(relationshipId) is not ImagePart imagePart)
            return null;

        byte[] data;
        try
        {
            using var stream = imagePart.GetStream();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            data = copy.ToArray();
        }
        catch (Exception ex)
        {
            this.unsupported.Report(new UnsupportedFeature("media", "Image", UnsupportedSeverity.NotRendered, ex.Message));
            return null;
        }

        return new SlideShape
        {
            X = OoxmlUnits.EmuToPixels(offset.X?.Value ?? 0),
            Y = OoxmlUnits.EmuToPixels(offset.Y?.Value ?? 0),
            Width = OoxmlUnits.EmuToPixels(extents.Cx?.Value ?? 0),
            Height = OoxmlUnits.EmuToPixels(extents.Cy?.Value ?? 0),
            Geometry = ShapeGeometry.None,
            Image = data,
            Rotation = transform?.Rotation?.Value is { } rotation ? OoxmlUnits.AngleToDegrees(rotation) : 0,
            Name = picture.NonVisualPictureProperties?.NonVisualDrawingProperties?.Name?.Value
        };
    }

    /// <summary>The mapping from a group's child space to the space the group itself is placed in.</summary>
    static ChildSpace ChildSpaceOf(D.TransformGroup? transform)
    {
        if (transform?.Offset is not { } offset || transform.Extents is not { } extents)
            return ChildSpace.Identity;

        // Missing child extents mean the child space is the group's own, which is identity.
        var childOffset = transform.ChildOffset;
        var childExtents = transform.ChildExtents;

        double ox = offset.X?.Value ?? 0, oy = offset.Y?.Value ?? 0;
        double cx = extents.Cx?.Value ?? 0, cy = extents.Cy?.Value ?? 0;
        double chx = childOffset?.X?.Value ?? ox, chy = childOffset?.Y?.Value ?? oy;
        double chcx = childExtents?.Cx?.Value ?? cx, chcy = childExtents?.Cy?.Value ?? cy;

        var sx = chcx == 0 ? 1 : cx / chcx;
        var sy = chcy == 0 ? 1 : cy / chcy;

        // In pixels: scale is unit-free, the offset is converted.
        return new ChildSpace(
            sx,
            OoxmlUnits.EmuToPixels((long)Math.Round(ox - chx * sx)),
            sy,
            OoxmlUnits.EmuToPixels((long)Math.Round(oy - chy * sy)));
    }

    SlideShape? ReadConnection(ConnectionShape connection)
    {
        var transform = connection.ShapeProperties?.Transform2D;
        var offset = transform?.Offset;
        var extents = transform?.Extents;
        if (offset is null || extents is null)
            return null;

        return new SlideShape
        {
            X = OoxmlUnits.EmuToPixels(offset.X?.Value ?? 0),
            Y = OoxmlUnits.EmuToPixels(offset.Y?.Value ?? 0),
            Width = OoxmlUnits.EmuToPixels(extents.Cx?.Value ?? 0),
            Height = OoxmlUnits.EmuToPixels(extents.Cy?.Value ?? 0),
            Geometry = ShapeGeometry.Line,
            Outline = this.drawing.ReadOutline(connection.ShapeProperties) ?? new ShapeOutline(new ArgbColor(255, 0, 0, 0), 1),
            FlipHorizontal = transform?.HorizontalFlip?.Value ?? false,
            FlipVertical = transform?.VerticalFlip?.Value ?? false
        };
    }

    SlideShape? ReadTable(GraphicFrame frame)
    {
        var table = frame.Graphic?.GraphicData?.GetFirstChild<D.Table>();
        if (table is null)
        {
            var uri = frame.Graphic?.GraphicData?.Uri?.Value ?? string.Empty;
            var kind = uri.Contains("chart") ? "Chart" : uri.Contains("diagram") ? "SmartArt" : "Embedded object";

            this.unsupported.Report(new UnsupportedFeature("slide", kind, UnsupportedSeverity.NotRendered));
            return null;
        }

        var transform = frame.Transform;
        var offset = transform?.Offset;
        var extents = transform?.Extents;
        if (offset is null || extents is null)
            return null;

        var columnWidths = table.TableGrid?.Elements<D.GridColumn>()
            .Select(x => OoxmlUnits.EmuToPixels(x.Width?.Value ?? 0))
            .ToList() ?? [];

        var rowHeights = new List<double>();
        var rows = new List<IReadOnlyList<SlideTableCell>>();

        foreach (var row in table.Elements<D.TableRow>())
        {
            rowHeights.Add(OoxmlUnits.EmuToPixels(row.Height?.Value ?? 0));
            var cells = new List<SlideTableCell>();

            foreach (var cell in row.Elements<D.TableCell>())
            {
                var merged = (cell.HorizontalMerge?.Value ?? false) || (cell.VerticalMerge?.Value ?? false);
                cells.Add(new SlideTableCell(
                    this.ReadTextBody(cell.TextBody, null, null),
                    this.drawing.ReadFill(cell.TableCellProperties).Solid,
                    (int)(cell.GridSpan?.Value ?? 1),
                    (int)(cell.RowSpan?.Value ?? 1),
                    merged));
            }

            rows.Add(cells);
        }

        return new SlideShape
        {
            X = OoxmlUnits.EmuToPixels(offset.X?.Value ?? 0),
            Y = OoxmlUnits.EmuToPixels(offset.Y?.Value ?? 0),
            Width = OoxmlUnits.EmuToPixels(extents.Cx?.Value ?? 0),
            Height = OoxmlUnits.EmuToPixels(extents.Cy?.Value ?? 0),
            Geometry = ShapeGeometry.None,
            Table = new SlideTable(columnWidths, rowHeights, rows),
            Name = frame.NonVisualGraphicFrameProperties?.NonVisualDrawingProperties?.Name?.Value
        };
    }

    ShapeFill ReadBackground()
    {
        var background = this.part.Slide?.CommonSlideData?.Background
            ?? this.layout?.SlideLayout?.CommonSlideData?.Background
            ?? this.master?.SlideMaster?.CommonSlideData?.Background;

        if (background?.BackgroundProperties is { } properties)
            return this.drawing.ReadFill(properties);

        // A background can also be a reference into the theme's fill style list, which the viewer
        // approximates with the scheme colour it names rather than the full styled fill.
        if (background?.BackgroundStyleReference is { } reference)
        {
            var color = this.drawing.ReadColor(reference);
            if (color is not null)
                return new ShapeFill { Solid = color };
        }

        return ShapeFill.None;
    }

    /// <summary>
    /// The speaker notes: the notes page's body placeholder, one line per paragraph.
    /// </summary>
    /// <remarks>
    /// The body placeholder only, when there is one. Walking every paragraph on the page also picked up
    /// the slide-number and header placeholders, and dropping blank paragraphs meant notes could not
    /// round-trip through an editor that writes them back.
    /// </remarks>
    string? ReadNotes()
    {
        var tree = this.part.NotesSlidePart?.NotesSlide?.CommonSlideData?.ShapeTree;
        if (tree is null)
            return null;

        if (SlideNotes.BodyOf(tree) is { } body)
        {
            var lines = body.Elements<D.Paragraph>().Select(x => x.InnerText).ToList();
            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
                lines.RemoveAt(lines.Count - 1);

            return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
        }

        var text = tree.Descendants<D.Paragraph>()
            .Select(x => x.InnerText)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        return text.Count == 0 ? null : string.Join(Environment.NewLine, text);
    }
}
