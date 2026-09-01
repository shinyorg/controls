using Shiny.Maui.Controls.Themes;
using Shiny.Controls.Office.Document;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// Edits a <c>.docx</c>. The lone editor — no chrome; <see cref="DocumentEditorView"/> adds that.
/// </summary>
/// <remarks>
/// <para>
/// Text arrives through a hidden <see cref="Entry"/>, which is what gives the platform's own keyboard,
/// IME, autocorrect and dictation somewhere to send characters. Pointer input, caret, selection and
/// formatting are handled by the shared <see cref="DocumentEditorController"/>.
/// </para>
/// <para>
/// <b>MAUI has no portable key-down event</b>, so arrow keys, Backspace and shortcuts cannot be
/// observed from cross-platform code. <see cref="HandleKey"/> is the seam: a desktop host wires its
/// own platform key hook and calls in. Typing, tapping, selection and every toolbar command work
/// without it; caret navigation from the keyboard does not.
/// </para>
/// </remarks>
public partial class DocumentEditor : ContentView, IDisposable
{
    readonly SKCanvasView canvas;
    readonly Entry input;
    readonly AbsoluteLayout root;
    readonly SkiaTextMeasurer measurer = new();
    readonly DocumentPainter painter;

    DocumentEditorController? controller;
    bool suppressInputEvents;
    bool focused;
    double lastPanY;
    double panOriginX;
    double panOriginY;
    double panScroll;
    double panScrollX;
    bool panMoved;
    bool panning;
    TextHandle? draggingHandle;
    DocumentPosition? tapCandidate;
    bool disposed;

    public DocumentEditor()
    {
        this.painter = new DocumentPainter(this.measurer);

        this.canvas = new SKCanvasView { EnableTouchEvents = true };
        this.canvas.PaintSurface += this.OnPaintSurface;
        this.canvas.Touch += this.OnTouch;

        // A one-character-wide entry parked at the caret rather than an offscreen one: the soft
        // keyboard and the IME candidate window both position themselves relative to this control.
        this.input = new Entry
        {
            Opacity = 0.01,
            WidthRequest = 1,
            HeightRequest = 18,
            Margin = 0,
            IsSpellCheckEnabled = false,
            IsTextPredictionEnabled = false
        };

        // Pinch is a MAUI gesture rather than something read out of SKTouch: the canvas reports each
        // finger separately with no notion of the pair, so recognising a pinch there would mean
        // tracking two ids and their distance by hand - which is exactly what this recognizer is.
        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += this.OnPinch;
        this.canvas.GestureRecognizers.Add(pinch);

        this.input.TextChanged += this.OnInputTextChanged;
        this.input.Completed += this.OnInputCompleted;
        this.input.Focused += this.OnInputFocused;
        this.input.Unfocused += this.OnInputUnfocused;

        this.root = new AbsoluteLayout();
        this.root.Add(this.canvas);
        AbsoluteLayout.SetLayoutFlags(this.canvas, AbsoluteLayoutFlags.All);
        AbsoluteLayout.SetLayoutBounds(this.canvas, new Rect(0, 0, 1, 1));
        this.root.Add(this.input);

        this.Content = this.root;

        // An unset Theme tracks the app's appearance, so a flip has to redraw.
        this.FollowAppTheme(static v => v.Invalidate());
    }

    public static readonly BindableProperty DocumentProperty = BindableProperty.Create(
        nameof(Document),
        typeof(WordDocument),
        typeof(DocumentEditor),
        propertyChanged: (b, _, _) => ((DocumentEditor)b).Rebuild());

    public static readonly BindableProperty ThemeProperty = BindableProperty.Create(
        nameof(Theme),
        typeof(DocumentTheme),
        typeof(DocumentEditor),
        null,
        propertyChanged: (b, _, _) => ((DocumentEditor)b).Invalidate());

    public static readonly BindableProperty ZoomProperty = BindableProperty.Create(
        nameof(Zoom),
        typeof(double),
        typeof(DocumentEditor),
        1.0,
        propertyChanged: (b, _, value) =>
        {
            if (((DocumentEditor)b).controller is { } controller)
                controller.Zoom = (double)value;
        });

