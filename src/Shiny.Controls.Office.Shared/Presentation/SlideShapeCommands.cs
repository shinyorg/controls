using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Presentation;
using Shiny.Controls.Office.Editing;
using Shiny.Controls.Office.Shapes;
using Shiny.Controls.Office.Spreadsheet;
using D = DocumentFormat.OpenXml.Drawing;

namespace Shiny.Controls.Office.Presentation;

/// <summary>
/// Keeps <c>p:spPr</c>'s children in schema order.
/// </summary>
/// <remarks>
/// The sequence is <c>xfrm, custGeom|prstGeom, fill, ln, effectLst|effectDag, scene3d, sp3d,
/// extLst</c>. Appending — which the first fill command did — lands a fill after an outline that was
/// already there, and PowerPoint refuses the file rather than repairing it.
/// </remarks>
static class ShapePropertiesOrder
{
    public static int RankOf(OpenXmlElement element) => element.LocalName switch
    {
        "xfrm" => 0,
        "custGeom" or "prstGeom" => 1,
        "noFill" or "solidFill" or "gradFill" or "blipFill" or "pattFill" or "grpFill" => 2,
        "ln" => 3,
        "effectLst" or "effectDag" => 4,
        "scene3d" => 5,
        "sp3d" => 6,
        _ => 7
    };

    public static void Insert(OpenXmlElement properties, OpenXmlElement child)
    {
        var rank = RankOf(child);
        OpenXmlElement? previous = null;

        foreach (var existing in properties.ChildElements)
        {
            if (RankOf(existing) > rank)
                break;

            previous = existing;
        }

        if (previous is null)
            properties.InsertAt(child, 0);
        else
            properties.InsertAfter(child, previous);
    }

    public static void RemoveFills(OpenXmlElement properties)
    {
        foreach (var fill in properties.ChildElements.Where(x => RankOf(x) == 2).ToList())
            fill.Remove();
    }

    public static D.SolidFill Solid(ArgbColor color)
    {
        var hex = new D.RgbColorModelHex { Val = SetShapeFillCommand.Hex(color) };
        if (color.A < 255)
            hex.AppendChild(new D.Alpha { Val = (int)Math.Round(color.A / 255d * 100000) });

        return new D.SolidFill(hex);
    }
}

/// <summary>A fill the Shape Fill menu can apply.</summary>
public sealed record SlideFillSpec
{
    /// <summary>No fill (<c>a:noFill</c>).</summary>
    public static readonly SlideFillSpec None = new();

    public ArgbColor? Solid { get; init; }

    /// <summary>A theme colour by scheme name (<c>accent1</c>, <c>tx1</c>...), which follows the theme.</summary>
    public string? SchemeColor { get; init; }

    /// <summary>Two-stop linear gradient: from, to and angle in degrees.</summary>
    public (ArgbColor From, ArgbColor To, double Angle)? Gradient { get; init; }

    public static SlideFillSpec Color(ArgbColor color) => new() { Solid = color };

    public static SlideFillSpec Theme(string scheme) => new() { SchemeColor = scheme };

    public static SlideFillSpec LinearGradient(ArgbColor from, ArgbColor to, double angle = 90) => new() { Gradient = (from, to, angle) };

    internal OpenXmlElement Build()
    {
        if (this.Gradient is { } gradient)
        {
            return new D.GradientFill(
                new D.GradientStopList(
                    new D.GradientStop(new D.RgbColorModelHex { Val = SetShapeFillCommand.Hex(gradient.From) }) { Position = 0 },
                    new D.GradientStop(new D.RgbColorModelHex { Val = SetShapeFillCommand.Hex(gradient.To) }) { Position = 100000 }),
                new D.LinearGradientFill { Angle = (int)Math.Round(gradient.Angle * 60000), Scaled = false })
            { RotateWithShape = true };
        }

        if (this.SchemeColor is { } scheme)
            return new D.SolidFill(SlideSchemeColor.Build(scheme));

        return this.Solid is { } solid ? ShapePropertiesOrder.Solid(solid) : new D.NoFill();
    }
}

