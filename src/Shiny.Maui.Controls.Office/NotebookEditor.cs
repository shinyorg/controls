using Shiny.Controls.Office.Notebook;
using Shiny.Controls.Office.Shapes;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.View;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// A free-form notebook page — write anywhere, draw over it, drop shapes and pictures on it.
/// The lone canvas; <see cref="NotebookEditorView"/> adds the toolbar, tabs and page list.
/// </summary>
/// <remarks>
/// <para>
/// The canvas differs from the slide editor in one structural way: a slide is a fixed artboard that
/// gets fitted to the viewport, and a page is an unbounded canvas that gets scrolled. Everything else
/// — the hidden <see cref="Entry"/> that gives the platform keyboard, IME and dictation somewhere to
/// send characters, the tap/double-tap split, the Skia surface — is the same machinery, because it is
/// the same job.
/// </para>
/// <para>
/// <b>MAUI has no portable key-down event</b>, so arrow keys, Escape, Delete and shortcuts cannot be
/// observed from cross-platform code. <see cref="HandleKey(EditorKey, bool, bool)"/> is the seam: a
/// desktop host wires its own platform hook and calls in. Writing, drawing, dragging and every toolbar
/// command work without it.
/// </para>
/// </remarks>
public class NotebookEditor : ContentView, IDisposable
{
    readonly SKCanvasView canvas;
    readonly Entry input;
    readonly AbsoluteLayout root;
    readonly SkiaTextMeasurer measurer = new();
    readonly NotebookPainter painter;

    NotebookEditorController? controller;
    bool suppressInputEvents;
    bool focused;
    bool disposed;

    public NotebookEditor()
    {
        this.painter = new NotebookPainter(this.measurer);

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
        this.input.Completed += this.OnInputCompleted;
        this.input.Focused += this.OnInputFocused;
        this.input.Unfocused += this.OnInputUnfocused;

        this.root = new AbsoluteLayout();
        this.root.Add(this.canvas);
        AbsoluteLayout.SetLayoutFlags(this.canvas, AbsoluteLayoutFlags.All);
        AbsoluteLayout.SetLayoutBounds(this.canvas, new Rect(0, 0, 1, 1));
        this.root.Add(this.input);

        this.Content = this.root;

        this.FollowAppTheme(static v => v.Invalidate());
    }

    // ---- bindable surface ----

    public static readonly BindableProperty NotebookProperty = BindableProperty.Create(
        nameof(Notebook),
        typeof(NotebookDocument),
        typeof(NotebookEditor),
        propertyChanged: (b, _, _) => ((NotebookEditor)b).Rebuild());

    public static readonly BindableProperty ThemeProperty = BindableProperty.Create(
        nameof(Theme),
        typeof(NotebookTheme),
        typeof(NotebookEditor),
        null,
        propertyChanged: (b, _, _) => ((NotebookEditor)b).Invalidate());

    public static readonly BindableProperty ToolProperty = BindableProperty.Create(
        nameof(Tool),
        typeof(NoteTool),
        typeof(NotebookEditor),
        NoteTool.Select,
        BindingMode.TwoWay,
        propertyChanged: (b, _, value) =>
        {
            if (((NotebookEditor)b).controller is { } controller)
                controller.Tool = (NoteTool)value;
        });

    public static readonly BindableProperty IsReadOnlyProperty = BindableProperty.Create(
        nameof(IsReadOnly),
        typeof(bool),
        typeof(NotebookEditor),
        false,
        propertyChanged: (b, _, value) =>
        {
            var editor = (NotebookEditor)b;
            if (editor.controller is { } controller)
                controller.IsReadOnly = (bool)value;

            editor.Invalidate();
        });

    public static readonly BindableProperty ZoomProperty = BindableProperty.Create(
        nameof(Zoom),
        typeof(double),
        typeof(NotebookEditor),
        1d,
        BindingMode.TwoWay,
        propertyChanged: (b, _, value) =>
        {
            if (((NotebookEditor)b).controller is { } controller)
                controller.Zoom = (double)value;
        });

    /// <summary>The notebook to edit.</summary>
    public NotebookDocument? Notebook
    {
        get => (NotebookDocument?)this.GetValue(NotebookProperty);
        set => this.SetValue(NotebookProperty, value);
    }

    /// <summary>
    /// Canvas colours. Left unset the control follows the app's light/dark appearance; setting it pins
    /// the choice, including to <see cref="NotebookTheme.Light"/>.
    /// </summary>
    public NotebookTheme? Theme
    {
        get => (NotebookTheme?)this.GetValue(ThemeProperty);
        set => this.SetValue(ThemeProperty, value);
    }

