using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Shiny.Controls.Office.Document;
using Shiny.Controls.Office.Editing;
using D = DocumentFormat.OpenXml.Drawing;
using PSlide = DocumentFormat.OpenXml.Presentation.Slide;

namespace Shiny.Controls.Office.Presentation;

/// <summary>Where a shape moves in the stacking order.</summary>
public enum ShapeZOrder
{
    /// <summary>In front of everything else on the slide (or in its group).</summary>
    BringToFront,

    /// <summary>One step towards the front.</summary>
    BringForward,

    /// <summary>One step towards the back.</summary>
    SendBackward,

    /// <summary>Behind everything else on the slide (or in its group).</summary>
    SendToBack
}

/// <summary>
/// Moves a shape in the stacking order among its siblings.
/// </summary>
/// <remarks>
/// The stacking order <em>is</em> document order: later children paint over earlier ones. Only
/// siblings that are shapes count — a tree's own non-visual and group properties come first and must
/// stay there, and an extension list comes last.
/// </remarks>
public sealed record ReorderShapeCommand(int Slide, int Shape, ShapeZOrder Order) : SlideCommand
{
    public override string Name => this.Order switch
    {
        ShapeZOrder.BringToFront => "Bring to front",
        ShapeZOrder.BringForward => "Bring forward",
        ShapeZOrder.SendBackward => "Send backward",
        _ => "Send to back"
    };

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (ShapeAt(context, this.Slide, this.Shape) is not { Element: { Parent: { } parent } element })
            return new NoOpSlideCommand();

        var siblings = parent.ChildElements.Where(SlideObjects.IsShapeElement).ToList();
        var at = siblings.IndexOf(element);
        if (at < 0)
            return new NoOpSlideCommand();

        var target = this.Order switch
        {
            ShapeZOrder.BringToFront => siblings.Count - 1,
            ShapeZOrder.BringForward => Math.Min(siblings.Count - 1, at + 1),
            ShapeZOrder.SendBackward => Math.Max(0, at - 1),
            _ => 0
        };

        if (target == at)
            return new NoOpSlideCommand();

        var children = parent.ChildElements.ToList();
        var from = children.IndexOf(element);
        var anchor = siblings[target];

        element.Remove();
        if (target > at)
            anchor.InsertAfterSelf(element);
        else
            anchor.InsertBeforeSelf(element);

        var to = parent.ChildElements.ToList().IndexOf(element);
        context.Reproject(this.Slide);

        return new MoveChildCommand(this.Slide, ShapeTreePath.Of(parent), to, from, this.Name);
    }
}

/// <summary>Moves one child of a tree or group to another position — the inverse of a reorder.</summary>
sealed record MoveChildCommand(int Slide, IReadOnlyList<int>? ParentPath, int From, int To, string Label) : SlideCommand
{
    public override string Name => this.Label;

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (context.TreeAt(this.Slide) is not { } tree ||
            ShapeTreePath.Resolve(tree, this.ParentPath) is not { } parent ||
            parent.ChildElements.ElementAtOrDefault(this.From) is not { } element)
        {
            return new NoOpSlideCommand();
        }

        element.Remove();
        if (this.To >= parent.ChildElements.Count)
            parent.AppendChild(element);
        else
            parent.InsertAt(element, this.To);

        context.Reproject(this.Slide);
        return this with { From = this.To, To = this.From };
    }
}

/// <summary>
/// Shapes copied or cut from a slide, ready to paste — into this deck or another one.
/// </summary>
/// <remarks>
/// Holds the shape's XML and the parts it refers to (a picture's image, a chart), keyed by the
/// relationship id the XML uses. Pasting relates the target slide to those parts and rewrites the ids,
/// because a relationship id means nothing outside the part that declared it. Pasting into another
/// deck copies the parts across.
/// </remarks>
public sealed class SlideClip
{
    internal SlideClip(IReadOnlyList<OpenXmlElement> elements, IReadOnlyDictionary<string, OpenXmlPart> parts, IReadOnlyDictionary<string, Uri> links, int sourceSlide, SlideDeck source)
    {
        this.Elements = elements;
        this.Parts = parts;
        this.Links = links;
        this.SourceSlide = sourceSlide;
        this.Source = new WeakReference<SlideDeck>(source);
    }

