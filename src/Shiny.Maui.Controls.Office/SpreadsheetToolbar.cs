using Shiny.Controls.Office.Icons;
using Shiny.Controls.Office.Spreadsheet.Calc;
using Shiny.Maui.Controls.ColorPicker;
using Shiny.Maui.Controls.FontPicker;
using Shiny.Maui.Controls.Ribbons;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// The formatting bar above a <see cref="SpreadsheetView"/>.
/// </summary>
/// <remarks>
/// <para>
/// Two tabs. <b>Home</b> is what any editor has — clipboard, font, alignment, number formats and the
/// editing commands — and <b>Data</b> is what only a spreadsheet has: rows and columns in and out,
/// column widths and visibility, and the function library. The second tab is the reason this is a
/// control rather than a copy of the document toolbar.
/// </para>
/// <para>
/// The split is by what a command changes, not by how often it is reached: Home changes how a cell
/// looks, Data changes the shape of the sheet under it. With one tab the structural half could never
/// grow past the two commands that fitted between clear-formatting and a colour picker.
/// </para>
/// <para>
/// Everything it does goes through <see cref="SpreadsheetController"/>, so a button here is the same
/// undoable command a keyboard shortcut would raise — not a second path into the workbook. It also
/// means the bar can be used on its own: set <see cref="Controller"/> from
/// <see cref="SpreadsheetView.Controller"/> and put it wherever the app's chrome belongs.
/// </para>
/// <para>
/// The icons come from the shared Office set, so this bar, the document bar and the slide bar draw
/// the same mark for the same command on both hosts.
/// </para>
/// </remarks>
public class SpreadsheetToolbar : ContentView
{
    readonly Ribbon ribbon;

    readonly RibbonToggleButton bold;
    readonly RibbonToggleButton italic;
    readonly RibbonToggleButton underline;
    readonly RibbonToggleButton strike;
    readonly RibbonToggleButton alignLeft;
    readonly RibbonToggleButton alignCenter;
    readonly RibbonToggleButton alignRight;
    readonly RibbonToggleButton alignTop;
    readonly RibbonToggleButton alignMiddle;
    readonly RibbonToggleButton alignBottom;
    readonly RibbonToggleButton wrap;
    readonly RibbonToggleButton currency;
    readonly RibbonToggleButton percent;
    readonly RibbonButton decimalDecrease;
    readonly RibbonButton decimalIncrease;
    readonly RibbonMenuButton numberFormats;
    readonly RibbonButton watermark;
    readonly RibbonSplitButton sum;
    readonly RibbonButton paste;
    readonly RibbonButton cut;
    readonly RibbonButton copy;
    readonly RibbonButton outdent;
    readonly RibbonButton indent;
    readonly RibbonButton insertRow;
    readonly RibbonButton insertColumn;
    readonly RibbonButton deleteRow;
    readonly RibbonButton deleteColumn;
    readonly RibbonSplitButton columnWidth;
    readonly RibbonButton hideColumns;
    readonly RibbonButton unhideColumns;
    readonly RibbonButton clearContents;
    readonly RibbonButton clearFormat;
    readonly OfficeFindBar findBar = new();
    readonly RibbonButton undo;
    readonly RibbonButton redo;

    // The Data tab's function library. Sum is a second button rather than the Home tab's split one:
    // an item model is rendered into a view per group, and one instance cannot be in two places.
    readonly RibbonButton[] functions;

    readonly OfficeToolbarButton fill;
    readonly ColorPickerButton textColor;

    FontPickerButton? fontPicker;
    FontSizePickerButton? sizePicker;
    SpreadsheetController? controller;
    bool suppressPickerEvents;