    NotebookTheme EffectiveTheme => this.Theme ?? OfficeScheme.DefaultNotebook;

    /// <summary>What a press on the canvas does — select, write, draw, erase, lasso.</summary>
    public NoteTool Tool
    {
        get => (NoteTool)this.GetValue(ToolProperty);
        set => this.SetValue(ToolProperty, value);
    }

    public bool IsReadOnly
    {
        get => (bool)this.GetValue(IsReadOnlyProperty);
        set => this.SetValue(IsReadOnlyProperty, value);
    }

    public double Zoom
    {
        get => (double)this.GetValue(ZoomProperty);
        set => this.SetValue(ZoomProperty, value);
    }

    /// <summary>The live controller — tools, selection, caret, formatting, undo.</summary>
    public NotebookEditorController? Controller => this.controller;

    /// <summary>Raised after every edit.</summary>
    public event EventHandler? NotebookChanged;

    /// <summary>Raised when the shown page changes.</summary>
    public event EventHandler<PageAddress>? PageChanged;

    /// <summary>Gives the editor keyboard focus, so the platform starts sending it text.</summary>
    public void FocusEditor() => this.input.FocusForEditing();

    void Rebuild()
    {
        if (this.controller is not null)
        {
            this.controller.Changed -= this.OnControllerChanged;
            this.controller.Edited -= this.OnEdited;
        }

        if (this.Notebook is null)
        {
            this.controller = null;
            this.Invalidate();
            return;
        }

        this.controller = new NotebookEditorController(this.Notebook, this.measurer)
        {
            IsReadOnly = this.IsReadOnly,
            Tool = this.Tool
        };

        this.controller.Changed += this.OnControllerChanged;
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

    PageAddress lastAddress = PageAddress.None;

    void OnControllerChanged(object? sender, EventArgs e)
    {
        if (this.controller is { } controller)
        {
            if (controller.Address != this.lastAddress)
            {
                this.lastAddress = controller.Address;
                this.PageChanged?.Invoke(this, controller.Address);
            }

            if (Math.Abs(controller.Zoom - this.Zoom) > 0.0001)
                this.Zoom = controller.Zoom;

            if (controller.Tool != this.Tool)
                this.Tool = controller.Tool;
        }

        this.PositionInput();
        this.Invalidate();
    }

    void OnEdited(object? sender, EventArgs e)
    {
        this.Invalidate();
        this.NotebookChanged?.Invoke(this, EventArgs.Empty);
    }

    void Invalidate() => this.canvas.InvalidateSurface();

    // ---- painting ----

    void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var theme = this.EffectiveTheme;
        var surface = e.Surface.Canvas;
        surface.Clear(new SKColor(theme.Paper.R, theme.Paper.G, theme.Paper.B));

        if (this.controller is not { Page: { } page } controller)
            return;

        var scale = this.Width > 0 ? (float)(e.Info.Width / this.Width) : 1f;

        // Each frame rather than once at construction: the theme can flip mid-session, and an unchosen
        // pen has to move with it or it draws invisible ink on the new ground.
        controller.ApplyDefaultInk(theme.DefaultInk);

        this.painter.Paint(surface, new NotebookPaintRequest
        {
            Page = page,
            Zoom = controller.Zoom,
            ScrollX = controller.ScrollX,
            ScrollY = controller.ScrollY,
            ViewportWidth = this.Width,
            ViewportHeight = this.Height,
            Theme = theme,
            DeviceScale = scale,
            Chrome = this.BuildChrome(controller, theme)
        });
    }

    /// <summary>
    /// The editing overlay, or null when there is none to draw.
    /// </summary>
    /// <remarks>
    /// The caret is dropped when the control does not have focus. One drawn without it reads as an
    /// editor that is accepting keystrokes when it is not — the same rule the slide and document
    /// editors follow.
    /// </remarks>
    NotebookChrome? BuildChrome(NotebookEditorController controller, NotebookTheme theme)
    {
        if (this.IsReadOnly && !controller.HasSelection)
            return null;

        var chrome = NotebookChrome.From(controller, theme.Accent);

        return this.focused ? chrome : chrome with { Caret = null };
    }

    // ---- pointer ----

