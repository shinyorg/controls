using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Shiny.Controls.Office.Editing;
using Shiny.Controls.Office.Shapes;
using Shiny.Controls.Office.Spreadsheet;
using D = DocumentFormat.OpenXml.Drawing;

namespace Shiny.Controls.Office.Presentation;

/// <summary>One page of the Slide Master view: the master itself, or one of its layouts.</summary>
public sealed record SlideMasterPage(string Name, bool IsMaster, int MasterIndex)
{
    internal OpenXmlPart? Part { get; init; }

    /// <summary>How many slides use this layout (always the deck's count for the master).</summary>
    public int UsedBy { get; init; }
}

/// <summary>A change to a master text style — the Font group while a master placeholder is selected.</summary>
public sealed record SlideMasterTextStyle
{
    public string? FontFamily { get; init; }

    /// <summary>In points.</summary>
    public double? FontSize { get; init; }

    public ArgbColor? Color { get; init; }

    public bool? Bold { get; init; }

    public bool? Italic { get; init; }
}

/// <summary>
/// The Slide Master view: the master and its layouts as pages, their placeholders selectable, movable and
/// restylable, and their backgrounds.
/// </summary>
/// <remarks>
/// <para>
/// What it edits is inherited by every slide, so every change re-reads the whole deck. The text styles
/// are the real mechanism: a title placeholder selected on the master restyles the master's
/// <c>p:titleStyle</c>, a body one its <c>p:bodyStyle</c> — every level for the face and colour, the first
/// for the size — and on a layout, the placeholder's own <c>a:lstStyle</c>.
/// </para>
/// <para>
/// Every edit undoes by putting the whole master or layout part back, which is small, exact and immune
/// to element references going stale.
/// </para>
/// </remarks>
public sealed class SlideMasterController
{
    readonly SlideDeck deck;
    readonly SlideEditorController editor;
    readonly List<SlideMasterPage> pages = [];

    int pageIndex;
    int selected = -1;
    Slide? current;

    ShapeHandle dragging;
    double dragStartX;
    double dragStartY;
    SlideRect dragOrigin;
    OpenXmlElement? dragBefore;

    internal SlideMasterController(SlideDeck deck, SlideEditorController editor)
    {
        this.deck = deck;
        this.editor = editor;
    }

    /// <summary>The master and its layouts, in PowerPoint's order: each master followed by its layouts.</summary>
    public IReadOnlyList<SlideMasterPage> Pages => this.pages;

    /// <summary>The page being edited.</summary>
    public int PageIndex
    {
        get => this.pageIndex;
        set
        {
            var clamped = Math.Clamp(value, 0, Math.Max(0, this.pages.Count - 1));
            if (clamped == this.pageIndex && this.current is not null)
                return;

            this.pageIndex = clamped;
            this.selected = -1;
            this.Refresh();
        }
    }

    public SlideMasterPage? Page => this.pages.ElementAtOrDefault(this.pageIndex);

    /// <summary>The page as a slide model, placeholders included, for painting.</summary>
    public Slide? CurrentPage => this.current;

    public int SelectedShape => this.selected;

    public SlideShape? Selection => this.current?.Shapes.ElementAtOrDefault(this.selected);

    /// <summary>Opens the view on the layout the given slide uses.</summary>
    internal void Open(int slideIndex)
    {
        this.Rebuild();
        var layout = this.deck.PartAt(slideIndex)?.SlideLayoutPart;
        var at = this.pages.FindIndex(x => ReferenceEquals(x.Part, layout));
        this.pageIndex = Math.Max(0, at);
        this.selected = -1;
        this.Refresh();
    }

    internal void Close()
    {
        this.selected = -1;
        this.current = null;
        this.dragging = ShapeHandle.None;
    }

    void Rebuild()
    {
        this.pages.Clear();
        var masters = this.deck.MasterParts;

        for (var m = 0; m < masters.Count; m++)
        {
            var master = masters[m];
            this.pages.Add(new SlideMasterPage(masters.Count > 1 ? $"Slide Master {m + 1}" : "Slide Master", true, m) { Part = master, UsedBy = this.deck.Slides.Count });

            var ordered = master.SlideMaster?.SlideLayoutIdList?.Elements<SlideLayoutId>()
                .Select(x => x.RelationshipId?.Value is { } id ? master.GetPartById(id) as SlideLayoutPart : null)
                .OfType<SlideLayoutPart>()
                .ToList() is { Count: > 0 } list ? list : master.SlideLayoutParts.ToList();

            for (var i = 0; i < ordered.Count; i++)
            {
                var layout = ordered[i];
                var used = Enumerable.Range(0, this.deck.Slides.Count).Count(s => ReferenceEquals(this.deck.PartAt(s)?.SlideLayoutPart, layout));
                this.pages.Add(new SlideMasterPage(layout.SlideLayout?.CommonSlideData?.Name?.Value ?? $"Layout {i + 1}", false, m) { Part = layout, UsedBy = used });
            }
        }
    }