/// <summary>Scheme colour references by the tokens DrawingML writes.</summary>
static class SlideSchemeColor
{
    /// <summary>
    /// A <c>a:schemeClr</c>, optionally with <c>lumMod</c>/<c>lumOff</c> — how "Accent 1, Lighter 80%"
    /// is spelled.
    /// </summary>
    public static D.SchemeColor Build(string token, int? lumMod = null, int? lumOff = null, int? shade = null)
    {
        var element = new D.SchemeColor();
        element.SetAttribute(new OpenXmlAttribute(string.Empty, "val", string.Empty, token));

        if (shade is { } s)
            element.AppendChild(new D.Shade { Val = s });

        if (lumMod is { } mod)
            element.AppendChild(new D.LuminanceModulation { Val = mod });

        if (lumOff is { } off)
            element.AppendChild(new D.LuminanceOffset { Val = off });

        return element;
    }
}

/// <summary>Sets a shape's fill from a <see cref="SlideFillSpec"/>, restoring the whole shape on undo.</summary>
public sealed record FormatShapeFillCommand(int Slide, int Shape, SlideFillSpec Fill) : SlideCommand
{
    public override string Name => "Shape fill";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (ShapeAt(context, this.Slide, this.Shape) is not { Element: { } element } ||
            SetShapeFillCommand.Properties(element) is not { } properties)
        {
            return new NoOpSlideCommand();
        }

        var inverse = CaptureShape(context, this.Slide, this.Shape);
        ShapePropertiesOrder.RemoveFills(properties);
        ShapePropertiesOrder.Insert(properties, this.Fill.Build());
        context.Reproject(this.Slide);
        return inverse;
    }
}

/// <summary>
/// Changes a shape's outline, leaving what is not given as it was — a new weight keeps the colour.
/// </summary>
public sealed record FormatShapeLineCommand(int Slide, int Shape, ArgbColor? Color = null, double? Width = null, LineDash? Dash = null, bool Remove = false) : SlideCommand
{
    /// <summary>A theme colour for the line instead of <see cref="Color"/>.</summary>
    public string? SchemeColor { get; init; }

    public override string Name => "Shape outline";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (ShapeAt(context, this.Slide, this.Shape) is not { Element: { } element } model ||
            SetShapeFillCommand.Properties(element) is not { } properties)
        {
            return new NoOpSlideCommand();
        }

        var inverse = CaptureShape(context, this.Slide, this.Shape);
        var line = properties.GetFirstChild<D.Outline>();

        if (this.Remove)
        {
            line?.Remove();
            line = new D.Outline(new D.NoFill());
            ShapePropertiesOrder.Insert(properties, line);
            context.Reproject(this.Slide);
            return inverse;
        }

        if (line is null)
        {
            line = new D.Outline();
            ShapePropertiesOrder.Insert(properties, line);
        }

        // An outline with no fill of its own is "no line"; one being given a weight or a dash needs
        // something to draw, so it takes the colour it was showing (or black).
        var hasFill = line.ChildElements.Any(x => x.LocalName is "solidFill" or "gradFill" or "pattFill");
        if (this.Color is not null || this.SchemeColor is not null || !hasFill)
        {
            foreach (var fill in line.ChildElements.Where(x => x.LocalName is "noFill" or "solidFill" or "gradFill" or "pattFill").ToList())
                fill.Remove();

            OpenXmlElement newFill = this.SchemeColor is { } scheme
                ? new D.SolidFill(SlideSchemeColor.Build(scheme))
                : ShapePropertiesOrder.Solid(this.Color ?? model.Outline?.Color ?? new ArgbColor(255, 0, 0, 0));

            // a:ln's children: fill, prstDash|custDash, join, headEnd, tailEnd, extLst.
            line.PrependChild(newFill);
        }

        if (this.Width is { } width)
            line.Width = (int)OoxmlUnits.PixelsToEmu(Math.Max(0.25, width));
        else if (line.Width is null && model.Outline is null)
            line.Width = 12700;

        if (this.Dash is { } dash)
        {
            foreach (var existing in line.ChildElements.Where(x => x.LocalName is "prstDash" or "custDash").ToList())
                existing.Remove();

            var preset = new D.PresetDash();
            preset.SetAttribute(new OpenXmlAttribute(string.Empty, "val", string.Empty, DrawingReader.DashToken(dash)));

            var fillElement = line.ChildElements.FirstOrDefault(x => x.LocalName is "noFill" or "solidFill" or "gradFill" or "pattFill");
            if (fillElement is not null)
                line.InsertAfter(preset, fillElement);
            else
                line.PrependChild(preset);
        }

        context.Reproject(this.Slide);
        return inverse;
    }
}