    internal IReadOnlyList<OpenXmlElement> Elements { get; }
    internal IReadOnlyDictionary<string, OpenXmlPart> Parts { get; }
    internal IReadOnlyDictionary<string, Uri> Links { get; }
    internal int SourceSlide { get; }
    internal WeakReference<SlideDeck> Source { get; }

    /// <summary>How many shapes the clip holds.</summary>
    public int Count => this.Elements.Count;

    /// <summary>Copies a shape off a slide. Null when the shape cannot be copied (a layout's, say).</summary>
    public static SlideClip? Copy(SlideDeck deck, int slide, int shape)
    {
        if (deck.Slides.ElementAtOrDefault(slide)?.Shapes.ElementAtOrDefault(shape) is not { IsEditable: true, Element: { } element } model ||
            deck.PartAt(slide) is not { } part)
        {
            return null;
        }

        var clone = element.CloneNode(true);

        // The copy carries the bounds it is drawn at, in slide units. A shape inside a group has its
        // position written in the group's units, and a placeholder often has none of its own at all -
        // it inherits one from the layout - so without this a paste would land wherever the group's
        // scale puts it, or could not be offset from the original.
        SlideObjects.WriteBounds(clone, model.X, model.Y, model.Width, model.Height);

        var parts = new Dictionary<string, OpenXmlPart>();
        var links = new Dictionary<string, Uri>();

        foreach (var id in SlideObjects.RelationshipIds(clone))
        {
            if (part.Parts.FirstOrDefault(x => x.RelationshipId == id) is { OpenXmlPart: { } target })
                parts[id] = target;
            else if (part.HyperlinkRelationships.FirstOrDefault(x => x.Id == id) is { } link)
                links[id] = link.Uri;
            else if (part.ExternalRelationships.FirstOrDefault(x => x.Id == id) is { } external)
                links[id] = external.Uri;
        }

        return new SlideClip([clone], parts, links, slide, deck);
    }
}

/// <summary>Puts a clip's shapes onto a slide, offset by a distance in slide pixels.</summary>
public sealed record PasteShapesCommand(int Slide, SlideClip Clip, double OffsetX = 0, double OffsetY = 0) : SlideCommand
{
    public override string Name => "Paste";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (context.TreeAt(this.Slide) is not { } tree || context.PartAt(this.Slide) is not { } part)
            return new NoOpSlideCommand();

        var nextId = SlideObjects.NextShapeId(tree);
        var added = new List<OpenXmlElement>();

        foreach (var source in this.Clip.Elements)
        {
            var clone = source.CloneNode(true);

            // Relationship ids are local to the part that declared them, so each one is re-pointed at
            // this slide's relationship to the same part — created, or copied across from another
            // deck, as needed.
            var remap = new Dictionary<string, string>();
            foreach (var id in SlideObjects.RelationshipIds(clone))
            {
                if (remap.ContainsKey(id))
                    continue;

                if (this.Clip.Parts.TryGetValue(id, out var target))
                {
                    var existing = part.Parts.FirstOrDefault(x => ReferenceEquals(x.OpenXmlPart, target));
                    remap[id] = existing.OpenXmlPart is not null
                        ? existing.RelationshipId
                        : part.GetIdOfPart(part.AddPart(target));
                }
                else if (this.Clip.Links.TryGetValue(id, out var uri))
                {
                    remap[id] = part.AddHyperlinkRelationship(uri, true).Id;
                }
            }

            SlideObjects.RewriteRelationshipIds(clone, remap);

            // Every drawing on a slide carries an id unique to that slide; a paste would otherwise
            // duplicate the original's.
            foreach (var properties in clone.Descendants<NonVisualDrawingProperties>())
                properties.Id = nextId++;

            if (this.OffsetX != 0 || this.OffsetY != 0)
                SlideObjects.Offset(clone, this.OffsetX, this.OffsetY);

            tree.AppendChild(clone);
            added.Add(clone);
        }

        context.Reproject(this.Slide);

        // Deleted back to front so each index is still right when its turn comes.
        var shapes = context.Slides[this.Slide].Shapes.ToList();
        var inverses = added
            .Select(x => shapes.FindIndex(s => ReferenceEquals(s.Element, x)))
            .Where(x => x >= 0)
            .OrderByDescending(x => x)
            .Select(x => (IEditCommand<SlideDeck>)new DeleteShapeCommand(this.Slide, x))
            .ToList();

