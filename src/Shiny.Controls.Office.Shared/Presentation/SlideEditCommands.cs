using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Presentation;
using Shiny.Controls.Office.Editing;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;
using D = DocumentFormat.OpenXml.Drawing;

namespace Shiny.Controls.Office.Presentation;

/// <summary>
/// A caret position inside a deck: which slide, which shape, which paragraph, and how far into it.
/// </summary>
/// <remarks>
/// Deeper than the Word editor's <c>(block, offset)</c> because a slide is not a flow — text lives
/// inside shapes that have no order relative to one another, so a position is meaningless without the
/// shape it belongs to.
/// </remarks>
public readonly record struct SlidePosition(int Slide, int Shape, int Paragraph, int Offset)
{
    /// <summary>
    /// The table cell the text is in, when the shape is a table; null for a shape's own text.
    /// </summary>
    /// <remarks>
    /// Row and column index <see cref="SlideTable.Rows"/> directly — a merged-away cell still has a
    /// slot. Nullable rather than defaulting to -1 so a <c>default</c> position means "not a cell".
    /// </remarks>
    public (int Row, int Column)? Cell { get; init; }

    public bool SameShape(SlidePosition other) => this.Slide == other.Slide && this.Shape == other.Shape && this.Cell == other.Cell;

    /// <summary>Ordering within one shape. Comparing across shapes is meaningless and returns 0.</summary>
    public int CompareWithin(SlidePosition other)
    {
        if (!this.SameShape(other))
            return 0;

        return this.Paragraph != other.Paragraph
            ? this.Paragraph.CompareTo(other.Paragraph)
            : this.Offset.CompareTo(other.Offset);
    }
}

/// <summary>A span of text inside one shape.</summary>
public readonly record struct SlideTextRange(SlidePosition Start, SlidePosition End)
{
    public bool IsEmpty => this.Start == this.End;

    public bool IsWithinOneParagraph => this.Start.Paragraph == this.End.Paragraph;

    /// <summary>The range with its ends in ascending order.</summary>
    public SlideTextRange Normalized()
        => this.Start.CompareWithin(this.End) <= 0 ? this : new SlideTextRange(this.End, this.Start);
}

/// <summary>
/// Base for slide edits.
/// </summary>
/// <remarks>
/// Every command captures what it needs to reverse itself while it runs, and reprojects the slide it
/// touched so the model and the XML never disagree.
/// </remarks>
public abstract record SlideCommand : IEditCommand<SlideDeck>
{
    public abstract string Name { get; }

    public abstract IEditCommand<SlideDeck> Apply(SlideDeck context);

    /// <summary>The <c>a:p</c> a position points at, or null when the position is stale.</summary>
    private protected static D.Paragraph? ParagraphAt(SlideDeck deck, SlidePosition at)
        => TextAt(deck, at)?.Paragraphs.ElementAtOrDefault(at.Paragraph)?.Element;

    /// <summary>The text body a position is in: the shape's own, or one of its table's cells.</summary>
    internal static ShapeTextBody? TextAt(SlideDeck deck, SlidePosition at)
    {
        var shape = deck.Slides.ElementAtOrDefault(at.Slide)?.Shapes.ElementAtOrDefault(at.Shape);

        return at.Cell is { } cell
            ? shape?.Table?.Rows.ElementAtOrDefault(cell.Row)?.ElementAtOrDefault(cell.Column)?.Text
            : shape?.Text;
    }

    private protected static SlideShape? ShapeAt(SlideDeck deck, int slide, int shape)
        => deck.Slides.ElementAtOrDefault(slide)?.Shapes.ElementAtOrDefault(shape);

    /// <summary>
    /// Clones a whole shape so an edit can be reversed by putting it back.
    /// </summary>
    /// <remarks>
    /// Text edits within a paragraph invert precisely, but anything that adds or removes paragraphs
    /// shifts every index after it — so those capture the shape wholesale rather than trying to
    /// describe an inverse that would go stale.
    /// </remarks>
    private protected static RestoreShapeCommand CaptureShape(SlideDeck deck, int slide, int shape)
    {
        var element = ShapeAt(deck, slide, shape)?.Element;
        return new RestoreShapeCommand(slide, shape, element?.CloneNode(true));
    }
}