    public SpreadsheetToolbar()
    {
        this.bold = this.Toggle(OfficeIcon.Bold, "Bold", c => c.ToggleBold());
        this.italic = this.Toggle(OfficeIcon.Italic, "Italic", c => c.ToggleItalic());
        this.underline = this.Toggle(OfficeIcon.Underline, "Underline", c => c.ToggleUnderline());
        this.strike = this.Toggle(OfficeIcon.Strikethrough, "Strikethrough", c => c.ToggleStrikethrough());

        this.alignLeft = this.Toggle(OfficeIcon.AlignLeft, "Align left", c => c.SetAlignment(CellHorizontalAlignment.Left));
        this.alignCenter = this.Toggle(OfficeIcon.AlignCenter, "Centre", c => c.SetAlignment(CellHorizontalAlignment.Center));
        this.alignRight = this.Toggle(OfficeIcon.AlignRight, "Align right", c => c.SetAlignment(CellHorizontalAlignment.Right));

        this.alignTop = this.Toggle(OfficeIcon.AlignTop, "Align top", c => c.SetVerticalAlignment(CellVerticalAlignment.Top));
        this.alignMiddle = this.Toggle(OfficeIcon.AlignMiddle, "Align middle", c => c.SetVerticalAlignment(CellVerticalAlignment.Center));
        this.alignBottom = this.Toggle(OfficeIcon.AlignBottom, "Align bottom", c => c.SetVerticalAlignment(CellVerticalAlignment.Bottom));

        this.wrap = this.Toggle(OfficeIcon.WrapText, "Wrap text", c => c.ToggleWrapText());

        // Indent is a cell format like the alignments it sits beside, not a structural edit - which is
        // why it is here rather than on the Data tab with the row and column commands.
        this.outdent = this.Command(OfficeIcon.Outdent, "Decrease indent", c => c.AdjustIndent(-1));
        this.indent = this.Command(OfficeIcon.Indent, "Increase indent", c => c.AdjustIndent(1));

        this.currency = this.Toggle(OfficeIcon.Currency, "Currency", c => c.SetNumberFormat(NumberFormatPreset.Currency));
        this.percent = this.Toggle(OfficeIcon.Percent, "Percent", c => c.SetNumberFormat(NumberFormatPreset.Percent));
        this.decimalDecrease = this.Command(OfficeIcon.DecimalDecrease, "Fewer decimal places", c => c.AdjustDecimals(-1));
        this.decimalIncrease = this.Command(OfficeIcon.DecimalIncrease, "More decimal places", c => c.AdjustDecimals(1));

        // Was a chevron button that opened a native action sheet. A ribbon menu button is the same
        // list in the bar's own idiom, and it can show each preset's live sample beside its name -
        // which an action sheet had no room for.
        this.numberFormats = new RibbonMenuButton
        {
            Text = "Formats",
            Tooltip = "More number formats",
            Size = RibbonItemSize.Small,
            AutomationId = "SheetToolbarNumberFormats"

            // No icon, deliberately: the only mark that fits is the currency one already sitting two
            // buttons to the left in the same group, and the same glyph twice reads as a duplicated
            // command. The label and the chevron say what it is.
        };

        // AutoSum stays a split button: totalling a column is the common case by a wide margin, and
        // making it a menu choice would put a click in front of it every time.
        this.sum = new RibbonSplitButton
        {
            Text = "AutoSum",
            Tooltip = "AutoSum",
            Size = RibbonItemSize.Small,
            AutomationId = "SheetToolbarAutoSum",
            IconTemplate = OfficeRibbonItems.IconTemplateFor(OfficeIcon.Sum),
            Command = new Command(() => this.RunCommand(c => c.ApplyAutoFunction(AutoFunction.Sum)))
        };

        this.paste = this.Command(OfficeIcon.Paste, "Paste", c => c.Paste(), "Paste");
        this.cut = this.Command(OfficeIcon.Cut, "Cut", c => c.Cut());
        this.copy = this.Command(OfficeIcon.Copy, "Copy", c => c.Copy());

        this.insertRow = this.Command(OfficeIcon.InsertRow, "Insert row above", c => c.InsertRows());
        this.insertColumn = this.Command(OfficeIcon.InsertColumn, "Insert column left", c => c.InsertColumns());
        this.deleteRow = this.Command(OfficeIcon.DeleteRow, "Delete rows", c => c.DeleteRows());
        this.deleteColumn = this.Command(OfficeIcon.DeleteColumn, "Delete columns", c => c.DeleteColumns());

        this.hideColumns = this.Command(OfficeIcon.Hide, "Hide columns", c => c.SetColumnsHidden(true));
        this.unhideColumns = this.Command(OfficeIcon.Unhide, "Unhide columns", c => c.SetColumnsHidden(false));

        // Fitting to contents is the common case, so it stays the face; the presets behind the chevron
        // are the only way back to a chosen width, which a fit cannot give you.
        this.columnWidth = new RibbonSplitButton
        {
            Text = "Width",
            Tooltip = "Fit columns to contents",
            Size = RibbonItemSize.Small,
            AutomationId = "SheetToolbarColumnWidth",
            IconTemplate = OfficeRibbonItems.IconTemplateFor(OfficeIcon.ColumnWidth),
            Command = new Command(() => this.RunCommand(c => c.AutoFitColumns()))
        };

        this.clearContents = this.Command(OfficeIcon.Delete, "Clear contents", c => c.ClearSelection());
        this.clearFormat = this.Command(OfficeIcon.ClearFormat, "Clear formatting", c => c.ClearFormatting());

        // Labelled, unlike the rest of the bar: five aggregates told apart by icon alone would be five
        // guesses, and the group has the room a Home-tab group does not.
        this.functions = SpreadsheetMenus.Functions
            .Select(function => this.Command(
                IconOf(function),
                AutoFunctions.DisplayName(function),
                c => c.ApplyAutoFunction(function),

                // Labelled with the formula name rather than the friendly one: the button writes
                // =AVERAGE(...) into a cell, and that is the thing worth naming.
                AutoFunctions.NameOf(function)))
            .ToArray();

        this.undo = this.Command(OfficeIcon.Undo, "Undo", c => c.Undo());
        this.redo = this.Command(OfficeIcon.Redo, "Redo", c => c.Redo());

        // The fill button keeps its own popup - it is a colour surface, which a ribbon button cannot be.
        this.fill = new OfficeToolbarButton(OfficeIcon.FillColor, "Fill colour");
        this.fill.Clicked += async (_, _) => await this.PickFillAsync();

        this.textColor = this.CreateColorPicker();

        // Not through the Command helper: that runs against the controller, and a watermark is drawn
        // by the view rather than stored in the workbook.
        this.watermark = OfficeRibbonItems.Command(
            OfficeIcon.Watermark,
            "Watermark",
            () => _ = this.PickWatermarkAsync(),
            automationId: "SheetToolbarWatermark");

        this.ribbon = new Ribbon
        {
            // Two rows rather than three: this is a bar above a grid, and the groups divide evenly.
            SmallItemRows = 2,

            // These bars mix 32px pickers with icon buttons, and every group sizes its own rows - so
            // without one height the groups stop lining up with one another and the titles under them
            // land on different baselines.
            SmallItemRowHeight = 32,
            AllowGroupCollapse = true,

            // Below this the bar runs dense instead of folding its groups away. At phone width there
            // is room for no group at all, so collapsing put every command behind a dropdown - worse
            // than the scrolling strip this replaced.
            SimplifyBelowWidth = 600
        };

        // Explicitly, because a BindableProperty's propertyChanged does not fire for its default -
        // so the accent every one of these ships with would never have been applied at all.
        this.ApplyAccent();

        this.Content = this.ribbon;
        this.BuildBar();

        // An unset Theme tracks the app's appearance, so a flip has to redraw.
        this.FollowAppTheme(static v => v.Refresh());
    }