    /// <summary>Re-reads the page without raising a change — the editor is about to raise one itself.</summary>
    internal void RefreshSilently()
    {
        this.Read();
    }

    /// <summary>Re-reads the page after an edit, keeping the selection.</summary>
    internal void Refresh()
    {
        this.Read();
        this.editor.NotifyChanged();
    }

    readonly Dictionary<int, Slide?> models = [];

    /// <summary>Any page as a slide model — the rail's thumbnails in the master view.</summary>
    public Slide? PageModel(int index)
    {
        if (index == this.pageIndex && this.current is not null)
            return this.current;

        if (this.models.TryGetValue(index, out var cached))
            return cached;

        var model = this.pages.ElementAtOrDefault(index)?.Part switch
        {
            SlideMasterPart master => new SlideReader(master, null, this.deck.Unsupported).ReadTemplate(index + 1),
            SlideLayoutPart layout when layout.SlideMasterPart is { } master => new SlideReader(master, layout, this.deck.Unsupported).ReadTemplate(index + 1),
            _ => null
        };

        this.models[index] = model;
        return model;
    }

    void Read()
    {
        this.models.Clear();
        if (this.pages.Count == 0)
            this.Rebuild();

        this.current = this.Page?.Part switch
        {
            SlideMasterPart master => new SlideReader(master, null, this.deck.Unsupported).ReadTemplate(this.pageIndex + 1),
            SlideLayoutPart layout when layout.SlideMasterPart is { } master => new SlideReader(master, layout, this.deck.Unsupported).ReadTemplate(this.pageIndex + 1),
            _ => null
        };

        if (this.selected >= (this.current?.Shapes.Count ?? 0))
            this.selected = -1;
    }

    // ---- geometry ----

    public SlideRect? SelectionBounds() => this.Selection is { } shape ? this.editor.BoundsOf(shape) : null;

    public IEnumerable<(ShapeHandle Handle, SlideRect Rect)> SelectionHandles()
    {
        if (this.SelectionBounds() is not { } bounds)
            yield break;

        var size = this.editor.HandleSize;
        var half = size / 2;
        var midX = bounds.X + bounds.Width / 2;
        var midY = bounds.Y + bounds.Height / 2;

        yield return (ShapeHandle.TopLeft, new(bounds.X - half, bounds.Y - half, size, size));
        yield return (ShapeHandle.Top, new(midX - half, bounds.Y - half, size, size));
        yield return (ShapeHandle.TopRight, new(bounds.Right - half, bounds.Y - half, size, size));
        yield return (ShapeHandle.Right, new(bounds.Right - half, midY - half, size, size));
        yield return (ShapeHandle.BottomRight, new(bounds.Right - half, bounds.Bottom - half, size, size));
        yield return (ShapeHandle.Bottom, new(midX - half, bounds.Bottom - half, size, size));
        yield return (ShapeHandle.BottomLeft, new(bounds.X - half, bounds.Bottom - half, size, size));
        yield return (ShapeHandle.Left, new(bounds.X - half, midY - half, size, size));
    }

    int ShapeAt(double x, double y)
    {
        if (this.current is not { } page)
            return -1;

        for (var i = page.Shapes.Count - 1; i >= 0; i--)
        {
            var shape = page.Shapes[i];
            if (shape is { IsEditable: true, IsInGroup: false } && this.editor.BoundsOf(shape) is { } bounds && bounds.Contains(x, y))
                return i;
        }

        return -1;
    }

    public void Select(int shape)
    {
        this.selected = this.current?.Shapes.ElementAtOrDefault(shape) is { IsEditable: true } ? shape : -1;
        this.editor.NotifyChanged();
    }

    // ---- pointer ----

    internal bool PointerDown(double x, double y)
    {
        var handle = ShapeHandle.None;
        foreach (var (candidate, rect) in this.SelectionHandles())
        {
            if (rect.Contains(x, y))
                handle = candidate;
        }

        if (handle == ShapeHandle.None)
        {
            var hit = this.ShapeAt(x, y);
            this.Select(hit);
            if (hit < 0)
                return false;

            handle = ShapeHandle.Body;
        }

        if (this.Page?.Part is not { } part || this.SelectionBounds() is not { } bounds)
            return false;

        this.dragging = handle;
        this.dragStartX = x;
        this.dragStartY = y;
        this.dragOrigin = bounds;
        this.dragBefore = part.RootElement?.CloneNode(true);
        return true;
    }