/// <summary>Puts a previously captured shape back, replacing whatever is there now.</summary>
public sealed record RestoreShapeCommand(int Slide, int Shape, OpenXmlElement? Snapshot) : SlideCommand
{
    public override string Name => "Restore";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (this.Snapshot is null)
            return new NoOpSlideCommand();

        var inverse = CaptureShape(context, this.Slide, this.Shape);

        var current = ShapeAt(context, this.Slide, this.Shape)?.Element;
        if (current is null)
            return new NoOpSlideCommand();

        current.Parent?.ReplaceChild(this.Snapshot.CloneNode(true), current);
        context.Reproject(this.Slide);

        return inverse;
    }
}

public sealed record NoOpSlideCommand : SlideCommand
{
    public override string Name => "Nothing";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context) => this;
}

/// <summary>Inserts text at a position inside a shape.</summary>
public sealed record InsertSlideTextCommand(SlidePosition At, string Text) : SlideCommand, IMergeableCommand<SlideDeck>
{
    public override string Name => "Typing";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (ParagraphAt(context, this.At) is not { } paragraph)
            return new NoOpSlideCommand();

        ShapeTextEditor.Insert(paragraph, this.At.Offset, this.Text);
        context.Reproject(this.At.Slide);

        return new DeleteSlideRangeCommand(new SlideTextRange(
            this.At,
            this.At with { Offset = this.At.Offset + this.Text.Length }));
    }

    /// <summary>
    /// Absorbs the next typed character, so a typed word undoes in one step.
    /// </summary>
    /// <remarks>
    /// Only when it continues immediately where this one ended — moving the caret, or typing in a
    /// different shape, ends the run.
    /// </remarks>
    public bool TryMerge(IEditCommand<SlideDeck> next, out IEditCommand<SlideDeck> merged)
    {
        merged = this;

        if (next is not InsertSlideTextCommand following)
            return false;

        if (!following.At.SameShape(this.At) ||
            following.At.Paragraph != this.At.Paragraph ||
            following.At.Offset != this.At.Offset + this.Text.Length)
            return false;

        if (this.Text.Length + following.Text.Length > 64)
            return false;

        merged = this with { Text = this.Text + following.Text };
        return true;
    }
}

/// <summary>Deletes a range, which may span paragraphs within one shape.</summary>
public sealed record DeleteSlideRangeCommand(SlideTextRange Range) : SlideCommand
{
    public override string Name => "Delete";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        var range = this.Range.Normalized();
        if (range.IsEmpty || !range.Start.SameShape(range.End))
            return new NoOpSlideCommand();

        // Captured before anything moves: after the edit the paragraphs it came from may be gone.
        var restore = CaptureShape(context, range.Start.Slide, range.Start.Shape);

        if (range.IsWithinOneParagraph)
        {
            if (ParagraphAt(context, range.Start) is not { } paragraph)
                return new NoOpSlideCommand();

            ShapeTextEditor.Delete(paragraph, range.Start.Offset, range.End.Offset);
            context.Reproject(range.Start.Slide);
            return restore;
        }

        if (ParagraphAt(context, range.Start) is not { } first ||
            ParagraphAt(context, range.End) is not { } last)
            return new NoOpSlideCommand();

        // Trim both ends, drop everything between, then join the survivors — the same shape as the
        // Word editor's multi-paragraph delete.
        var between = new List<D.Paragraph>();
        for (var i = range.Start.Paragraph + 1; i < range.End.Paragraph; i++)
        {
            if (ParagraphAt(context, range.Start with { Paragraph = i }) is { } middle)
                between.Add(middle);
        }

        ShapeTextEditor.Delete(first, range.Start.Offset, ShapeTextEditor.LengthOf(first));
        ShapeTextEditor.Delete(last, 0, range.End.Offset);

        foreach (var middle in between)
            middle.Remove();

        ShapeTextEditor.Merge(first, last);
        context.Reproject(range.Start.Slide);
        return restore;
    }
}

/// <summary>Splits a paragraph at a position — what Enter does.</summary>
public sealed record SplitSlideParagraphCommand(SlidePosition At) : SlideCommand
{
    public override string Name => "New paragraph";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        var restore = CaptureShape(context, this.At.Slide, this.At.Shape);

        if (ParagraphAt(context, this.At) is not { } paragraph)
            return new NoOpSlideCommand();

