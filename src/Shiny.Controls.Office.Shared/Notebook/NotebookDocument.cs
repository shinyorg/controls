using Shiny.Controls.Office.Editing;
using Shiny.Controls.Office.Packaging;
using Shiny.Controls.Office.Shapes;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;

namespace Shiny.Controls.Office.Notebook;

/// <summary>Where a page sits in the notebook.</summary>
public readonly record struct PageAddress(int Section, int Page)
{
    public static readonly PageAddress None = new(-1, -1);

    public bool IsValid => this.Section >= 0 && this.Page >= 0;
}

/// <summary>
/// An open notebook: sections of pages, with transactional undo over every edit.
/// </summary>
/// <remarks>
/// <para>
/// Unlike the deck, the workbook and the document, this one is not a projection of an OOXML package —
/// the model here <i>is</i> the truth and the file is written from it. That removes the whole reproject
/// step the other three need, and it is why an edit command can simply swap an immutable
/// <see cref="NoteItem"/> for another and return the old one as its inverse.
/// </para>
/// <para>
/// It still derives from <see cref="OfficeDocument"/> so a host gets the same dirty tracking, the same
/// atomic <c>SaveAsAsync</c> through a temporary file, and the same <c>ToArray</c> as every other
/// editor here. <see cref="FlushToPackage"/> rewrites the whole buffer rather than patching it, which
/// is cheap because a notebook's bytes are JSON and pictures that are already in memory.
/// </para>
/// </remarks>
public sealed class NotebookDocument : OfficeDocument
{
    NotebookDocument(MemoryStream buffer, string? path, IUnsupportedFeatureSink unsupported)
        : base(buffer, path, unsupported)
        => this.Undo = new UndoStack<NotebookDocument>(this);

    internal NotebookDocument(string? path)
        : this(new MemoryStream(), path, NullUnsupportedFeatureSink.Instance)
    {
    }

    public string Title { get; set; } = "Notebook";

    /// <summary>
    /// The sections, top to bottom.
    /// </summary>
    /// <remarks>
    /// A mutable list, matching <see cref="NotebookSection.Pages"/> — building a notebook up before
    /// anyone edits it (a template, an import, seed content) is a real thing to want, and routing that
    /// through the undo stack would leave a fresh document with a history of its own construction.
    /// Every edit a <i>user</i> makes goes through a command instead, because an edit that cannot be
    /// undone is not one this control offers.
    /// </remarks>
    public List<NotebookSection> Sections { get; } = new();

    public UndoStack<NotebookDocument> Undo { get; }

    /// <summary>Raised after any edit that changed the notebook, so a view can repaint.</summary>
    public event EventHandler? ContentChanged;

    /// <summary>Raised when sections or pages are added, removed or renamed, so a navigator can rebuild.</summary>
    public event EventHandler? StructureChanged;

    public static string NewId() => Guid.NewGuid().ToString("N")[..16];

    /// <summary>A brand-new notebook with one section holding one empty page.</summary>
    /// <remarks>
    /// Never zero pages. Every surface here — the canvas, the page list, the section tabs — has a
    /// sensible empty state only when there is somewhere to start writing; an empty notebook reads as
    /// a control that failed to load.
    /// </remarks>
    public static NotebookDocument Create(string title = "Notebook")
    {
        var document = new NotebookDocument((string?)null) { Title = title };
        document.Sections.Add(NewSection("Section 1"));

        return document;
    }

    internal static NotebookSection NewSection(string title)
    {
        var section = new NotebookSection(NewId(), title);
        section.Pages.Add(new NotebookPage(NewId(), "Untitled page"));

        return section;
    }

