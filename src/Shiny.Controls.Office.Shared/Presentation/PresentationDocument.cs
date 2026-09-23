using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Shiny.Controls.Office.Editing;
using Shiny.Controls.Office.Packaging;
using Shiny.Controls.Office.Text;
using D = DocumentFormat.OpenXml.Drawing;
using Package = DocumentFormat.OpenXml.Packaging.PresentationDocument;

namespace Shiny.Controls.Office.Presentation;

/// <summary>
/// An open <c>.pptx</c>, read into a slide/shape model.
/// </summary>
/// <remarks>
/// Shapes come out already resolved through the layout and master, because a placeholder on a slide
/// routinely carries no position, no size and no text formatting of its own — all of that lives on the
/// layout it came from, and on the master behind that. Reading only the slide produces a deck of
/// correctly-worded shapes stacked in the top-left corner.
/// </remarks>
public sealed class SlideDeck : OfficeDocument
{
    readonly Package document;
    readonly List<Slide> slides = new();
    readonly List<SlidePart> parts = new();
    readonly IUnsupportedFeatureSink sink;
    bool contentChanged;
    bool structureChanged;

    SlideDeck(MemoryStream buffer, string? path, Package document, IUnsupportedFeatureSink unsupported)
        : base(buffer, path, unsupported)
    {
        this.document = document;
        this.sink = unsupported;

        var presentationPart = document.PresentationPart
            ?? throw new InvalidDataException("The package has no presentation part.");

        var size = presentationPart.Presentation?.SlideSize;
        this.SlideWidth = size?.Cx?.Value is { } cx ? OoxmlUnits.EmuToPixels(cx) : 960;
        this.SlideHeight = size?.Cy?.Value is { } cy ? OoxmlUnits.EmuToPixels(cy) : 540;

        var number = 1;
        foreach (var slidePart in EnumerateSlides(presentationPart))
        {
            var reader = new SlideReader(slidePart, unsupported);
            this.parts.Add(slidePart);
            this.slides.Add(reader.Read(number++));
        }

        this.Undo = new UndoStack<SlideDeck>(this);
    }

    public IReadOnlyList<Slide> Slides => this.slides;

    /// <summary>Undo history for edits. Empty for a deck opened read-only.</summary>
    public UndoStack<SlideDeck> Undo { get; }

    /// <summary>True when the deck was opened for editing.</summary>
    public bool IsEditable { get; private set; }

    /// <summary>Raised after any edit, so a view can repaint.</summary>
    public event EventHandler? ContentChanged;

    /// <summary>
    /// Raised when slides are added, removed or reordered — including by undo and redo — before
    /// <see cref="ContentChanged"/>.
    /// </summary>
    /// <remarks>
    /// Carries the slide the change happened at, so a view can go there. Every index a view is holding
    /// — the slide it shows, the shape selected on it — may now point at a different slide.
    /// </remarks>
    public event EventHandler<SlidesChangedEventArgs>? SlidesChanged;

    /// <summary>Applies an edit through the undo stack.</summary>
    public void Execute(IEditCommand<SlideDeck> command)
    {
        if (!this.IsEditable)
            throw new InvalidOperationException("This deck was opened read-only. Use OpenAsync(..., editable: true).");

        this.Undo.Execute(command);
    }

    /// <summary>Slide width in pixels at 96 dpi. 960x540 for the usual 16:9 deck.</summary>
    public double SlideWidth { get; }

    public double SlideHeight { get; }

    public double AspectRatio => this.SlideHeight <= 0 ? 16d / 9 : this.SlideWidth / this.SlideHeight;

    public static async Task<SlideDeck> OpenAsync(
        string path,
        IUnsupportedFeatureSink? unsupported = null,
        bool editable = false,
        CancellationToken cancellationToken = default)
    {
        var buffer = await ReadIntoBufferAsync(path, cancellationToken).ConfigureAwait(false);
        return Create(buffer, path, unsupported, editable);
    }

    public static async Task<SlideDeck> OpenAsync(
        Stream source,
        IUnsupportedFeatureSink? unsupported = null,
        bool editable = false,
        CancellationToken cancellationToken = default)
    {
        var buffer = await ReadIntoBufferAsync(source, cancellationToken).ConfigureAwait(false);
        return Create(buffer, null, unsupported, editable);
    }