        return inverses.Count switch
        {
            0 => new NoOpSlideCommand(),
            1 => inverses[0],
            _ => new CompositeCommand<SlideDeck>(this.Name, inverses)
        };
    }
}

/// <summary>
/// Gives a slide another layout from its master, carrying its content across.
/// </summary>
/// <remarks>
/// Placeholders are matched to the new layout's by index, then by type, and take the new layout's
/// position. One the new layout has no place for keeps its content, fixed where it was drawn; left
/// empty, it goes, as in PowerPoint. The new layout's unmatched placeholders arrive empty with their
/// prompts.
/// </remarks>
public sealed record SetSlideLayoutCommand(int Slide, SlideLayoutPart Layout) : SlideCommand
{
    public override string Name => "Layout";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (context.PartAt(this.Slide) is not { Slide: { } slide } part ||
            part.SlideLayoutPart is not { } current ||
            ReferenceEquals(current, this.Layout))
        {
            return new NoOpSlideCommand();
        }

        var inverse = new RestoreSlideLayoutCommand(this.Slide, current, (PSlide)slide.CloneNode(true));

        if (slide.CommonSlideData?.ShapeTree is { } tree)
            SlideLayouts.Adapt(tree, current, this.Layout);

        SlideLayouts.Relate(part, this.Layout);
        context.Reproject(this.Slide);
        return inverse;
    }
}

/// <summary>Puts a slide back on a layout with the exact content it had there.</summary>
sealed record RestoreSlideLayoutCommand(int Slide, SlideLayoutPart Layout, PSlide Snapshot) : SlideCommand
{
    public override string Name => "Layout";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (context.PartAt(this.Slide) is not { Slide: { } slide } part || part.SlideLayoutPart is not { } current)
            return new NoOpSlideCommand();

        var inverse = new RestoreSlideLayoutCommand(this.Slide, current, (PSlide)slide.CloneNode(true));

        part.Slide = (PSlide)this.Snapshot.CloneNode(true);
        SlideLayouts.Relate(part, this.Layout);
        context.Reproject(this.Slide);
        return inverse;
    }
}

/// <summary>Replaces a slide's speaker notes, creating the notes page (and the notes master) if needed.</summary>
/// <remarks>
/// Consecutive notes edits on one slide merge into one undo step, so a notes box can write through
/// on every keystroke and still undo as one "Notes" change, the way typing on the slide does.
/// </remarks>
public sealed record SetSlideNotesCommand(int Slide, string? Text) : SlideCommand, IMergeableCommand<SlideDeck>
{
    public override string Name => "Notes";

    public bool TryMerge(IEditCommand<SlideDeck> next, out IEditCommand<SlideDeck> merged)
    {
        merged = next;
        return next is SetSlideNotesCommand following && following.Slide == this.Slide;
    }

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (context.PartAt(this.Slide) is not { } part)
            return new NoOpSlideCommand();

        var before = context.Slides[this.Slide].Notes;
        var text = string.IsNullOrWhiteSpace(this.Text) ? null : this.Text;
        if (Normalize(before) == Normalize(text))
            return new NoOpSlideCommand();

        // Nothing to clear on a slide that has no notes page: no page is created just to hold nothing.
        if (text is null && part.NotesSlidePart is null)
            return new NoOpSlideCommand();

        var body = SlideNotes.EnsureBody(context, part);
        SlideNotes.Write(body, text);
        if (part.NotesSlidePart is { } notes)
            context.MarkPartDirty(notes);
        context.Reproject(this.Slide);

        return new SetSlideNotesCommand(this.Slide, before);
    }

    static string Normalize(string? text) => (text ?? string.Empty).Replace("\r\n", "\n").TrimEnd();
}

/// <summary>The XML side of copying, pasting and reordering shapes.</summary>
static class SlideObjects
{
    const string RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static bool IsShapeElement(OpenXmlElement element)
        => element is Shape or Picture or GraphicFrame or GroupShape or ConnectionShape;