    /// <summary>
    /// Continuous column, or sheets of paper with the document's own headers and footers.
    /// </summary>
    /// <remarks>
    /// Print by default here, unlike <see cref="DocumentView"/>: an editor is where a page break,
    /// a header and a page number are authored, and none of them can be seen in a reflowed column.
    /// </remarks>
    public static readonly BindableProperty PageLayoutProperty = BindableProperty.Create(
        nameof(PageLayout),
        typeof(DocumentPageLayout),
        typeof(DocumentEditor),
        DocumentPageLayout.Print,
        propertyChanged: (b, _, value) =>
        {
            if (((DocumentEditor)b).controller is { } controller)
                controller.PageLayout = (DocumentPageLayout)value;
        });

    public static readonly BindableProperty IsReadOnlyProperty = BindableProperty.Create(
        nameof(IsReadOnly),
        typeof(bool),
        typeof(DocumentEditor),
        false,
        propertyChanged: (b, _, _) => ((DocumentEditor)b).Invalidate());

    /// <summary>
    /// Replaces the platform spell checker for this editor only.
    /// </summary>
    /// <remarks>
    /// Left unset, the editor uses <see cref="SpellCheckers.Default"/> - which on iOS, Android, macOS
    /// and Windows is the platform's own, registered automatically. Set this to supply a bundled
    /// dictionary, a server-side service, or a domain word list instead.
    /// </remarks>
    public static readonly BindableProperty SpellCheckerProperty = BindableProperty.Create(
        nameof(SpellChecker),
        typeof(ISpellChecker),
        typeof(DocumentEditor),
        propertyChanged: (b, _, value) =>
        {
            if (((DocumentEditor)b).controller is { } controller)
                controller.SpellChecker = (ISpellChecker?)value ?? SpellCheckers.Default;
        });

    public static readonly BindableProperty IsSpellCheckEnabledProperty = BindableProperty.Create(
        nameof(IsSpellCheckEnabled),
        typeof(bool),
        typeof(DocumentEditor),
        true,
        propertyChanged: (b, _, value) =>
        {
            if (((DocumentEditor)b).controller is { } controller)
                controller.IsSpellCheckEnabled = (bool)value;
        });

    public WordDocument? Document
    {
        get => (WordDocument?)this.GetValue(DocumentProperty);
        set => this.SetValue(DocumentProperty, value);
    }

    /// <summary>
    /// Chrome colours. Left unset the control follows the app's light/dark appearance; setting it
    /// pins the choice, including to <see cref="DocumentTheme.Light"/>.
    /// </summary>
    public DocumentTheme? Theme
    {
        get => (DocumentTheme?)this.GetValue(ThemeProperty);
        set => this.SetValue(ThemeProperty, value);
    }

    DocumentTheme EffectiveTheme => this.Theme ?? OfficeScheme.DefaultDocument;

    public double Zoom
    {
        get => (double)this.GetValue(ZoomProperty);
        set => this.SetValue(ZoomProperty, value);
    }

    /// <inheritdoc cref="PageLayoutProperty"/>
    public DocumentPageLayout PageLayout
    {
        get => (DocumentPageLayout)this.GetValue(PageLayoutProperty);
        set => this.SetValue(PageLayoutProperty, value);
    }

    public bool IsReadOnly
    {
        get => (bool)this.GetValue(IsReadOnlyProperty);
        set => this.SetValue(IsReadOnlyProperty, value);
    }

    public ISpellChecker? SpellChecker
    {
        get => (ISpellChecker?)this.GetValue(SpellCheckerProperty);
        set => this.SetValue(SpellCheckerProperty, value);
    }

    public bool IsSpellCheckEnabled
    {
        get => (bool)this.GetValue(IsSpellCheckEnabledProperty);
        set => this.SetValue(IsSpellCheckEnabledProperty, value);
    }

    /// <summary>The live controller — selection, caret format, formatting commands, undo.</summary>
    public DocumentEditorController? Controller => this.controller;

    /// <summary>Raised after every edit.</summary>
    public event EventHandler? DocumentChanged;

    /// <summary>Gives the editor keyboard focus, so the platform starts sending it text.</summary>
    public void FocusEditor() => this.input.FocusForEditing();