        var tail = ShapeTextEditor.Split(paragraph, this.At.Offset);
        paragraph.Parent?.InsertAfter(tail, paragraph);

        context.Reproject(this.At.Slide);
        return restore;
    }
}

/// <summary>Joins a paragraph onto the one before it — what Backspace at offset 0 does.</summary>
public sealed record MergeSlideParagraphCommand(SlidePosition At) : SlideCommand
{
    public override string Name => "Join paragraphs";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (this.At.Paragraph <= 0)
            return new NoOpSlideCommand();

        var restore = CaptureShape(context, this.At.Slide, this.At.Shape);

        if (ParagraphAt(context, this.At) is not { } paragraph ||
            ParagraphAt(context, this.At with { Paragraph = this.At.Paragraph - 1 }) is not { } previous)
            return new NoOpSlideCommand();

        ShapeTextEditor.Merge(previous, paragraph);
        context.Reproject(this.At.Slide);
        return restore;
    }
}

/// <summary>Applies a run-property change over a range, splitting runs at the boundaries.</summary>
public sealed record FormatSlideRunsCommand(SlideTextRange Range, Action<D.RunProperties> Apply_, string Label)
    : SlideCommand
{
    public override string Name => this.Label;

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        var range = this.Range.Normalized();
        if (!range.Start.SameShape(range.End))
            return new NoOpSlideCommand();

        var restore = CaptureShape(context, range.Start.Slide, range.Start.Shape);

        if (range.IsEmpty)
        {
            // Nothing selected: the choice goes on the paragraph's end mark, which is where
            // PowerPoint keeps formatting for text that has not been typed yet.
            if (ParagraphAt(context, range.Start) is not { } only)
                return new NoOpSlideCommand();

            ShapeTextEditor.FormatEndMark(only, this.Apply_);
            context.Reproject(range.Start.Slide);
            return restore;
        }

        for (var i = range.Start.Paragraph; i <= range.End.Paragraph; i++)
        {
            if (ParagraphAt(context, range.Start with { Paragraph = i }) is not { } paragraph)
                continue;

            var from = i == range.Start.Paragraph ? range.Start.Offset : 0;
            var to = i == range.End.Paragraph ? range.End.Offset : ShapeTextEditor.LengthOf(paragraph);

            ShapeTextEditor.Format(paragraph, from, to, this.Apply_);
        }

        context.Reproject(range.Start.Slide);
        return restore;
    }
}

/// <summary>Applies a paragraph-property change across every paragraph a range touches.</summary>
public sealed record FormatSlideParagraphsCommand(SlideTextRange Range, Action<D.ParagraphProperties> Apply_, string Label)
    : SlideCommand
{
    public override string Name => this.Label;

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        var range = this.Range.Normalized();
        if (!range.Start.SameShape(range.End))
            return new NoOpSlideCommand();

        var restore = CaptureShape(context, range.Start.Slide, range.Start.Shape);

        for (var i = range.Start.Paragraph; i <= range.End.Paragraph; i++)
        {
            if (ParagraphAt(context, range.Start with { Paragraph = i }) is { } paragraph)
                ShapeTextEditor.FormatParagraph(paragraph, this.Apply_);
        }

        context.Reproject(range.Start.Slide);
        return restore;
    }
}

/// <summary>
/// Moves and resizes a shape, in slide coordinates.
/// </summary>
/// <remarks>
/// <para>
/// A placeholder frequently has no <c>a:xfrm</c> of its own — its position comes from the layout —
/// so dragging one has to write a transform that was never there. That is correct and is what
/// PowerPoint itself does; from then on the shape no longer follows its layout's position.
/// </para>
/// <para>
/// This inverts exactly rather than through a snapshot, because the previous rectangle is all it
/// takes to undo and a drag produces a great many of these.
/// </para>
/// </remarks>
/// <summary>
/// Sets a shape's fill, or takes it away.
/// </summary>
/// <remarks>
/// <para>
/// Inverted by putting the whole shape back rather than by describing the previous fill. A fill is not
/// one value: it can be a gradient with any number of stops, a picture, a pattern, or absent entirely
/// so the shape inherits from its style. Reconstructing the one that was there is a great deal of work
/// to undo one colour, and getting it subtly wrong loses the author's artwork.
/// </para>
/// <para>
/// Null clears to <c>a:noFill</c>, which is a real state — a transparent shape with a visible outline —
/// and not the same as having no fill element at all, which means "use the style's".
/// </para>
/// </remarks>
public sealed record SetShapeFillCommand(int Slide, int Shape, ArgbColor? Color) : SlideCommand
{
    public override string Name => "Shape fill";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (ShapeAt(context, this.Slide, this.Shape) is not { Element: { } element })
            return new NoOpSlideCommand();

