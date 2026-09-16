namespace Shiny.Maui.Controls.Office;

/// <summary>
/// The name box and formula field above a workbook grid.
/// </summary>
/// <remarks>
/// <para>
/// Two jobs, and the second is the one that matters: it shows the <em>formula</em> in the active cell
/// rather than the value the formula produced, which is the only way to see what a cell actually
/// contains. A grid without one can display 84 and give no way to discover that it holds
/// <c>=B1*2</c>.
/// </para>
/// <para>
/// It edits the same cell the grid does, through <see cref="SpreadsheetController"/>, so a commit here
/// is the same undoable command as typing into the cell — not a second path into the workbook.
/// </para>
/// </remarks>
public class FormulaBar : ContentView
{
    readonly Entry nameBox;
    readonly Entry field;
    readonly Border frame;

    SpreadsheetController? controller;
    bool suppress;

    /// <summary>
    /// The cell the text being typed belongs to, captured when editing started.
    /// </summary>
    /// <remarks>
    /// Not the active cell at commit time: tapping from this field into the grid moves the selection
    /// before this field gives up focus, so committing to whatever is active by then would write into
    /// the cell that was tapped rather than the one being edited.
    /// </remarks>
    CellRef? editingCell;

    public FormulaBar()
    {
        this.nameBox = new Entry
        {
            WidthRequest = 92,
            FontSize = 12,
            HorizontalTextAlignment = TextAlignment.Center,
            ReturnType = ReturnType.Go,
            Placeholder = "A1"
        };

        this.nameBox.Completed += this.OnAddressCompleted;
        this.nameBox.Unfocused += this.OnAddressCompleted;

        this.field = new Entry
        {
            FontSize = 12,
            FontFamily = "Monospace",
            ReturnType = ReturnType.Done,
            Placeholder = "fx"
        };

        this.field.Focused += this.OnFieldFocused;
        this.field.Completed += this.OnFieldCompleted;
        this.field.Unfocused += this.OnFieldUnfocused;

        var row = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)],
            ColumnSpacing = 6,
            Padding = new Thickness(6, 4)
        };

        row.Add(this.nameBox);
        row.Add(this.field, 1);

        this.frame = new Border { StrokeThickness = 0, Padding = 0, Content = row };
        this.Content = this.frame;

        // An unset Theme tracks the app's appearance, so a flip has to redraw.
        this.FollowAppTheme(static v => v.Refresh());
    }

    public static readonly BindableProperty ThemeProperty = BindableProperty.Create(
        nameof(Theme),
        typeof(SpreadsheetTheme),
        typeof(FormulaBar),
        null,
        propertyChanged: (b, _, _) => ((FormulaBar)b).Refresh());

    public static readonly BindableProperty IsReadOnlyProperty = BindableProperty.Create(
        nameof(IsReadOnly),
        typeof(bool),
        typeof(FormulaBar),
        false,
        propertyChanged: (b, _, _) => ((FormulaBar)b).Refresh());

    /// <summary>
    /// Chrome colours. Left unset the bar follows the app's light/dark appearance; setting it pins
    /// the choice. <see cref="SpreadsheetView"/> pushes its own value down here.
    /// </summary>
    public SpreadsheetTheme? Theme
    {
        get => (SpreadsheetTheme?)this.GetValue(ThemeProperty);
        set => this.SetValue(ThemeProperty, value);
    }

    SpreadsheetTheme EffectiveTheme => this.Theme ?? OfficeScheme.Default;

    /// <summary>Shows the content but refuses edits.</summary>
    public bool IsReadOnly
    {
        get => (bool)this.GetValue(IsReadOnlyProperty);
        set => this.SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>The grid this bar belongs to. Set by <see cref="SpreadsheetView"/>.</summary>
    public SpreadsheetController? Controller
    {
        get => this.controller;
        set
        {
            if (ReferenceEquals(this.controller, value))
                return;

            if (this.controller is not null)
                this.controller.Changed -= this.OnControllerChanged;

            this.controller = value;

            if (this.controller is not null)
                this.controller.Changed += this.OnControllerChanged;

            this.Refresh();
        }
    }

    /// <summary>Raised after a commit, so a host can repaint and track the dirty state.</summary>
    public event EventHandler? Changed;

    void OnControllerChanged(object? sender, EventArgs e)
    {
        // Not while the field is being typed in, or an arrow key in the grid would overwrite a
        // half-typed formula with the contents of whatever cell it landed on.
        if (this.editingCell is null)
            this.Refresh();
    }

    /// <summary>Redraws both boxes from the controller.</summary>
    public void Refresh()
    {
        var theme = this.EffectiveTheme;
        var ground = OfficeScheme.ToMauiColor(theme.Background);
        this.frame.BackgroundColor = ground;

        // The boxes take their ink from the same theme as the ground under them. Left to the platform
        // (or an app's implicit Entry style) the text followed the *app's* appearance while the ground
        // followed this theme, so a pinned dark theme in a light app drew black text on a near-black bar
        // and the reverse drew white on white.
        foreach (var box in (Entry[])[this.nameBox, this.field])
        {
            box.BackgroundColor = ground;
            box.TextColor = OfficeScheme.ToMauiColor(theme.CellText);
            box.PlaceholderColor = OfficeScheme.ToMauiColor(theme.HeaderText);
        }

        this.IsVisible = this.controller is not null;
        this.field.IsReadOnly = this.IsReadOnly;

        // Writing Text raises TextChanged, and the handlers below must not read that as the user
        // typing something.
        this.suppress = true;
        this.nameBox.Text = this.controller?.ActiveCellAddress ?? string.Empty;
        this.field.Text = this.controller?.ActiveCellText ?? string.Empty;
        this.suppress = false;
    }

    /// <summary>
    /// Ends any edit in progress, committing what was typed.
    /// </summary>
    /// <remarks>
    /// The grid calls this when it is touched. Nothing else would: a canvas is not focusable, so on
    /// iOS the field keeps first responder when a cell is tapped and no <c>Unfocused</c> ever arrives.
    /// The bar then sits on <see cref="editingCell"/> forever, and because that is exactly the flag
    /// that stops a controller change from overwriting a half-typed formula, every later selection is
    /// ignored - the grid moves, the address and the contents do not. It reads as a formula bar that
    /// has simply stopped working, with nothing to say why.
    /// </remarks>
    public void EndEditing()
    {
        if (this.controller is null)
            return;

        // Unfocus raises Unfocused, which commits; the call below is for a platform that does not
        // raise it, and is a no-op once editingCell has been cleared.
        if (this.field.IsFocused)
            this.field.Unfocus();

        if (this.nameBox.IsFocused)
            this.nameBox.Unfocus();

        this.Commit(advance: false);
    }

    void OnFieldFocused(object? sender, FocusEventArgs e)
        => this.editingCell = this.controller?.Selection.Active;

    void OnFieldCompleted(object? sender, EventArgs e) => this.Commit(advance: true);

    void OnFieldUnfocused(object? sender, FocusEventArgs e) => this.Commit(advance: false);

    void Commit(bool advance)
    {
        if (this.suppress || this.controller is not { } current || this.editingCell is not { } cell)
            return;

        this.editingCell = null;

        var text = this.field.Text ?? string.Empty;
        if (this.IsReadOnly || text == current.CellText(cell))
        {
            this.Refresh();
            return;
        }

        current.SetCellText(cell, text);

        // Enter moves down, the way it does after an in-cell edit - but only when the cell that was
        // edited is still the one selected. After a tap into the grid it is not, and moving on from
        // there would be a jump nobody asked for.
        if (advance && current.Selection.Active.Relative() == cell.Relative())
            current.Advance(byRow: true);

        this.Refresh();
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    void OnAddressCompleted(object? sender, EventArgs e)
    {
        if (this.suppress || this.controller is not { } current)
            return;

        if (CellRef.TryParse(this.nameBox.Text?.Trim() ?? string.Empty, out var cell))
            current.GoTo(cell);

        // Redraw either way: a name that did not parse has to snap back to the real address rather
        // than sitting there looking like it was accepted.
        this.Refresh();
    }

    /// <summary>Detaches from the controller. Called by the hosting view when it is disposed.</summary>
    public void Detach() => this.Controller = null;
}