    void Rebuild()
    {
        this.Detach();

        if (this.Document is null)
        {
            this.controller = null;
            this.Invalidate();
            return;
        }

        this.controller = new DocumentEditorController(this.Document, this.measurer, this.SpellChecker)
        {
            Zoom = this.Zoom,
            PageLayout = this.PageLayout,
            IsSpellCheckEnabled = this.IsSpellCheckEnabled
        };
        this.controller.Changed += this.OnControllerChanged;

        if (this.Width > 0 && this.Height > 0)
            this.controller.Resize(this.Width, this.Height);

        // Resize only schedules a check when there is already a size; a document opened before layout
        // would otherwise sit unchecked until the first scroll.
        this.controller.ScheduleSpellCheck(0);

        this.Invalidate();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width > 0 && height > 0)
            this.controller?.Resize(width, height);
    }

    /// <summary>
    /// A picture drawn behind the content — a logo, a DRAFT stamp, a company mark.
    /// </summary>
    /// <remarks>
    /// A <b>display</b> watermark: it is drawn, not written into the file. The three Office formats
    /// have no common notion of one, so persisting would mean three unrelated mechanisms where drawing
    /// means one. See <see cref="OfficeWatermark"/>.
    /// </remarks>
    public static readonly BindableProperty WatermarkProperty = BindableProperty.Create(
        nameof(Watermark),
        typeof(OfficeWatermark),
        typeof(DocumentEditor),
        null,
        propertyChanged: (b, _, _) => ((DocumentEditor)b).Invalidate());

    /// <inheritdoc cref="WatermarkProperty"/>
    public OfficeWatermark? Watermark
    {
        get => (OfficeWatermark?)this.GetValue(WatermarkProperty);
        set => this.SetValue(WatermarkProperty, value);
    }

    void OnControllerChanged(object? sender, EventArgs e)
    {
        this.PositionInput();
        this.RefreshSpellingAccessory();
        this.Invalidate();
    }

    void Invalidate() => this.canvas.InvalidateSurface();

    void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var theme = this.EffectiveTheme;
        if (this.controller is null)
        {
            e.Surface.Canvas.Clear(new SKColor(theme.SurroundBackground.R, theme.SurroundBackground.G, theme.SurroundBackground.B));
            return;
        }

        // Device scale times the view scale: in print the controller works in the paper's units and
        // zoom is applied here, so one layout unit is dpr * zoom device pixels.
        var scale = this.Width > 0
            ? (float)(e.Info.Width / this.Width * this.controller.ViewScale)
            : (float)this.controller.ViewScale;

        this.painter.Paint(e.Surface.Canvas, new DocumentPaintRequest
        {
            Watermark = this.Watermark,
            Blocks = this.controller.Blocks,
            Viewport = this.controller.Viewport,
            Theme = theme,
            Scale = scale,
            PageX = this.controller.PageX,
            ContentX = this.controller.ContentX,
            PageWidth = this.controller.PageWidth,
            PageHeight = this.controller.PageHeight,
            Pages = this.controller.VisiblePages(),
            Setup = this.controller.Document.Page,
            Selection = this.controller.SelectionRects().ToList(),
            FindMatches = this.controller.FindMatchRects().ToList(),
            Spelling = this.controller.SpellingRects().ToList(),
            Caret = this.focused && !this.IsReadOnly && this.controller.SelectedObject is null
                ? this.controller.CaretRect(this.controller.Selection.Focus)
                : null,

            TouchHandles = this.controller.UsesTouch
                ? this.controller.TouchHandleRects()
                : [],

            ObjectChrome = this.BuildObjectChrome()
        });
    }

    /// <summary>The frame and handles around a selected inline object, or null when none is selected.</summary>
    DocumentObjectChrome? BuildObjectChrome()
    {
        if (this.IsReadOnly || this.controller is null)
            return null;

        if (this.controller.SelectedObjectBounds() is not { } bounds)
            return null;

        return new DocumentObjectChrome
        {
            Frame = bounds,
            Handles = this.controller.SelectedObjectHandles().Select(x => x.Rect).ToList()
        };
    }

    /// <summary>
    /// How long after a press a second one still counts as part of the same multi-tap.
    /// </summary>
    /// <remarks>
    /// SkiaSharp's touch events carry no click count - unlike the browser, which works one out for us -
    /// so a double tap has to be recognised from the timing and the distance here.
    /// </remarks>
    static readonly TimeSpan MultiTapInterval = TimeSpan.FromMilliseconds(450);

    /// <summary>How far a second tap may land from the first and still be the same gesture.</summary>
    const double MultiTapSlop = 10;

    DateTime lastPressAt;
    double lastPressX;
    double lastPressY;
    int pressCount;

    /// <summary>How far a finger may travel before a press stops being a tap. See SpreadsheetController.</summary>
    const double TapSlop = 6;

    /// <summary>Counts consecutive taps in the same spot: 1 places a caret, 2 a word, 3 a paragraph.</summary>
    int CountPress(double x, double y)
    {
        var now = DateTime.UtcNow;
        var near = Math.Abs(x - this.lastPressX) <= MultiTapSlop && Math.Abs(y - this.lastPressY) <= MultiTapSlop;

        this.pressCount = near && now - this.lastPressAt <= MultiTapInterval ? this.pressCount + 1 : 1;
        this.lastPressAt = now;
        this.lastPressX = x;
        this.lastPressY = y;

        return this.pressCount;
    }

    void OnTouch(object? sender, SKTouchEventArgs e)
    {
        if (this.controller is null)
        {
            e.Handled = true;
            return;
        }

        var scale = this.Width > 0 ? (float)(this.canvas.CanvasSize.Width / this.Width) : 1f;
        var x = e.Location.X / scale;
        var y = e.Location.Y / scale;
        var touch = e.DeviceType == SKTouchDeviceType.Touch;

        switch (e.ActionType)
        {
            case SKTouchAction.Pressed:
                this.lastPanY = y;
                this.CloseSpellingMenu();

                if (touch)
                    this.controller.UsesTouch = true;

                // Objects first: a picture sits on top of the text it displaces, so a press inside
                // one is always about the picture even though there is a caret position underneath.
                if (this.controller.BeginObjectDrag(x, y))
                {
                    this.FocusEditor();
                    break;
                }

                var taps = this.CountPress(x, y);

                // A finger dragging a handle is adjusting the selection it already has; a finger
                // anywhere else is panning. Neither may move the caret on the way down - see the
                // Released case, which is where a touch that turned out to be a tap is resolved.
                if (touch && taps == 1)
                {
                    this.draggingHandle = this.controller.TouchHandleAt(x, y);
                    if (this.draggingHandle is not null)
                        break;

                    this.panning = true;
                    this.panOriginX = x;
                    this.panOriginY = y;
                    this.panScroll = this.controller.Viewport.ScrollY;
                    this.panScrollX = this.controller.Viewport.ScrollX;
                    this.panMoved = false;
                    this.tapCandidate = this.controller.PositionAt(x, y);

                    if (this.tapCandidate is { } pending)
                        this.ArmLongPress(pending, x, y);

                    break;
                }

                if (this.controller.PositionAt(x, y) is { } position)
                {
                    if (taps >= 3)
                        this.controller.SelectParagraphAt(position);
                    else if (taps == 2)
                        this.controller.SelectWordAt(position);
                    else
                        this.controller.Selection.MoveTo(position);

                    this.FocusEditor();

                    // Right-click is the desktop gesture; touch gets the same menu from a long press,
                    // timed from this press because a pan gesture only starts once the finger moves.
                    // A second tap is neither: arming it there would race the word selection.
                    if (e.MouseButton == SKMouseButton.Right)
                        _ = this.ShowSpellingMenuAsync(position, x, y);
                    else if (taps == 1)
                        this.ArmLongPress(position, x, y);
                }

                break;

            case SKTouchAction.Moved when e.InContact:
                // A drag inside the text extends the selection; the caret has to already be down for
                // that to be what the user meant, which is why this only runs while in contact.
                this.CancelLongPress();

                if (this.controller.IsDraggingObject)
                {
                    this.controller.DragObject(x, y);
                    break;
                }

                if (this.draggingHandle is { } grabbed)
                {
                    this.controller.DragTouchHandle(grabbed, x, y);
                    break;
                }

                if (this.panning)
                {
                    var travelled = this.panOriginY - y;
                    var travelledX = this.panOriginX - x;

                    if (!this.panMoved && Math.Abs(travelled) <= TapSlop && Math.Abs(travelledX) <= TapSlop)
                        break;

                    this.panMoved = true;

                    // Both axes. A page is a fixed width, so on a phone it is always wider than the
                    // screen and the right-hand end of every line is off it - vertical panning alone
                    // scrolls past a document that still cannot be read.
                    //
                    // travelled is in control pixels and the viewport scrolls in view units, which
                    // differ by the zoom - the same conversion ScrollByControlPixels does.
                    var zoomScale = this.controller.ViewScale;
                    this.controller.ScrollToHorizontal(this.panScrollX + (travelledX / zoomScale));
                    this.controller.ScrollTo(this.panScroll + (travelled / zoomScale));
                    break;
                }

                // Only a single-tap drag extends. After a double tap the finger is still down over the
                // word that was just selected, and extending from there would collapse it to a caret.
                if (this.pressCount == 1 && this.controller.PositionAt(x, y) is { } dragged)
                    this.controller.Selection.ExtendTo(dragged);

                break;

            case SKTouchAction.Released:
            case SKTouchAction.Cancelled:
                this.CancelLongPress();
                this.controller.EndObjectDrag();

                // A press that went nowhere was a tap, and a tap places the caret. Deferred to here
                // rather than done on the way down because until the finger lifts there is no telling
                // a tap from the start of a pan.
                if (this.panning && !this.panMoved && this.tapCandidate is { } tapped)
                {
                    this.controller.Selection.MoveTo(tapped);
                    this.FocusEditor();
                }

                this.panning = false;
                this.panMoved = false;
                this.draggingHandle = null;
                this.tapCandidate = null;
                break;

            case SKTouchAction.WheelChanged:
                this.controller.ScrollByControlPixels(-e.WheelDelta);
                break;
        }

        e.Handled = true;
    }

    // ---- spelling menu ----

    CancellationTokenSource? longPress;
    Border? spellingMenu;

    /// <summary>
    /// Starts the long-press timer for the spelling menu.
    /// </summary>
    /// <remarks>
    /// Timed from the touch-down rather than from a gesture recogniser: a pan only begins once the
    /// finger moves, so it can never tell a long press from a slow one.
    /// </remarks>
    void ArmLongPress(DocumentPosition position, double x, double y)
    {
        this.CancelLongPress();

        if (this.IsReadOnly)
            return;

        var cts = new CancellationTokenSource();
        this.longPress = cts;

        _ = WaitAsync(cts.Token);

        async Task WaitAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(500, cancellationToken);
                await this.ShowSpellingMenuAsync(position, x, y);
            }
            catch (OperationCanceledException)
            {
                // The finger moved or lifted first, which is the normal case.
            }
        }
    }

    double pinchStartZoom = 1;

    /// <summary>
    /// Pinch to zoom, which on a phone is the gesture for this and no other.
    /// </summary>
    /// <remarks>
    /// The scale is applied against the zoom the pinch <i>started</i> at rather than compounded per
    /// update: PinchGestureRecognizer reports a cumulative scale for the gesture, so multiplying it
    /// into the current zoom on every frame squares it and the page leaps away from the fingers.
    /// </remarks>
    void OnPinch(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (this.controller is null)
            return;

        switch (e.Status)
        {
            case GestureStatus.Started:
                this.pinchStartZoom = this.controller.Zoom;

                // A pinch that began as a pan must not leave the page scrolling under the fingers.
                this.panning = false;
                this.panMoved = false;
                this.CancelLongPress();
                break;

            case GestureStatus.Running:
                this.controller.Zoom = this.pinchStartZoom * e.Scale;
                break;
        }
    }

    void CancelLongPress()
    {
        this.longPress?.Cancel();
        this.longPress?.Dispose();
        this.longPress = null;
    }

    /// <summary>
    /// Opens the spelling menu for the word the caret is in, anchored under it.
    /// </summary>
    /// <remarks>
    /// The toolbar's entry point. A long press supplies its own coordinates because the finger says
    /// where the menu goes; stepping to an error from the Review tab has no pointer, so the caret's
    /// own rectangle is the anchor.
    /// </remarks>
    public Task ShowSpellingMenuForCaretAsync()
    {
        if (this.controller is null)
            return Task.CompletedTask;

        var caret = this.controller.CaretRect(this.controller.Selection.Focus);
        var scale = this.controller.ViewScale;

        // CaretRect is in flow coordinates; the menu is placed in the control's.
        var x = (caret.X + this.controller.ContentX) * scale;
        var y = (this.controller.Pagination.FlowToView(caret.Bottom) - this.controller.Viewport.ScrollY) * scale;

        return this.ShowSpellingMenuAsync(this.controller.Selection.Focus, x, y);
    }

    async Task ShowSpellingMenuAsync(DocumentPosition position, double x, double y)
    {
        if (this.controller is null || this.IsReadOnly)
            return;

        if (this.controller.SpellingErrorAt(position) is not { } error)
            return;

        var suggestions = await this.controller.SuggestAtAsync(position);

        // Everything below touches the visual tree, and the suggestion may well have arrived on a
        // platform callback thread - Android's checker answers on a binder thread.
        await this.Dispatcher.DispatchAsync(() => this.BuildSpellingMenu(position, error.Word, suggestions, x, y));
    }

    void BuildSpellingMenu(DocumentPosition position, string word, IReadOnlyList<string> suggestions, double x, double y)
    {
        this.CloseSpellingMenu();

        var items = new VerticalStackLayout { Spacing = 0 };

        if (suggestions.Count == 0)
        {
            var empty = new Label
            {
                Text = "No suggestions",
                FontSize = 13,
                FontAttributes = FontAttributes.Italic,
                Opacity = 0.6,
                Padding = new Thickness(12, 6)
            };

            empty.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
            items.Add(empty);
        }
        else
        {
            foreach (var suggestion in suggestions.Take(6))
            {
                var value = suggestion;
                items.Add(MenuItem(value, () =>
                {
                    this.controller?.ApplySuggestion(position, value);
                    this.CloseSpellingMenu();
                }));
            }
        }

        var separator = new BoxView { HeightRequest = 1, Margin = new Thickness(0, 4) };

        // BoxView.Color, not Background: on macOS AppKit a solid Background renders transparent.
        separator.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.OutlineVariant);
        items.Add(separator);
        items.Add(MenuItem("Ignore", () => { this.controller?.IgnoreSpelling(word); this.CloseSpellingMenu(); }, secondary: true));
        items.Add(MenuItem("Add to dictionary", () => { this.controller?.LearnSpelling(word); this.CloseSpellingMenu(); }, secondary: true));

        // Stroke is a Brush, and a colour token assigned straight onto it is dropped; the token has to
        // drive the brush's own Color instead.
        var stroke = new SolidColorBrush(Colors.Gray);
        stroke.SetDynamicResource(SolidColorBrush.ColorProperty, ShinyThemeKeys.Color.OutlineVariant);

        this.spellingMenu = new Border
        {
            Content = items,
            Stroke = stroke,
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(0, 4),
            MinimumWidthRequest = 160
        };

        this.spellingMenu.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.Surface);

        this.root.Add(this.spellingMenu);
        AbsoluteLayout.SetLayoutFlags(this.spellingMenu, AbsoluteLayoutFlags.None);
        AbsoluteLayout.SetLayoutBounds(this.spellingMenu, new Rect(x, y, AbsoluteLayout.AutoSize, AbsoluteLayout.AutoSize));
    }

    static View MenuItem(string text, Action action, bool secondary = false)
    {
        var label = new Label
        {
            Text = text,
            FontSize = 13,
            Padding = new Thickness(12, 6)
        };

        // Corrections are the primary action; ignore and learn read as secondary.
        label.SetDynamicResource(
            Label.TextColorProperty,
            secondary ? ShinyThemeKeys.Color.OnSurfaceVariant : ShinyThemeKeys.Color.OnSurface);

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => action();
        label.GestureRecognizers.Add(tap);

        return label;
    }

    void CloseSpellingMenu()
    {
        if (this.spellingMenu is null)
            return;

        this.root.Remove(this.spellingMenu);
        this.spellingMenu = null;
    }

    // ---- text input ----

    /// <summary>
    /// Turns whatever the hidden entry now holds into the characters that are actually new, and feeds
    /// those to the controller. Keeping a running buffer of our own would fight the IME, which
    /// rewrites what it has already committed — so the entry's own text is the buffer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ClearInput"/> empties the entry after every insert, so on most heads the text
    /// arriving here is just the new characters and <c>consumedInput</c> is empty. The macOS AppKit
    /// head does not apply that clear to the native field before the next keystroke reaches it, so the
    /// entry keeps accumulating: typing "hello" arrives as "h", "he", "hel", "hell" — which inserted
    /// <c>hhehelhell</c>.
    /// </para>
    /// <para>
    /// Diffing against what was last consumed rather than against <c>OldTextValue</c> is what covers
    /// both — <c>OldTextValue</c> is the entry's *bindable* previous value, which the clear resets to
    /// empty even on the head where the native field kept its text.
    /// </para>
    /// </remarks>
    void OnInputTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (this.suppressInputEvents || this.controller is null || this.IsReadOnly)
            return;

        var text = e.NewTextValue ?? string.Empty;
