using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Shiny.Controls.Office.Editing;
using D = DocumentFormat.OpenXml.Drawing;
using P14 = DocumentFormat.OpenXml.Office2010.PowerPoint;
using PSlide = DocumentFormat.OpenXml.Presentation.Slide;

namespace Shiny.Controls.Office.Presentation;

/// <summary>
/// Adds a new, empty slide built from a layout — what PowerPoint's New Slide button does.
/// </summary>
/// <param name="At">Where the slide goes in the running order; past the end appends it.</param>
/// <param name="LayoutOf">
/// The slide whose layout to use, normally the one being edited. After a title slide the new one is
/// "Title and Content" rather than a second title slide, which is PowerPoint's rule too. Null uses the
/// first master's content layout.
/// </param>
public sealed record NewSlideCommand(int At, int? LayoutOf = null) : SlideCommand
{
    public override string Name => "New slide";

    /// <summary>A specific layout to use instead of choosing one from <see cref="LayoutOf"/>.</summary>
    public SlideLayoutPart? Layout { get; init; }

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if ((this.Layout ?? SlideStructureEdits.ChooseLayout(context, this.LayoutOf)) is not { } layout)
            return new NoOpSlideCommand();

        var before = SlideStructure.Capture(context);
        var at = Math.Clamp(this.At, 0, context.Slides.Count);

        var part = context.PresentationPart.AddNewPart<SlidePart>();
        part.Slide = SlideStructureEdits.FromLayout(layout.SlideLayout);
        part.AddPart(layout);

        SlideStructureEdits.Insert(context, part, at);
        context.Restructure(at, part);

        return new RestoreSlideStructureCommand(this.Name, before, Park: part, Unpark: null, Focus: Math.Max(0, at - 1), ReturnFocus: at);
    }
}

/// <summary>Copies a slide — its shapes, pictures and notes — and puts the copy straight after it.</summary>
public sealed record DuplicateSlideCommand(int Index) : SlideCommand
{
    public override string Name => "Duplicate slide";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (context.PartAt(this.Index) is not { Slide: { } slide } source)
            return new NoOpSlideCommand();

        var before = SlideStructure.Capture(context);

        var part = context.PresentationPart.AddNewPart<SlidePart>();
        var copy = (PSlide)slide.CloneNode(true);
        SlideStructureEdits.ForgetIdentity(copy);
        part.Slide = copy;

        SlideStructureEdits.CopyRelationships(source, part);
        SlideStructureEdits.Insert(context, part, this.Index + 1);
        context.Restructure(this.Index + 1, part);

        return new RestoreSlideStructureCommand(this.Name, before, Park: part, Unpark: null, Focus: this.Index, ReturnFocus: this.Index + 1);
    }
}

/// <summary>
/// Takes a slide out of the deck.
/// </summary>
/// <remarks>
/// The slide's part stays in the live package, out of the running order, so undo puts back the very
/// same slide rather than a reconstruction of it. It is left out of whatever is saved — see
/// <see cref="SlideDeck"/>'s save copy.
/// </remarks>
public sealed record DeleteSlideCommand(int Index) : SlideCommand
{
    public override string Name => "Delete slide";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (context.PartAt(this.Index) is not { } part || SlideStructureEdits.SlideIdOf(context, this.Index) is not { } entry)
            return new NoOpSlideCommand();

        var before = SlideStructure.Capture(context);
        var presentation = context.PresentationRoot!;
        var relationshipId = entry.RelationshipId?.Value;

        entry.Remove();

        if (entry.Id?.Value is { } id)
            SlideStructureEdits.RemoveFromSections(presentation, id);

        // A custom show naming the slide would name nothing once it is gone.
        if (relationshipId is not null && presentation.CustomShowList is { } shows)
        {
            foreach (var reference in shows.Descendants<SlideListEntry>().Where(x => x.Id?.Value == relationshipId).ToList())
                reference.Remove();
        }