    internal void PointerMove(double x, double y, bool constrain)
    {
        if (this.dragging == ShapeHandle.None || this.Selection?.Element is not { } element || this.editor.Scale <= 0)
            return;

        var scale = this.editor.Scale;
        var dx = (x - this.dragStartX) / scale;
        var dy = (y - this.dragStartY) / scale;

        if (constrain && this.dragging == ShapeHandle.Body)
        {
            if (Math.Abs(dx) >= Math.Abs(dy)) dy = 0; else dx = 0;
        }

        if (this.editor.ToSlide(this.dragOrigin.X, this.dragOrigin.Y) is not { } origin)
            return;

        var (sx, sy, sw, sh) = (origin.X, origin.Y, this.dragOrigin.Width / scale, this.dragOrigin.Height / scale);
        var next = this.dragging switch
        {
            ShapeHandle.Body => (sx + dx, sy + dy, sw, sh),
            ShapeHandle.Left => (sx + dx, sy, sw - dx, sh),
            ShapeHandle.Right => (sx, sy, sw + dx, sh),
            ShapeHandle.Top => (sx, sy + dy, sw, sh - dy),
            ShapeHandle.Bottom => (sx, sy, sw, sh + dy),
            ShapeHandle.TopLeft => (sx + dx, sy + dy, sw - dx, sh - dy),
            ShapeHandle.TopRight => (sx, sy + dy, sw + dx, sh - dy),
            ShapeHandle.BottomLeft => (sx + dx, sy, sw - dx, sh + dy),
            _ => (sx, sy, sw + dx, sh + dy)
        };

        // Written live, undone as one step on release: a template drag has no merging command of its own.
        WriteBounds(element, next.Item1, next.Item2, Math.Max(4, next.Item3), Math.Max(4, next.Item4));
        this.Refresh();
    }

    internal void PointerUp()
    {
        if (this.dragging == ShapeHandle.None)
            return;

        this.dragging = ShapeHandle.None;

        if (this.Page?.Part is not { RootElement: { } root } part || this.dragBefore is not { } before)
            return;

        this.dragBefore = null;
        if (before.OuterXml == root.OuterXml)
            return;

        this.deck.Execute(new SetTemplateRootCommand(part, root.CloneNode(true), before, "Move placeholder"));
        this.Refresh();
    }

    static void WriteBounds(OpenXmlElement element, double x, double y, double width, double height)
    {
        if (element is not Shape shape)
            return;

        var properties = shape.ShapeProperties ??= new ShapeProperties();
        var transform = properties.Transform2D;
        if (transform is null)
        {
            transform = new D.Transform2D();
            properties.InsertAt(transform, 0);
        }

        transform.Offset = new D.Offset { X = OoxmlUnits.PixelsToEmu(x), Y = OoxmlUnits.PixelsToEmu(y) };
        transform.Extents = new D.Extents { Cx = OoxmlUnits.PixelsToEmu(width), Cy = OoxmlUnits.PixelsToEmu(height) };
    }

    // ---- styles and backgrounds ----

    /// <summary>The page's background: a fill, a picture, or null to inherit (a layout) or reset (the master).</summary>
    public void SetBackground(SlideBackgroundSpec? background)
    {
        if (this.Page?.Part is not { } part)
            return;

        this.Edit(part, "Format background", root =>
        {
            var data = root switch
            {
                SlideMaster master => master.CommonSlideData,
                SlideLayout layout => layout.CommonSlideData,
                _ => null
            };

            if (data is not null)
                SetSlideBackgroundCommand.Write(part, data, background);
        });
    }