#if MACOS
        this.sawInputSinceFocus = true;
#endif

        if (text.Length < this.consumedInput.Length && this.consumedInput.StartsWith(text, StringComparison.Ordinal))
        {
            // The entry shrank: Backspace once per character lost. A cleared entry reports deletion as
            // a single character going to empty, which is this same path.
            for (var i = this.consumedInput.Length; i > text.Length; i--)
                this.controller.DeleteBackward();

            this.consumedInput = text;
            this.RaiseDocumentChanged();
            return;
        }

        var inserted = text.StartsWith(this.consumedInput, StringComparison.Ordinal)
            ? text[this.consumedInput.Length..]
            : text;

        if (inserted.Length == 0)
            return;

        this.controller.InsertText(inserted);

        // What the native field holds now, whether or not the clear below reaches it. A head that does
        // apply the clear sends the next keystroke as a string that does not start with this, which
        // falls into the replacement branch above and re-bases on its own.
        this.consumedInput = text;
        this.ClearInput();
        this.RaiseDocumentChanged();
    }

    /// <summary>What the hidden entry held the last time characters were taken from it.</summary>
    string consumedInput = string.Empty;

#if MACOS
    /// <summary>
    /// Whether anything has been typed since the hidden entry was focused. Only the macOS AppKit head
    /// needs it, to tell a real Return from the completion that head raises on a focus change.
    /// </summary>
    bool sawInputSinceFocus;
