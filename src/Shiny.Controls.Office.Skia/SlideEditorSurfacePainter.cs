using Shiny.Controls.Office.Presentation;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;
using Shiny.Controls.Office.Theming;
using SkiaSharp;

namespace Shiny.Controls.Office.Skia;

/// <summary>What a host tells the editor surface painter about itself.</summary>
public sealed record SlideEditorSurfaceOptions
{
    public SlideTheme Theme { get; init; } = SlideTheme.Light;

    /// <summary>Device pixels per logical pixel.</summary>
    public float Scale { get; init; } = 1;

    /// <summary>The editor has keyboard focus, so the caret is drawn.</summary>
    public bool IsFocused { get; init; }

    public bool IsReadOnly { get; init; }

    public OfficeWatermark? Watermark { get; init; }

    /// <summary>The accent the selection chrome is drawn in.</summary>
    public ArgbColor Accent { get; init; } = new(255, 0x2F, 0x6F, 0xED);
}

/// <summary>
/// Paints the slide editor's whole surface for whichever view it is in — the slide with its chrome, the
/// slide sorter, the notes page, or a Slide Master page.
/// </summary>
/// <remarks>
/// Both hosts' editors call this one method from their paint handler, so the chrome — handles, the
/// rotation grip, smart guides, the marquee, rulers, gridlines, animation markers — is built in one
/// place and cannot drift between them.
/// </remarks>
public sealed class SlideEditorSurfacePainter(SlidePainter slides) : IDisposable
{
    readonly SKPaint fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    readonly SKPaint stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
    readonly SKFont numberFont = new(SKTypeface.Default, 11);

    public void Paint(SKCanvas canvas, SlideEditorController controller, SlideEditorSurfaceOptions options)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(options);

        var theme = options.Theme;
        canvas.Clear(new SKColor(theme.Surround.R, theme.Surround.G, theme.Surround.B));

        switch (controller.ViewMode)
        {
            case SlideEditorViewMode.SlideSorter:
                this.PaintSorter(canvas, controller, options);
                return;

            case SlideEditorViewMode.NotesPage:
                this.PaintNotesPage(canvas, controller, options);
                return;

            case SlideEditorViewMode.SlideMaster:
                this.PaintMaster(canvas, controller, options);
                return;
        }

        if (controller.SinglePlacement() is not { } placement)
            return;