/// <summary>Turns a shape's outer shadow on (with PowerPoint's "Offset: Bottom Right" preset) or off.</summary>
public sealed record SetShapeShadowCommand(int Slide, int Shape, ShapeShadow? Shadow) : SlideCommand
{
    /// <summary>PowerPoint's "Offset: Bottom Right": 4pt blur, 3pt away, 45 degrees, 40% black.</summary>
    public static ShapeShadow Default { get; } = new(new ArgbColor(102, 0, 0, 0), OoxmlUnits.EmuToPixels(50800), OoxmlUnits.EmuToPixels(38100), 45);

    public override string Name => "Shadow";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (ShapeAt(context, this.Slide, this.Shape) is not { Element: { } element } ||
            SetShapeFillCommand.Properties(element) is not { } properties)
        {
            return new NoOpSlideCommand();
        }

        var inverse = CaptureShape(context, this.Slide, this.Shape);
        var effects = properties.GetFirstChild<D.EffectList>();

        foreach (var shadow in effects?.Elements<D.OuterShadow>().ToList() ?? [])
            shadow.Remove();

        if (this.Shadow is { } value)
        {
            if (effects is null)
            {
                effects = new D.EffectList();
                ShapePropertiesOrder.Insert(properties, effects);
            }

            var color = new D.RgbColorModelHex { Val = SetShapeFillCommand.Hex(value.Color) };
            color.AppendChild(new D.Alpha { Val = (int)Math.Round(value.Color.A / 255d * 100000) });

            // a:effectLst's children are a sequence too: blur, fillOverlay, glow, innerShdw, outerShdw,
            // prstShdw, reflection, softEdge.
            var outer = new D.OuterShadow(color)
            {
                BlurRadius = OoxmlUnits.PixelsToEmu(value.Blur),
                Distance = OoxmlUnits.PixelsToEmu(value.Distance),
                Direction = (int)Math.Round(value.Direction * 60000),
                Alignment = D.RectangleAlignmentValues.TopLeft,
                RotateWithShape = false
            };

            var after = effects.ChildElements.LastOrDefault(x => x.LocalName is "blur" or "fillOverlay" or "glow" or "innerShdw");
            if (after is not null)
                effects.InsertAfter(outer, after);
            else
                effects.PrependChild(outer);
        }
        else if (effects is { HasChildren: false })
        {
            // An empty effect list is still "no effects, and do not inherit the style's": left alone
            // rather than removed, which would bring a themed shadow back.
        }

        context.Reproject(this.Slide);
        return inverse;
    }
}

/// <summary>A shape style from the Quick Styles gallery.</summary>
public enum SlideQuickStyleKind
{
    /// <summary>"Colored Fill": the accent, with white text.</summary>
    ColoredFill,

    /// <summary>"Colored Outline": white, an accent outline and accent text.</summary>
    ColoredOutline,

    /// <summary>"Subtle Effect": a light tint of the accent, with dark text.</summary>
    SubtleEffect,

    /// <summary>"Intense Effect": a gradient of the accent, with white text and a shadow.</summary>
    IntenseEffect
}

/// <summary>One Quick Styles gallery entry: a kind in one of the theme's accents.</summary>
public sealed record SlideQuickStyle(SlideQuickStyleKind Kind, int Accent)
{
    public string Name => $"{this.Kind switch
    {
        SlideQuickStyleKind.ColoredFill => "Colored Fill",
        SlideQuickStyleKind.ColoredOutline => "Colored Outline",
        SlideQuickStyleKind.SubtleEffect => "Subtle Effect",
        _ => "Intense Effect"
    }} - Accent {this.Accent}";

    public static IReadOnlyList<SlideQuickStyle> Gallery { get; } =
        Enum.GetValues<SlideQuickStyleKind>()
            .SelectMany(kind => Enumerable.Range(1, 6).Select(accent => new SlideQuickStyle(kind, accent)))
            .ToList();
}

/// <summary>
/// Applies a Quick Style: fill, outline and text colour together, all as theme colours so the shape
/// follows a theme change the way PowerPoint's own styled shapes do.
/// </summary>
public sealed record ApplyQuickStyleCommand(int Slide, int Shape, SlideQuickStyle Style) : SlideCommand
{
    public override string Name => "Shape style";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (ShapeAt(context, this.Slide, this.Shape) is not { Element: { } element } ||
            SetShapeFillCommand.Properties(element) is not { } properties)
        {
            return new NoOpSlideCommand();
        }

