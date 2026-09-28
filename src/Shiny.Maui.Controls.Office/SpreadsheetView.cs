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
/// the same <see cref="SpreadsheetController"/>. This class owns only what MAUI has to provide: a Skia
/// surface, a real <see cref="Entry"/> to host the in-cell editor so the platform's soft keyboard and IME
/// work without a custom text stack, and the overlay the controller's dialogs, menus and formula
/// autocomplete are drawn on.
/// </para>
/// <para>
/// Requires <c>UseShinyOffice()</c> in <c>MauiProgram</c>.
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
    readonly Grid overlay;
    readonly SheetDialogHost dialogs = new();
    readonly SheetMenuHost menus = new();
    readonly FormulaAssistView assist = new();
    readonly SpreadsheetPainter painter = new();

    SpreadsheetController? controller;
    IDispatcherTimer? marching;
    float dashPhase;
    bool suppressEditorEvents;
    bool disposed;

    // Long-press: a finger held still on the grid opens the context menu, the touch equivalent of a
    // right-click.
    int pressToken;
    Point pressAt;
    bool pressMoved;

    double pinchStart = 1;

    public SpreadsheetView()
    {
        this.canvas = new SKCanvasView { EnableTouchEvents = true };
        this.canvas.PaintSurface += this.OnPaintSurface;
        this.canvas.Touch += this.OnTouch;

        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += this.OnPinch;
        this.canvas.GestureRecognizers.Add(pinch);

        this.editor = new Entry
        {
            IsVisible = false,
            Margin = 0,
            ReturnType = ReturnType.Done
        };

        this.editor.TextChanged += this.OnEditorTextChanged;
        this.editor.Completed += this.OnEditorCompleted;
        this.editor.Unfocused += this.OnEditorUnfocused;
        this.editor.PropertyChanged += (_, e) =>
        {
            // The caret moving inside a formula changes which argument the tip is on.
            if (e.PropertyName == nameof(Entry.CursorPosition) && this.editor.IsVisible)
                this.UpdateAssist(this.editor);
        };

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
        this.formulaBar.FormulaTextChanged += (_, entry) => this.UpdateAssist(entry);
        this.formulaBar.FormulaEditingEnded += (_, _) => this.assist.Hide();

        this.toolbar = new SpreadsheetToolbar();
        this.toolbar.Changed += this.OnSheetTabsChanged;
        this.toolbar.WatermarkPicked += (_, mark) =>
        {
            this.Watermark = mark;
            this.toolbar.HasWatermark = mark is not null;
        };

        this.toolbar.FileMenuRequested += (_, _) => this.FileMenuRequested?.Invoke(this, EventArgs.Empty);
        this.toolbar.FormulaBarToggled += (_, visible) => this.ShowFormulaBar = visible;

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

        // Everything that floats: the formula-autocomplete list, popup menus and dialogs, over all of the
        // chrome rather than just the grid, because a menu opened near the bottom of the grid has to be
        // able to run over the tab strip.
        this.overlay = new Grid { InputTransparent = true, CascadeInputTransparent = false };
        this.overlay.Add(this.assist);
        this.overlay.Add(this.menus);
        this.overlay.Add(this.dialogs);
        this.assist.HorizontalOptions = LayoutOptions.Start;
        this.assist.VerticalOptions = LayoutOptions.Start;
        this.dialogs.Closed += (_, _) => this.AfterOverlayChanged();
        this.layout.Add(this.overlay);
        Grid.SetRowSpan(this.overlay, 4);

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
        BindingMode.TwoWay,
        propertyChanged: (b, _, _) => ((SpreadsheetView)b).UpdateChrome());

    public static readonly BindableProperty ShowToolbarProperty = BindableProperty.Create(
        nameof(ShowToolbar),
        typeof(bool),
        typeof(SpreadsheetView),
        true,
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

    /// <summary>
    /// The magnification, 0.1 to 4 — 10% to 400%. Two-way: pinching the grid, the View tab's zoom
    /// commands and a status bar's slider all move it.
    /// </summary>
    public static readonly BindableProperty ZoomProperty = BindableProperty.Create(
        nameof(Zoom),
        typeof(double),
        typeof(SpreadsheetView),
        1d,
        BindingMode.TwoWay,
        coerceValue: (_, value) => Math.Clamp((double)value, SpreadsheetController.MinZoom, SpreadsheetController.MaxZoom),
        propertyChanged: (b, _, value) =>
        {
            var view = (SpreadsheetView)b;
            if (view.controller is { } current && Math.Abs(current.Zoom - (double)value) > 0.0005)
                current.Zoom = (double)value;
        });

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
    /// Grid chrome colours. Left unset the control follows the app's light/dark appearance. Setting it
    /// pins the choice.
    /// </summary>
    public SpreadsheetTheme? Theme
    {
        get => (SpreadsheetTheme?)this.GetValue(ThemeProperty);
        set => this.SetValue(ThemeProperty, value);
    }

    /// <summary>The theme actually painted: <see cref="Theme"/> when set, otherwise the app's.</summary>
    SpreadsheetTheme EffectiveTheme => this.Theme ?? OfficeScheme.Default;

    /// <summary>Whether to show the strip of sheet tabs under the grid. On by default.</summary>
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

    /// <inheritdoc cref="ZoomProperty"/>
    public double Zoom
    {
        get => (double)this.GetValue(ZoomProperty);
        set => this.SetValue(ZoomProperty, value);
    }

    /// <summary>The live controller, so a toolbar or formula bar can drive the same state.</summary>
    public SpreadsheetController? Controller => this.controller;

    /// <summary>
    /// Average, Count, Numerical Count, Min, Max and Sum of the selection — what a status bar shows.
    /// </summary>
    public SelectionStatistics SelectionStatistics => this.controller?.SelectionStatistics ?? SelectionStatistics.Empty;

    /// <summary>Raised when <see cref="SelectionStatistics"/> may have changed — a new selection, or an edit under it.</summary>
    public event EventHandler? SelectionStatisticsChanged;

    /// <summary>Raised when <see cref="Zoom"/> changes, however it changed.</summary>
    public event EventHandler<double>? ZoomChanged;

    /// <summary>
    /// Raised when the ribbon's File button is pressed. What File opens — save, export, recent files —
    /// is the app's; the control only reports the press.
    /// </summary>
    public event EventHandler? FileMenuRequested;

    /// <summary>Raised after a cell is committed.</summary>
    public event EventHandler<CellRef>? CellChanged;

    /// <summary>Raised when the sheet on screen changes, by a tab tap or by a sheet edit.</summary>
    public event EventHandler<Worksheet>? ActiveSheetChanged;

    /// <summary>Whether to show the name box and formula field above the grid. On by default.</summary>
    public bool ShowFormulaBar
    {
        get => (bool)this.GetValue(ShowFormulaBarProperty);
        set => this.SetValue(ShowFormulaBarProperty, value);
    }

    /// <summary>
    /// Whether to show the ribbon above the formula bar.
    /// </summary>
    /// <remarks>
    /// <b>On by default</b> — a behaviour change: it used to be off, back when the bar was a strip of
    /// formatting buttons. It is now Excel's ribbon, and a spreadsheet without it hides most of what the
    /// control can do. Set it to false for a viewer.
    /// </remarks>
    public bool ShowToolbar
    {
        get => (bool)this.GetValue(ShowToolbarProperty);
        set => this.SetValue(ShowToolbarProperty, value);
    }

    /// <summary>Shows the workbook but refuses formatting and sheet edits.</summary>
    public bool IsReadOnly
    {
        get => (bool)this.GetValue(IsReadOnlyProperty);
        set => this.SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>The tab strip, exposed so a host can hide or restyle it.</summary>
    public SheetTabStrip SheetTabs => this.sheetTabs;

    /// <summary>The ribbon, exposed so a host can add its own items to it.</summary>
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

        // Switch rather than rebuild when it is the same workbook, keeping each sheet's remembered state.
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
        this.controller.DialogRequested += this.OnDialogRequested;
        this.controller.MenuRequested += this.OnMenuRequested;
        this.controller.HyperlinkActivated += this.OnHyperlinkActivated;
        this.controller.ZoomChanged += this.OnZoomChanged;
        this.controller.SelectionStatisticsChanged += this.OnSelectionStatisticsChanged;
        this.controller.Resize(
            this.canvas.Width > 0 ? this.canvas.Width : 800,
            this.canvas.Height > 0 ? this.canvas.Height : 600);

        // The bound zoom wins over whatever the file was saved at, unless it was left at the default.
        if (Math.Abs(this.Zoom - 1) > 0.0005)
            this.controller.Zoom = this.Zoom;
        else if (Math.Abs(this.controller.Zoom - 1) > 0.0005)
            this.SetValue(ZoomProperty, this.controller.Zoom);

        this.UpdateChrome();
        this.Invalidate();
        this.SelectionStatisticsChanged?.Invoke(this, EventArgs.Empty);
    }

    void OnCanvasSizeChanged(object? sender, EventArgs e)
    {
        if (this.canvas.Width > 0 && this.canvas.Height > 0)
            this.controller?.Resize(this.canvas.Width, this.canvas.Height);
    }

    void UpdateChrome()
    {
        this.toolbar.IsReadOnly = this.IsReadOnly;
        this.toolbar.IsFormulaBarVisible = this.ShowFormulaBar;
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
        this.SetValue(SheetNameProperty, sheet.Name);

        this.ActiveSheetChanged?.Invoke(this, sheet);
        this.sheetTabs.Rebuild();
        this.formulaBar.Refresh();
        this.Invalidate();
    }

    void OnControllerChanged(object? sender, EventArgs e)
    {
        // The editor tracks the cell it covers: a zoom or a scroll moves the cell under it.
        if (this.controller?.EditorBounds is { } bounds && this.editor.IsVisible)
            this.PlaceEditor(bounds);

        this.Invalidate();
    }

    void OnZoomChanged(object? sender, double zoom)
    {
        this.SetValue(ZoomProperty, zoom);
        this.ZoomChanged?.Invoke(this, zoom);
    }

    void OnSelectionStatisticsChanged(object? sender, EventArgs e)
        => this.SelectionStatisticsChanged?.Invoke(this, EventArgs.Empty);

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

        this.painter.Paint(e.Surface.Canvas, SpreadsheetPaintRequest.For(this.controller, theme, scale) with
        {
            Watermark = this.Watermark,
            ClipboardDashPhase = this.dashPhase
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
                this.formulaBar.EndEditing();
                this.menus.Close();

                // A right-click is a context menu, not a selection drag.
                if (e.MouseButton == SKMouseButton.Right)
                {
                    this.controller.OpenContextMenu(x, y);
                    break;
                }

                // SKTouchEventArgs carries no modifier state, so a Ctrl-click cannot be told from a click
                // here; a desktop host that can see modifiers reaches links through HandleKey or the
                // context menu's Open Hyperlink.
                this.controller.PointerDown(x, y, kind: KindOf(e.DeviceType));

                if (e.DeviceType == SKTouchDeviceType.Touch)
                    this.StartLongPress(x, y);

                break;

            case SKTouchAction.Moved:
                if (e.InContact)
                {
                    if (Math.Abs(x - this.pressAt.X) > 6 || Math.Abs(y - this.pressAt.Y) > 6)
                        this.pressMoved = true;

                    this.controller.PointerMove(x, y);
                }
                else
                {
                    // A mouse moving with no button down: the note under it opens.
                    this.controller.PointerHover(x, y);
                }

                break;

            case SKTouchAction.Released:
            case SKTouchAction.Cancelled:
                this.pressToken++;
                this.controller.PointerUp();
                break;

            case SKTouchAction.Exited:
                this.controller.PointerExit();
                break;

            case SKTouchAction.WheelChanged:
                // SKTouchEventArgs carries no second axis and no modifier state, so Ctrl+wheel zoom is not
                // reachable from here - pinch is, and the Zoom property is what a desktop host drives.
                this.controller.Scroll(0, -e.WheelDelta);
                break;
        }

        e.Handled = true;
    }

    void StartLongPress(double x, double y)
    {
        var token = ++this.pressToken;
        this.pressAt = new Point(x, y);
        this.pressMoved = false;

        this.Dispatcher?.DispatchDelayed(TimeSpan.FromMilliseconds(550), () =>
        {
            if (token != this.pressToken || this.pressMoved || this.controller is not { } current)
                return;

            // End the press first: the finger is still down, and the grid would otherwise take the lift
            // that dismisses the menu as the end of a tap.
            current.PointerUp();
            current.OpenContextMenu(x, y);
        });
    }

    void OnPinch(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (this.controller is not { } current)
            return;

        switch (e.Status)
        {
            case GestureStatus.Started:
                this.pinchStart = current.Zoom;
                this.pressToken++;
                break;

            case GestureStatus.Running:
                // Scale arrives as the change since the last update, so it accumulates.
                this.pinchStart *= e.Scale;
                current.Zoom = this.pinchStart;
                break;
        }
    }

    // ---- editing ----

    void OnEditingChanged(object? sender, CellRef? cell)
    {
        if (this.controller is null)
            return;

        if (cell is null || this.controller.EditorBounds is not { } bounds)
        {
            this.editor.IsVisible = false;
            this.assist.Hide();
            return;
        }

        this.suppressEditorEvents = true;
        this.editor.Text = this.controller.EditingText;
        this.suppressEditorEvents = false;

        this.editor.FontSize = Math.Max(8, this.controller.EditorFontSize);
        this.PlaceEditor(bounds);
        this.editor.IsVisible = true;
        this.editor.FocusForEditing();
        this.editor.CursorPosition = this.editor.Text?.Length ?? 0;
        this.UpdateAssist(this.editor);
    }

    void PlaceEditor(GridRect bounds)
    {
        AbsoluteLayout.SetLayoutFlags(this.editor, AbsoluteLayoutFlags.None);
        AbsoluteLayout.SetLayoutBounds(this.editor, new Rect(bounds.X, bounds.Y, Math.Max(bounds.Width, 60), Math.Max(bounds.Height, 24)));
    }

    void OnEditorTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (this.suppressEditorEvents)
            return;

        this.controller?.UpdateEditingText(e.NewTextValue ?? string.Empty);
        this.UpdateAssist(this.editor);
    }

    void OnEditorCompleted(object? sender, EventArgs e) => this.Commit(EditCommitDirection.Down);

    void OnEditorUnfocused(object? sender, FocusEventArgs e)
    {
        if (!this.editor.IsVisible)
            return;

        // Tapping an autocomplete suggestion takes focus away for a moment and hands it straight back;
        // that is not the user leaving the cell, so the commit waits to see whether focus returns.
        if (this.assist.IsVisible && this.Dispatcher is { } dispatcher)
        {
            dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(200), () =>
            {
                if (this.editor.IsVisible && !this.editor.IsFocused)
                    this.Commit(EditCommitDirection.None);
            });

            return;
        }

        this.Commit(EditCommitDirection.None);
    }

    void Commit(EditCommitDirection direction)
    {
        var cell = this.controller?.EditingCell;
        this.assist.Hide();
        this.controller?.CommitEdit(direction);

        if (cell is { } committed)
            this.CellChanged?.Invoke(this, committed);
    }

    /// <summary>
    /// Shows formula autocomplete under whichever entry is being typed into — the in-cell editor or the
    /// formula bar's field.
    /// </summary>
    void UpdateAssist(Entry entry)
    {
        if (this.controller is not { } current || !(entry.Text ?? string.Empty).StartsWith('='))
        {
            this.assist.Hide();
            return;
        }

        this.assist.Update(entry, current, this.EffectiveTheme);
        if (!this.assist.IsVisible)
            return;

        // Under the entry, in the overlay's coordinates.
        var origin = PositionIn(entry, this.layout);
        this.assist.Margin = new Thickness(origin.X, origin.Y + entry.Height + 2, 0, 0);
        this.assist.WidthRequest = Math.Max(260, Math.Min(360, entry.Width));
        this.AfterOverlayChanged();
    }

    static Point PositionIn(VisualElement element, VisualElement ancestor)
    {
        double x = 0, y = 0;
        for (Element? current = element; current is VisualElement visual && current != ancestor; current = current.Parent)
        {
            x += visual.X;
            y += visual.Y;
        }

        return new Point(x, y);
    }

    // ---- dialogs and menus ----

    void OnDialogRequested(object? sender, SheetDialog dialog)
    {
        this.menus.Close();
        this.assist.Hide();
        this.dialogs.Show(dialog, this.EffectiveTheme);
        this.AfterOverlayChanged();
    }

    void OnMenuRequested(object? sender, SheetMenuRequest request)
    {
        // The request is in the grid's coordinates; the overlay spans the whole control.
        var origin = PositionIn(this.root, this.layout);
        this.menus.Show(request, new Point(origin.X + request.X, origin.Y + request.Y), this.EffectiveTheme);
        this.AfterOverlayChanged();
    }

    /// <summary>
    /// Repaints after an overlay opens or closes. The overlay layer itself is permanently input
    /// transparent with the cascade off, so only a visible card or backdrop takes a touch — a layer
    /// spanning the whole control that swallowed input would leave the grid unclickable.
    /// </summary>
    void AfterOverlayChanged() => this.Invalidate();

    async void OnHyperlinkActivated(object? sender, string address)
    {
        try
        {
            if (Uri.TryCreate(address, UriKind.Absolute, out var uri))
                await Launcher.Default.OpenAsync(uri);
        }
        catch (Exception)
        {
            // A link to something nothing on the device can open is the link's problem, not a crash.
        }
    }

    /// <summary>Opens the editor on the active cell.</summary>
    public void BeginEdit(string? initialText = null) => this.controller?.BeginEdit(initialText);

    /// <summary>
    /// Handles a key pressed while the grid has focus — Excel's shortcuts, the same table the Blazor host
    /// uses. MAUI has no portable key-down event, so a desktop host wires its platform's keys here.
    /// </summary>
    /// <param name="key">The key as a browser names it — <c>"ArrowDown"</c>, <c>"a"</c>, <c>"F2"</c>.</param>
    /// <returns>True when the key did something.</returns>
    public bool HandleKey(string key, SheetKeyModifiers modifiers = SheetKeyModifiers.None)
    {
        if (this.controller is not { } current)
            return false;

        if (key == "Escape" && (this.dialogs.IsOpen || this.menus.IsOpen))
        {
            this.dialogs.Close();
            this.menus.Close();
            this.AfterOverlayChanged();
            return true;
        }

        if (key == "Tab" && this.assist.AcceptFirst())
            return true;

        return current.HandleKey(key, modifiers);
    }

    public void Move(MoveDirection direction, bool extend = false, bool toEdge = false)
        => this.controller?.Move(direction, extend, toEdge);

    /// <summary>Scrolls the grid by a delta in layout units, clamped to the sheet's content.</summary>
    public void ScrollBy(double dx, double dy)
        => this.controller?.Scroll(dx, dy);

    public void ClearSelection() => this.controller?.ClearSelection();

    public void Undo() => this.controller?.Undo();

    public void Redo() => this.controller?.Redo();

    /// <summary>Moves to the next or previous visible sheet, stopping at either end.</summary>
    public void StepSheet(int offset) => this.controller?.StepSheet(offset);

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

    /// <summary>Walks the dash phase forward until the clipboard is abandoned.</summary>
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

    /// <summary>Stops the marching-ants clock when the view leaves the screen and resumes it on return.</summary>
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
        this.controller.DialogRequested -= this.OnDialogRequested;
        this.controller.MenuRequested -= this.OnMenuRequested;
        this.controller.HyperlinkActivated -= this.OnHyperlinkActivated;
        this.controller.ZoomChanged -= this.OnZoomChanged;
        this.controller.SelectionStatisticsChanged -= this.OnSelectionStatisticsChanged;
        this.dialogs.Close();
        this.menus.Close();
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

    /// <summary>A picture drawn behind the content — a logo, a DRAFT stamp, a company mark.</summary>
    /// <remarks>
    /// A <b>display</b> watermark: it is drawn, not written into the file. See <see cref="OfficeWatermark"/>.
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