    public static async Task<NotebookDocument> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        return await OpenAsync(file, path, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<NotebookDocument> OpenAsync(Stream source, string? path = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        // Buffered rather than read in place: ZipArchive seeks, and the stream a host hands over is
        // routinely a network or content-provider stream that cannot.
        var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        buffer.Position = 0;

        return NotebookPackage.Read(buffer, path);
    }

    // ---- navigation ----

    public NotebookSection? SectionAt(int index) => this.Sections.ElementAtOrDefault(index);

    public NotebookPage? PageAt(PageAddress address)
        => this.SectionAt(address.Section)?.Pages.ElementAtOrDefault(address.Page);

    public PageAddress Locate(string pageId)
    {
        for (var s = 0; s < this.Sections.Count; s++)
        {
            var pages = this.Sections[s].Pages;
            for (var p = 0; p < pages.Count; p++)
            {
                if (pages[p].Id == pageId)
                    return new PageAddress(s, p);
            }
        }

        return PageAddress.None;
    }

    public IEnumerable<(PageAddress Address, NotebookPage Page)> AllPages()
    {
        for (var s = 0; s < this.Sections.Count; s++)
        {
            var pages = this.Sections[s].Pages;
            for (var p = 0; p < pages.Count; p++)
                yield return (new PageAddress(s, p), pages[p]);
        }
    }

    // ---- editing ----

    public void Execute(IEditCommand<NotebookDocument> command) => this.Undo.Execute(command);

    /// <summary>
    /// Marks a page edited and tells the views.
    /// </summary>
    /// <remarks>
    /// Called from the commands rather than from the controller, so it covers every route into the
    /// model — including undo and redo, which do not go through the controller's methods at all.
    /// </remarks>
    internal void NotifyContentChanged(NotebookPage? page)
    {
        if (page is not null)
            page.Modified = DateTimeOffset.UtcNow;

        this.MarkDirty();
        this.ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void NotifyStructureChanged()
    {
        this.MarkDirty();
        this.StructureChanged?.Invoke(this, EventArgs.Empty);
        this.ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void FlushToPackage()
    {
        this.Buffer.SetLength(0);
        this.Buffer.Position = 0;
        NotebookPackage.Write(this, this.Buffer);
        this.Buffer.Position = 0;
    }

    // ---- construction helpers ----

    /// <summary>A default text container, which is what a click on empty canvas creates.</summary>
    public static NoteItem NewTextItem(double x, double y, double width = 320, string? text = null, TextStyle? style = null)
    {
        var runs = string.IsNullOrEmpty(text)
            ? Array.Empty<StyledRun>()
            : [new StyledRun(text, style ?? DefaultTextStyle)];

        return new NoteItem
        {
            Id = NewId(),
            Kind = NoteItemKind.Text,
            X = x,
            Y = y,
            Width = width,
            Height = 32,
            AutoHeight = true,
            Text = new ShapeTextBody([new ShapeParagraph(runs)])
            {
                InsetLeft = 4,
                InsetRight = 4,
                InsetTop = 3,
                InsetBottom = 3
            }
        };
    }

    public static readonly TextStyle DefaultTextStyle = TextStyle.Default with { FontFamily = "Calibri", FontSize = 12 };

    public static NoteItem NewShapeItem(ShapeGeometry geometry, double x, double y, double width, double height, ArgbColor? fill = null, ArgbColor? outline = null)
        => new()
        {
            Id = NewId(),
            Kind = NoteItemKind.Shape,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            Geometry = geometry,
            Fill = fill is { } f ? new ShapeFill { Solid = f } : ShapeFill.None,
            Outline = new ShapeOutline(outline ?? new ArgbColor(255, 0x33, 0x33, 0x33), 1.5),
            Text = new ShapeTextBody([new ShapeParagraph([]) { Alignment = TextAlignment.Center }])
            {
                Anchor = TextAnchor.Middle
            }
        };

    public static NoteItem NewImageItem(byte[] bytes, string contentType, double x, double y, double width, double height)
        => new()
        {
            Id = NewId(),
            Kind = NoteItemKind.Image,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            Image = bytes,
            ImageContentType = contentType
        };

    public static NoteItem NewInkItem(InkStroke stroke)
        => new()
        {
            Id = NewId(),
            Kind = NoteItemKind.Ink,
            Stroke = stroke
        };
}