    /// <summary>Every relationship id an element (or anything inside it) refers to.</summary>
    public static IEnumerable<string> RelationshipIds(OpenXmlElement element)
        => element.Descendants().Prepend(element)
            .SelectMany(x => x.GetAttributes())
            .Where(x => x.NamespaceUri == RelationshipNamespace && !string.IsNullOrEmpty(x.Value))
            .Select(x => x.Value!)
            .Distinct();

    public static void RewriteRelationshipIds(OpenXmlElement element, IReadOnlyDictionary<string, string> remap)
    {
        foreach (var node in element.Descendants().Prepend(element).ToList())
        {
            foreach (var attribute in node.GetAttributes().Where(x => x.NamespaceUri == RelationshipNamespace).ToList())
            {
                if (attribute.Value is { } value && remap.TryGetValue(value, out var replacement))
                    node.SetAttribute(new OpenXmlAttribute(attribute.Prefix, attribute.LocalName, attribute.NamespaceUri, replacement));
            }
        }
    }

    public static uint NextShapeId(ShapeTree tree)
        => tree.Descendants<NonVisualDrawingProperties>().Select(x => x.Id?.Value ?? 0).DefaultIfEmpty(1U).Max() + 1;

    /// <summary>Writes top-level slide-pixel bounds into a shape's own transform.</summary>
    public static void WriteBounds(OpenXmlElement element, double x, double y, double width, double height)
    {
        var (ox, oy, cx, cy) = (OoxmlUnits.PixelsToEmu(x), OoxmlUnits.PixelsToEmu(y), OoxmlUnits.PixelsToEmu(width), OoxmlUnits.PixelsToEmu(height));

        if (TransformOf(element) is { } transform)
        {
            transform.Offset = (ox, oy);
            transform.Extents = (cx, cy);
        }
    }

    /// <summary>Shifts a shape by a distance in slide pixels.</summary>
    public static void Offset(OpenXmlElement element, double dx, double dy)
    {
        if (TransformOf(element) is not { } transform || transform.Offset is not { } offset)
            return;

        transform.Offset = (offset.X + OoxmlUnits.PixelsToEmu(dx), offset.Y + OoxmlUnits.PixelsToEmu(dy));
    }

    /// <summary>Uniform access to the offset and extents every kind of shape keeps somewhere different.</summary>
    sealed class TransformAccess(Func<(long X, long Y)?> getOffset, Action<(long X, long Y)> setOffset, Action<(long Cx, long Cy)> setExtents)
    {
        public (long X, long Y)? Offset
        {
            get => getOffset();
            set { if (value is { } v) setOffset(v); }
        }

        public (long Cx, long Cy) Extents { set => setExtents(value); }
    }

    static TransformAccess? TransformOf(OpenXmlElement element)
    {
        switch (element)
        {
            case GraphicFrame { Transform: { } frame }:
                return new TransformAccess(
                    () => frame.Offset is { } o ? (o.X?.Value ?? 0, o.Y?.Value ?? 0) : null,
                    v => { frame.Offset ??= new D.Offset(); frame.Offset.X = v.X; frame.Offset.Y = v.Y; },
                    v => { frame.Extents ??= new D.Extents(); frame.Extents.Cx = v.Cx; frame.Extents.Cy = v.Cy; });

            case GroupShape { GroupShapeProperties.TransformGroup: { } group }:
                return new TransformAccess(
                    () => group.Offset is { } o ? (o.X?.Value ?? 0, o.Y?.Value ?? 0) : null,
                    v =>
                    {
                        group.Offset ??= new D.Offset();
                        group.ChildOffset ??= new D.ChildOffset { X = group.Offset.X?.Value ?? v.X, Y = group.Offset.Y?.Value ?? v.Y };
                        group.Offset.X = v.X;
                        group.Offset.Y = v.Y;
                    },
                    v =>
                    {
                        group.Extents ??= new D.Extents();
                        group.ChildExtents ??= new D.ChildExtents { Cx = group.Extents.Cx?.Value ?? v.Cx, Cy = group.Extents.Cy?.Value ?? v.Cy };
                        group.Extents.Cx = v.Cx;
                        group.Extents.Cy = v.Cy;
                    });
        }

        var properties = element switch
        {
            Shape shape => shape.ShapeProperties,
            Picture picture => picture.ShapeProperties,
            ConnectionShape connection => connection.ShapeProperties,
            _ => null
        };

        if (properties is null)
            return null;

        return new TransformAccess(
            () => properties.Transform2D?.Offset is { } o ? (o.X?.Value ?? 0, o.Y?.Value ?? 0) : null,
            v =>
            {
                var t = Ensure(properties);
                t.Offset ??= new D.Offset();
                t.Offset.X = v.X;
                t.Offset.Y = v.Y;
            },
            v =>
            {
                var t = Ensure(properties);
                t.Extents ??= new D.Extents();
                t.Extents.Cx = v.Cx;
                t.Extents.Cy = v.Cy;
            });

        static D.Transform2D Ensure(ShapeProperties properties)
        {
            if (properties.Transform2D is { } existing)
                return existing;

            var created = new D.Transform2D();
            properties.InsertAt(created, 0);
            return created;
        }
    }
}