#endif

    void OnInputCompleted(object? sender, EventArgs e)
    {
        if (this.controller is null || this.IsReadOnly)
            return;

#if MACOS
        // On the macOS AppKit head this event is not only Return: it also arrives when the hidden
        // entry gains or loses first responder, which happens on every click that moves the caret.
        // Acting on those inserted a paragraph break into the document each time the user clicked.
        // A completion that follows no typing at all is one of those, so it is ignored - the cost is
        // that Return as the very first keystroke after a click does nothing on that head.
        if (!this.sawInputSinceFocus)
            return;
#endif

        this.controller.InsertParagraph();
        this.ClearInput();
        this.RaiseDocumentChanged();
    }

    void ClearInput()
    {
        this.suppressInputEvents = true;
        this.input.Text = string.Empty;
        this.suppressInputEvents = false;
    }

    void OnInputFocused(object? sender, FocusEventArgs e)
    {
        this.focused = true;
        this.consumedInput = string.Empty;
#if MACOS
        this.sawInputSinceFocus = false;
#endif
        this.RefreshSpellingAccessory();
        this.Invalidate();
    }

    void OnInputUnfocused(object? sender, FocusEventArgs e)
    {
        this.focused = false;
        this.consumedInput = string.Empty;
#if MACOS
        this.sawInputSinceFocus = false;
#endif
        this.RefreshSpellingAccessory();
        this.Invalidate();
    }

    /// <summary>Keeps the hidden entry under the caret so the IME window lands in the right place.</summary>
    void PositionInput()
    {
        if (this.controller is null)
            return;

        var caret = this.controller.CaretRect(this.controller.Selection.Focus);
        var x = caret.X + this.controller.PageX + this.controller.PagePadding;
        var y = caret.Y - this.controller.Viewport.ScrollY;

        AbsoluteLayout.SetLayoutFlags(this.input, AbsoluteLayoutFlags.None);
        AbsoluteLayout.SetLayoutBounds(this.input, new Rect(x, y, 1, Math.Max(12, caret.Height)));
    }

    // ---- the key seam ----

    /// <summary>
    /// Routes a physical key press into the editor.
    /// </summary>
    /// <remarks>
    /// MAUI exposes no cross-platform key-down event, so this cannot be wired from here. A desktop
    /// host adds its own platform hook — <c>NSEvent</c> on macOS, <c>KeyDown</c> on Windows — and calls
    /// this. Returns true when the key was consumed.
    /// </remarks>
    public bool HandleKey(EditorKey key, bool shift = false, bool control = false)
    {
        if (this.controller is null)
            return false;

        switch (key)
        {
            case EditorKey.Left: this.controller.Move(control ? CaretMove.WordLeft : CaretMove.Left, shift); return true;
            case EditorKey.Right: this.controller.Move(control ? CaretMove.WordRight : CaretMove.Right, shift); return true;
            case EditorKey.Up: this.controller.Move(CaretMove.Up, shift); return true;
            case EditorKey.Down: this.controller.Move(CaretMove.Down, shift); return true;
            case EditorKey.Home: this.controller.Move(control ? CaretMove.DocumentStart : CaretMove.LineStart, shift); return true;
            case EditorKey.End: this.controller.Move(control ? CaretMove.DocumentEnd : CaretMove.LineEnd, shift); return true;
            case EditorKey.SelectAll: this.controller.SelectAll(); return true;
        }

        if (this.IsReadOnly)
            return false;

        switch (key)
        {
            case EditorKey.Backspace: this.controller.DeleteBackward(); break;
            case EditorKey.Delete: this.controller.DeleteForward(); break;
            case EditorKey.Enter: this.controller.InsertParagraph(); break;

            case EditorKey.Tab:
                if (!this.controller.HandleTab(shift))
                    return false;

                break;

            case EditorKey.Bold: this.controller.ToggleBold(); break;
            case EditorKey.Italic: this.controller.ToggleItalic(); break;
            case EditorKey.Underline: this.controller.ToggleUnderline(); break;
            case EditorKey.Undo: this.controller.Undo(); break;
            case EditorKey.Redo: this.controller.Redo(); break;
            default: return false;
        }

        this.RaiseDocumentChanged();
        return true;
    }

    void RaiseDocumentChanged() => this.DocumentChanged?.Invoke(this, EventArgs.Empty);

    void Detach()
    {
        if (this.controller is not null)
            this.controller.Changed -= this.OnControllerChanged;
    }

    /// <summary>
    /// Hooks the keyboard once there is a platform view to measure against, and lets go when there is
    /// not — a control moved between pages gets a new handler and must not keep observing on the old.
    /// </summary>
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (this.Handler is null)
            this.UnhookKeyboard();
        else
            this.HookKeyboard();
    }

    public void Dispose()
    {
        this.UnhookKeyboard();

        if (this.disposed)
            return;

        this.disposed = true;
        this.Detach();

        this.canvas.PaintSurface -= this.OnPaintSurface;
        this.canvas.Touch -= this.OnTouch;
        this.input.TextChanged -= this.OnInputTextChanged;
        this.input.Completed -= this.OnInputCompleted;
        this.input.Focused -= this.OnInputFocused;
        this.input.Unfocused -= this.OnInputUnfocused;

        this.painter.Dispose();
        this.measurer.Dispose();

        GC.SuppressFinalize(this);
    }
}

/// <summary>Keys the editor understands, for hosts routing physical key presses in.</summary>
public enum EditorKey
{
    Left,
    Right,
    Up,
    Down,
    Home,
    End,
    Backspace,
    Delete,
    Enter,

    /// <summary>
    /// Tab.
    /// </summary>
    /// <remarks>
    /// Routed in rather than left to the platform because inside a list it nests the item, which is
    /// the only gesture there is for creating a second level. A host that lets Tab through instead
    /// moves the focus off the editor.
    /// </remarks>
    Tab,
    SelectAll,
    Bold,
    Italic,
    Underline,
    Undo,
    Redo,

    /// <summary>
    /// Escape.
    /// </summary>
    /// <remarks>
    /// Added for the notebook canvas, where it is the way back out of a mode: it leaves text editing,
    /// then puts the pen down and returns to the select tool, then clears the selection. The document
    /// and slide editors have no modes to leave and simply do not route it.
    /// </remarks>
    Escape
}
