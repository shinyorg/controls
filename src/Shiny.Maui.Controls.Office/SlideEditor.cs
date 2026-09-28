using Shiny.Controls.Office.Presentation;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// Edits a <c>.pptx</c>. The lone editor — no chrome; <see cref="SlideEditorView"/> adds that.
/// </summary>
/// <remarks>
/// <para>
/// Two gestures, and the distinction is the whole design. A tap selects a shape and starts a drag;
/// a double tap puts a caret inside that shape's text and routes typing there — the same split
/// PowerPoint uses. Text arrives through a hidden <see cref="Entry"/>, which is what gives the
/// platform's own keyboard, IME, autocorrect and dictation somewhere to send characters.
/// </para>
/// <para>
/// <b>MAUI has no portable key-down event</b>, so arrow keys, Escape, Delete and shortcuts cannot be
/// observed from cross-platform code. <see cref="HandleKey"/> is the seam: a desktop host wires its
/// own platform key hook and calls in. Tapping, dragging, typing and every toolbar command work
/// without it.
/// </para>
/// </remarks>
public partial class SlideEditor : ContentView, IDisposable
{
    readonly SKCanvasView canvas;
    readonly Entry input;
    readonly AbsoluteLayout root;
    readonly SkiaTextMeasurer measurer = new();
    readonly SlidePainter painter;

    SlideEditorController? controller;
    bool suppressInputEvents;
    bool focused;
    bool disposed;

