using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// Displays and edits an <c>.xlsx</c> worksheet.
/// </summary>
/// <remarks>
/// <para>
/// The grid is painted by the same <see cref="SpreadsheetPainter"/> the Blazor host uses, and driven by
/// the same <see cref="SpreadsheetController"/>. This class owns only two things MAUI has to provide:
/// a Skia surface, and a real <see cref="Entry"/> to host the in-cell editor so the platform's soft
/// keyboard and IME work without a custom text stack.
/// </para>
/// <para>
/// Requires <c>UseSkiaSharp()</c> in <c>MauiProgram</c>.
/// </para>
/// </remarks>
public class SpreadsheetView : ContentView, IDisposable
{
    readonly SKCanvasView canvas;
    readonly Entry editor;
    readonly AbsoluteLayout root;
    readonly SheetTabStrip sheetTabs;
    readonly FormulaBar formulaBar;
    readonly SpreadsheetToolbar toolbar;
    readonly Grid layout;
    readonly SpreadsheetPainter painter = new();

    SpreadsheetController? controller;
    IDispatcherTimer? marching;
    float dashPhase;
    bool suppressEditorEvents;
    bool disposed;

    public SpreadsheetView()
    {
        this.canvas = new SKCanvasView { EnableTouchEvents = true };
        this.canvas.PaintSurface += this.OnPaintSurface;
        this.canvas.Touch += this.OnTouch;

        this.editor = new Entry
        {
            IsVisible = false,
            Margin = 0,
            ReturnType = ReturnType.Done
        };

        this.editor.TextChanged += this.OnEditorTextChanged;
        this.editor.Completed += this.OnEditorCompleted;
        this.editor.Unfocused += this.OnEditorUnfocused;

        this.root = new AbsoluteLayout();
        this.root.Add(this.canvas);
        AbsoluteLayout.SetLayoutFlags(this.canvas, AbsoluteLayoutFlags.All);
        AbsoluteLayout.SetLayoutBounds(this.canvas, new Rect(0, 0, 1, 1));
        this.root.Add(this.editor);

        // Repaint when the OS appearance flips, so an unset Theme keeps up with it.
        this.FollowAppTheme(static v => v.Invalidate());

        this.sheetTabs = new SheetTabStrip();
        this.sheetTabs.Changed += this.OnSheetTabsChanged;

        this.formulaBar = new FormulaBar();
        this.formulaBar.Changed += this.OnSheetTabsChanged;

        this.toolbar = new SpreadsheetToolbar();
        this.toolbar.Changed += this.OnSheetTabsChanged;
        this.toolbar.WatermarkPicked += (_, mark) =>
        {
            this.Watermark = mark;
            this.toolbar.HasWatermark = mark is not null;
        };

        this.layout = new Grid
        {
            RowDefinitions =
            [
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            ]
        };

        this.layout.Add(this.toolbar);
        this.layout.Add(this.formulaBar, 0, 1);
        this.layout.Add(this.root, 0, 2);
        this.layout.Add(this.sheetTabs, 0, 3);

        // The canvas no longer fills this view - the strip takes a slice off the bottom - so the grid
        // has to be sized from the canvas rather than from the control, or every pointer coordinate
        // and every visible row is out by the height of the tabs.
        this.canvas.SizeChanged += this.OnCanvasSizeChanged;

        this.Content = this.layout;
    }

    public static readonly BindableProperty WorkbookProperty = BindableProperty.Create(
        nameof(Workbook),
        typeof(Workbook),
        typeof(SpreadsheetView),
        propertyChanged: (b, _, _) => ((SpreadsheetView)b).Rebuild());

    public static readonly BindableProperty SheetNameProperty = BindableProperty.Create(
        nameof(SheetName),
        typeof(string),
        typeof(SpreadsheetView),
        propertyChanged: (b, _, _) => ((SpreadsheetView)b).Rebuild());