        var inverse = CaptureShape(context, this.Slide, this.Shape);
        var accent = $"accent{Math.Clamp(this.Style.Accent, 1, 6)}";

        ShapePropertiesOrder.RemoveFills(properties);
        properties.GetFirstChild<D.Outline>()?.Remove();

        OpenXmlElement fill = this.Style.Kind switch
        {
            SlideQuickStyleKind.ColoredOutline => new D.SolidFill(SlideSchemeColor.Build("lt1")),
            SlideQuickStyleKind.SubtleEffect => new D.SolidFill(SlideSchemeColor.Build(accent, lumMod: 20000, lumOff: 80000)),
            SlideQuickStyleKind.IntenseEffect => new D.GradientFill(
                new D.GradientStopList(
                    new D.GradientStop(SlideSchemeColor.Build(accent, lumMod: 60000, lumOff: 40000)) { Position = 0 },
                    new D.GradientStop(SlideSchemeColor.Build(accent, shade: 70000)) { Position = 100000 }),
                new D.LinearGradientFill { Angle = 5400000, Scaled = false }) { RotateWithShape = true },
            _ => new D.SolidFill(SlideSchemeColor.Build(accent))
        };

        ShapePropertiesOrder.Insert(properties, fill);
        ShapePropertiesOrder.Insert(properties, new D.Outline(new D.SolidFill(SlideSchemeColor.Build(accent, shade: this.Style.Kind == SlideQuickStyleKind.ColoredOutline ? null : 50000))) { Width = 12700 });

        if (this.Style.Kind == SlideQuickStyleKind.IntenseEffect)
        {
            var effects = properties.GetFirstChild<D.EffectList>();
            effects?.Remove();
            effects = new D.EffectList(new D.OuterShadow(new D.RgbColorModelHex(new D.Alpha { Val = 40000 }) { Val = "000000" })
            {
                BlurRadius = 50800,
                Distance = 38100,
                Direction = 2700000,
                Alignment = D.RectangleAlignmentValues.TopLeft,
                RotateWithShape = false
            });
            ShapePropertiesOrder.Insert(properties, effects);
        }

        // The text in the shape takes the style's ink, the way PowerPoint's styles colour their text.
        var ink = this.Style.Kind switch
        {
            SlideQuickStyleKind.ColoredOutline => accent,
            SlideQuickStyleKind.SubtleEffect => "dk1",
            _ => "lt1"
        };

        if (element is Shape { TextBody: { } body })
        {
            foreach (var paragraph in body.Elements<D.Paragraph>())
            {
                foreach (var run in paragraph.Elements<D.Run>())
                    ShapeTextEditor.SetThemeColor(ink)(run.RunProperties ??= new D.RunProperties());

                ShapeTextEditor.FormatEndMark(paragraph, ShapeTextEditor.SetThemeColor(ink));
            }
        }

        context.Reproject(this.Slide);
        return inverse;
    }
}