    public SlideEditor()
    {
        this.painter = new SlidePainter(this.measurer);

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

        this.input.TextChanged += this.OnInputTextChanged;
        HiddenInputKeys.Attach(this.input, this.HandleKey);
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

    public static readonly BindableProperty DeckProperty = BindableProperty.Create(
        nameof(Deck),
        typeof(SlideDeck),
        typeof(SlideEditor),
        propertyChanged: (b, _, _) => ((SlideEditor)b).Rebuild());

    public static readonly BindableProperty ThemeProperty = BindableProperty.Create(
        nameof(Theme),
        typeof(SlideTheme),
        typeof(SlideEditor),
        null,
        propertyChanged: (b, _, _) => ((SlideEditor)b).Invalidate());

    public static readonly BindableProperty SlideIndexProperty = BindableProperty.Create(
        nameof(SlideIndex),
        typeof(int),
        typeof(SlideEditor),
        0,
        BindingMode.TwoWay,
        propertyChanged: (b, _, value) =>
        {
            if (((SlideEditor)b).controller is { } controller)
                controller.Index = (int)value;
        });

    public static readonly BindableProperty IsReadOnlyProperty = BindableProperty.Create(
        nameof(IsReadOnly),
        typeof(bool),
        typeof(SlideEditor),
        false,
        propertyChanged: (b, _, value) =>
        {
            var editor = (SlideEditor)b;
            if (editor.controller is { } controller)
                controller.IsReadOnly = (bool)value;

            editor.Invalidate();
        });

    /// <summary>The deck to edit. Must have been opened with <c>editable: true</c>.</summary>
    public SlideDeck? Deck
    {
        get => (SlideDeck?)this.GetValue(DeckProperty);
        set => this.SetValue(DeckProperty, value);
    }

    /// <summary>
    /// Chrome colours. Left unset the control follows the app's light/dark appearance; setting it
    /// pins the choice, including to <see cref="SlideTheme.Light"/>.
    /// </summary>
    public SlideTheme? Theme
    {
        get => (SlideTheme?)this.GetValue(ThemeProperty);
        set => this.SetValue(ThemeProperty, value);
    }

    SlideTheme EffectiveTheme => this.Theme ?? OfficeScheme.DefaultSlide;

    public int SlideIndex
    {
        get => (int)this.GetValue(SlideIndexProperty);
        set => this.SetValue(SlideIndexProperty, value);
    }

    public bool IsReadOnly
    {
        get => (bool)this.GetValue(IsReadOnlyProperty);
        set => this.SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>The live controller — selection, caret, formatting commands, undo.</summary>
    public SlideEditorController? Controller => this.controller;

    /// <summary>Raised after every edit.</summary>
    public event EventHandler? DeckChanged;

    /// <summary>Raised when the shown slide changes.</summary>
    public event EventHandler<int>? SlideChanged;

    /// <summary>Gives the editor keyboard focus, so the platform starts sending it text.</summary>
    public void FocusEditor() => this.input.FocusForEditing();

    void Rebuild()
    {
        if (this.controller is not null)
        {
            this.controller.Changed -= this.OnControllerChanged;
            this.controller.Edited -= this.OnEdited;
        }

        if (this.Deck is null)
        {
            this.controller = null;
            this.Invalidate();
            return;
        }

        this.controller = new SlideEditorController(this.Deck, this.measurer)
        {
            IsReadOnly = this.IsReadOnly,
            Index = this.SlideIndex
        };

        this.controller.Changed += this.OnControllerChanged;

        // Every edit reports through the controller, including a drag - which is driven straight
        // through the commands and never passes through the hidden entry.
        this.controller.Edited += this.OnEdited;

        if (this.Width > 0 && this.Height > 0)
            this.controller.Resize(this.Width, this.Height);

        this.Invalidate();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width > 0 && height > 0)
            this.controller?.Resize(width, height);
    }

    void OnControllerChanged(object? sender, EventArgs e)
    {
        if (this.controller is { } controller && this.SlideIndex != controller.Index)
        {
            this.SlideIndex = controller.Index;
            this.SlideChanged?.Invoke(this, controller.Index);
        }

        this.PositionInput();
        this.Invalidate();
    }

    void OnEdited(object? sender, EventArgs e) => this.RaiseDeckChanged();

    void Invalidate() => this.canvas.InvalidateSurface();

    public void Next() => this.controller?.Next();

    public void Previous() => this.controller?.Previous();

    // ---- painting ----

    SlideEditorSurfacePainter? surfacePainter;
    SlideShowPainter? previewPainter;
    SlideShowController? preview;
    IDispatcherTimer? previewTicker;

    /// <summary>The accent the selection chrome is drawn in. Unset is the editor's blue.</summary>
    public ArgbColor? ChromeAccent { get; set; }

    void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var surface = e.Surface.Canvas;

        if (this.controller is null)
        {
            var theme = this.EffectiveTheme;
            surface.Clear(new SKColor(theme.Surround.R, theme.Surround.G, theme.Surround.B));
            return;
        }

        var scale = this.Width > 0 ? (float)(e.Info.Width / this.Width) : 1f;

        // Every view and all the chrome come from the shared surface painter, so Blazor draws the same.
        this.surfacePainter ??= new SlideEditorSurfacePainter(this.painter);
        this.surfacePainter.Paint(surface, this.controller, new SlideEditorSurfaceOptions
        {
            Theme = this.EffectiveTheme,
            Scale = scale,
            IsFocused = this.focused,
            IsReadOnly = this.IsReadOnly,
            Watermark = this.Watermark,
            Accent = this.ChromeAccent ?? new ArgbColor(255, 0x2F, 0x6F, 0xED)
        });

        // A Preview plays over the slide itself, inside its own rectangle.
        if (this.preview is { } running && running.Frame() is { } frame && this.controller.SinglePlacement() is { } placement)
        {
            this.previewPainter ??= new SlideShowPainter(this.painter);
            surface.Save();
            surface.Scale(scale);
            this.previewPainter.PaintInto(surface, frame, this.controller.Deck,
                new SKRect((float)placement.X, (float)placement.Y, (float)(placement.X + placement.Width), (float)(placement.Y + placement.Height)),
                this.Watermark);
            surface.Restore();
        }
    }

    /// <summary>
    /// Plays the current slide's transition and animations in place — the Preview button on the
    /// Transitions and Animations tabs.
    /// </summary>
    public void Preview()
    {
        if (this.controller is not { Count: > 0 } controller || this.Dispatcher is null)
            return;

        this.StopPreview();
        controller.ClearSelection();
        this.preview = SlideShowController.Preview(controller.Deck, controller.Index);

        var timer = this.Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(16);
        timer.Tick += (_, _) =>
        {
            if (this.preview is not { } running)
                return;

            running.Tick();
            this.Invalidate();

            if (running.IsPreviewDone)
                this.StopPreview();
        };

        this.previewTicker = timer;
        timer.Start();
    }

    /// <summary>Ends a preview early; a tap on the slide does the same.</summary>
    public void StopPreview()
    {
        this.previewTicker?.Stop();
        this.previewTicker = null;

        if (this.preview is not null)
        {
            this.preview = null;
            this.Invalidate();
        }
    }

    public bool IsPreviewing => this.preview is not null;

    /// <summary>Repaints — after a view setting (ruler, gridlines, markers) that is not an edit.</summary>
    public void Repaint() => this.Invalidate();