    public static readonly BindableProperty ThemeProperty = BindableProperty.Create(
        nameof(Theme),
        typeof(SpreadsheetTheme),
        typeof(SpreadsheetView),
        null,
        propertyChanged: (b, _, value) =>
        {
            var view = (SpreadsheetView)b;
            var theme = (SpreadsheetTheme?)value;
            view.sheetTabs.Theme = theme;
            view.formulaBar.Theme = theme;
            view.toolbar.Theme = theme;
            view.Invalidate();
        });

    public static readonly BindableProperty ShowFormulaBarProperty = BindableProperty.Create(
        nameof(ShowFormulaBar),
        typeof(bool),
        typeof(SpreadsheetView),
        true,
        propertyChanged: (b, _, _) => ((SpreadsheetView)b).UpdateChrome());

    public static readonly BindableProperty ShowToolbarProperty = BindableProperty.Create(
        nameof(ShowToolbar),
        typeof(bool),
        typeof(SpreadsheetView),
        false,
        propertyChanged: (b, _, _) => ((SpreadsheetView)b).UpdateChrome());

    public static readonly BindableProperty IsReadOnlyProperty = BindableProperty.Create(
        nameof(IsReadOnly),
        typeof(bool),
        typeof(SpreadsheetView),
        false,
        propertyChanged: (b, _, _) => ((SpreadsheetView)b).UpdateChrome());

    public static readonly BindableProperty ShowSheetTabsProperty = BindableProperty.Create(
        nameof(ShowSheetTabs),
        typeof(bool),
        typeof(SpreadsheetView),
        true,
        propertyChanged: (b, _, _) => ((SpreadsheetView)b).UpdateChrome());

    public static readonly BindableProperty AllowSheetEditingProperty = BindableProperty.Create(
        nameof(AllowSheetEditing),
        typeof(bool),
        typeof(SpreadsheetView),
        true,
        propertyChanged: (b, _, _) => ((SpreadsheetView)b).UpdateChrome());

    public Workbook? Workbook
    {
        get => (Workbook?)this.GetValue(WorkbookProperty);
        set => this.SetValue(WorkbookProperty, value);
    }

    public string? SheetName
    {
        get => (string?)this.GetValue(SheetNameProperty);
        set => this.SetValue(SheetNameProperty, value);
    }

    /// <summary>
    /// Grid chrome colours. Left unset the control follows the app's light/dark appearance, so a
    /// workbook in a dark app is dark without the host wiring anything up. Setting it pins the
    /// choice - including to <see cref="SpreadsheetTheme.Light"/>, which is how a host asks for a
    /// paper-white grid whatever the app around it is doing.
    /// </summary>
    public SpreadsheetTheme? Theme
    {
        get => (SpreadsheetTheme?)this.GetValue(ThemeProperty);
        set => this.SetValue(ThemeProperty, value);
    }

    /// <summary>The theme actually painted: <see cref="Theme"/> when set, otherwise the app's.</summary>
    SpreadsheetTheme EffectiveTheme => this.Theme ?? OfficeScheme.Default;

    /// <summary>
    /// Whether to show the strip of sheet tabs under the grid. On by default: a workbook with no way
    /// to reach its other sheets is a worse default than a single tab that does nothing.
    /// </summary>
    public bool ShowSheetTabs
    {
        get => (bool)this.GetValue(ShowSheetTabsProperty);
        set => this.SetValue(ShowSheetTabsProperty, value);
    }

    /// <summary>
    /// Whether the tab strip can add, rename, reorder, hide and delete sheets, as opposed to only
    /// switching between them.
    /// </summary>
    public bool AllowSheetEditing
    {
        get => (bool)this.GetValue(AllowSheetEditingProperty);
        set => this.SetValue(AllowSheetEditingProperty, value);
    }

    /// <summary>The live controller, so a toolbar or formula bar can drive the same state.</summary>
    public SpreadsheetController? Controller => this.controller;

    /// <summary>Raised after a cell is committed.</summary>
    public event EventHandler<CellRef>? CellChanged;