    static SlideDeck Create(MemoryStream buffer, string? path, IUnsupportedFeatureSink? unsupported, bool editable)
    {
        var sink = unsupported ?? NullUnsupportedFeatureSink.Instance;
        Package document;
        try
        {
            // AutoSave off for the same reason as the workbook and the document: OpenXml otherwise
            // re-serialises every part it has materialised, so merely opening a deck rewrites it.
            document = Package.Open(buffer, isEditable: editable, new OpenSettings { AutoSave = false });
        }
        catch
        {
            buffer.Dispose();
            throw;
        }

        try
        {
            return new SlideDeck(buffer, path, document, sink) { IsEditable = editable };
        }
        catch
        {
            document.Dispose();
            buffer.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Slides in presentation order.
    /// </summary>
    /// <remarks>
    /// <c>SlideParts</c> is in package order, which is not the order the deck plays in. The
    /// <c>sldIdLst</c> is the running order, and using the wrong one shuffles the deck.
    /// </remarks>
    static IEnumerable<SlidePart> EnumerateSlides(PresentationPart presentationPart)
    {
        var list = presentationPart.Presentation?.SlideIdList;
        if (list is null)
        {
            foreach (var part in presentationPart.SlideParts)
                yield return part;

            yield break;
        }

        foreach (var slideId in list.Elements<SlideId>())
        {
            if (slideId.RelationshipId?.Value is not { } id)
                continue;

            if (presentationPart.GetPartById(id) is SlidePart part)
                yield return part;
        }
    }

    /// <summary>
    /// Writes pending edits into the package.
    /// </summary>
    /// <remarks>
    /// An unedited deck is never re-serialised, so opening and saving without changing anything
    /// produces a byte-identical file — and a deck opened read-only can never reach the save branch
    /// at all.
    /// </remarks>
    protected override void FlushToPackage()
    {
        if (!this.contentChanged)
            return;

        foreach (var part in this.dirty)
            part.Slide?.Save();

        foreach (var part in this.dirtyParts)
            part.RootElement?.Save();

        if (this.structureChanged)
            this.document.PresentationPart?.Presentation?.Save();

        // document.Save() is the only public flush the SDK offers, and it re-serialises every part
        // whose DOM has been materialised - which, for a deck, is every slide, layout, master, theme
        // and notes part the reader had to walk. Those round-trip through the same object model, so
        // nothing is lost, but their bytes change. Byte-identity is therefore promised for an
        // *unedited* deck only, which is what the early return above guarantees.
        this.document.Save();
        this.dirty.Clear();
        this.dirtyParts.Clear();
        this.contentChanged = false;
        this.structureChanged = false;
    }

    /// <summary>
    /// Writes a copy with deleted slides stripped out, when there are any.
    /// </summary>
    /// <remarks>
    /// A deleted slide's part stays in the live package, out of the running order, so undoing the
    /// delete can put the very same part back — its pictures, notes and relationships included. The
    /// saved file must not carry it: a slide part the presentation relates to but never lists is one
    /// PowerPoint offers to repair. So the strip happens on a copy, and the deck being edited keeps
    /// everything it needs to undo, even across a save.
    /// </remarks>
    protected override MemoryStream? CreateSaveCopy()
    {
        if (this.parked.Count == 0)
            return null;

        var uris = this.parked.Select(x => x.Uri).ToHashSet();
        var copy = new MemoryStream();

        try
        {
            this.Buffer.Position = 0;
            this.Buffer.CopyTo(copy);
            copy.Position = 0;

            using (var clone = Package.Open(copy, isEditable: true, new OpenSettings { AutoSave = false }))
            {
                var presentationPart = clone.PresentationPart!;
                foreach (var part in presentationPart.SlideParts.Where(x => uris.Contains(x.Uri)).ToList())
                {
                    // The notes page points back at its slide, so it is not an orphan the SDK would
                    // collect on its own once the slide goes.
                    if (part.NotesSlidePart is { } notes)
                        part.DeletePart(notes);

                    presentationPart.DeletePart(part);
                }

                clone.Save();
            }

            copy.Position = 0;
            return copy;
        }
        catch
        {
            copy.Dispose();
            throw;
        }
    }

    // ---- editing surface, driven by the commands ----

    readonly HashSet<SlidePart> dirty = new();

    internal SlidePart? PartAt(int slide)
        => slide >= 0 && slide < this.parts.Count ? this.parts[slide] : null;

    /// <summary>The shape tree a slide's own shapes live in.</summary>
    internal ShapeTree? TreeAt(int slide) => this.PartAt(slide)?.Slide?.CommonSlideData?.ShapeTree;

    /// <summary>
    /// Stores image bytes as a part of one slide and returns the relationship id to reference it by.
    /// </summary>
    /// <remarks>
    /// The part hangs off the slide rather than the presentation, because that is where a picture's
    /// <c>r:embed</c> is resolved from — a relationship on the presentation part is invisible to the
    /// slide that would need it.
    /// </remarks>
    internal string? AddImagePart(int slide, byte[] data, string contentType)
    {
        if (this.PartAt(slide) is not { } part)
            return null;

        var image = part.AddImagePart(contentType);
        using var stream = new MemoryStream(data, writable: false);
        image.FeedData(stream);

        return part.GetIdOfPart(image);
    }

    /// <summary>Re-reads one slide from its (now edited) XML and marks it for saving.</summary>
    internal void Reproject(int slide)
    {
        if (this.PartAt(slide) is not { } part)
            return;

        var reader = new SlideReader(part, this.sink);
        this.slides[slide] = reader.Read(slide + 1);

        this.dirty.Add(part);
        this.contentChanged = true;
        this.MarkDirty();
        this.ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- slide structure, driven by the slide commands ----

    /// <summary>Slides deleted in this session, kept in the package so an undo can restore them.</summary>
    readonly HashSet<SlidePart> parked = new();

    uint nextSlideId;

    internal PresentationPart PresentationPart => this.document.PresentationPart!;

    internal DocumentFormat.OpenXml.Presentation.Presentation? PresentationRoot => this.PresentationPart.Presentation;

    /// <summary>The running order, created in its schema position if the deck has none.</summary>
    internal SlideIdList EnsureSlideIdList()
    {
        var presentation = this.PresentationRoot
            ?? throw new InvalidDataException("The package has no presentation element.");

        if (presentation.SlideIdList is { } list)
            return list;

        // p:sldIdLst follows the three master lists and precedes p:sldSz; appended anywhere else it is
        // a file PowerPoint refuses.
        list = new SlideIdList();
        OpenXmlElement? after = presentation.HandoutMasterIdList
            ?? (OpenXmlElement?)presentation.NotesMasterIdList
            ?? presentation.SlideMasterIdList;

        if (after is not null)
            presentation.InsertAfter(list, after);
        else
            presentation.PrependChild(list);

        return list;
    }

    /// <summary>
    /// An id no slide in this session has used.
    /// </summary>
    /// <remarks>
    /// Monotonic rather than "one past the current maximum": a deleted slide's id is still held by the
    /// undo history, and handing it to a new slide would put two slides with one id in the running order
    /// the moment that delete is undone.
    /// </remarks>
    internal uint AllocateSlideId()
    {
        if (this.nextSlideId == 0)
        {
            var highest = this.PresentationRoot?.SlideIdList?.Elements<SlideId>()
                .Select(x => x.Id?.Value ?? 0)
                .DefaultIfEmpty(0U)
                .Max() ?? 0;

            this.nextSlideId = Math.Max(256, highest + 1);
        }

        // 2147483648 and above are reserved for masters and layouts.
        if (this.nextSlideId >= 2147483648U)
            throw new InvalidOperationException("The deck has run out of slide ids.");

        return this.nextSlideId++;
    }

    readonly HashSet<OpenXmlPart> dirtyParts = new();

    /// <summary>A part other than a slide that an edit wrote into — a notes page, a new notes master.</summary>
    internal void MarkPartDirty(OpenXmlPart part)
    {
        this.dirtyParts.Add(part);
        this.contentChanged = true;
    }

    /// <summary>The presentation element itself changed, outside a slide-structure edit.</summary>
    internal void MarkStructureDirty()
    {
        this.structureChanged = true;
        this.contentChanged = true;
    }

    internal void Park(SlidePart part) => this.parked.Add(part);

    internal void Unpark(SlidePart part) => this.parked.Remove(part);

    /// <summary>
    /// Rebuilds the slide list from the running order after slides were added, removed or reordered.
    /// </summary>
    /// <remarks>
    /// A slide that was already read is reused and only renumbered — a reorder has not changed what is
    /// on it. Only a part this deck has not read before is read.
    /// </remarks>
    internal void Restructure(int focus, SlidePart? added = null)
    {
        var known = new Dictionary<SlidePart, Slide>();
        for (var i = 0; i < this.parts.Count; i++)
            known[this.parts[i]] = this.slides[i];

        this.parts.Clear();
        this.slides.Clear();

        var number = 1;
        foreach (var part in EnumerateSlides(this.PresentationPart))
        {
            this.parts.Add(part);
            this.slides.Add(known.TryGetValue(part, out var slide)
                ? slide with { Number = number }
                : new SlideReader(part, this.sink).Read(number));

            number++;
        }

        if (added is not null)
            this.dirty.Add(added);

        this.structureChanged = true;
        this.contentChanged = true;
        this.MarkDirty();

        this.SlidesChanged?.Invoke(this, new SlidesChangedEventArgs(Math.Clamp(focus, 0, Math.Max(0, this.slides.Count - 1))));
        this.ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            this.document.Dispose();

        base.Dispose(disposing);
    }
}

/// <summary>Slides were added, removed or reordered.</summary>
public sealed class SlidesChangedEventArgs(int focus) : EventArgs
{
    /// <summary>The slide the change happened at, which is where a view should now be.</summary>
    public int Focus { get; } = focus;
}