    public static readonly BindableProperty ThemeProperty = BindableProperty.Create(
        nameof(Theme),
        typeof(SpreadsheetTheme),
        typeof(SpreadsheetToolbar),
        null,
        propertyChanged: (b, _, _) => ((SpreadsheetToolbar)b).Refresh());

    public static readonly BindableProperty IsReadOnlyProperty = BindableProperty.Create(
        nameof(IsReadOnly),
        typeof(bool),
        typeof(SpreadsheetToolbar),
        false,
        propertyChanged: (b, _, _) => ((SpreadsheetToolbar)b).Refresh());

    public static readonly BindableProperty FontFamiliesProperty = BindableProperty.Create(
        nameof(FontFamilies),
        typeof(IList<string>),
        typeof(SpreadsheetToolbar),
        propertyChanged: (b, _, _) => ((SpreadsheetToolbar)b).BuildBar());

    public static readonly BindableProperty FontSizesProperty = BindableProperty.Create(
        nameof(FontSizes),
        typeof(IList<double>),
        typeof(SpreadsheetToolbar),
        propertyChanged: (b, _, _) => ((SpreadsheetToolbar)b).BuildBar());

    public static readonly BindableProperty ShowTooltipsProperty = BindableProperty.Create(
        nameof(ShowTooltips),
        typeof(bool),
        typeof(SpreadsheetToolbar),
        OfficeToolbarButton.TooltipsByDefault,
        propertyChanged: (b, _, _) => ((SpreadsheetToolbar)b).Refresh());

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