    /// <summary>
    /// Held modifiers, for a host with a platform key hook: Shift constrains a drag (15° rotation,
    /// proportional corners, one axis) and adds to the selection on a tap; Control toggles a shape in
    /// the selection. MAUI's touch events carry no modifier state of their own.
    /// </summary>
    public bool ShiftHeld { get; set; }

    public bool ControlHeld { get; set; }

    // ---- pointer ----

    readonly Dictionary<long, SKPoint> touches = [];
    double pinchStartDistance;
    double pinchStartZoom;
    bool pinching;

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

        switch (e.ActionType)
        {
            case SKTouchAction.Pressed:
                this.StopPreview();
                this.touches[e.Id] = new SKPoint(x, y);

                // A second finger turns the gesture into a pinch: the first finger's drag is dropped.
                if (this.touches.Count == 2)
                {
                    this.controller.PointerUp();
                    var pair = this.touches.Values.ToArray();
                    this.pinchStartDistance = Math.Max(1, SKPoint.Distance(pair[0], pair[1]));
                    this.pinchStartZoom = this.controller.EffectiveZoom;
                    this.pinching = true;
                    break;
                }

                if (this.IsDoubleTap(x, y))
                {
                    this.controller.PointerDoubleClick(x, y);
                    this.FocusEditor();
                    break;
                }

                this.controller.PointerDown(x, y, this.ShiftHeld, this.ControlHeld);
                this.FocusEditor();
                break;

            case SKTouchAction.Moved when e.InContact:
                this.touches[e.Id] = new SKPoint(x, y);

                if (this.pinching && this.touches.Count >= 2)
                {
                    var pair = this.touches.Values.Take(2).ToArray();
                    var distance = SKPoint.Distance(pair[0], pair[1]);
                    var centre = new SKPoint((pair[0].X + pair[1].X) / 2, (pair[0].Y + pair[1].Y) / 2);
                    this.controller.ZoomAt(this.pinchStartZoom * distance / this.pinchStartDistance, centre.X, centre.Y);
                    break;
                }

                if (!this.pinching)
                    this.controller.PointerMove(x, y, this.ShiftHeld);

                break;

            case SKTouchAction.Released:
            case SKTouchAction.Cancelled:
                this.touches.Remove(e.Id);

                if (this.pinching)
                {
                    if (this.touches.Count == 0)
                        this.pinching = false;

                    break;
                }

                this.controller.PointerUp();
                break;

            case SKTouchAction.WheelChanged:
                if (this.controller.Mode == SlideViewMode.Grid)
                {
                    this.controller.Scroll(-e.WheelDelta);
                }
                else if (this.ControlHeld)
                {
                    this.controller.ZoomAt(this.controller.EffectiveZoom * Math.Exp(e.WheelDelta * 0.0025), x, y);
                }
                else if (this.controller.CanPan)
                {
                    this.controller.PanBy(0, -e.WheelDelta);
                }
                else if (!this.controller.IsEditingText)
                {
                    if (e.WheelDelta < 0)
                        this.controller.Next();
                    else
                        this.controller.Previous();
                }

                break;
        }