        slides.Paint(canvas, new SlidePaintRequest
        {
            Watermark = options.Watermark,
            Slide = placement.Slide,
            SlideWidth = controller.Deck.SlideWidth,
            SlideHeight = controller.Deck.SlideHeight,
            DestinationX = placement.X,
            DestinationY = placement.Y,
            DestinationWidth = placement.Width,
            DestinationHeight = placement.Height,
            Theme = theme,
            Scale = options.Scale,
            Chrome = BuildChrome(controller, options, placement),

            // An empty placeholder's "Click to add title" is editing chrome, so it belongs to the
            // editor and not the viewer - and not to the one placeholder the caret is inside.
            ShowPlaceholderPrompts = !options.IsReadOnly,
            PromptHiddenShape = controller is { IsEditingText: true } editing ? editing.SelectedShape : -1
        });
    }

    /// <summary>
    /// The selection frame, handles, rotation grip, guides, text highlight, find highlights and caret.
    /// </summary>
    /// <remarks>
    /// Find highlights are not editing chrome — a search marks what it turned up, as usefully in a deck
    /// that cannot be edited — so they are drawn whether or not the editor is read-only. The frame, the
    /// handles and the caret say "you can change this" and are held to the editable case.
    /// </remarks>
    public static SlideEditorChrome BuildChrome(SlideEditorController controller, SlideEditorSurfaceOptions options, SlidePlacement placement)
    {
        var editable = controller.SelectedShape >= 0 && !options.IsReadOnly;
        var scale = controller.Scale;

        (double, double, double, double) ToViewport(double x1, double y1, double x2, double y2)
            => (placement.X + x1 * scale, placement.Y + y1 * scale, placement.X + x2 * scale, placement.Y + y2 * scale);

        var cells = new List<(double, double, double, double)>();
        if (editable && controller.CellSelection is { } block && controller.Selection is { Table: { } table } shape &&
            controller.BoundsOf(shape) is { } tableBounds && (block.Row != block.ToRow || block.Column != block.ToColumn))
        {
            for (var r = block.Row; r <= block.ToRow; r++)
            {
                for (var c = block.Column; c <= block.ToColumn; c++)
                {
                    var (x, y, w, h) = table.CellBounds(r, c, shape.Width, shape.Height);
                    cells.Add((tableBounds.X + x * scale, tableBounds.Y + y * scale, w * scale, h * scale));
                }
            }
        }

        return new SlideEditorChrome
        {
            SelectionFrame = editable ? Rect(controller.SelectionBounds()) : null,
            SelectionRotation = editable ? controller.SelectionRotation : 0,
            Handles = editable ? controller.SelectionHandles().Select(x => Tuple(x.Rect)).ToList() : [],
            RotationHandle = editable ? Rect(controller.RotationHandle()) : null,
            OtherFrames = editable ? controller.SecondarySelectionFrames().Select(x => (Tuple(x.Bounds), x.Rotation)).ToList() : [],
            Marquee = Rect(controller.Marquee),
            Guides = controller.ActiveGuides
                .Select(g => g.IsVertical ? ToViewport(g.Position, g.From, g.Position, g.To) : ToViewport(g.From, g.Position, g.To, g.Position))
                .ToList(),
            SlideBounds = (placement.X, placement.Y, placement.Width, placement.Height),
            GridSpacing = controller.ShowGridlines ? controller.GridSpacing * scale : 0,
            ShowStaticGuides = controller.ShowGuides,
            RulerPixelsPerInch = controller.ShowRuler ? 96 * scale : 0,
            AnimationMarkers = controller.AnimationMarkers().Select(x => (x.Label, Tuple(x.Rect))).ToList(),
            CellSelection = cells,

            // Drawn even when the deck is read-only: stepping to a match selects the word, and the
            // wash over it is the only thing saying which of the highlighted hits is the current one.
            TextSelection = controller.TextSelectionRects().Select(Tuple).ToList(),
            FindMatches = controller.FindMatchRects().Select(Tuple).ToList(),

            // The caret is only drawn while the editor actually has focus; one shown without it reads
            // as an editor accepting keystrokes when it is not.
            Caret = editable && options.IsFocused ? Rect(controller.CaretRect()) : null,
            IsEditingText = controller.IsEditingText,
            Accent = options.Accent,
            SelectionFill = options.Accent with { A = 90 }
        };
    }

    static (double X, double Y, double Width, double Height)? Rect(SlideRect? r)
        => r is { } value ? Tuple(value) : null;

    static (double X, double Y, double Width, double Height) Tuple(SlideRect r)
        => (r.X, r.Y, r.Width, r.Height);

    // ---- slide sorter ----

    void PaintSorter(SKCanvas canvas, SlideEditorController controller, SlideEditorSurfaceOptions options)
    {
        canvas.Save();
        canvas.Scale(options.Scale);

        var accent = new SKColor(options.Accent.R, options.Accent.G, options.Accent.B);
        var ink = options.Theme.Surround.R + options.Theme.Surround.G + options.Theme.Surround.B > 380
            ? new SKColor(0x40, 0x40, 0x40)
            : new SKColor(0xD8, 0xD8, 0xD8);

        var slidesList = controller.Deck.Slides;
        foreach (var placement in controller.VisibleThumbnails())
        {
            var index = IndexOf(slidesList, placement.Slide);
            var dragged = index == controller.SorterDraggedIndex;

            if (dragged)
            {
                using var dim = new SKPaint { Color = SKColors.White.WithAlpha(110) };
                canvas.SaveLayer(dim);
            }

            slides.Paint(canvas, new SlidePaintRequest
            {
                Slide = placement.Slide,
                Watermark = options.Watermark,
                SlideWidth = controller.Deck.SlideWidth,
                SlideHeight = controller.Deck.SlideHeight,
                DestinationX = placement.X,
                DestinationY = placement.Y,
                DestinationWidth = placement.Width,
                DestinationHeight = placement.Height,
                Theme = options.Theme,
                DrawBorder = true
            });

            if (dragged)
                canvas.Restore();

            var frame = new SKRect((float)placement.X, (float)placement.Y, (float)(placement.X + placement.Width), (float)(placement.Y + placement.Height));
            if (index == controller.Index)
            {
                this.stroke.Color = accent;
                this.stroke.StrokeWidth = 3;
                frame.Inflate(2, 2);
                canvas.DrawRect(frame, this.stroke);
                frame.Inflate(-2, -2);
            }

            // The number under the thumbnail, struck through for a hidden slide.
            var label = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            this.fill.Color = ink;
            var y = frame.Bottom + 12;
            canvas.DrawText(label, frame.Left, y, SKTextAlign.Left, this.numberFont, this.fill);

            if (placement.Slide.IsHidden)
            {
                var width = this.numberFont.MeasureText(label);
                this.stroke.Color = ink;
                this.stroke.StrokeWidth = 1;
                canvas.DrawLine(frame.Left - 1, y - 4, frame.Left + width + 1, y - 4, this.stroke);

                using var veil = new SKPaint { Color = SKColors.White.WithAlpha(120) };
                canvas.DrawRect(frame, veil);
            }

            if (placement.Slide.Transition is { Kind: not SlideTransitionKind.None })
            {
                // PowerPoint's star-ish mark under a slide with a transition: a small play wedge.
                using var mark = new SKPath();
                mark.MoveTo(frame.Right - 10, y - 8);
                mark.LineTo(frame.Right - 2, y - 4);
                mark.LineTo(frame.Right - 10, y);
                mark.Close();
                canvas.DrawPath(mark, this.fill);
            }
        }

        if (controller.SorterDropIndicator is { } indicator)
        {
            this.fill.Color = accent;
            canvas.DrawRect(new SKRect((float)indicator.X, (float)indicator.Y, (float)indicator.Right, (float)indicator.Bottom), this.fill);
        }

        canvas.Restore();
    }

    static int IndexOf(IReadOnlyList<Slide> slides, Slide slide)
    {
        for (var i = 0; i < slides.Count; i++)
        {
            if (ReferenceEquals(slides[i], slide))
                return i;
        }

        return -1;
    }

    // ---- notes page ----

    /// <summary>
    /// The printed notes page: a portrait sheet with the slide in the top half and the notes under it.
    /// </summary>
    void PaintNotesPage(SKCanvas canvas, SlideEditorController controller, SlideEditorSurfaceOptions options)
    {
        if (controller.Current is not { } slide)
            return;

        canvas.Save();
        canvas.Scale(options.Scale);

        // US Letter portrait, as PowerPoint's default notes page.
        const double pageRatio = 7.5 / 10;
        var margin = 16d;
        var availableW = controller.ViewportWidth - margin * 2;
        var availableH = controller.ViewportHeight - margin * 2;
        var height = Math.Min(availableH, availableW / pageRatio);
        var width = height * pageRatio;
        var page = new SKRect(
            (float)((controller.ViewportWidth - width) / 2),
            (float)((controller.ViewportHeight - height) / 2),
            (float)((controller.ViewportWidth + width) / 2),
            (float)((controller.ViewportHeight + height) / 2));

        this.fill.Color = SKColors.White;
        canvas.DrawRect(page, this.fill);
        this.stroke.Color = new SKColor(0xBB, 0xBB, 0xBB);
        this.stroke.StrokeWidth = 1;
        canvas.DrawRect(page, this.stroke);

        var slideWidth = page.Width * 0.75f;
        var slideHeight = slideWidth / (float)controller.Deck.AspectRatio;
        var slideRect = new SKRect(page.MidX - slideWidth / 2, page.Top + page.Height * 0.08f, page.MidX + slideWidth / 2, page.Top + page.Height * 0.08f + slideHeight);

        slides.Paint(canvas, new SlidePaintRequest
        {
            Slide = slide,
            Watermark = options.Watermark,
            SlideWidth = controller.Deck.SlideWidth,
            SlideHeight = controller.Deck.SlideHeight,
            DestinationX = slideRect.Left,
            DestinationY = slideRect.Top,
            DestinationWidth = slideRect.Width,
            DestinationHeight = slideRect.Height,
            Theme = SlideTheme.Light,
            DrawBorder = true
        });

        var notes = string.IsNullOrWhiteSpace(slide.Notes) ? "Click to add text" : slide.Notes;
        var style = TextStyle.Default with
        {
            FontSize = Math.Max(8, page.Height * 0.022),
            Color = string.IsNullOrWhiteSpace(slide.Notes) ? new ArgbColor(255, 0x90, 0x90, 0x90) : new ArgbColor(255, 0x20, 0x20, 0x20)
        };

        var body = new ShapeTextBody(notes.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(line => new ShapeParagraph([new StyledRun(line, style)])).ToList());
        var notesRect = new SKRect(slideRect.Left, slideRect.Bottom + page.Height * 0.06f, slideRect.Right, page.Bottom - page.Height * 0.06f);
        ShapeTextPainter.Draw(canvas, this.fill, this.stroke, slides.Measurer, body, notesRect);

        canvas.Restore();
    }

    // ---- slide master ----

    void PaintMaster(SKCanvas canvas, SlideEditorController controller, SlideEditorSurfaceOptions options)
    {
        var master = controller.Master;
        if (master.CurrentPage is not { } page || controller.SinglePlacement() is not { } placement)
            return;

        var selection = master.SelectionBounds();

        slides.Paint(canvas, new SlidePaintRequest
        {
            Slide = page,
            Watermark = options.Watermark,
            SlideWidth = controller.Deck.SlideWidth,
            SlideHeight = controller.Deck.SlideHeight,
            DestinationX = placement.X,
            DestinationY = placement.Y,
            DestinationWidth = placement.Width,
            DestinationHeight = placement.Height,
            Theme = options.Theme,
            Scale = options.Scale,
            Chrome = new SlideEditorChrome
            {
                SelectionFrame = Rect(selection),
                Handles = options.IsReadOnly ? [] : master.SelectionHandles().Select(x => Tuple(x.Rect)).ToList(),
                Accent = options.Accent,
                SelectionFill = options.Accent with { A = 90 }
            }
        });

        // Every placeholder outlined, as the master view draws them, so empty ones can be found.
        canvas.Save();
        canvas.Scale(options.Scale);
        this.stroke.Color = new SKColor(0x9A, 0x9A, 0x9A);
        this.stroke.StrokeWidth = 1;
        this.stroke.PathEffect = SKPathEffect.CreateDash([4f, 3f], 0);

        foreach (var shape in page.Shapes)
        {
            if (shape.PlaceholderType is null || !shape.IsEditable || controller.BoundsOf(shape) is not { } bounds)
                continue;

            canvas.DrawRect(new SKRect((float)bounds.X, (float)bounds.Y, (float)bounds.Right, (float)bounds.Bottom), this.stroke);
        }

        this.stroke.PathEffect.Dispose();
        this.stroke.PathEffect = null;
        canvas.Restore();
    }

    public void Dispose()
    {
        this.fill.Dispose();
        this.stroke.Dispose();
        this.numberFont.Dispose();
    }
}