/// <summary>Switching a slide from one layout to another.</summary>
static class SlideLayouts
{
    /// <summary>Points the slide's layout relationship at <paramref name="layout"/>, keeping its id.</summary>
    /// <remarks>
    /// The slide's layout relationship is replaced, never the layout: the old layout is still the
    /// master's and every other slide's using it, so only the relationship from this slide goes.
    /// </remarks>
    public static void Relate(SlidePart part, SlideLayoutPart layout)
    {
        if (part.SlideLayoutPart is { } old)
        {
            var id = part.GetIdOfPart(old);
            part.DeletePart(id);
            part.AddPart(layout, id);
        }
        else
        {
            part.AddPart(layout);
        }
    }

    public static void Adapt(ShapeTree tree, SlideLayoutPart from, SlideLayoutPart to)
    {
        var targets = to.SlideLayout?.CommonSlideData?.ShapeTree?.Elements<Shape>()
            .Where(x => PlaceholderOf(x) is not null)
            .ToList() ?? [];

        var used = new HashSet<Shape>();
        var nextId = SlideObjects.NextShapeId(tree);

        foreach (var shape in tree.Elements<Shape>().ToList())
        {
            if (PlaceholderOf(shape) is not { } placeholder)
                continue;

            var match = Match(placeholder, targets.Where(x => !used.Contains(x)));
            if (match is not null)
            {
                used.Add(match);
                var target = PlaceholderOf(match)!;

                // Take the new layout's identity, so position and formatting come from it.
                placeholder.Type = target.Type?.Value;
                placeholder.Index = target.Index?.Value;
                continue;
            }

            if (IsEmpty(shape))
            {
                shape.Remove();
                continue;
            }

            // Content with nowhere to go on the new layout: fixed where it was drawn, which it could
            // only have got from the old layout.
            if (shape.ShapeProperties?.Transform2D is null &&
                Match(placeholder, from.SlideLayout?.CommonSlideData?.ShapeTree?.Elements<Shape>().Where(x => PlaceholderOf(x) is not null) ?? [])?.ShapeProperties?.Transform2D is { } inherited)
            {
                var properties = shape.ShapeProperties ??= new ShapeProperties();
                properties.InsertAt((D.Transform2D)inherited.CloneNode(true), 0);
            }
        }

        foreach (var target in targets.Where(x => !used.Contains(x)))
        {
            if (SlideStructureEdits.NewPlaceholder(target, nextId) is { } added)
            {
                tree.AppendChild(added);
                nextId++;
            }
        }
    }

    static PlaceholderShape? PlaceholderOf(Shape shape)
        => shape.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape;

    static bool IsEmpty(Shape shape)
        => string.IsNullOrWhiteSpace(shape.TextBody?.InnerText);

    /// <summary>By index first, then type — the same rule the reader inherits by.</summary>
    static Shape? Match(PlaceholderShape placeholder, IEnumerable<Shape> candidates)
    {
        var list = candidates.ToList();
        var type = placeholder.Type?.Value ?? PlaceholderValues.Object;

        if (placeholder.Index?.Value is { } index &&
            list.FirstOrDefault(x => PlaceholderOf(x)!.Index?.Value == index && Compatible(type, PlaceholderOf(x)!.Type?.Value ?? PlaceholderValues.Object)) is { } byIndex)
        {
            return byIndex;
        }

        return list.FirstOrDefault(x => Compatible(type, PlaceholderOf(x)!.Type?.Value ?? PlaceholderValues.Object));
    }