        context.Park(part);
        context.Restructure(this.Index);

        return new RestoreSlideStructureCommand(this.Name, before, Park: null, Unpark: part, Focus: this.Index, ReturnFocus: this.Index);
    }
}

/// <summary>Moves a slide to another place in the running order.</summary>
/// <param name="From">The slide to move.</param>
/// <param name="To">Where it ends up, counted after it has been taken out.</param>
public sealed record MoveSlideCommand(int From, int To) : SlideCommand
{
    public override string Name => "Move slide";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        var count = context.Slides.Count;
        var to = Math.Clamp(this.To, 0, Math.Max(0, count - 1));

        if (this.From == to || SlideStructureEdits.SlideIdOf(context, this.From) is not { } entry)
            return new NoOpSlideCommand();

        var before = SlideStructure.Capture(context);
        var presentation = context.PresentationRoot!;

        var others = Enumerable.Range(0, count)
            .Where(x => x != this.From)
            .Select(x => SlideStructureEdits.SlideIdOf(context, x))
            .OfType<SlideId>()
            .ToList();

        entry.Remove();

        if (to < others.Count)
            others[to].InsertBeforeSelf(entry);
        else if (others.Count > 0)
            others[^1].InsertAfterSelf(entry);
        else
            context.EnsureSlideIdList().AppendChild(entry);

        if (entry.Id?.Value is { } id)
        {
            SlideStructureEdits.RemoveFromSections(presentation, id);
            SlideStructureEdits.PlaceInSection(
                presentation,
                id,
                previous: to > 0 ? others[to - 1].Id?.Value : null,
                next: to < others.Count ? others[to].Id?.Value : null);
        }

        context.Restructure(to);

        return new RestoreSlideStructureCommand(this.Name, before, Park: null, Unpark: null, Focus: this.From, ReturnFocus: to);
    }
}

/// <summary>
/// Puts the running order back as it was captured, parking or un-parking the slide the edit added or
/// removed.
/// </summary>
/// <remarks>
/// Every structural edit undoes through this. The order, the sections and the custom shows are
/// restored wholesale rather than by describing an inverse move, because the inverse of "move slide 1
/// to 4" is not "move slide 4 to 1" once sections are involved — the slide would come back into
/// whichever section it landed beside, not the one it left.
/// </remarks>
sealed record RestoreSlideStructureCommand(
    string Label,
    SlideStructure Structure,
    SlidePart? Park,
    SlidePart? Unpark,
    int Focus,
    int ReturnFocus) : SlideCommand
{
    public override string Name => this.Label;

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        var now = SlideStructure.Capture(context);

        this.Structure.Restore(context);

        if (this.Park is not null)
            context.Park(this.Park);

        if (this.Unpark is not null)
            context.Unpark(this.Unpark);

        context.Restructure(this.Focus);

        return new RestoreSlideStructureCommand(this.Label, now, this.Unpark, this.Park, this.ReturnFocus, this.Focus);
    }
}

/// <summary>The parts of <c>p:presentation</c> that name slides, captured so an edit to them can be undone exactly.</summary>
sealed record SlideStructure(OpenXmlElement? Ids, OpenXmlElement? Extensions, OpenXmlElement? CustomShows)
{
    public static SlideStructure Capture(SlideDeck deck)
    {
        var presentation = deck.PresentationRoot;

        return new SlideStructure(
            presentation?.SlideIdList?.CloneNode(true),
            presentation?.PresentationExtensionList?.CloneNode(true),
            presentation?.CustomShowList?.CloneNode(true));
    }