/// <summary>
/// Renders slides to images and documents — File ▸ Export.
/// </summary>
/// <remarks>
/// Drawn by the same painter as the editor and the show, so an exported slide is exactly what is on
/// screen. A PDF is vector: text stays text and shapes stay paths.
/// </remarks>
public static class SlideExporter
{
    /// <summary>One slide as a PNG, <paramref name="width"/> pixels wide.</summary>
    public static byte[] ToPng(SlideDeck deck, int slide, int width = 1920, OfficeWatermark? watermark = null)
    {
        ArgumentNullException.ThrowIfNull(deck);
        if (slide < 0 || slide >= deck.Slides.Count)
            throw new ArgumentOutOfRangeException(nameof(slide));

        var height = (int)Math.Round(width / deck.AspectRatio);
        using var measurer = new SkiaTextMeasurer();
        using var painter = new SlidePainter(measurer);
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));

        Draw(surface.Canvas, painter, deck, deck.Slides[slide], width, height, watermark);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Every slide as a PNG, in order. Hidden slides are included unless asked otherwise.</summary>
    public static IReadOnlyList<byte[]> ToPngs(SlideDeck deck, int width = 1920, bool includeHidden = true, OfficeWatermark? watermark = null)
    {
        ArgumentNullException.ThrowIfNull(deck);
        return Enumerable.Range(0, deck.Slides.Count)
            .Where(i => includeHidden || !deck.Slides[i].IsHidden)
            .Select(i => ToPng(deck, i, width, watermark))
            .ToList();
    }

    /// <summary>
    /// The deck as a PDF, a page per slide at the slide's own size in points. Throws
    /// <see cref="NotSupportedException"/> where the platform's Skia build has no PDF backend.
    /// </summary>
    public static void ToPdf(SlideDeck deck, Stream output, bool includeHidden = false, OfficeWatermark? watermark = null)
    {
        ArgumentNullException.ThrowIfNull(deck);
        ArgumentNullException.ThrowIfNull(output);

        using var document = SKDocument.CreatePdf(output)
            ?? throw new NotSupportedException("This platform's SkiaSharp has no PDF backend.");

        using var measurer = new SkiaTextMeasurer();
        using var painter = new SlidePainter(measurer);

        // 96 px per inch on screen, 72 pt per inch on paper.
        var pageWidth = (float)(deck.SlideWidth * 72 / 96);
        var pageHeight = (float)(deck.SlideHeight * 72 / 96);

        for (var i = 0; i < deck.Slides.Count; i++)
        {
            if (!includeHidden && deck.Slides[i].IsHidden)
                continue;

            var canvas = document.BeginPage(pageWidth, pageHeight);
            canvas.Scale(72f / 96f);
            Draw(canvas, painter, deck, deck.Slides[i], deck.SlideWidth, deck.SlideHeight, watermark);
            document.EndPage();
        }

        document.Close();
    }

    public static byte[] ToPdf(SlideDeck deck, bool includeHidden = false, OfficeWatermark? watermark = null)
    {
        using var output = new MemoryStream();
        ToPdf(deck, output, includeHidden, watermark);
        return output.ToArray();
    }

    static void Draw(SKCanvas canvas, SlidePainter painter, SlideDeck deck, Slide slide, double width, double height, OfficeWatermark? watermark)
        => painter.Paint(canvas, new SlidePaintRequest
        {
            Slide = slide,
            Watermark = watermark,
            SlideWidth = deck.SlideWidth,
            SlideHeight = deck.SlideHeight,
            DestinationX = 0,
            DestinationY = 0,
            DestinationWidth = width,
            DestinationHeight = height,
            Theme = SlideTheme.Light,
            DrawBorder = false,
            ShowMediaOverlay = false
        });
}