    /// <summary>Shows the current formatting but refuses to change it.</summary>
    public bool IsReadOnly
    {
        get => (bool)this.GetValue(IsReadOnlyProperty);
        set => this.SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>Font families offered by the picker. Defaults to the set Excel ships with.</summary>
    public IList<string>? FontFamilies
    {
        get => (IList<string>?)this.GetValue(FontFamiliesProperty);
        set => this.SetValue(FontFamiliesProperty, value);
    }

    public IList<double>? FontSizes
    {
        get => (IList<double>?)this.GetValue(FontSizesProperty);
        set => this.SetValue(FontSizesProperty, value);
    }

    /// <summary>
    /// Whether the buttons show a hover tooltip. On everywhere but phones by default.
    /// </summary>
    /// <remarks>
    /// The buttons are icon-only, so the tooltip is the only place the command is named. It is off on
    /// iOS and Android because a hover tooltip has nothing to open it there, not because the label
    /// matters less — which is why the accessible description is set regardless.
    /// </remarks>
    public bool ShowTooltips
    {
        get => (bool)this.GetValue(ShowTooltipsProperty);
        set => this.SetValue(ShowTooltipsProperty, value);
    }

    /// <summary>The grid this bar drives. Set by <see cref="SpreadsheetView"/>.</summary>
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

            // A new workbook is a new controller and therefore a new finder; a bar left holding the
            // old one would count matches in a workbook that is no longer on screen.
            this.findBar.Find = this.controller?.Find;

            this.Refresh();
        }
    }

    /// <summary>Raised after a command runs, so a host can repaint and track the dirty state.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Raised when the watermark button picks a picture, or clears the one already set.
    /// </summary>
    /// <remarks>
    /// An event rather than a property the toolbar owns: a watermark is drawn by the grid, and the bar
    /// has no grid - it has a controller, which is the workbook rather than the view of it.
    /// </remarks>
    public event EventHandler<OfficeWatermark?>? WatermarkPicked;

    /// <summary>Whether a mark is currently set, so the button can offer to take it off.</summary>
    public bool HasWatermark { get; set; }

    /// <summary>Extra views appended after the built-in controls.</summary>
    /// <remarks>
    /// A list rather than a template because the bar is rebuilt whenever the font lists change, and
    /// the extras have to survive that. Assign it before the toolbar is first laid out.
    /// </remarks>
    public IList<View> ToolbarItems { get; } = new List<View>();

    /// <summary>Group title for <see cref="ToolbarItems"/>.</summary>
    public static readonly BindableProperty ToolbarItemsTitleProperty = BindableProperty.Create(
        nameof(ToolbarItemsTitle), typeof(string), typeof(SpreadsheetToolbar), "Actions",
        propertyChanged: (b, _, _) => ((SpreadsheetToolbar)b).BuildBar());

    /// <inheritdoc cref="ToolbarItemsTitleProperty" />
    public string ToolbarItemsTitle
    {
        get => (string)this.GetValue(ToolbarItemsTitleProperty);
        set => this.SetValue(ToolbarItemsTitleProperty, value);
    }

    /// <summary>The preset the active cell's number format matches, if any.</summary>
    NumberFormatPreset? ActivePreset
        => NumberFormats.PresetOf((this.controller?.ActiveFormat ?? ResolvedFormat.Default).NumberFormatCode);

    /// <summary>
    /// What a preset does to a number, so the menu shows the format rather than naming it.
    /// </summary>
    /// <remarks>
    /// Formatted through the same resolver the grid paints with, so the sample is the truth rather
    /// than a hard-coded string that drifts from what the line actually applies.
    /// </remarks>
    string SampleOf(NumberFormatPreset preset)
    {
        if (this.controller?.Workbook.Styles is not { } styles)
            return string.Empty;

        var value = preset switch
        {
            NumberFormatPreset.Percent => 0.256,
            NumberFormatPreset.ShortDate or NumberFormatPreset.Time => ExcelDate.FromDateTime(DateTime.Now),
            _ => 1234.5
        };

        var format = ResolvedFormat.Default with { NumberFormatCode = NumberFormats.CodeOf(preset) };
        return styles.Format(CellValue.FromNumber(value), format);
    }

    /// <summary>
    /// Detaches from the controller. Called by the hosting view when it is disposed.
    /// </summary>
    /// <remarks>
    /// Setting <see cref="Controller"/> to null is what drops both subscriptions — this bar's, and the
    /// find bar's to the finder, which outlives the view since it belongs to the workbook.
    /// </remarks>
    public void Detach() => this.Controller = null;

    void BuildBar()
    {
        this.ribbon.Tabs.Clear();

        this.fontPicker = this.CreateFontPicker();
        this.sizePicker = this.CreateSizePicker();

        // Undo and redo apply whatever the selection is, so they sit outside the groups where they
        // never move or disappear.
        this.ribbon.QuickAccessItems.Clear();
        this.ribbon.QuickAccessItems.Add(this.undo);
        this.ribbon.QuickAccessItems.Add(this.redo);

        var home = new RibbonTab { Title = "Home", Key = "home" };

        // Clipboard leads, as it does in Excel: cut/copy/paste apply to whatever is selected and are
        // reached far more often than any formatting command.
        // Paste is large and the other two stack beside it - Excel's own arrangement, and the shape
        // three items want: filling two-deep columns left copy alone in a second column with a hole
        // under it.
        var clipboard = new RibbonGroup { Title = "Clipboard", Priority = 110 };
        this.paste.Size = RibbonItemSize.Large;
        this.paste.Text = "Paste";
        clipboard.Items.Add(this.paste);
        clipboard.Items.Add(this.cut);
        clipboard.Items.Add(this.copy);
        home.Groups.Add(clipboard);

        // Rows, which is how Excel's Font group is arranged: the two boxes on top, the run of marks
        // underneath. Filling columns instead put bold above italic and underline above strikethrough,
        // and forced the font picker to share a column with a toggle - where the column took the
        // picker's width and stretched the 16px B across all of it.
        var font = new RibbonGroup { Title = "Font", Priority = 100 };
        font.Items.Add(OfficeRibbonItems.Row(
            OfficeRibbonItems.Host(this.fontPicker),
            OfficeRibbonItems.Host(this.sizePicker)
        ));
        font.Items.Add(OfficeRibbonItems.Row(
            this.bold,
            this.italic,
            this.underline,
            this.strike,
            new RibbonSeparator(),
            OfficeRibbonItems.Host(this.textColor),
            OfficeRibbonItems.Host(this.fill)
        ));
        home.Groups.Add(font);

        // Excel's own two rows: how the text sits in the cell top-to-bottom on one, left-to-right on
        // the other, with the things that nudge it inside that box at the end of each. Filling columns
        // turned three runs of three into a 2xN grid that read align-left / align-right down the first
        // column, and put the wrap toggle under an indent arrow.
        var alignment = new RibbonGroup { Title = "Alignment", Priority = 90 };
        alignment.Items.Add(OfficeRibbonItems.Row(
            this.alignTop,
            this.alignMiddle,
            this.alignBottom,

            // Wrapping is about the height the text takes in the cell, which is what this row is about.
            new RibbonSeparator(),
            this.wrap
        ));
        alignment.Items.Add(OfficeRibbonItems.Row(
            this.alignLeft,
            this.alignCenter,
            this.alignRight,

            // The indent pair moves text inside the cell it is already aligned in, which is a third
            // thing again - and the two arrows are close enough to the alignment marks to need a break.
            new RibbonSeparator(),
            this.outdent,
            this.indent
        ));
        home.Groups.Add(alignment);

        // The named formats on top and the four marks that nudge one underneath - Excel's arrangement.
        // Filling columns put the format menu in a column of its own with a hole beneath it, and split
        // the decimal pair across two columns.
        var number = new RibbonGroup { Title = "Number", Priority = 80 };
        number.Items.Add(OfficeRibbonItems.Row(this.numberFormats));
        number.Items.Add(OfficeRibbonItems.Row(
            this.currency,
            this.percent,
            this.decimalDecrease,
            this.decimalIncrease
        ));
        home.Groups.Add(number);

        // AutoSum is on both tabs, as it is in Excel - the face of Home's Editing group and the head of
        // the Data tab's function library. It is the one command here reached often enough that a tab
        // switch in front of it would be felt, and large enough to be the group's head: two icon-only
        // rows of three left a hole where the third would be.
        var editing = new RibbonGroup { Title = "Editing", Priority = 70 };
        this.sum.Size = RibbonItemSize.Large;
        this.sum.Text = "AutoSum";
        editing.Items.Add(this.sum);
        editing.Items.Add(this.clearContents);
        editing.Items.Add(this.clearFormat);
        home.Groups.Add(editing);

        // Its own group rather than a fourth item in Editing: finding changes nothing, and the box is
        // as wide as the three buttons beside it put together. Last on Home, which is what decides the
        // order groups fold into the overflow in on a narrow window. It spans the rows: on a single row
        // it left the row underneath empty for the width of a search box.
        var finding = new RibbonGroup { Title = "Find", Priority = 65 };
        finding.Items.Add(OfficeRibbonItems.HostLarge(this.findBar));
        home.Groups.Add(finding);

        if (this.ToolbarItems.Count > 0)
        {
            // Never folds into an overflow button: whatever the host added is theirs, and it is not
            // for this control to decide it is the least important thing on the bar. It goes on Home
            // rather than a tab of its own, so a host's commands are on the tab that opens.
            var extras = new RibbonGroup { Title = this.ToolbarItemsTitle, Priority = 200, CanCollapse = false };
            foreach (var item in this.ToolbarItems)
                extras.Items.Add(OfficeRibbonItems.Host(item));

            home.Groups.Add(extras);
        }

        // ---- Data ----
        //
        // Everything that changes the shape of the sheet rather than the look of a cell. All of it was
        // on Home, where insert-row sat between clear-formatting and a colour picker; with one tab the
        // structural commands could never grow past the two that fitted.
        var data = new RibbonTab { Title = "Data", Key = "data" };

        // Insert on one row and delete on the other, rows and columns in the same order on both - so
        // the pair a command belongs to is its line rather than a rule you have to notice.
        var cells = new RibbonGroup { Title = "Cells", Priority = 110 };
        cells.Items.Add(OfficeRibbonItems.Row(this.insertRow, this.insertColumn));
        cells.Items.Add(OfficeRibbonItems.Row(this.deleteRow, this.deleteColumn));
        data.Groups.Add(cells);

        // A sheet has no page setup to put this beside, so it goes on the Data tab with the other
        // things that are about the sheet rather than about a cell.
        // One command, so it is drawn the way a single command should be: large, with its name on it.
        // A lone 16px glyph under a caption reading "Sheet" said nothing.
        var sheet = new RibbonGroup { Title = "Sheet", Priority = 105 };
        this.watermark.Size = RibbonItemSize.Large;
        this.watermark.Text = "Watermark";
        sheet.Items.Add(this.watermark);
        data.Groups.Add(sheet);

        // Width is the head - fitting to contents is the common case - and hide/unhide stack beside it,
        // which is the shape three items want.
        var columns = new RibbonGroup { Title = "Columns", Priority = 100 };
        this.columnWidth.Size = RibbonItemSize.Large;
        this.columnWidth.Text = "Width";
        columns.Items.Add(this.columnWidth);
        columns.Items.Add(this.hideColumns);
        columns.Items.Add(this.unhideColumns);
        data.Groups.Add(columns);

        var library = new RibbonGroup { Title = "Functions", Priority = 90 };
        foreach (var function in this.functions)
            library.Items.Add(function);

        data.Groups.Add(library);

        this.ribbon.Tabs.Add(home);
        this.ribbon.Tabs.Add(data);
        this.RebuildMenus();
        this.Refresh();
    }

    /// <summary>Fills the two dropdowns. Rebuilt on refresh so the ticks track the active cell.</summary>
    void RebuildMenus()
    {
        this.numberFormats.Menu.Clear();
        foreach (var preset in SpreadsheetMenus.Formats)
        {
            var captured = preset;
            this.numberFormats.Menu.Add(new RibbonMenuEntry
            {
                // Name and live sample on one line, formatted through the same resolver the grid
                // paints with - so the sample is the truth rather than a string that drifts.
                Text = $"{NumberFormats.DisplayName(captured)}   {this.SampleOf(captured)}",
                IsChecked = this.ActivePreset == captured,
                Command = new Command(() => this.RunCommand(c => c.SetNumberFormat(captured)))
            });
        }

        this.sum.Menu.Clear();
        foreach (var function in SpreadsheetMenus.Functions)
        {
            var captured = function;
            this.sum.Menu.Add(new RibbonMenuEntry
            {
                Text = $"{AutoFunctions.DisplayName(captured)}   {AutoFunctions.NameOf(captured)}",
                Command = new Command(() => this.RunCommand(c => c.ApplyAutoFunction(captured)))
            });
        }

        this.columnWidth.Menu.Clear();
        foreach (var (name, characters) in ColumnWidthPresets.All)
        {
            var width = ColumnWidthPresets.PixelsOf(characters);
            this.columnWidth.Menu.Add(new RibbonMenuEntry
            {
                // The pixel width beside the name, for the same reason the format menu carries a live
                // sample: "Wide" on its own is a promise the reader cannot check.
                Text = $"{name}   {width:0} px",
                Command = new Command(() => this.RunCommand(c => c.SetColumnWidth(width)))
            });
        }
    }


    /// <summary>The mark for an aggregate. Sum's sigma is shared with the Home tab's split button.</summary>
    static OfficeIcon IconOf(AutoFunction function) => function switch
    {
        AutoFunction.Average => OfficeIcon.Average,
        AutoFunction.Count => OfficeIcon.Count,
        AutoFunction.Min => OfficeIcon.Min,
        AutoFunction.Max => OfficeIcon.Max,
        _ => OfficeIcon.Sum
    };


    // Named off the icon: the ribbon item models are not in the visual tree, so the rendered view
    // carrying this id is the only handle a UI test has on a command.
    RibbonToggleButton Toggle(OfficeIcon icon, string hint, Action<SpreadsheetController> action)
        => OfficeRibbonItems.Toggle(icon, hint, () => this.RunCommand(action), $"SheetToolbar{icon}");

    RibbonButton Command(OfficeIcon icon, string hint, Action<SpreadsheetController> action, string? text = null)
        => OfficeRibbonItems.Command(icon, hint, () => this.RunCommand(action), text, $"SheetToolbar{icon}");

    void RunCommand(Action<SpreadsheetController> action)
    {
        if (this.controller is not { } current || this.IsReadOnly)
            return;

        action(current);
        this.AfterCommand();
    }


    ColorPickerButton CreateColorPicker()
    {
        var picker = new ColorPickerButton
        {
            Text = string.Empty,
            ShowOpacity = false,
            WidthRequest = 44,
            HeightRequest = OfficeToolbarButton.ItemHeight,
            VerticalOptions = LayoutOptions.Center
        };

        picker.ColorChanged += (_, color) =>
        {
            if (this.suppressPickerEvents || this.controller is not { } current || this.IsReadOnly)
                return;

            current.SetTextColor(ToArgb(color));
            this.AfterCommand();
        };

        return picker;
    }

    FontPickerButton CreateFontPicker()
    {
        var picker = new FontPickerButton
        {
            AvailableFonts = (this.FontFamilies ?? DefaultFontFamilies).ToList(),
            Placeholder = "Font",
            WidthRequest = 150,
            HeightRequest = OfficeToolbarButton.ItemHeight,
            VerticalOptions = LayoutOptions.Center
        };

        picker.FontChanged += (_, family) =>
        {
            if (this.suppressPickerEvents || this.controller is not { } current || this.IsReadOnly)
                return;

            current.SetFontFamily(family);
            this.AfterCommand();
        };

        return picker;
    }

    FontSizePickerButton CreateSizePicker()
    {
        var picker = new FontSizePickerButton
        {
            AvailableFontSizes = (this.FontSizes ?? DefaultFontSizes).ToList(),
            WidthRequest = 84,
            HeightRequest = OfficeToolbarButton.ItemHeight,
            VerticalOptions = LayoutOptions.Center
        };

        picker.FontSizeChanged += (_, size) =>
        {
            if (this.suppressPickerEvents || this.controller is not { } current || this.IsReadOnly)
                return;

            current.SetFontSize(size);
            this.AfterCommand();
        };

        return picker;
    }

    async Task PickFillAsync()
    {
        if (this.controller is not { } current || this.IsReadOnly)
            return;

        // The same gallery the document toolbar's highlight button opens, and for the same reason: a
        // cell fill wants a few readable colours plus a way to remove it, not a colour spectrum.
        var (chosen, color) = await OfficeMenus.PickHighlightAsync(OfficeMenus.PageOf(this));
        if (!chosen)
            return;

        current.SetFillColor(color);
        this.AfterCommand();
    }



    /// <summary>Every ribbon item that is only usable with a live, writable controller.</summary>
    IEnumerable<RibbonItem> FormattingItems()
    {
        yield return this.bold;
        yield return this.italic;
        yield return this.underline;
        yield return this.strike;
        yield return this.alignLeft;
        yield return this.alignCenter;
        yield return this.alignRight;
        yield return this.alignTop;
        yield return this.alignMiddle;
        yield return this.alignBottom;
        yield return this.wrap;
        yield return this.currency;
        yield return this.percent;
        yield return this.decimalDecrease;
        yield return this.decimalIncrease;
        yield return this.numberFormats;
        yield return this.sum;
        yield return this.cut;
        yield return this.copy;
        yield return this.outdent;
        yield return this.indent;
        yield return this.insertRow;
        yield return this.insertColumn;
        yield return this.deleteRow;
        yield return this.deleteColumn;
        yield return this.columnWidth;
        yield return this.hideColumns;
        yield return this.unhideColumns;
        yield return this.clearContents;
        yield return this.clearFormat;

        foreach (var function in this.functions)
            yield return function;
    }

    void OnControllerChanged(object? sender, EventArgs e) => this.Refresh();

    void AfterCommand()
    {
        this.Refresh();
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reflects the active cell's formatting back into the bar.</summary>
    void Refresh()
    {
        var format = this.controller?.ActiveFormat ?? ResolvedFormat.Default;
        var enabled = this.controller is not null && !this.IsReadOnly;

        this.IsVisible = this.controller is not null;

        this.bold.IsChecked = format.Bold;
        this.italic.IsChecked = format.Italic;
        this.underline.IsChecked = format.Underline;
        this.strike.IsChecked = format.Strike;
        this.wrap.IsChecked = format.WrapText;

        this.alignLeft.IsChecked = format.HorizontalAlignment == CellHorizontalAlignment.Left;
        this.alignCenter.IsChecked = format.HorizontalAlignment == CellHorizontalAlignment.Center;
        this.alignRight.IsChecked = format.HorizontalAlignment == CellHorizontalAlignment.Right;

        this.alignTop.IsChecked = format.VerticalAlignment == CellVerticalAlignment.Top;
        this.alignMiddle.IsChecked = format.VerticalAlignment == CellVerticalAlignment.Center;
        this.alignBottom.IsChecked = format.VerticalAlignment == CellVerticalAlignment.Bottom;

        var preset = NumberFormats.PresetOf(format.NumberFormatCode);
        this.currency.IsChecked = preset == NumberFormatPreset.Currency;
        this.percent.IsChecked = preset == NumberFormatPreset.Percent;

        this.fill.IsActive = !format.Background.IsTransparent;
        this.fill.SetEnabled(enabled);
        this.fill.SetTooltipEnabled(this.ShowTooltips);

        foreach (var item in this.FormattingItems())
            item.IsEnabled = enabled;

        // Paste is the one clipboard command with a precondition of its own: there has to be
        // something held. Cut and copy only need a selection, which there always is.
        this.paste.IsEnabled = enabled && (this.controller?.CanPaste ?? false);

        this.undo.IsEnabled = enabled && (this.controller?.CanUndo ?? false);
        this.redo.IsEnabled = enabled && (this.controller?.CanRedo ?? false);

        // The ticks in the formats menu track the active cell, so they are rebuilt with it.
        this.RebuildMenus();

        // Writing a picker's selection raises its change event, which would immediately re-apply the
        // format that was only being displayed.
        this.suppressPickerEvents = true;

        if (this.fontPicker is not null)
            this.fontPicker.SelectedFont = format.FontName;

        if (this.sizePicker is not null)
        {
            // Snap to the nearest offered size: a cell can hold any value, the picker only some.
            var sizes = this.FontSizes ?? DefaultFontSizes;
            this.sizePicker.SelectedFontSize = sizes.OrderBy(x => Math.Abs(x - format.FontSize)).FirstOrDefault();
        }

        this.textColor.SelectedColor = Color.FromRgba(format.Foreground.R, format.Foreground.G, format.Foreground.B, format.Foreground.A);
        this.textColor.IsEnabled = enabled;

        if (this.fontPicker is not null)
            this.fontPicker.IsEnabled = enabled;

        if (this.sizePicker is not null)
            this.sizePicker.IsEnabled = enabled;

        this.suppressPickerEvents = false;

        // Finding works in a read-only workbook - it changes nothing - so it follows whether one is
        // open rather than whether it can be edited.
        this.findBar.IsEnabled = this.controller is not null;
        this.findBar.SetTooltipsEnabled(this.ShowTooltips);
    }

    /// <summary>MAUI colours are floats in 0..1; the spreadsheet kernel stores bytes.</summary>
    static ArgbColor ToArgb(Color color) => new(
        (byte)Math.Round(color.Alpha * 255),
        (byte)Math.Round(color.Red * 255),
        (byte)Math.Round(color.Green * 255),
        (byte)Math.Round(color.Blue * 255));


    /// <summary>Excel's own default plus the faces most workbooks actually use.</summary>
    static readonly IList<string> DefaultFontFamilies =
        ["Calibri", "Cambria", "Arial", "Times New Roman", "Georgia", "Verdana", "Courier New"];

    static readonly IList<double> DefaultFontSizes =
        [8, 9, 10, 11, 12, 14, 16, 18, 20, 24, 28, 36, 48, 72];

    /// <summary>
    /// The colour this control wears: its ribbon's header band and tab underline.
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="OfficeAccent.Spreadsheet"/> — the colour Microsoft's own Excel wears,
    /// because that is what a user reads as "a spreadsheet" before any label has been looked at. Set it to
    /// take on the app's own brand instead, or to <c>null</c> to leave the bar on the theme's neutrals
    /// like the rest of the chrome.
    /// </remarks>
    public static readonly BindableProperty AccentProperty = BindableProperty.Create(
        nameof(Accent),
        typeof(OfficeAccent),
        typeof(SpreadsheetToolbar),
        OfficeAccent.Spreadsheet,
        propertyChanged: (b, _, _) => ((SpreadsheetToolbar)b).ApplyAccent());

    /// <inheritdoc cref="AccentProperty"/>
    public OfficeAccent? Accent
    {
        get => (OfficeAccent?)this.GetValue(AccentProperty);
        set => this.SetValue(AccentProperty, value);
    }

    /// <summary>Paints the ribbon in the accent, or puts it back on the theme when there is none.</summary>
    void ApplyAccent()
    {
        // A propertyChanged can arrive from a Style before this constructor has built the ribbon.
        if (this.ribbon is null)
            return;

        if (this.Accent is not { } accent)
        {
            this.ribbon.HeaderBackgroundColor = null;
            this.ribbon.HeaderForegroundColor = null;
            this.ribbon.AccentColor = null;
            return;
        }

        this.ribbon.HeaderBackgroundColor = ToColor(accent.Color);
        this.ribbon.HeaderForegroundColor = ToColor(accent.Ink);

        // The underline is the ink rather than the accent: on a band already painted the accent, an
        // accent-coloured underline is invisible.
        this.ribbon.AccentColor = ToColor(accent.Ink);
    }

    static Color ToColor(ArgbColor value)
        => Color.FromRgba(value.R / 255f, value.G / 255f, value.B / 255f, value.A / 255f);

    /// <summary>
    /// Picks a picture for the watermark, or clears one already there.
    /// </summary>
    /// <remarks>
    /// The same picker the document and slide editors use for a picture - camera or gallery on a
    /// phone, the platform's own image-filtered dialog on a desktop - because a watermark is a picture
    /// and there is no reason for choosing one to work differently here.
    /// </remarks>
    async Task PickWatermarkAsync()
    {
        if (this.HasWatermark)
        {
            this.WatermarkPicked?.Invoke(this, null);
            return;
        }

        var (image, rejected) = await OfficeMenus.PickImageAsync(OfficeMenus.PageOf(this));

        if (rejected is not null || image is null)
            return;

        this.WatermarkPicked?.Invoke(this, new OfficeWatermark
        {
            Image = image.Data,
            RotationDegrees = 315
        });
    }

}