    public void Restore(SlideDeck deck)
    {
        var presentation = deck.PresentationRoot!;

        var ids = deck.EnsureSlideIdList();
        ids.RemoveAllChildren();
        if (this.Ids is not null)
        {
            foreach (var child in this.Ids.ChildElements)
                ids.AppendChild(child.CloneNode(true));
        }

        // Neither of these is ever created or removed by a slide edit, only edited inside, so they
        // exist now exactly when they existed at capture.
        if (this.Extensions is not null && presentation.PresentationExtensionList is { } extensions)
            presentation.ReplaceChild(this.Extensions.CloneNode(true), extensions);

        if (this.CustomShows is not null && presentation.CustomShowList is { } shows)
            presentation.ReplaceChild(this.CustomShows.CloneNode(true), shows);
    }
}

/// <summary>The OOXML side of the slide-structure commands.</summary>
static class SlideStructureEdits
{
    /// <summary>The running-order entry for the slide at a model index.</summary>
    /// <remarks>
    /// Found through the part rather than by position: an entry whose relationship points at nothing is
    /// skipped when the deck is read, so the n-th entry is not always the n-th slide.
    /// </remarks>
    public static SlideId? SlideIdOf(SlideDeck deck, int index)
    {
        if (deck.PartAt(index) is not { } part)
            return null;

        var relationshipId = deck.PresentationPart.GetIdOfPart(part);
        return deck.PresentationRoot?.SlideIdList?.Elements<SlideId>()
            .FirstOrDefault(x => x.RelationshipId?.Value == relationshipId);
    }

    /// <summary>Adds a part that is already related to the presentation into the running order.</summary>
    public static void Insert(SlideDeck deck, SlidePart part, int at)
    {
        var list = deck.EnsureSlideIdList();
        var entry = new SlideId
        {
            Id = deck.AllocateSlideId(),
            RelationshipId = deck.PresentationPart.GetIdOfPart(part)
        };

        var next = SlideIdOf(deck, at);
        var previous = at > 0 ? SlideIdOf(deck, Math.Min(at, deck.Slides.Count) - 1) : null;

        if (next is not null)
            next.InsertBeforeSelf(entry);
        else if (previous is not null)
            previous.InsertAfterSelf(entry);
        else
            list.AppendChild(entry);

        PlaceInSection(deck.PresentationRoot!, entry.Id!.Value, previous?.Id?.Value, next?.Id?.Value);
    }

    /// <summary>
    /// Picks the layout for a new slide.
    /// </summary>
    /// <remarks>
    /// The layout of the slide being edited, because a deck is built slide after slide of the same
    /// kind — except after a title slide, where the next one is content. With nothing to go by, the
    /// first master's content layout, which is what a blank deck's first New Slide gives in PowerPoint.
    /// </remarks>
    public static SlideLayoutPart? ChooseLayout(SlideDeck deck, int? like)
    {
        if (like is { } index && deck.PartAt(index)?.SlideLayoutPart is { } reference)
        {
            if (reference.SlideLayout?.Type?.Value == SlideLayoutValues.Title &&
                reference.SlideMasterPart is { } master &&
                FindLayout(master, SlideLayoutValues.Object) is { } content)
            {
                return content;
            }

            return reference;
        }

        var first = FirstMaster(deck);
        if (first is null)
            return null;

        return FindLayout(first, SlideLayoutValues.Object)
            ?? FindLayout(first, SlideLayoutValues.Text)
            ?? first.SlideLayoutParts.FirstOrDefault();
    }

    static SlideMasterPart? FirstMaster(SlideDeck deck)
    {
        var presentationPart = deck.PresentationPart;
        var id = presentationPart.Presentation?.SlideMasterIdList?.Elements<SlideMasterId>().FirstOrDefault()?.RelationshipId?.Value;

        return id is not null && presentationPart.GetPartById(id) is SlideMasterPart master
            ? master
            : presentationPart.SlideMasterParts.FirstOrDefault();
    }

    static SlideLayoutPart? FindLayout(SlideMasterPart master, SlideLayoutValues type)
        => master.SlideLayoutParts.FirstOrDefault(x => x.SlideLayout?.Type?.Value == type);