        var inverse = CaptureShape(context, this.Slide, this.Shape);
        if (Properties(element) is not { } properties)
            return new NoOpSlideCommand();

        // Every fill kind is a sibling in the same slot, so they all have to go before the new one
        // lands - leaving a gradient behind would win over the solid colour just set.
        RemoveFills(properties);

        if (this.Color is { } color)
        {
            properties.Append(new D.SolidFill(new D.RgbColorModelHex { Val = Hex(color) }));
        }
        else
        {
            properties.Append(new D.NoFill());
        }

        context.Reproject(this.Slide);
        return inverse;
    }

    internal static void RemoveFills(OpenXmlElement properties)
    {
        foreach (var fill in properties.ChildElements
            .OfType<OpenXmlElement>()
            .Where(x => x is D.SolidFill or D.NoFill or D.GradientFill or D.BlipFill or D.PatternFill or D.GroupFill)
            .ToList())
        {
            fill.Remove();
        }
    }

    /// <summary>The <c>spPr</c> the fill and line live on, whatever kind of shape this is.</summary>
    internal static OpenXmlElement? Properties(OpenXmlElement element) => element switch
    {
        Shape shape => shape.ShapeProperties ??= new ShapeProperties(),
        Picture picture => picture.ShapeProperties ??= new ShapeProperties(),
        ConnectionShape connector => connector.ShapeProperties ??= new ShapeProperties(),
        _ => null
    };

    internal static string Hex(ArgbColor color) => $"{color.R:X2}{color.G:X2}{color.B:X2}";
}


/// <summary>
/// Sets a shape's outline colour and weight, or takes the outline away.
/// </summary>
/// <remarks>
/// The line element carries the weight as well as the colour, so setting one without the other would
/// silently reset it — a shape given a red border would come back hairline-thin. Both are written
/// together, and the inverse is the whole shape for the same reason the fill's is.
/// </remarks>
public sealed record SetShapeOutlineCommand(int Slide, int Shape, ArgbColor? Color, double Width = 1) : SlideCommand
{
    public override string Name => "Shape outline";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (ShapeAt(context, this.Slide, this.Shape) is not { Element: { } element })
            return new NoOpSlideCommand();

        var inverse = CaptureShape(context, this.Slide, this.Shape);
        if (SetShapeFillCommand.Properties(element) is not { } properties)
            return new NoOpSlideCommand();

        properties.GetFirstChild<D.Outline>()?.Remove();

        var line = new D.Outline();

        if (this.Color is { } color)
        {
            line.Width = (int)OoxmlUnits.PixelsToEmu(Math.Max(0.25, this.Width));
            line.Append(new D.SolidFill(new D.RgbColorModelHex { Val = SetShapeFillCommand.Hex(color) }));
        }
        else
        {
            line.Append(new D.NoFill());
        }

        // a:ln comes after the fill in the sequence, and appending is only right because the fill is
        // written first - the schema is ordered and PowerPoint refuses a file that is not.
        properties.Append(line);

        context.Reproject(this.Slide);
        return inverse;
    }
}