    /// <summary>Raised when the sheet on screen changes, by a tab tap or by a sheet edit.</summary>
    public event EventHandler<Worksheet>? ActiveSheetChanged;

    /// <summary>
    /// Whether to show the name box and formula field above the grid.
    /// </summary>
    /// <remarks>
    /// On by default. The grid paints the <em>result</em> of a formula, so without this a cell reading
    /// 84 gives no way to discover that it holds <c>=B1*2</c> — and no way to edit it as a formula
    /// rather than retyping it from scratch.
    /// </remarks>
    public bool ShowFormulaBar
    {
        get => (bool)this.GetValue(ShowFormulaBarProperty);
        set => this.SetValue(ShowFormulaBarProperty, value);
    }

    /// <summary>
    /// Whether to show the formatting toolbar above the formula bar.
    /// </summary>
    /// <remarks>
    /// Off by default, unlike the formula bar and the tab strip. Those two are how a workbook is read;
    /// the toolbar is how one is authored, it is the tallest piece of chrome here, and a viewer that
    /// gained a formatting bar it never asked for would be a breaking change to every existing use.
    /// </remarks>
    public bool ShowToolbar
    {
        get => (bool)this.GetValue(ShowToolbarProperty);
        set => this.SetValue(ShowToolbarProperty, value);
    }

    /// <summary>Shows the workbook but refuses formatting and sheet edits.</summary>
    /// <remarks>
    /// Cell editing is deliberately not covered: the grid's own read-only story is a separate piece of
    /// work, and a property that half-locked the sheet would be worse than one that says what it does.
    /// </remarks>
    public bool IsReadOnly
    {
        get => (bool)this.GetValue(IsReadOnlyProperty);
        set => this.SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>The tab strip, exposed so a host can hide or restyle it beyond the two properties above.</summary>
    public SheetTabStrip SheetTabs => this.sheetTabs;

    /// <summary>The formatting toolbar, exposed so a host can add its own items to it.</summary>
    public SpreadsheetToolbar Toolbar => this.toolbar;

    /// <summary>The formula bar, exposed so a host can make it read-only or restyle it.</summary>
    public FormulaBar FormulaBar => this.formulaBar;

    void Rebuild()
    {
        var workbook = this.Workbook;
        if (workbook is null)
        {
            this.DetachController();
            this.controller = null;
            this.UpdateChrome();
            this.Invalidate();
            return;
        }

        var sheet = this.SheetName is null
            ? workbook.Sheets.FirstOrDefault()
            : workbook.Sheets.FirstOrDefault(x => x.Name == this.SheetName);

        if (sheet is null)
        {
            this.DetachController();
            this.controller = null;
            this.UpdateChrome();
            this.Invalidate();
            return;
        }

        // Switch rather than rebuild when it is the same workbook. A new controller would throw away
        // every sheet's remembered selection, scroll position and column widths - and since the tab
        // strip writes the sheet name back through this property, that would happen on every tap.
        if (this.controller is { } existing && ReferenceEquals(existing.Workbook, workbook))
        {
            if (!ReferenceEquals(existing.Sheet, sheet))
                existing.SwitchSheet(sheet);

            this.UpdateChrome();
            this.Invalidate();
            return;
        }

        this.DetachController();
        this.controller = new SpreadsheetController(workbook, sheet);
        this.controller.Changed += this.OnControllerChanged;
        this.controller.EditingChanged += this.OnEditingChanged;
        this.controller.ActiveSheetChanged += this.OnActiveSheetChanged;
        this.controller.ClipboardChanged += this.OnClipboardChanged;
        this.controller.Resize(
            this.canvas.Width > 0 ? this.canvas.Width : 800,
            this.canvas.Height > 0 ? this.canvas.Height : 600);

        this.UpdateChrome();
        this.Invalidate();
    }

    void OnCanvasSizeChanged(object? sender, EventArgs e)
    {
        if (this.canvas.Width > 0 && this.canvas.Height > 0)
            this.controller?.Resize(this.canvas.Width, this.canvas.Height);
    }

    void UpdateChrome()
    {
        this.toolbar.IsReadOnly = this.IsReadOnly;
        this.toolbar.Controller = this.ShowToolbar ? this.controller : null;

        this.sheetTabs.AllowEditing = this.AllowSheetEditing && !this.IsReadOnly;
        this.sheetTabs.Controller = this.ShowSheetTabs ? this.controller : null;
        this.sheetTabs.Rebuild();

        this.formulaBar.Controller = this.ShowFormulaBar ? this.controller : null;
        this.formulaBar.Refresh();
    }

    void OnSheetTabsChanged(object? sender, EventArgs e) => this.Invalidate();

    void OnActiveSheetChanged(object? sender, Worksheet sheet)
    {
        // Written back so that a host binding SheetName and a tab tap do not fight: without this the
        // next Rebuild would switch straight back to the sheet the user just left.
        this.SetValue(SheetNameProperty, sheet.Name);

        this.ActiveSheetChanged?.Invoke(this, sheet);
        this.sheetTabs.Rebuild();
        this.formulaBar.Refresh();
        this.Invalidate();
    }

    void OnControllerChanged(object? sender, EventArgs e) => this.Invalidate();

    void Invalidate() => this.canvas.InvalidateSurface();

    void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var theme = this.EffectiveTheme;
        if (this.controller is null)
        {
            e.Surface.Canvas.Clear(new SKColor(theme.Background.R, theme.Background.G, theme.Background.B));
            return;
        }

        // The surface is in device pixels while layout is in device-independent units.
        var scale = this.canvas.Width > 0 ? (float)(e.Info.Width / this.canvas.Width) : 1f;

        this.painter.Paint(e.Surface.Canvas, new SpreadsheetPaintRequest
        {
            Watermark = this.Watermark,
            Workbook = this.controller.Workbook,
            Sheet = this.controller.Sheet,
            Viewport = this.controller.Viewport,
            Selection = this.controller.Selection,
            Theme = theme,
            Scale = scale,
            EditingCell = this.controller.EditingCell,
            ClipboardRange = this.controller.ClipboardRange,
            FindMatches = this.controller.FindMatchCells(),
            ClipboardDashPhase = this.dashPhase,
            ShowTouchHandles = this.controller.UsesTouch
        });
    }