    static bool Compatible(PlaceholderValues a, PlaceholderValues b)
    {
        if (a == b)
            return true;

        static bool Title(PlaceholderValues x) => x == PlaceholderValues.Title || x == PlaceholderValues.CenteredTitle;
        static bool Body(PlaceholderValues x) => x == PlaceholderValues.Body || x == PlaceholderValues.Object || x == PlaceholderValues.SubTitle;

        return (Title(a) && Title(b)) || (Body(a) && Body(b));
    }
}

/// <summary>Reading and writing a slide's notes page.</summary>
static class SlideNotes
{
    /// <summary>The notes page's body placeholder text, or null.</summary>
    public static OpenXmlCompositeElement? BodyOf(ShapeTree tree)
        => tree.Elements<Shape>()
            .FirstOrDefault(x => x.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape?.Type?.Value == PlaceholderValues.Body)?
            .TextBody;

    public static OpenXmlCompositeElement EnsureBody(SlideDeck deck, SlidePart slide)
    {
        var notes = slide.NotesSlidePart ?? CreateNotesPage(deck, slide);
        notes.NotesSlide ??= NewNotesSlide();

        var tree = notes.NotesSlide.CommonSlideData!.ShapeTree!;
        if (BodyOf(tree) is { } body)
            return body;

        var shape = NotesBodyShape(SlideObjects.NextShapeId(tree));
        tree.AppendChild(shape);
        return shape.TextBody!;
    }