public sealed record SetShapeBoundsCommand(int Slide, int Shape, double X, double Y, double Width, double Height)
    : SlideCommand, IMergeableCommand<SlideDeck>
{
    public override string Name => "Move shape";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (ShapeAt(context, this.Slide, this.Shape) is not { } shape || shape.Element is null)
            return new NoOpSlideCommand();

        var inverse = new SetShapeBoundsCommand(this.Slide, this.Shape, shape.X, shape.Y, shape.Width, shape.Height);

        // A zero-sized shape cannot be grabbed again, so a resize can never take a shape below a
        // size that still has a handle on it. The request is in slide space; the file wants the space
        // the shape's transform is written in, which inside a group is the group's child space.
        var (x, y, width, height) = shape.Space.FromSlide(this.X, this.Y, Math.Max(4, this.Width), Math.Max(4, this.Height));

        if (!WriteBounds(
                shape.Element,
                OoxmlUnits.PixelsToEmu(x),
                OoxmlUnits.PixelsToEmu(y),
                Math.Max(1, OoxmlUnits.PixelsToEmu(width)),
                Math.Max(1, OoxmlUnits.PixelsToEmu(height))))
        {
            return new NoOpSlideCommand();
        }

        context.Reproject(this.Slide);
        return inverse;
    }

    static bool WriteBounds(OpenXmlElement element, long x, long y, long cx, long cy)
    {
        switch (element)
        {
            case GraphicFrame frame:
                // p:xfrm, a direct child of the frame, and a different type from a:xfrm. This used to
                // return nothing, so a table could be added but never moved or resized.
                var frameTransform = frame.Transform ??= new Transform();
                frameTransform.Offset ??= new D.Offset();
                frameTransform.Extents ??= new D.Extents();
                frameTransform.Offset.X = x;
                frameTransform.Offset.Y = y;
                frameTransform.Extents.Cx = cx;
                frameTransform.Extents.Cy = cy;
                return true;

            case GroupShape group:
                var properties = group.GroupShapeProperties ??= new GroupShapeProperties();
                var groupTransform = properties.TransformGroup;
                if (groupTransform is null)
                {
                    groupTransform = new D.TransformGroup();
                    properties.InsertAt(groupTransform, 0);
                }

                groupTransform.Offset ??= new D.Offset { X = x, Y = y };
                groupTransform.Extents ??= new D.Extents { Cx = cx, Cy = cy };

                // The child space is pinned to where the group was, so moving or resizing the group
                // carries and scales its children rather than leaving them behind.
                groupTransform.ChildOffset ??= new D.ChildOffset { X = groupTransform.Offset.X?.Value ?? x, Y = groupTransform.Offset.Y?.Value ?? y };
                groupTransform.ChildExtents ??= new D.ChildExtents { Cx = groupTransform.Extents.Cx?.Value ?? cx, Cy = groupTransform.Extents.Cy?.Value ?? cy };

                groupTransform.Offset.X = x;
                groupTransform.Offset.Y = y;
                groupTransform.Extents.Cx = cx;
                groupTransform.Extents.Cy = cy;
                return true;
        }

        var transform = EnsureTransform(element);
        if (transform is null)
            return false;

        transform.Offset ??= new D.Offset();
        transform.Extents ??= new D.Extents();
        transform.Offset.X = x;
        transform.Offset.Y = y;
        transform.Extents.Cx = cx;
        transform.Extents.Cy = cy;
        return true;
    }

    /// <summary>A drag is one undo step, not one per pointer sample.</summary>
    public bool TryMerge(IEditCommand<SlideDeck> next, out IEditCommand<SlideDeck> merged)
    {
        merged = this;

        if (next is not SetShapeBoundsCommand following ||
            following.Slide != this.Slide ||
            following.Shape != this.Shape)
            return false;

        // Keep this command's identity but take the newer rectangle: the inverse the stack already
        // holds points at where the shape started, which is where an undo has to put it back.
        merged = following;
        return true;
    }

    /// <summary>
    /// The shape's transform, created if the shape inherited its position from a layout.
    /// </summary>
    /// <remarks>
    /// Every shape kind keeps its transform somewhere different — <c>p:spPr</c>, <c>p:grpSpPr</c>,
    /// <c>p:xfrm</c> on a graphic frame — so this reaches for the properties element by type rather
    /// than assuming the shape is a <c>p:sp</c>.
    /// </remarks>
    static D.Transform2D? EnsureTransform(OpenXmlElement element)
    {
        switch (element)
        {
            case Shape shape:
                var shapeProperties = shape.ShapeProperties ??= new ShapeProperties();
                return shapeProperties.Transform2D ??= NewTransform(shapeProperties);

            case Picture picture:
                var pictureProperties = picture.ShapeProperties ??= new ShapeProperties();
                return pictureProperties.Transform2D ??= NewTransform(pictureProperties);

            case ConnectionShape connection:
                var connectionProperties = connection.ShapeProperties ??= new ShapeProperties();
                return connectionProperties.Transform2D ??= NewTransform(connectionProperties);

            default:
                return null;
        }
    }

    /// <summary>a:xfrm is the first child of the properties element; appending it is invalid.</summary>
    static D.Transform2D NewTransform(ShapeProperties properties)
    {
        var transform = new D.Transform2D();
        properties.InsertAt(transform, 0);
        return transform;
    }
}