/// <summary>
/// Rotates and flips a shape — the Rotate menu and the rotation handle.
/// </summary>
/// <remarks>
/// A drag of the rotation handle produces one of these per pointer sample, so they merge into one undo
/// step the way a move does.
/// </remarks>
public sealed record SetShapeRotationCommand(int Slide, int Shape, double? Rotation, bool? FlipHorizontal = null, bool? FlipVertical = null)
    : SlideCommand, IMergeableCommand<SlideDeck>
{
    public override string Name => this.Rotation is not null ? "Rotate" : "Flip";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (ShapeAt(context, this.Slide, this.Shape) is not { Element: { } element } shape)
            return new NoOpSlideCommand();

        var transform = element switch
        {
            GroupShape group => (OpenXmlElement?)group.GroupShapeProperties?.TransformGroup,
            GraphicFrame => null,
            _ => (SetShapeFillCommand.Properties(element) as ShapeProperties) is { } properties
                ? properties.Transform2D ?? EnsureTransform(properties, shape)
                : null
        };

        if (transform is null)
            return new NoOpSlideCommand();

        var inverse = new SetShapeRotationCommand(this.Slide, this.Shape, shape.Rotation, shape.FlipHorizontal, shape.FlipVertical);

        switch (transform)
        {
            case D.Transform2D t:
                if (this.Rotation is { } r) t.Rotation = Normalize(r);
                if (this.FlipHorizontal is { } h) t.HorizontalFlip = h ? true : null;
                if (this.FlipVertical is { } v) t.VerticalFlip = v ? true : null;
                break;

            case D.TransformGroup g:
                if (this.Rotation is { } gr) g.Rotation = Normalize(gr);
                if (this.FlipHorizontal is { } gh) g.HorizontalFlip = gh ? true : null;
                if (this.FlipVertical is { } gv) g.VerticalFlip = gv ? true : null;
                break;
        }

        context.Reproject(this.Slide);
        return inverse;
    }

    /// <summary>Degrees to DrawingML's 60000ths, wrapped into [0, 360) and left off entirely at zero.</summary>
    static int? Normalize(double degrees)
    {
        var wrapped = ((degrees % 360) + 360) % 360;
        var value = (int)Math.Round(wrapped * 60000);
        return value is 0 or 21600000 ? null : value;
    }

    /// <summary>A placeholder inheriting its position gets one written, as a move would.</summary>
    static D.Transform2D EnsureTransform(ShapeProperties properties, SlideShape shape)
    {
        var transform = new D.Transform2D(
            new D.Offset { X = OoxmlUnits.PixelsToEmu(shape.X), Y = OoxmlUnits.PixelsToEmu(shape.Y) },
            new D.Extents { Cx = OoxmlUnits.PixelsToEmu(shape.Width), Cy = OoxmlUnits.PixelsToEmu(shape.Height) });
        properties.InsertAt(transform, 0);
        return transform;
    }

    public bool TryMerge(IEditCommand<SlideDeck> next, out IEditCommand<SlideDeck> merged)
    {
        merged = this;
        if (next is not SetShapeRotationCommand following || following.Slide != this.Slide || following.Shape != this.Shape ||
            following.Rotation is null || this.Rotation is null)
            return false;

        merged = following;
        return true;
    }
}

/// <summary>
/// Moves and resizes several shapes as one step — a multi-selection drag, an align, a distribute.
/// </summary>
/// <remarks>
/// Each shape goes through <see cref="SetShapeBoundsCommand"/>, so a child inside a group is written back
/// in its group's units. Successive commands over the same set of shapes merge, so a drag of three
/// shapes is one undo step rather than one per shape per pointer sample.
/// </remarks>
public sealed record SetShapesBoundsCommand(int Slide, IReadOnlyList<(int Shape, double X, double Y, double Width, double Height)> Bounds, string Label = "Move shapes")
    : SlideCommand, IMergeableCommand<SlideDeck>
{
    public override string Name => this.Label;

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        var inverses = new List<(int, double, double, double, double)>();

        foreach (var (index, x, y, width, height) in this.Bounds)
        {
            if (ShapeAt(context, this.Slide, index) is not { Element: not null } shape)
                continue;

            inverses.Add((index, shape.X, shape.Y, shape.Width, shape.Height));
            new SetShapeBoundsCommand(this.Slide, index, x, y, width, height).Apply(context);
        }

        return inverses.Count == 0
            ? new NoOpSlideCommand()
            : new SetShapesBoundsCommand(this.Slide, inverses, this.Label);
    }

    public bool TryMerge(IEditCommand<SlideDeck> next, out IEditCommand<SlideDeck> merged)
    {
        merged = this;
        if (next is not SetShapesBoundsCommand following ||
            following.Slide != this.Slide ||
            !following.Bounds.Select(x => x.Shape).SequenceEqual(this.Bounds.Select(x => x.Shape)))
            return false;

        merged = following;
        return true;
    }
}

/// <summary>
/// Puts a slide's whole shape tree back — the undo of the edits that move shapes between parents
/// (group, ungroup) and so change every index after them.
/// </summary>
public sealed record RestoreSlideTreeCommand(int Slide, OpenXmlElement Tree, string Label) : SlideCommand
{
    public override string Name => this.Label;

    public static RestoreSlideTreeCommand Capture(SlideDeck deck, int slide, string label)
        => new(slide, deck.TreeAt(slide)!.CloneNode(true), label);

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (context.TreeAt(this.Slide) is not { } tree)
            return new NoOpSlideCommand();

        var inverse = Capture(context, this.Slide, this.Label);
        tree.Parent!.ReplaceChild(this.Tree.CloneNode(true), tree);
        context.Reproject(this.Slide);
        return inverse;
    }
}