    /// <summary>One paragraph per line, each a single run.</summary>
    public static void Write(OpenXmlCompositeElement body, string? text)
    {
        foreach (var paragraph in body.Elements<D.Paragraph>().ToList())
            paragraph.Remove();

        var lines = (text ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        foreach (var line in lines)
        {
            var paragraph = new D.Paragraph();
            if (line.Length > 0)
                paragraph.Append(new D.Run(new D.RunProperties { Language = "en-US" }, new D.Text(line)));

            paragraph.Append(new D.EndParagraphRunProperties { Language = "en-US" });
            body.AppendChild(paragraph);
        }
    }

    static NotesSlidePart CreateNotesPage(SlideDeck deck, SlidePart slide)
    {
        var master = deck.PresentationPart.NotesMasterPart ?? CreateNotesMaster(deck);

        var notes = slide.AddNewPart<NotesSlidePart>();
        notes.NotesSlide = NewNotesSlide();
        notes.AddPart(master);
        notes.AddPart(slide);
        return notes;
    }

    static NotesSlide NewNotesSlide() => new(
        new CommonSlideData(new ShapeTree(
            new NonVisualGroupShapeProperties(
                new NonVisualDrawingProperties { Id = 1U, Name = string.Empty },
                new NonVisualGroupShapeDrawingProperties(),
                new ApplicationNonVisualDrawingProperties()),
            new GroupShapeProperties(new D.TransformGroup()),
            new Shape(
                new NonVisualShapeProperties(
                    new NonVisualDrawingProperties { Id = 2U, Name = "Slide Image Placeholder 1" },
                    new NonVisualShapeDrawingProperties(new D.ShapeLocks { NoGrouping = true, NoRotation = true, NoChangeAspect = true }),
                    new ApplicationNonVisualDrawingProperties(new PlaceholderShape { Type = PlaceholderValues.SlideImage })),
                new ShapeProperties()),
            NotesBodyShape(3U))),
        new ColorMapOverride(new D.MasterColorMapping()));

    static Shape NotesBodyShape(uint id) => new(
        new NonVisualShapeProperties(
            new NonVisualDrawingProperties { Id = id, Name = "Notes Placeholder 2" },
            new NonVisualShapeDrawingProperties(new D.ShapeLocks { NoGrouping = true }),
            new ApplicationNonVisualDrawingProperties(new PlaceholderShape { Type = PlaceholderValues.Body, Index = 1U })),
        new ShapeProperties(),
        new TextBody(new D.BodyProperties(), new D.ListStyle(), new D.Paragraph(new D.EndParagraphRunProperties { Language = "en-US" })));

    /// <summary>
    /// A minimal notes master — PowerPoint's own shape of one — for a deck that never had notes.
    /// </summary>
    /// <remarks>
    /// A notes page must relate to a notes master, and a notes master must relate to a theme. The
    /// theme is a copy of the slide master's, so notes text is set in the deck's own fonts.
    /// </remarks>
    static NotesMasterPart CreateNotesMaster(SlideDeck deck)
    {
        var presentationPart = deck.PresentationPart;
        var master = presentationPart.AddNewPart<NotesMasterPart>();

        var widthEmu = presentationPart.Presentation?.NotesSize?.Cx?.Value ?? 6858000L;
        var heightEmu = presentationPart.Presentation?.NotesSize?.Cy?.Value ?? 9144000L;

        master.NotesMaster = new NotesMaster(
            new CommonSlideData(new ShapeTree(
                new NonVisualGroupShapeProperties(
                    new NonVisualDrawingProperties { Id = 1U, Name = string.Empty },
                    new NonVisualGroupShapeDrawingProperties(),
                    new ApplicationNonVisualDrawingProperties()),
                new GroupShapeProperties(new D.TransformGroup()),
                Placeholder(2U, "Slide Image Placeholder 1", PlaceholderValues.SlideImage, null,
                    widthEmu / 8, heightEmu / 12, widthEmu * 3 / 4, heightEmu * 3 / 8),
                Placeholder(3U, "Notes Placeholder 2", PlaceholderValues.Body, 1U,
                    widthEmu / 10, heightEmu / 2, widthEmu * 4 / 5, heightEmu * 2 / 5))),
            new ColorMap
            {
                Background1 = D.ColorSchemeIndexValues.Light1,
                Text1 = D.ColorSchemeIndexValues.Dark1,
                Background2 = D.ColorSchemeIndexValues.Light2,
                Text2 = D.ColorSchemeIndexValues.Dark2,
                Accent1 = D.ColorSchemeIndexValues.Accent1,
                Accent2 = D.ColorSchemeIndexValues.Accent2,
                Accent3 = D.ColorSchemeIndexValues.Accent3,
                Accent4 = D.ColorSchemeIndexValues.Accent4,
                Accent5 = D.ColorSchemeIndexValues.Accent5,
                Accent6 = D.ColorSchemeIndexValues.Accent6,
                Hyperlink = D.ColorSchemeIndexValues.Hyperlink,
                FollowedHyperlink = D.ColorSchemeIndexValues.FollowedHyperlink
            });

        var theme = master.AddNewPart<ThemePart>();
        theme.Theme = presentationPart.SlideMasterParts.FirstOrDefault()?.ThemePart?.Theme?.CloneNode(true) as D.Theme
            ?? new D.Theme { Name = "Office Theme" };

        var presentation = deck.PresentationRoot!;
        var list = new NotesMasterIdList(new NotesMasterId { Id = presentationPart.GetIdOfPart(master) });

        // p:notesMasterIdLst sits straight after p:sldMasterIdLst.
        if (presentation.SlideMasterIdList is { } slideMasters)
            presentation.InsertAfter(list, slideMasters);
        else
            presentation.PrependChild(list);

        deck.MarkPartDirty(master);
        deck.MarkPartDirty(theme);
        deck.MarkStructureDirty();
        return master;

        static Shape Placeholder(uint id, string name, PlaceholderValues type, uint? index, long x, long y, long cx, long cy)
        {
            var placeholder = new PlaceholderShape { Type = type };
            if (index is not null)
                placeholder.Index = index;

            var shape = new Shape(
                new NonVisualShapeProperties(
                    new NonVisualDrawingProperties { Id = id, Name = name },
                    new NonVisualShapeDrawingProperties(new D.ShapeLocks { NoGrouping = true }),
                    new ApplicationNonVisualDrawingProperties(placeholder)),
                new ShapeProperties(
                    new D.Transform2D(new D.Offset { X = x, Y = y }, new D.Extents { Cx = cx, Cy = cy }),
                    new D.PresetGeometry(new D.AdjustValueList()) { Preset = D.ShapeTypeValues.Rectangle }));

            if (type == PlaceholderValues.Body)
                shape.Append(new TextBody(new D.BodyProperties(), new D.ListStyle(), new D.Paragraph()));

            return shape;
        }
    }
}