    static PointerKind KindOf(SKTouchDeviceType device)
        => device switch
        {
            SKTouchDeviceType.Touch => PointerKind.Touch,
            SKTouchDeviceType.Pen => PointerKind.Pen,
            _ => PointerKind.Mouse
        };

    void OnTouch(object? sender, SKTouchEventArgs e)
    {
        if (this.controller is null)
        {
            e.Handled = true;
            return;
        }

        // Touch locations arrive in device pixels; the controller works in the same units as layout.
        var scale = this.canvas.Width > 0 ? (float)(this.canvas.CanvasSize.Width / this.canvas.Width) : 1f;
        var x = e.Location.X / scale;
        var y = e.Location.Y / scale;

        switch (e.ActionType)
        {
            case SKTouchAction.Pressed:
                // Any touch on the grid ends an edit in the bar above it. See FormulaBar.EndEditing.
                this.formulaBar.EndEditing();
                this.controller.PointerDown(x, y, kind: KindOf(e.DeviceType));
                break;

            case SKTouchAction.Moved:
                if (e.InContact)
                    this.controller.PointerMove(x, y);

                break;

            case SKTouchAction.Released:
            case SKTouchAction.Cancelled:
                this.controller.PointerUp();
                break;

            case SKTouchAction.WheelChanged:
                // A wheel notch is reported in platform units; treat it as a vertical scroll.
                // SKTouchEventArgs carries no second axis and no modifier state, so a horizontal
                // wheel is not reachable from here - ScrollBy is what a desktop host drives instead.
                this.controller.Scroll(0, -e.WheelDelta);
                break;
        }

        e.Handled = true;
    }