    /// <summary>
    /// An empty slide carrying the layout's placeholders, the way PowerPoint writes one.
    /// </summary>
    /// <remarks>
    /// Each placeholder is only its identity — type and index — with empty shape properties, so its
    /// position, size and text formatting all come from the layout, and changing the layout later
    /// moves it. Date, footer and slide-number placeholders are left off, as PowerPoint leaves them off:
    /// those are switched on for the whole deck from Header &amp; Footer, not per new slide.
    /// </remarks>
    public static PSlide FromLayout(SlideLayout? layout)
    {
        var tree = new ShapeTree(
            new NonVisualGroupShapeProperties(
                new NonVisualDrawingProperties { Id = 1U, Name = string.Empty },
                new NonVisualGroupShapeDrawingProperties(),
                new ApplicationNonVisualDrawingProperties()),
            new GroupShapeProperties(new D.TransformGroup()));

        var id = 2U;
        foreach (var source in layout?.CommonSlideData?.ShapeTree?.Elements<Shape>() ?? [])
        {
            if (NewPlaceholder(source, id) is { } shape)
            {
                tree.Append(shape);
                id++;
            }
        }

        return new PSlide(
            new CommonSlideData(tree),
            new ColorMapOverride(new D.MasterColorMapping()));
    }

    /// <summary>
    /// An empty slide placeholder standing in for a layout's, or null when the layout shape is not a
    /// placeholder a new slide carries.
    /// </summary>
    public static Shape? NewPlaceholder(Shape source, uint id)
    {
        if (source.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape is not { } template)
            return null;

        var type = template.Type?.Value;
        if (type == PlaceholderValues.DateAndTime || type == PlaceholderValues.Footer || type == PlaceholderValues.SlideNumber)
            return null;

        var placeholder = new PlaceholderShape();
        if (template.Type is not null)
            placeholder.Type = template.Type.Value;
        if (template.Orientation is not null)
            placeholder.Orientation = template.Orientation.Value;
        if (template.Size is not null)
            placeholder.Size = template.Size.Value;
        if (template.Index is not null)
            placeholder.Index = template.Index.Value;

        var shape = new Shape(
            new NonVisualShapeProperties(
                new NonVisualDrawingProperties
                {
                    Id = id,
                    Name = source.NonVisualShapeProperties?.NonVisualDrawingProperties?.Name?.Value ?? "Placeholder"
                },
                new NonVisualShapeDrawingProperties(new D.ShapeLocks { NoGrouping = true }),
                new ApplicationNonVisualDrawingProperties(placeholder)),
            new ShapeProperties());

        if (HoldsText(type))
        {
            shape.Append(new TextBody(
                new D.BodyProperties(),
                new D.ListStyle(),
                new D.Paragraph(new D.EndParagraphRunProperties { Language = "en-US" })));
        }

        return shape;
    }

    /// <summary>A missing type means <c>obj</c>, the content placeholder, which takes text.</summary>
    static bool HoldsText(PlaceholderValues? type)
        => type is null ||
           type == PlaceholderValues.Title ||
           type == PlaceholderValues.CenteredTitle ||
           type == PlaceholderValues.SubTitle ||
           type == PlaceholderValues.Body ||
           type == PlaceholderValues.Object;

    /// <summary>
    /// Drops what makes a slide <em>that</em> slide, so a copy is not mistaken for its original.
    /// </summary>
    /// <remarks>
    /// <c>p14:creationId</c> is how PowerPoint tells slides apart when merging and co-authoring; two
    /// slides carrying one id look like the same slide edited twice.
    /// </remarks>
    public static void ForgetIdentity(PSlide slide)
    {
        foreach (var creation in slide.Descendants<P14.CreationId>().ToList())
            creation.Parent?.Remove();

        if (slide.SlideExtensionList is { HasChildren: false } empty)
            empty.Remove();
    }