/// <summary>Removes a shape from its slide.</summary>
public sealed record DeleteShapeCommand(int Slide, int Shape) : SlideCommand
{
    public override string Name => "Delete shape";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (ShapeAt(context, this.Slide, this.Shape) is not { Element: { } element } shape || !shape.IsEditable)
            return new NoOpSlideCommand();

        // The index a shape sits at in its *own parent* — the slide's tree, or the group it is in —
        // which is what an undo has to put it back at. The model's index also counts the layout and
        // master shapes painted underneath, and the children of every group.
        var parent = element.Parent;
        var position = parent is null ? -1 : parent.ChildElements.ToList().IndexOf(element);
        var path = parent is null ? null : ShapeTreePath.Of(parent);

        var snapshot = element.CloneNode(true);
        element.Remove();
        context.Reproject(this.Slide);

        return new InsertShapeCommand(this.Slide, position, snapshot) { ParentPath = path };
    }
}

/// <summary>Puts a shape into a slide's tree at a known position.</summary>
public sealed record InsertShapeCommand(int Slide, int TreeIndex, OpenXmlElement Element) : SlideCommand
{
    public override string Name => "Add shape";

    /// <summary>
    /// Child indices from the slide's shape tree down to the group to insert into; null or empty for
    /// the tree itself.
    /// </summary>
    /// <remarks>An index path rather than an element, because an undo may have replaced the element since.</remarks>
    public IReadOnlyList<int>? ParentPath { get; init; }

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (context.TreeAt(this.Slide) is not { } root || ShapeTreePath.Resolve(root, this.ParentPath) is not { } tree)
            return new NoOpSlideCommand();

        var clone = this.Element.CloneNode(true);

        if (this.TreeIndex >= 0 && this.TreeIndex < tree.ChildElements.Count)
            tree.InsertAt(clone, this.TreeIndex);
        else
            tree.AppendChild(clone);

        context.Reproject(this.Slide);

        // The model index of what was just added, so the inverse deletes the right shape.
        var added = context.Slides.ElementAtOrDefault(this.Slide)?
            .Shapes.ToList()
            .FindIndex(x => ReferenceEquals(x.Element, clone)) ?? -1;

        return added < 0
            ? new NoOpSlideCommand()
            : new DeleteShapeCommand(this.Slide, added);
    }
}

/// <summary>The formatting under the caret, so a toolbar can show what is active.</summary>
public readonly record struct SlideCaretFormat(
    bool Bold,
    bool Italic,
    bool Underline,
    bool Strike,
    double FontSize,
    string FontFamily,
    ArgbColor Color,
    TextAlignment Alignment,
    ArgbColor? Highlight = null)
{
    /// <summary>Which list the caret's paragraph is in, so a toolbar can light the right button.</summary>
    public ListStyle List { get; init; }

    /// <summary>The paragraph's outline level, 0-8. What Tab moves.</summary>
    public int Level { get; init; }

    public static SlideCaretFormat Default => new(
        false, false, false, false, 18, "Calibri", new ArgbColor(255, 0, 0, 0), TextAlignment.Left);
}

/// <summary>Addresses an element inside a slide's shape tree by child indices, so it survives a DOM swap.</summary>
static class ShapeTreePath
{
    public static IReadOnlyList<int>? Of(OpenXmlElement element)
    {
        var path = new List<int>();
        for (var current = element; current is not ShapeTree; current = current.Parent!)
        {
            if (current.Parent is null)
                return null;

            path.Insert(0, current.Parent.ChildElements.ToList().IndexOf(current));
        }

        return path;
    }

    public static OpenXmlElement? Resolve(ShapeTree tree, IReadOnlyList<int>? path)
    {
        OpenXmlElement current = tree;
        foreach (var index in path ?? [])
        {
            if (current.ChildElements.ElementAtOrDefault(index) is not { } next)
                return null;

            current = next;
        }

        return current;
    }
}