/// <summary>
/// Groups shapes that share a parent into a new <c>p:grpSp</c> — Ctrl+G.
/// </summary>
/// <remarks>
/// <para>
/// The group lands where the frontmost of its members was, so nothing moves in the stacking order
/// relative to the shapes around it, and its members keep their order inside it.
/// </para>
/// <para>
/// Its child space is its own extent (<c>chOff</c>/<c>chExt</c> equal to <c>off</c>/<c>ext</c>), which
/// is what PowerPoint writes for a fresh group and means the members' transforms need no rewriting.
/// Members inside a group already had their bounds in that group's space, so only shapes that share a
/// parent can be grouped — which is PowerPoint's rule too.
/// </para>
/// <para>
/// Animations on the members are dropped: PowerPoint cannot animate a shape inside a group, and a
/// timing tree pointing at one is a repair prompt.
/// </para>
/// </remarks>
public sealed record GroupShapesCommand(int Slide, IReadOnlyList<int> Shapes) : SlideCommand
{
    public override string Name => "Group";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        var slide = context.Slides.ElementAtOrDefault(this.Slide);
        var members = this.Shapes.Distinct()
            .Select(i => slide?.Shapes.ElementAtOrDefault(i))
            .Where(x => x is { Element: not null, IsEditable: true })
            .Select(x => x!)
            .ToList();

        if (members.Count < 2 || members.Select(x => x.Element!.Parent).Distinct().Count() != 1 ||
            members[0].Element!.Parent is not { } parent || context.TreeAt(this.Slide) is not { } tree ||
            context.PartAt(this.Slide)?.Slide is not { } part)
        {
            return new NoOpSlideCommand();
        }

        var inverse = new CompositeCommand<SlideDeck>(this.Name,
        [
            RestoreSlideTreeCommand.Capture(context, this.Slide, this.Name),
            new RestoreSlideTimingCommand(this.Slide, SlideTimingXml.Find(part)?.CloneNode(true))
        ]);

        // The group's bounds in the space its members are written in.
        var space = members[0].Space;
        var left = members.Min(x => x.X);
        var top = members.Min(x => x.Y);
        var right = members.Max(x => x.X + x.Width);
        var bottom = members.Max(x => x.Y + x.Height);
        var (x, y, w, h) = space.FromSlide(left, top, right - left, bottom - top);

        var (ox, oy, cx, cy) = (OoxmlUnits.PixelsToEmu(x), OoxmlUnits.PixelsToEmu(y), OoxmlUnits.PixelsToEmu(w), OoxmlUnits.PixelsToEmu(h));
        var id = SlideObjects.NextShapeId(tree);

        var group = new GroupShape(
            new NonVisualGroupShapeProperties(
                new NonVisualDrawingProperties { Id = id, Name = $"Group {id}" },
                new NonVisualGroupShapeDrawingProperties(),
                new ApplicationNonVisualDrawingProperties()),
            new GroupShapeProperties(new D.TransformGroup(
                new D.Offset { X = ox, Y = oy },
                new D.Extents { Cx = cx, Cy = cy },
                new D.ChildOffset { X = ox, Y = oy },
                new D.ChildExtents { Cx = cx, Cy = cy })));

        var ordered = parent.ChildElements.Where(e => members.Any(m => ReferenceEquals(m.Element, e))).ToList();
        ordered[^1].InsertAfterSelf(group);

        var memberIds = new HashSet<uint>();
        foreach (var element in ordered)
        {
            memberIds.UnionWith(element.Descendants<NonVisualDrawingProperties>().Select(p => p.Id?.Value ?? 0));
            element.Remove();
            group.AppendChild(element);
        }

        var animations = SlideTimingXml.Read(part);
        if (animations.Any(a => memberIds.Contains(a.ShapeId)))
        {
            var withText = tree.Descendants<Shape>().Where(s => s.TextBody is not null)
                .Select(s => s.NonVisualShapeProperties?.NonVisualDrawingProperties?.Id?.Value ?? 0).ToHashSet();
            SlideTimingXml.Write(part, animations.Where(a => !memberIds.Contains(a.ShapeId)).ToList(), withText);
        }

        context.Reproject(this.Slide);
        return inverse;
    }
}