    void OnTouch(object? sender, SKTouchEventArgs e)
    {
        if (this.controller is not { } controller)
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
                if (this.IsDoubleTap(x, y))
                {
                    controller.PointerDoubleClick(x, y);
                    this.FocusEditor();
                    break;
                }

                controller.PointerDown(x, y, KindOf(e.DeviceType));
                this.FocusEditor();
                break;

            case SKTouchAction.Moved when e.InContact:
                // Pressure is only meaningful from a pen; every other device reports a constant, which
                // would make a mouse-drawn stroke either hairline or slab depending on the platform's
                // idea of "no pressure".
                controller.PointerMoveWithPressure(
                    x, y, e.DeviceType == SKTouchDeviceType.Pen ? Math.Clamp(e.Pressure, 0.05f, 1f) : 0.5);
                break;

            case SKTouchAction.Released:
            case SKTouchAction.Cancelled:
                controller.PointerUp();
                break;

            case SKTouchAction.WheelChanged:
                controller.ScrollBy(0, -e.WheelDelta);
                break;
        }

        e.Handled = true;
    }

    static PointerKind KindOf(SKTouchDeviceType device) => device switch
    {
        SKTouchDeviceType.Pen => PointerKind.Pen,
        SKTouchDeviceType.Mouse => PointerKind.Mouse,
        _ => PointerKind.Touch
    };

    DateTime lastTapTime;
    double lastTapX;
    double lastTapY;

    /// <summary>
    /// Whether this press continues a double tap.
    /// </summary>
    /// <remarks>
    /// Timed here rather than through a <c>TapGestureRecognizer</c> with <c>NumberOfTapsRequired=2</c>:
    /// that recogniser only reports the second tap, so the first would never select anything, and it
    /// competes with the touch events the canvas needs for dragging and for ink.
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

        if (isDouble)
            this.lastTapTime = DateTime.MinValue;

        return isDouble;
    }

    // ---- text input ----

    /// <summary>What the hidden entry held the last time characters were taken from it.</summary>
    string consumedInput = string.Empty;

#if MACOS
    /// <summary>
    /// Whether anything has been typed since the hidden entry was focused. Only the macOS AppKit head
    /// needs it, to tell a real Return from the completion that head raises on a focus change.
    /// </summary>
    bool sawInputSinceFocus;
#endif

    /// <summary>
    /// Turns whatever the hidden entry now holds into the characters that are actually new, and feeds
    /// those to the controller.
    /// </summary>
    /// <remarks>
    /// Diffed against what was last consumed rather than against <c>OldTextValue</c>. Most heads apply
    /// the clear below before the next keystroke arrives, so the text here is just the new characters;
    /// the macOS AppKit head does not, and the entry keeps accumulating — typing "hello" arrives as
    /// "h", "he", "hel", "hell", which inserted <c>hhehelhell</c>. The same reasoning, and the same
    /// fix, as the slide and document editors.
    /// </remarks>
    void OnInputTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (this.suppressInputEvents || this.controller is not { } controller || this.IsReadOnly)
            return;

        if (!controller.IsEditingText)
        {
            // Typing with something merely selected has nowhere to go. Cleared rather than buffered,
            // so the characters do not arrive all at once the moment a caret appears.
            this.ClearInput();
            return;
        }

        var text = e.NewTextValue ?? string.Empty;
#if MACOS
        this.sawInputSinceFocus = true;