    /// <summary>
    /// Restyles the text of the selected placeholder — on the master, the text style every slide's
    /// matching placeholders inherit; on a layout, the placeholder's own list style.
    /// </summary>
    public void SetTextStyle(SlideMasterTextStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        if (this.Page?.Part is not { } part || this.Selection is not { } selection)
            return;

        var type = selection.PlaceholderType;
        var selectedElement = selection.Element;

        this.Edit(part, "Master text style", root =>
        {
            if (root is SlideMaster master)
            {
                var styles = master.TextStyles ??= new TextStyles();
                OpenXmlCompositeElement list = type switch
                {
                    "title" or "ctrTitle" => styles.TitleStyle ??= new TitleStyle(),
                    "body" or "obj" or "subTitle" => styles.BodyStyle ??= new BodyStyle(),
                    _ => styles.OtherStyle ??= new OtherStyle()
                };

                ApplyToLevels(list, style);
                return;
            }

            // On a layout the element is the one in this part's tree; find it again by id after the clone.
            if (selectedElement is Shape shape && root.Descendants<Shape>().FirstOrDefault(x => x.NonVisualShapeProperties?.NonVisualDrawingProperties?.Id?.Value == shape.NonVisualShapeProperties?.NonVisualDrawingProperties?.Id?.Value) is { } target)
            {
                var body = target.TextBody ??= new TextBody(new D.BodyProperties(), new D.ListStyle(), new D.Paragraph());
                var list = body.ListStyle;
                if (list is null)
                {
                    list = new D.ListStyle();
                    body.InsertAfter(list, body.BodyProperties);
                }

                ApplyToLevels(list, style);
            }
        });
    }

    /// <summary>
    /// Writes a style into level one (and, for the face and the colour, every level present), creating
    /// level one's <c>a:defRPr</c> when there is none.
    /// </summary>
    static void ApplyToLevels(OpenXmlCompositeElement list, SlideMasterTextStyle style)
    {
        var level1 = list.GetFirstChild<D.Level1ParagraphProperties>();
        if (level1 is null)
        {
            level1 = new D.Level1ParagraphProperties();
            list.PrependChild(level1);
        }

        var levels = list.ChildElements.OfType<D.TextParagraphPropertiesType>().ToList();
        foreach (var level in levels)
        {
            var isFirst = ReferenceEquals(level, level1);
            var defaults = level.GetFirstChild<D.DefaultRunProperties>();
            if (defaults is null)
            {
                defaults = new D.DefaultRunProperties();
                var extensions = level.GetFirstChild<D.ExtensionList>();
                if (extensions is not null)
                    level.InsertBefore(defaults, extensions);
                else
                    level.AppendChild(defaults);
            }

            if (isFirst && style.FontSize is { } size)
                defaults.FontSize = (int)Math.Round(size * 100);

            if (isFirst && style.Bold is { } bold)
                defaults.Bold = bold;

            if (isFirst && style.Italic is { } italic)
                defaults.Italic = italic;

            if (style.FontFamily is { } family)
            {
                foreach (var existing in defaults.Elements<D.LatinFont>().ToList())
                    existing.Remove();

                Insert(defaults, new D.LatinFont { Typeface = family });
            }

            if (style.Color is { } color)
            {
                foreach (var existing in defaults.ChildElements.Where(x => ShapeTextEditor.OrderOf(x) == 1).ToList())
                    existing.Remove();

                Insert(defaults, new D.SolidFill(new D.RgbColorModelHex { Val = SetShapeFillCommand.Hex(color) }));
            }
        }

        static void Insert(OpenXmlElement properties, OpenXmlElement child)
        {
            var rank = ShapeTextEditor.OrderOf(child);
            OpenXmlElement? previous = null;
            foreach (var existing in properties.ChildElements)
            {
                if (ShapeTextEditor.OrderOf(existing) > rank)
                    break;

                previous = existing;
            }

            if (previous is null)
                properties.InsertAt(child, 0);
            else
                properties.InsertAfter(child, previous);
        }
    }

    /// <summary>Runs an edit on the page's root, as one undo step that restores the whole part.</summary>
    void Edit(OpenXmlPart part, string name, Action<OpenXmlElement> edit)
    {
        if (this.editor.IsReadOnly || part.RootElement is not { } root)
            return;

        var before = root.CloneNode(true);
        var after = root.CloneNode(true);
        edit(after);

        this.deck.Execute(new SetTemplateRootCommand(part, after, before, name));
        this.Refresh();
    }
}

/// <summary>Replaces a master's or layout's root element — and the reverse, on undo.</summary>
sealed record SetTemplateRootCommand(OpenXmlPart Part, OpenXmlElement Root, OpenXmlElement Previous, string Label) : SlideCommand
{
    public override string Name => this.Label;

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        switch (this.Part)
        {
            case SlideMasterPart master:
                master.SlideMaster = (SlideMaster)this.Root.CloneNode(true);
                break;

            case SlideLayoutPart layout:
                layout.SlideLayout = (SlideLayout)this.Root.CloneNode(true);
                break;

            default:
                return new NoOpSlideCommand();
        }

        context.TemplateChanged(this.Part);
        return this with { Root = this.Previous, Previous = this.Root };
    }
}