/// <summary>
/// Takes a group apart — Ctrl+Shift+G — putting its members where the group was, in slide units.
/// </summary>
/// <remarks>
/// A member's transform is written in the group's child space; lifted out of the group, it has to be
/// rewritten in the space the group itself lives in, or a scaled group's members jump the moment it is
/// ungrouped. A rotated group's rotation is not carried onto its members.
/// </remarks>
public sealed record UngroupShapeCommand(int Slide, int Shape) : SlideCommand
{
    public override string Name => "Ungroup";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        var slide = context.Slides.ElementAtOrDefault(this.Slide);
        if (slide?.Shapes.ElementAtOrDefault(this.Shape) is not { IsGroup: true, Element: GroupShape group } model ||
            group.Parent is not { } parent)
        {
            return new NoOpSlideCommand();
        }

        var inverse = RestoreSlideTreeCommand.Capture(context, this.Slide, this.Name);

        // Every direct member's slide bounds, read before anything moves.
        var children = slide.Shapes
            .Where(x => x.Element is not null && ReferenceEquals(x.Element.Parent, group))
            .ToList();

        var anchor = (OpenXmlElement)group;
        foreach (var child in group.ChildElements.Where(SlideObjects.IsShapeElement).ToList())
        {
            var bounds = children.FirstOrDefault(x => ReferenceEquals(x.Element, child));
            child.Remove();
            anchor.InsertAfterSelf(child);
            anchor = child;

            if (bounds is not null)
            {
                // Into the space the group sat in: the group's own parent space.
                var (x, y, w, h) = model.Space.FromSlide(bounds.X, bounds.Y, bounds.Width, bounds.Height);
                SlideObjects.WriteBounds(child, x, y, w, h);
            }
        }

        group.Remove();
        context.Reproject(this.Slide);
        return inverse;
    }
}

/// <summary>
/// Sets or removes a shape's own click action — Insert ▸ Link with a shape (not text) selected.
/// </summary>
public sealed record SetShapeHyperlinkCommand(int Slide, int Shape, SlideHyperlink? Link) : SlideCommand
{
    public override string Name => this.Link is null ? "Remove link" : "Link";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (ShapeAt(context, this.Slide, this.Shape) is not { Element: { } element } ||
            element.Descendants<NonVisualDrawingProperties>().FirstOrDefault() is not { } drawing ||
            context.PartAt(this.Slide) is not { } part)
        {
            return new NoOpSlideCommand();
        }

        var inverse = CaptureShape(context, this.Slide, this.Shape);

        foreach (var existing in drawing.Elements<D.HyperlinkOnClick>().ToList())
            existing.Remove();

        if (this.Link is { } link && SlideHyperlinks.Relate(context, part, link) is { } target)
        {
            var element_ = new D.HyperlinkOnClick { Id = target.Id ?? string.Empty };
            if (target.Action is not null)
                element_.Action = target.Action;

            if (link.Tooltip is { Length: > 0 } tooltip)
                element_.Tooltip = tooltip;

            // First child of cNvPr: hlinkClick, hlinkHover, extLst.
            drawing.PrependChild(element_);
        }

        context.Reproject(this.Slide);
        return inverse;
    }
}

/// <summary>
/// Sets how a shape's text sits in it: vertical anchor, direction and autofit.
/// </summary>
/// <remarks>
/// Written onto the shape's own <c>a:bodyPr</c>. A placeholder inherits its body properties from the
/// layout, so writing only the attribute asked for — rather than copying the layout's — keeps the rest
/// inheriting.
/// </remarks>
public sealed record SetTextBodyCommand(int Slide, int Shape) : SlideCommand
{
    public Text.TextAnchor? Anchor { get; init; }

    public ShapeTextDirection? Direction { get; init; }

    public TextAutofit? Autofit { get; init; }

    /// <summary>With <see cref="TextAutofit.ShrinkOnOverflow"/>: the scale the text is drawn at, as PowerPoint records it.</summary>
    public double? FontScale { get; init; }

    /// <summary>With <see cref="TextAutofit.ResizeShape"/>: the height the shape grows or shrinks to.</summary>
    public double? FitHeight { get; init; }

    public override string Name => "Text layout";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (ShapeAt(context, this.Slide, this.Shape) is not { Element: Shape { TextBody: { } body } shape } model)
            return new NoOpSlideCommand();

        var inverse = CaptureShape(context, this.Slide, this.Shape);
        var properties = body.BodyProperties ??= new D.BodyProperties();

        if (this.Anchor is { } anchor)
        {
            properties.Anchor = anchor switch
            {
                Text.TextAnchor.Middle => D.TextAnchoringTypeValues.Center,
                Text.TextAnchor.Bottom => D.TextAnchoringTypeValues.Bottom,
                _ => D.TextAnchoringTypeValues.Top
            };
        }