        e.Handled = true;
    }

    DateTime lastTapTime;
    double lastTapX;
    double lastTapY;

    /// <summary>
    /// Whether this press continues a double tap.
    /// </summary>
    /// <remarks>
    /// Timed here rather than through a <c>TapGestureRecognizer</c> with <c>NumberOfTapsRequired=2</c>:
    /// that recogniser only reports the second tap, so the first would never select the shape, and it
    /// competes with the touch events the canvas needs for dragging.
    /// </remarks>
    bool IsDoubleTap(double x, double y)
    {
        var now = DateTime.UtcNow;
        var isDouble = (now - this.lastTapTime).TotalMilliseconds < 400 &&
            Math.Abs(x - this.lastTapX) < 12 &&
            Math.Abs(y - this.lastTapY) < 12;

        this.lastTapTime = now;
        this.lastTapX = x;
        this.lastTapY = y;

        // A double tap must not seed a triple: reset, so three quick taps are a double then a single.
        if (isDouble)
            this.lastTapTime = DateTime.MinValue;

        return isDouble;
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
#if !MACOS
        if (this.clearPending && string.IsNullOrEmpty(e.NewTextValue))
        {
            // Our own clear landing (see ClearInput), not a deletion.
            this.clearPending = false;
            this.consumedInput = string.Empty;
            return;
        }
#endif

        if (this.suppressInputEvents || this.controller is null || this.IsReadOnly)
            return;

        if (!this.controller.IsEditingText)
        {
            // Typing with a shape merely selected has nowhere to go. Cleared rather than buffered, so
            // the characters do not arrive all at once the moment the caret enters the text.
            this.ClearInput();
            return;
        }

        var text = e.NewTextValue ?? string.Empty;
#if MACOS
        this.sawInputSinceFocus = true;
#endif

        if (text.Length < this.consumedInput.Length && this.consumedInput.StartsWith(text, StringComparison.Ordinal))
        {
            // The entry shrank: Backspace once per character lost. A cleared entry reports deletion as
            // a single character going to empty, which is this same path.
            for (var i = this.consumedInput.Length; i > text.Length; i--)
                this.controller.Backspace();

            this.consumedInput = text;
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
    }

    /// <summary>What the hidden entry held the last time characters were taken from it.</summary>
    string consumedInput = string.Empty;

#if !MACOS
    /// <summary>Set while a clear of the hidden entry is still to arrive through TextChanged.</summary>
    bool clearPending;
#endif

#if MACOS
    /// <summary>
    /// Whether anything has been typed since the hidden entry was focused. Only the macOS AppKit head
    /// needs it, to tell a real Return from the completion that head raises on a focus change.
    /// </summary>
    bool sawInputSinceFocus;
#endif

    void OnInputCompleted(object? sender, EventArgs e)
    {
        if (this.controller is null || this.IsReadOnly || !this.controller.IsEditingText)
            return;

#if MACOS
        // On the macOS AppKit head this event is not only Return: it also arrives when the hidden
        // entry gains or loses first responder, which happens on every click that moves the caret.
        // Acting on those inserted a paragraph break into the shape each time the user clicked.
        // A completion that follows no typing at all is one of those, so it is ignored - the cost is
        // that Return as the very first keystroke after a click does nothing on that head.
        if (!this.sawInputSinceFocus)
            return;
#endif

        this.controller.InsertParagraph();
        this.ClearInput();
    }

    void ClearInput()
    {
#if !MACOS
        // Usually called from inside TextChanged, where MAUI queues the write and applies it after the
        // handler returns - past the suppress flag. The flag below recognises that late empty text as
        // ours; without it the "shrink" branch read it as Backspace and deleted every character the
        // moment it was typed (nothing could be typed on Android).
        this.clearPending = !string.IsNullOrEmpty(this.input.Text);
#endif
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
        this.Invalidate();
    }

    void OnInputUnfocused(object? sender, FocusEventArgs e)
    {
        this.focused = false;
        this.consumedInput = string.Empty;
#if MACOS
        this.sawInputSinceFocus = false;
#endif
        this.Invalidate();
    }

    /// <summary>Keeps the hidden entry under the caret so the IME window lands in the right place.</summary>
    void PositionInput()
    {
        if (this.controller?.CaretRect() is not { } caret)
        {
            AbsoluteLayout.SetLayoutBounds(this.input, new Rect(-1000, 0, 1, 18));
            return;
        }

        AbsoluteLayout.SetLayoutBounds(this.input, new Rect(caret.X, caret.Y, 1, Math.Max(12, caret.Height)));
    }

    /// <summary>
    /// Routes a physical key press to the editor.
    /// </summary>
    /// <remarks>
    /// MAUI exposes no portable key-down event, so a host that wants arrow keys and shortcuts wires
    /// its own platform hook and calls this. Returns false when the key was not consumed, so the host
    /// can let it fall through.
    /// </remarks>
    public bool HandleKey(EditorKey key, bool shift = false, bool control = false)
    {
        if (this.controller is null || this.IsReadOnly)
            return false;

        switch (key)
        {
            case EditorKey.Undo when control:
                this.controller.Undo();
                break;

            case EditorKey.Redo when control:
                this.controller.Redo();
                break;

            case EditorKey.Delete when !this.controller.IsEditingText:
                this.controller.DeleteSelectedShape();
                break;

            case EditorKey.Enter when !this.controller.IsEditingText && this.controller.SelectedShape >= 0:
                // Enter steps into the selected shape's text — the keyboard equivalent of a double
                // tap, and the only way in for a user who cannot double-tap.
                this.controller.BeginTextEditing(0, 0);
                break;

            // With a shape selected the arrows nudge it (Ctrl for a fine nudge); with nothing
            // selected, left and right page through the deck.
            case EditorKey.Left or EditorKey.Right or EditorKey.Up or EditorKey.Down
                when !this.controller.IsEditingText && this.controller.SelectedShape >= 0:
                this.controller.Nudge(
                    key == EditorKey.Left ? -1 : key == EditorKey.Right ? 1 : 0,
                    key == EditorKey.Up ? -1 : key == EditorKey.Down ? 1 : 0,
                    fine: control);
                break;

            case EditorKey.Left when !this.controller.IsEditingText:
                this.controller.Previous();
                break;

            case EditorKey.Right when !this.controller.IsEditingText:
                this.controller.Next();
                break;

            case EditorKey.Up or EditorKey.Down when !this.controller.IsEditingText:
                return false;

            case EditorKey.Copy when !this.controller.IsEditingText:
                this.controller.CopyShape();
                break;

            case EditorKey.Cut when !this.controller.IsEditingText:
                this.controller.CutShape();
                break;

            case EditorKey.Paste when !this.controller.IsEditingText:
                this.controller.Paste();
                break;

            case EditorKey.Duplicate when !this.controller.IsEditingText:
                this.controller.DuplicateShape();
                break;

            case EditorKey.NewSlide:
                this.controller.NewSlide();
                break;

            case EditorKey.BringForward when !this.controller.IsEditingText:
                this.controller.Arrange(shift ? ShapeZOrder.BringToFront : ShapeZOrder.BringForward);
                break;

            case EditorKey.SendBackward when !this.controller.IsEditingText:
                this.controller.Arrange(shift ? ShapeZOrder.SendToBack : ShapeZOrder.SendBackward);
                break;

            case EditorKey.Left: this.controller.MoveLeft(shift); break;
            case EditorKey.Right: this.controller.MoveRight(shift); break;
            case EditorKey.Up: this.controller.MoveUp(shift); break;
            case EditorKey.Down: this.controller.MoveDown(shift); break;
            case EditorKey.Home: this.controller.MoveToLineStart(shift); break;
            case EditorKey.End: this.controller.MoveToLineEnd(shift); break;
            case EditorKey.Backspace: this.controller.Backspace(); break;
            case EditorKey.Enter when shift: this.controller.InsertLineBreak(); break;
            case EditorKey.Enter: this.controller.InsertParagraph(); break;
            case EditorKey.Tab: this.controller.HandleTab(shift); break;
            case EditorKey.SelectAll when !this.controller.IsEditingText: this.controller.SelectAllShapes(); break;
            case EditorKey.SelectAll: this.controller.SelectAll(); break;
            case EditorKey.Bold: this.controller.ToggleBold(); break;
            case EditorKey.Italic: this.controller.ToggleItalic(); break;
            case EditorKey.Underline: this.controller.ToggleUnderline(); break;

            default:
                return false;
        }

        // Not RaiseDeckChanged: a real edit already reports through the controller's Edited event,
        // and raising it here as well made one keystroke look like two. Caret movement, which changes
        // nothing, still needs the repaint.
        this.Invalidate();
        return true;
    }

    void RaiseDeckChanged()
    {
        this.Invalidate();
        this.DeckChanged?.Invoke(this, EventArgs.Empty);
    }


    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (this.disposed || !disposing)
            return;

        this.disposed = true;

        if (this.controller is not null)
        {
            this.controller.Changed -= this.OnControllerChanged;
            this.controller.Edited -= this.OnEdited;
        }

        this.canvas.PaintSurface -= this.OnPaintSurface;
        this.canvas.Touch -= this.OnTouch;

        this.input.TextChanged -= this.OnInputTextChanged;
        this.input.Completed -= this.OnInputCompleted;
        this.input.Focused -= this.OnInputFocused;
        this.input.Unfocused -= this.OnInputUnfocused;

        this.StopPreview();
        this.surfacePainter?.Dispose();
        this.previewPainter?.Dispose();
        this.painter.Dispose();
        this.measurer.Dispose();
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
        typeof(SlideEditor),
        null,
        propertyChanged: (b, _, _) => ((SlideEditor)b).Invalidate());

    /// <inheritdoc cref="WatermarkProperty"/>
    public OfficeWatermark? Watermark
    {
        get => (OfficeWatermark?)this.GetValue(WatermarkProperty);
        set => this.SetValue(WatermarkProperty, value);
    }

}