#endif

        if (text.Length < this.consumedInput.Length && this.consumedInput.StartsWith(text, StringComparison.Ordinal))
        {
            for (var i = this.consumedInput.Length; i > text.Length; i--)
                controller.Backspace();

            this.consumedInput = text;
            return;
        }

        var inserted = text.StartsWith(this.consumedInput, StringComparison.Ordinal)
            ? text[this.consumedInput.Length..]
            : text;

        if (inserted.Length == 0)
            return;

        controller.InsertText(inserted);

        this.consumedInput = text;
        this.ClearInput();
    }

    void OnInputCompleted(object? sender, EventArgs e)
    {
        if (this.controller is not { } controller || this.IsReadOnly || !controller.IsEditingText)
            return;

#if MACOS
        // On the macOS AppKit head this event is not only Return: it also arrives when the hidden
        // entry gains or loses first responder, which happens on every click that moves the caret.
        if (!this.sawInputSinceFocus)
            return;
#endif

        controller.InsertParagraph();
        this.ClearInput();
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
        this.Invalidate();
    }

    void OnInputUnfocused(object? sender, FocusEventArgs e)
    {
        this.focused = false;
        this.consumedInput = string.Empty;
#if MACOS
        this.sawInputSinceFocus = false;
#endif

        // Leaving the caret parked in a container the user has clicked away from would leave an empty
        // one behind on the page; EndTextEditing is what sweeps those up.
        this.controller?.EndTextEditing();
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

    // ---- commands a toolbar drives ----

    public void ZoomIn() => this.controller?.SetZoom(this.controller.Zoom * 1.25, this.Width / 2, this.Height / 2);

    public void ZoomOut() => this.controller?.SetZoom(this.controller.Zoom / 1.25, this.Width / 2, this.Height / 2);

    public void ResetZoom() => this.controller?.SetZoom(1, this.Width / 2, this.Height / 2);

    /// <summary>Adds a shape in the middle of what is currently on screen.</summary>
    public void InsertShape(ShapeGeometry geometry)
    {
        if (this.controller is not { } controller)
            return;

        var (x, y) = controller.ToPage(this.Width / 2 - 90, this.Height / 2 - 55);
        controller.AddShape(geometry, x, y);
        this.FocusEditor();
    }

    public void InsertTextBox()
    {
        if (this.controller is not { } controller)
            return;

        var (x, y) = controller.ToPage(this.Width / 2 - 160, this.Height / 2 - 16);
        controller.AddTextBox(x, y);
        this.FocusEditor();
    }

    public void InsertImage(byte[] bytes, string contentType)
    {
        if (this.controller is not { } controller)
            return;

        var size = MeasureImage(bytes);
        var (x, y) = controller.ToPage(this.Width / 2 - 120, this.Height / 2 - 90);
        controller.AddImage(bytes, contentType, x, y, size.Width, size.Height);
    }

    /// <summary>
    /// The pixel size of encoded image bytes, so a picture arrives at its own aspect ratio.
    /// </summary>
    /// <remarks>
    /// Decoded here rather than passed in because every route that produces bytes — a file pick, a
    /// drop, a paste — would otherwise have to work it out separately, and a wrong guess is a squashed
    /// photo rather than an error anyone would notice.
    /// </remarks>
    static (double Width, double Height) MeasureImage(byte[] bytes)
    {
        try
        {
            using var codec = SKCodec.Create(new MemoryStream(bytes, writable: false));
            if (codec is { Info: { Width: > 0, Height: > 0 } info })
                return (info.Width, info.Height);
        }
        catch (Exception)
        {
            // A picture that will not decode is still inserted, at a default size, so the user can see
            // that something landed and delete it.
        }

        return (320, 240);
    }

    /// <summary>
    /// Routes a physical key press to the editor.
    /// </summary>
    /// <remarks>
    /// MAUI exposes no portable key-down event, so a host that wants arrow keys and shortcuts wires its
    /// own platform hook and calls this. Returns false when the key was not consumed.
    /// </remarks>
    public bool HandleKey(EditorKey key, bool shift = false, bool control = false)
    {
        if (this.controller is not { } controller || this.IsReadOnly)
            return false;

        // Translated into the browser's key names once, here, so the controller has one spelling to
        // switch on and the Blazor host can pass its own through untouched.
        var name = key switch
        {
            EditorKey.Left => "ArrowLeft",
            EditorKey.Right => "ArrowRight",
            EditorKey.Up => "ArrowUp",
            EditorKey.Down => "ArrowDown",
            EditorKey.Home => "Home",
            EditorKey.End => "End",
            EditorKey.Backspace => "Backspace",
            EditorKey.Delete => "Delete",
            EditorKey.Enter => "Enter",
            EditorKey.Tab => "Tab",
            EditorKey.Escape => "Escape",
            EditorKey.Undo => "z",
            EditorKey.Redo => "y",
            EditorKey.SelectAll => "a",
            EditorKey.Bold => "b",
            EditorKey.Italic => "i",
            EditorKey.Underline => "u",
            _ => null
        };

        if (name is null)
            return false;

        var isShortcut = key is EditorKey.Undo or EditorKey.Redo or EditorKey.SelectAll
            or EditorKey.Bold or EditorKey.Italic or EditorKey.Underline;

        if (!controller.HandleKey(name, shift, control || isShortcut))
            return false;

        // Not raising NotebookChanged: a real edit already reports through the controller's Edited
        // event, and raising it here as well made one keystroke look like two. Caret movement, which
        // changes nothing, still needs the repaint.
        this.Invalidate();
        return true;
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

        this.painter.Dispose();
        this.measurer.Dispose();
    }
}