    void OnEditingChanged(object? sender, CellRef? cell)
    {
        if (this.controller is null)
            return;

        if (cell is not { } target)
        {
            this.editor.IsVisible = false;
            return;
        }

        var rect = this.controller.Viewport.CellRect(target);

        this.suppressEditorEvents = true;
        this.editor.Text = this.controller.EditingText;
        this.suppressEditorEvents = false;

        AbsoluteLayout.SetLayoutFlags(this.editor, AbsoluteLayoutFlags.None);
        AbsoluteLayout.SetLayoutBounds(this.editor, new Rect(rect.X, rect.Y, rect.Width, rect.Height));
        this.editor.IsVisible = true;
        this.editor.FocusForEditing();
    }

    void OnEditorTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!this.suppressEditorEvents)
            this.controller?.UpdateEditingText(e.NewTextValue ?? string.Empty);
    }

    void OnEditorCompleted(object? sender, EventArgs e) => this.Commit(EditCommitDirection.Down);

    void OnEditorUnfocused(object? sender, FocusEventArgs e)
    {
        // Tapping elsewhere commits, matching Excel rather than discarding what was typed.
        if (this.editor.IsVisible)
            this.Commit(EditCommitDirection.None);
    }

    void Commit(EditCommitDirection direction)
    {
        var cell = this.controller?.EditingCell;
        this.controller?.CommitEdit(direction);

        if (cell is { } committed)
            this.CellChanged?.Invoke(this, committed);
    }

    /// <summary>
    /// Opens the editor on the active cell. Exposed because MAUI has no portable key-down event, so a
    /// desktop host wires physical keys through its own platform hook and calls in here.
    /// </summary>
    public void BeginEdit(string? initialText = null) => this.controller?.BeginEdit(initialText);

    public void Move(MoveDirection direction, bool extend = false, bool toEdge = false)
        => this.controller?.Move(direction, extend, toEdge);

    /// <summary>
    /// Scrolls the grid by a delta in layout units, clamped to the sheet's content.
    /// </summary>
    /// <remarks>
    /// Public because the wheel has only one axis: a host wanting a horizontal scrollbar, a trackpad's
    /// sideways swipe or a "scroll right" command has nowhere else to send it.
    /// </remarks>
    public void ScrollBy(double dx, double dy)
        => this.controller?.Scroll(dx, dy);

    public void ClearSelection() => this.controller?.ClearSelection();

    public void Undo() => this.controller?.Undo();

    public void Redo() => this.controller?.Redo();

    /// <summary>Moves to the next or previous visible sheet, stopping at either end.</summary>
    public void StepSheet(int offset)
    {
        if (this.controller is not { } current)
            return;

        var sheets = current.VisibleSheets;
        var at = -1;
        for (var i = 0; i < sheets.Count && at < 0; i++)
        {
            if (ReferenceEquals(sheets[i], current.Sheet))
                at = i;
        }

        if (at >= 0 && sheets.ElementAtOrDefault(at + offset) is { } target)
            current.SwitchSheet(target);
    }

    /// <summary>Takes a copy of the selection, marking it with the marching-ants border.</summary>
    public void Copy() => this.controller?.Copy();

    /// <summary>Marks the selection to be moved by the next paste. Nothing is removed until then.</summary>
    public void Cut() => this.controller?.Cut();

    /// <summary>Writes the pending cut or copy at the selection, as one undo step.</summary>
    public void Paste() => this.controller?.Paste();

    /// <summary>Abandons the pending cut or copy, taking the marching-ants border with it.</summary>
    public void ClearClipboard() => this.controller?.ClearClipboard();

    /// <summary>Inserts blank rows above the selection.</summary>
    public void InsertRows(int count = 1) => this.controller?.InsertRows(count);

    /// <summary>Inserts blank columns to the left of the selection.</summary>
    public void InsertColumns(int count = 1) => this.controller?.InsertColumns(count);

    /// <summary>Removes rows from the top of the selection down, closing the gap.</summary>
    public void DeleteRows(int count = 1) => this.controller?.DeleteRows(count);

    /// <summary>Removes columns from the left of the selection across, closing the gap.</summary>
    public void DeleteColumns(int count = 1) => this.controller?.DeleteColumns(count);

    void OnClipboardChanged(object? sender, EventArgs e)
    {
        if (this.controller?.ClipboardRange is null)
            this.StopMarching();
        else
            this.StartMarching();
    }

    /// <summary>
    /// Walks the dash phase forward until the clipboard is abandoned.
    /// </summary>
    /// <remarks>
    /// The border lives inside a Skia surface that only redraws when something invalidates it, so the
    /// marching has to be driven by a clock rather than by an animation the platform owns. It runs
    /// only while there is something on the clipboard: a permanent timer under a grid that is usually
    /// idle is a repaint every frame for nothing, and on a phone that is battery.
    /// </remarks>
    void StartMarching()
    {
        if (this.marching is not null || this.Dispatcher is null)
            return;

        var timer = this.Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(120);
        timer.IsRepeating = true;
        timer.Tick += this.OnMarchingTick;
        this.marching = timer;
        timer.Start();
    }

    void OnMarchingTick(object? sender, EventArgs e)
    {
        // Two dashes' worth of travel per tick, so the phase never grows without bound.
        this.dashPhase = (this.dashPhase + 2f) % 10f;
        this.Invalidate();
    }

    void StopMarching()
    {
        var timer = this.marching;
        this.marching = null;

        if (timer is null)
            return;

        timer.Stop();
        timer.Tick -= this.OnMarchingTick;
        this.dashPhase = 0;
        this.Invalidate();
    }

    /// <summary>
    /// Stops the marching-ants clock when the view leaves the screen and resumes it on return.
    /// </summary>
    /// <remarks>
    /// A running dispatcher timer is rooted by the platform's run loop, so a copy left pending when the
    /// page was popped kept ticking - and kept this view and its page alive - for the life of the app.
    /// </remarks>
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (this.Handler is null)
            this.StopMarching();
        else if (this.controller?.ClipboardRange is not null)
            this.StartMarching();
    }

    void DetachController()
    {
        if (this.controller is null)
            return;

        this.controller.Changed -= this.OnControllerChanged;
        this.controller.EditingChanged -= this.OnEditingChanged;
        this.controller.ActiveSheetChanged -= this.OnActiveSheetChanged;
        this.controller.ClipboardChanged -= this.OnClipboardChanged;
        this.StopMarching();
    }

    public void Dispose()
    {
        if (this.disposed)
            return;

        this.disposed = true;
        this.DetachController();
        this.sheetTabs.Changed -= this.OnSheetTabsChanged;
        this.sheetTabs.Controller = null;
        this.formulaBar.Changed -= this.OnSheetTabsChanged;
        this.formulaBar.Detach();
        this.toolbar.Changed -= this.OnSheetTabsChanged;
        this.toolbar.Detach();
        this.canvas.SizeChanged -= this.OnCanvasSizeChanged;
        this.canvas.PaintSurface -= this.OnPaintSurface;
        this.canvas.Touch -= this.OnTouch;
        this.editor.TextChanged -= this.OnEditorTextChanged;
        this.editor.Completed -= this.OnEditorCompleted;
        this.editor.Unfocused -= this.OnEditorUnfocused;
        this.painter.Dispose();

        GC.SuppressFinalize(this);
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
        typeof(SpreadsheetView),
        null,
        propertyChanged: (b, _, _) => ((SpreadsheetView)b).Invalidate());

    /// <inheritdoc cref="WatermarkProperty"/>
    public OfficeWatermark? Watermark
    {
        get => (OfficeWatermark?)this.GetValue(WatermarkProperty);
        set => this.SetValue(WatermarkProperty, value);
    }

}