        if (this.Direction is { } direction)
        {
            properties.Vertical = direction switch
            {
                ShapeTextDirection.Rotate90 => D.TextVerticalValues.Vertical,
                ShapeTextDirection.Rotate270 => D.TextVerticalValues.Vertical270,
                _ => null
            };
        }

        if (this.Autofit is { } autofit)
        {
            foreach (var existing in properties.ChildElements.Where(x => x.LocalName is "noAutofit" or "normAutofit" or "spAutoFit").ToList())
                existing.Remove();

            OpenXmlElement choice = autofit switch
            {
                TextAutofit.ShrinkOnOverflow => this.FontScale is { } scale && scale < 0.999
                    ? new D.NormalAutoFit { FontScale = (int)Math.Round(scale * 100000), LineSpaceReduction = scale < 0.9 ? 10000 : null }
                    : new D.NormalAutoFit(),
                TextAutofit.ResizeShape => new D.ShapeAutoFit(),
                _ => new D.NoAutoFit()
            };

            // bodyPr's children: prstTxWarp, (autofit), scene3d, sp3d, flatTx, extLst.
            var warp = properties.GetFirstChild<D.PresetTextWarp>();
            if (warp is not null)
                properties.InsertAfter(choice, warp);
            else
                properties.PrependChild(choice);

            if (autofit == TextAutofit.ResizeShape && this.FitHeight is { } height)
                new SetShapeBoundsCommand(this.Slide, this.Shape, model.X, model.Y, model.Width, height).Apply(context);
        }

        context.Reproject(this.Slide);
        return inverse;
    }
}

/// <summary>
/// Deletes shapes, and any animation on them — an animation targeting a shape that is gone is a
/// repair prompt in PowerPoint.
/// </summary>
/// <remarks>Indices are deleted largest first, so each is still right when its turn comes.</remarks>
public sealed record DeleteShapesCommand(int Slide, IReadOnlyList<int> Shapes) : SlideCommand
{
    public override string Name => "Delete shape";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (context.PartAt(this.Slide)?.Slide is not { } part)
            return new NoOpSlideCommand();

        var timing = SlideTimingXml.Find(part)?.CloneNode(true);
        var inverses = new List<IEditCommand<SlideDeck>>();

        foreach (var index in this.Shapes.Distinct().OrderByDescending(x => x))
            inverses.Add(new DeleteShapeCommand(this.Slide, index).Apply(context));

        if (inverses.All(x => x is NoOpSlideCommand))
            return new NoOpSlideCommand();

        // Drop animations whose shape went with the delete.
        var present = part.CommonSlideData?.ShapeTree?.Descendants<NonVisualDrawingProperties>().Select(x => x.Id?.Value ?? 0).ToHashSet() ?? [];
        if (timing is not null && SlideTimingXml.TargetIds(part).Any(x => !present.Contains(x)))
        {
            var kept = SlideTimingXml.Read(part).Where(x => present.Contains(x.ShapeId)).ToList();
            var withText = part.CommonSlideData?.ShapeTree?.Descendants<Shape>().Where(s => s.TextBody is not null)
                .Select(s => s.NonVisualShapeProperties?.NonVisualDrawingProperties?.Id?.Value ?? 0).ToHashSet() ?? [];

            SlideTimingXml.Write(part, kept, withText);
            context.Reproject(this.Slide);
            inverses.Add(new RestoreSlideTimingCommand(this.Slide, timing));
        }

        // Undo puts the timing back last and the shapes back smallest index first.
        inverses.Reverse();
        return inverses.Count == 1 ? inverses[0] : new CompositeCommand<SlideDeck>(this.Name, inverses);
    }
}

/// <summary>Hides a slide from the show, or shows it again — <c>show="0"</c>.</summary>
public sealed record SetSlideHiddenCommand(int Slide, bool Hidden) : SlideCommand
{
    public override string Name => this.Hidden ? "Hide slide" : "Unhide slide";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (context.PartAt(this.Slide)?.Slide is not { } slide)
            return new NoOpSlideCommand();

        var was = context.Slides[this.Slide].IsHidden;
        if (was == this.Hidden)
            return new NoOpSlideCommand();

        slide.Show = this.Hidden ? false : null;
        context.Reproject(this.Slide);
        return new SetSlideHiddenCommand(this.Slide, was);
    }
}