    /// <summary>
    /// Gives a copied slide everything its original pointed at.
    /// </summary>
    /// <remarks>
    /// Pictures, charts and media are shared rather than copied — two relationships to one part, which
    /// is what PowerPoint itself does for a picture pasted twice. The notes page is the exception: it
    /// points back at its slide, so the copy needs a notes page of its own that points at the copy.
    /// Relationship ids are kept, because the copied XML refers to its targets by them.
    /// </remarks>
    public static void CopyRelationships(SlidePart source, SlidePart target)
    {
        foreach (var pair in source.Parts)
        {
            if (pair.OpenXmlPart is NotesSlidePart notes)
            {
                CopyNotes(notes, source, target, pair.RelationshipId);
                continue;
            }

            target.AddPart(pair.OpenXmlPart, pair.RelationshipId);
        }

        foreach (var external in source.ExternalRelationships)
            target.AddExternalRelationship(external.RelationshipType, external.Uri, external.Id);

        foreach (var link in source.HyperlinkRelationships)
            target.AddHyperlinkRelationship(link.Uri, link.IsExternal, link.Id);

        foreach (var reference in source.DataPartReferenceRelationships)
        {
            if (reference.DataPart is not MediaDataPart media)
                continue;

            switch (reference)
            {
                case VideoReferenceRelationship:
                    target.AddVideoReferenceRelationship(media, reference.Id);
                    break;

                case AudioReferenceRelationship:
                    target.AddAudioReferenceRelationship(media, reference.Id);
                    break;

                case MediaReferenceRelationship:
                    target.AddMediaReferenceRelationship(media, reference.Id);
                    break;
            }
        }
    }

    static void CopyNotes(NotesSlidePart notes, SlidePart source, SlidePart target, string relationshipId)
    {
        if (notes.NotesSlide is not { } page)
            return;

        var copy = target.AddNewPart<NotesSlidePart>(relationshipId);
        copy.NotesSlide = (NotesSlide)page.CloneNode(true);

        foreach (var pair in notes.Parts)
        {
            copy.AddPart(
                ReferenceEquals(pair.OpenXmlPart, source) ? target : pair.OpenXmlPart,
                pair.RelationshipId);
        }
    }

    // ---- sections ----

    /// <summary>Takes a slide id out of every section that lists it.</summary>
    public static void RemoveFromSections(DocumentFormat.OpenXml.Presentation.Presentation presentation, uint id)
    {
        foreach (var entry in presentation.Descendants<P14.SectionSlideIdListEntry>().Where(x => x.Id?.Value == id).ToList())
            entry.Remove();
    }

    /// <summary>
    /// Puts a slide id into the section of the slide it now follows, or else the one it now precedes.
    /// </summary>
    /// <remarks>
    /// A deck with sections must list every slide in exactly one of them, in running order; a slide in
    /// none is one PowerPoint repairs on open. A deck without sections is left alone.
    /// </remarks>
    public static void PlaceInSection(DocumentFormat.OpenXml.Presentation.Presentation presentation, uint id, uint? previous, uint? next)
    {
        if (presentation.Descendants<P14.SectionList>().FirstOrDefault() is not { } sections)
            return;

        var entry = new P14.SectionSlideIdListEntry { Id = id };
        var entries = sections.Descendants<P14.SectionSlideIdListEntry>().ToList();

        if (previous is not null && entries.FirstOrDefault(x => x.Id?.Value == previous) is { } after)
        {
            after.InsertAfterSelf(entry);
            return;
        }

        if (next is not null && entries.FirstOrDefault(x => x.Id?.Value == next) is { } before)
        {
            before.InsertBeforeSelf(entry);
            return;
        }

        if (sections.Elements<P14.Section>().FirstOrDefault() is not { } first)
            return;

        var list = first.GetFirstChild<P14.SectionSlideIdList>();
        if (list is null)
        {
            list = new P14.SectionSlideIdList();
            first.PrependChild(list);
        }

        list.AppendChild(entry);
    }
}
