using Shiny.Controls.Office.Icons;
using Shiny.Controls.Office.Spreadsheet.Calc;
using Shiny.Controls.Office.Spreadsheet.Commands;
using Shiny.Maui.Controls.ColorPicker;
using Shiny.Maui.Controls.FontPicker;
using Shiny.Maui.Controls.Ribbons;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// The ribbon above a <see cref="SpreadsheetView"/>, laid out the way Excel's is.
/// </summary>
/// <remarks>
/// <para>
/// Excel's tabs, in Excel's order: <b>File</b> (a hook — <see cref="FileMenuRequested"/>; the backstage
/// belongs to the app), <b>Home</b>, <b>Insert</b>, <b>Formulas</b>, <b>Data</b>, <b>Review</b> and
/// <b>View</b>. The groups inside each are Excel's too — Home carries Clipboard, Font, Alignment, Number,
/// Styles, Cells and Editing — because the point of a spreadsheet that looks like Excel is that a hand
/// that knows where Excel keeps a command finds it here without looking.
/// </para>
/// <para>
/// Everything goes through <see cref="SpreadsheetController"/>, so a button is the same undoable command
/// a keyboard shortcut or a context-menu line would raise. A command that needs input — Format Cells, a
/// conditional-format value, the name manager — asks the controller for its dialog, and the
/// <see cref="SpreadsheetView"/> renders it; the bar itself never opens a window.
/// </para>
/// <para>
/// The icons come from the shared Office set, so this bar and the Blazor one draw the same mark for the
/// same command.
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
    readonly RibbonSplitButton formulaSum;
    readonly RibbonButton paste;
    readonly RibbonButton cut;
    readonly RibbonButton copy;
    readonly RibbonButton outdent;
    readonly RibbonButton indent;
    readonly RibbonSplitButton borders;
    readonly RibbonSplitButton merge;
    readonly RibbonMenuButton conditional;
    readonly RibbonMenuButton formatAsTable;
    readonly RibbonMenuButton cellStyles;
    readonly RibbonMenuButton insertCells;
    readonly RibbonMenuButton deleteCells;
    readonly RibbonMenuButton formatCells;
    readonly RibbonMenuButton fillMenu;
    readonly RibbonMenuButton clearMenu;
    readonly RibbonMenuButton sortFilterMenu;
    readonly RibbonButton goTo;
    readonly OfficeFindBar findBar = new();
    readonly RibbonButton undo;
    readonly RibbonButton redo;

    // Insert
    readonly RibbonButton insertTable;
    readonly RibbonButton[] chartButtons;
    readonly RibbonButton insertLink;
    readonly RibbonButton insertNote;

    // Formulas
    readonly RibbonButton insertFunction;
    readonly RibbonMenuButton[] libraryMenus;
    readonly RibbonButton nameManager;
    readonly RibbonButton defineName;
    readonly RibbonMenuButton useInFormula;
    readonly RibbonButton calculateNow;
    readonly RibbonToggleButton showFormulas;

    // Data
    readonly RibbonButton sortAscending;
    readonly RibbonButton sortDescending;
    readonly RibbonButton customSort;
    readonly RibbonToggleButton filter;
    readonly RibbonButton clearFilter;
    readonly RibbonButton reapplyFilter;
    readonly RibbonButton dataValidation;
    readonly RibbonButton clearValidation;

    // Review
    readonly RibbonButton newNote;
    readonly RibbonButton deleteNote;
    readonly RibbonButton previousNote;
    readonly RibbonButton nextNote;
    readonly RibbonToggleButton showAllNotes;

    // View
    readonly RibbonToggleButton gridlines;
    readonly RibbonToggleButton headings;
    readonly RibbonToggleButton formulaBar;
    readonly RibbonToggleButton viewFormulas;
    readonly RibbonButton zoomDialog;
    readonly RibbonButton zoom100;
    readonly RibbonButton zoomSelection;
    readonly RibbonMenuButton freeze;

    readonly RibbonMenuEntry filterEntry;
    readonly RibbonMenuEntry convertToRange;

    readonly OfficeToolbarButton fill;
    readonly ColorPickerButton textColor;

    /// <summary>Items that change nothing, so a read-only workbook still has them.</summary>
    readonly HashSet<RibbonItem> readOnlySafe = [];

    /// <summary>Every command item, for the enabled pass.</summary>
    readonly List<RibbonItem> items = [];

    FontPickerButton? fontPicker;
    FontSizePickerButton? sizePicker;
    SpreadsheetController? controller;
    bool suppressPickerEvents;
    string namesShown = "\u0000";

    public SpreadsheetToolbar()
    {
        this.bold = this.Toggle(OfficeIcon.Bold, "Bold (Ctrl+B)", c => c.ToggleBold());
        this.italic = this.Toggle(OfficeIcon.Italic, "Italic (Ctrl+I)", c => c.ToggleItalic());
        this.underline = this.Toggle(OfficeIcon.Underline, "Underline (Ctrl+U)", c => c.ToggleUnderline());
        this.strike = this.Toggle(OfficeIcon.Strikethrough, "Strikethrough (Ctrl+5)", c => c.ToggleStrikethrough());

        this.alignLeft = this.Toggle(OfficeIcon.AlignLeft, "Align left", c => c.SetAlignment(CellHorizontalAlignment.Left));
        this.alignCenter = this.Toggle(OfficeIcon.AlignCenter, "Centre", c => c.SetAlignment(CellHorizontalAlignment.Center));
        this.alignRight = this.Toggle(OfficeIcon.AlignRight, "Align right", c => c.SetAlignment(CellHorizontalAlignment.Right));

        this.alignTop = this.Toggle(OfficeIcon.AlignTop, "Align top", c => c.SetVerticalAlignment(CellVerticalAlignment.Top));
        this.alignMiddle = this.Toggle(OfficeIcon.AlignMiddle, "Align middle", c => c.SetVerticalAlignment(CellVerticalAlignment.Center));
        this.alignBottom = this.Toggle(OfficeIcon.AlignBottom, "Align bottom", c => c.SetVerticalAlignment(CellVerticalAlignment.Bottom));

        this.wrap = this.Toggle(OfficeIcon.WrapText, "Wrap text", c => c.ToggleWrapText());
        this.outdent = this.Command(OfficeIcon.Outdent, "Decrease indent", c => c.AdjustIndent(-1));
        this.indent = this.Command(OfficeIcon.Indent, "Increase indent", c => c.AdjustIndent(1));

        // Merge & Center: the face merges and centres - or unmerges a merged cell, which is how Excel's
        // own button behaves - and the chevron offers the other three.
        this.merge = this.Split(OfficeIcon.MergeCells, "Merge & Center", "Merge & Center", c => c.ToggleMergeAndCenter(), RibbonItemSize.Small);
        this.merge.Menu.Add(this.Entry("Merge & Center", c => c.MergeCells(MergeMode.MergeAndCenter)));
        this.merge.Menu.Add(this.Entry("Merge Across", c => c.MergeCells(MergeMode.MergeAcross)));
        this.merge.Menu.Add(this.Entry("Merge Cells", c => c.MergeCells(MergeMode.MergeCells)));
        this.merge.Menu.Add(this.Entry("Unmerge Cells", c => c.UnmergeCells()));

        this.currency = this.Toggle(OfficeIcon.Currency, "Currency", c => c.SetNumberFormat(NumberFormatPreset.Currency));
        this.percent = this.Toggle(OfficeIcon.Percent, "Percent (Ctrl+Shift+%)", c => c.SetNumberFormat(NumberFormatPreset.Percent));
        this.decimalDecrease = this.Command(OfficeIcon.DecimalDecrease, "Fewer decimal places", c => c.AdjustDecimals(-1));
        this.decimalIncrease = this.Command(OfficeIcon.DecimalIncrease, "More decimal places", c => c.AdjustDecimals(1));

        // No icon, deliberately: the only mark that fits is the currency one beside it in the group.
        this.numberFormats = new RibbonMenuButton
        {
            Text = "Formats",
            Tooltip = "More number formats",
            Size = RibbonItemSize.Small,
            AutomationId = "SheetToolbarNumberFormats"
        };

        this.sum = this.AutoSum("SheetToolbarAutoSum");
        this.formulaSum = this.AutoSum("SheetToolbarFormulaAutoSum");

        this.paste = this.Command(OfficeIcon.Paste, "Paste (Ctrl+V)", c => c.Paste(), "Paste");
        this.cut = this.Command(OfficeIcon.Cut, "Cut (Ctrl+X)", c => c.Cut());
        this.copy = this.Command(OfficeIcon.Copy, "Copy (Ctrl+C)", c => c.Copy());

        // Borders: the face draws the last preset picked, bottom to begin with, as Excel's does.
        this.borders = this.Split(OfficeIcon.BorderBottom, "Borders", "Bottom Border", c => c.ApplyBorders(this.lastBorder), RibbonItemSize.Small);
        this.BuildBordersMenu();

        this.conditional = this.MenuButton(OfficeIcon.ConditionalFormat, "Conditional Formatting", "Conditional Formatting");
        this.BuildConditionalMenu();

        this.formatAsTable = this.MenuButton(OfficeIcon.FormatAsTable, "Format as Table", "Format as Table");
        foreach (var style in SheetTables.GalleryStyles)
        {
            var captured = style;
            this.formatAsTable.Menu.Add(this.Entry(TableStyleName(captured), c => this.FormatAsTable(c, captured)));
        }

        this.convertToRange = this.Entry("Convert to Range", c => c.ConvertTableToRange());
        this.formatAsTable.Menu.Add(new RibbonMenuEntry { IsSeparator = true });
        this.formatAsTable.Menu.Add(this.convertToRange);

        this.cellStyles = this.MenuButton(OfficeIcon.CellStyles, "Cell Styles", "Cell Styles");
        foreach (var group in CellStylePresets.All.GroupBy(x => x.Group))
        {
            var parent = new RibbonMenuEntry { Text = group.Key };
            foreach (var preset in group)
            {
                var captured = preset;
                parent.Children.Add(this.Entry(captured.Name, c => c.ApplyCellStyle(captured)));
            }

            this.cellStyles.Menu.Add(parent);
        }

        this.insertCells = this.MenuButton(OfficeIcon.InsertRow, "Insert", "Insert cells, rows, columns or sheets");
        this.insertCells.Menu.Add(this.Entry("Insert Sheet Rows", c => c.InsertRows(Math.Clamp(c.Selection.Range.RowCount, 1, 1000))));
        this.insertCells.Menu.Add(this.Entry("Insert Sheet Columns", c => c.InsertColumns(Math.Clamp(c.Selection.Range.ColumnCount, 1, 1000))));
        this.insertCells.Menu.Add(new RibbonMenuEntry { IsSeparator = true });
        this.insertCells.Menu.Add(this.Entry("Insert Sheet", c => c.AddSheet()));

        this.deleteCells = this.MenuButton(OfficeIcon.DeleteRow, "Delete", "Delete cells, rows, columns or sheets");
        this.deleteCells.Menu.Add(this.Entry("Delete Sheet Rows", c => c.DeleteRows(Math.Clamp(c.Selection.Range.RowCount, 1, 1000))));
        this.deleteCells.Menu.Add(this.Entry("Delete Sheet Columns", c => c.DeleteColumns(Math.Clamp(c.Selection.Range.ColumnCount, 1, 1000))));
        this.deleteCells.Menu.Add(new RibbonMenuEntry { IsSeparator = true });
        this.deleteCells.Menu.Add(this.Entry("Delete Sheet", c =>
        {
            if (c.CanRemoveFromView(c.Sheet))
                c.DeleteSheet(c.Sheet);
        }));

        this.formatCells = this.MenuButton(OfficeIcon.FormatCells, "Format", "Row height, column width, visibility and cell format");
        this.formatCells.Menu.Add(this.Entry("Row Height...", c => c.ShowDialog(SpreadsheetDialogs.RowHeight(c))));
        this.formatCells.Menu.Add(this.Entry("AutoFit Column Width", c => c.AutoFitColumns()));
        this.formatCells.Menu.Add(this.Entry("Column Width...", c => c.ShowDialog(SpreadsheetDialogs.ColumnWidth(c))));

        var widths = new RibbonMenuEntry { Text = "Column Width Presets" };
        foreach (var (name, characters) in ColumnWidthPresets.All)
        {
            var width = ColumnWidthPresets.PixelsOf(characters);
            widths.Children.Add(this.Entry($"{name}   {width:0} px", c => c.SetColumnWidth(width)));
        }

        this.formatCells.Menu.Add(widths);
        this.formatCells.Menu.Add(new RibbonMenuEntry { IsSeparator = true });
        this.formatCells.Menu.Add(this.Entry("Hide Rows", c => c.SetRowsHidden(true)));
        this.formatCells.Menu.Add(this.Entry("Hide Columns", c => c.SetColumnsHidden(true)));
        this.formatCells.Menu.Add(this.Entry("Unhide Rows", c => c.SetRowsHidden(false)));
        this.formatCells.Menu.Add(this.Entry("Unhide Columns", c => c.SetColumnsHidden(false)));
        this.formatCells.Menu.Add(new RibbonMenuEntry { IsSeparator = true });
        this.formatCells.Menu.Add(this.Entry("Format Cells...", c => c.ShowDialog(SpreadsheetDialogs.FormatCells(c))));

        this.fillMenu = this.MenuButton(OfficeIcon.FillDown, "Fill", "Fill");
        this.fillMenu.Menu.Add(this.Entry("Down (Ctrl+D)", c => c.FillDown()));
        this.fillMenu.Menu.Add(this.Entry("Right (Ctrl+R)", c => c.FillRight()));

        this.clearMenu = this.MenuButton(OfficeIcon.ClearFormat, "Clear", "Clear");
        this.clearMenu.Menu.Add(this.Entry("Clear All", c => c.ClearAll()));
        this.clearMenu.Menu.Add(this.Entry("Clear Formats", c => c.ClearFormatting()));
        this.clearMenu.Menu.Add(this.Entry("Clear Contents", c => c.ClearSelection()));
        this.clearMenu.Menu.Add(this.Entry("Clear Notes", c => c.ClearNotes()));
        this.clearMenu.Menu.Add(this.Entry("Clear Hyperlinks", c => c.ClearHyperlinks()));

        this.filterEntry = this.Entry("Filter (Ctrl+Shift+L)", c => c.ToggleAutoFilter());
        this.sortFilterMenu = this.MenuButton(OfficeIcon.SortAscending, "Sort & Filter", "Sort & Filter");
        this.sortFilterMenu.Menu.Add(this.Entry("Sort A to Z", c => c.SortAscending()));
        this.sortFilterMenu.Menu.Add(this.Entry("Sort Z to A", c => c.SortDescending()));
        this.sortFilterMenu.Menu.Add(this.Entry("Custom Sort...", c => c.ShowDialog(SpreadsheetDialogs.CustomSort(c))));
        this.sortFilterMenu.Menu.Add(new RibbonMenuEntry { IsSeparator = true });
        this.sortFilterMenu.Menu.Add(this.filterEntry);
        this.sortFilterMenu.Menu.Add(this.Entry("Clear", c => c.ClearFilters()));
        this.sortFilterMenu.Menu.Add(this.Entry("Reapply", c => c.ReapplyFilters()));

        this.goTo = this.Command(OfficeIcon.GoTo, "Go To (Ctrl+G)", c => c.ShowDialog(SpreadsheetDialogs.GoTo(c)));
        this.readOnlySafe.Add(this.goTo);

        this.undo = this.Command(OfficeIcon.Undo, "Undo (Ctrl+Z)", c => c.Undo());
        this.redo = this.Command(OfficeIcon.Redo, "Redo (Ctrl+Y)", c => c.Redo());

        // ---- Insert ----

        this.insertTable = this.Large(OfficeIcon.FormatAsTable, "Table", "Create a table", c => c.ShowDialog(SpreadsheetDialogs.CreateTable(c, "TableStyleMedium2")));
        this.chartButtons = SheetCharts.Gallery
            .Select(x => this.Command(ChartIcon(x.Kind), $"Insert {x.Name} chart", c => c.InsertChart(x.Kind), x.Kind.ToString()))
            .ToArray();

        this.insertLink = this.Large(OfficeIcon.Hyperlink, "Link", "Insert a hyperlink (Ctrl+K)", c => c.ShowDialog(SpreadsheetDialogs.Hyperlink(c)));
        this.insertNote = this.Large(OfficeIcon.NewNote, "Note", "Insert a note (Shift+F2)", c => c.ShowDialog(SpreadsheetDialogs.Note(c)));

        // Not through the Command helper: a watermark is drawn by the view, not stored in the workbook.
        this.watermark = OfficeRibbonItems.Command(
            OfficeIcon.Watermark,
            "Watermark",
            () => _ = this.PickWatermarkAsync(),
            automationId: "SheetToolbarWatermark");

        // ---- Formulas ----

        this.insertFunction = this.Large(OfficeIcon.Function, "Insert Function", "Insert Function (Shift+F3)", c => c.ShowDialog(SpreadsheetDialogs.InsertFunction(c)));

        var menus = new List<RibbonMenuButton>();
        foreach (var category in new[] { FunctionCatalog.Financial, FunctionCatalog.Logical, FunctionCatalog.Text, FunctionCatalog.DateTime, FunctionCatalog.Lookup, FunctionCatalog.Math })
            menus.Add(this.LibraryMenu(category, category));

        var more = this.MenuButton(OfficeIcon.Function, "More Functions", "More Functions");
        foreach (var category in new[] { FunctionCatalog.Statistical, FunctionCatalog.Information })
        {
            var parent = new RibbonMenuEntry { Text = category };
            foreach (var info in FunctionCatalog.InCategory(category))
                parent.Children.Add(this.FunctionEntry(info));

            more.Menu.Add(parent);
        }

        menus.Add(more);
        this.libraryMenus = menus.ToArray();

        this.nameManager = this.Large(OfficeIcon.NameManager, "Name Manager", "Name Manager (Ctrl+F3)", c => c.ShowDialog(SpreadsheetDialogs.NameManager(c)));
        this.defineName = this.Command(OfficeIcon.DefineName, "Define Name", c => c.ShowDialog(SpreadsheetDialogs.DefineName(c, null)), "Define Name");
        this.useInFormula = this.MenuButton(OfficeIcon.Function, "Use in Formula", "Use a defined name in a formula");
        this.calculateNow = this.Large(OfficeIcon.Calculate, "Calculate Now", "Calculate Now (F9)", c => c.CalculateNow());
        this.showFormulas = this.Toggle(OfficeIcon.ShowFormulas, "Show Formulas (Ctrl+`)", c => c.ToggleShowFormulas());
        this.readOnlySafe.Add(this.showFormulas);
        this.readOnlySafe.Add(this.calculateNow);

        // ---- Data ----

        this.sortAscending = this.Command(OfficeIcon.SortAscending, "Sort A to Z", c => c.SortAscending(), "A to Z");
        this.sortDescending = this.Command(OfficeIcon.SortDescending, "Sort Z to A", c => c.SortDescending(), "Z to A");
        this.customSort = this.Large(OfficeIcon.CustomSort, "Sort", "Custom sort", c => c.ShowDialog(SpreadsheetDialogs.CustomSort(c)));
        this.filter = this.LargeToggle(OfficeIcon.Filter, "Filter", "Filter (Ctrl+Shift+L)", c => c.ToggleAutoFilter());
        this.clearFilter = this.Command(OfficeIcon.FilterClear, "Clear filters", c => c.ClearFilters(), "Clear");
        this.reapplyFilter = this.Command(OfficeIcon.Filter, "Reapply filters", c => c.ReapplyFilters(), "Reapply");
        this.dataValidation = this.Large(OfficeIcon.DataValidation, "Data Validation", "Data Validation", c => c.ShowDialog(SpreadsheetDialogs.DataValidation(c)));
        this.clearValidation = this.Command(OfficeIcon.Delete, "Clear validation from the selection", c => c.SetValidation(null), "Clear Validation");

        // ---- Review ----

        this.newNote = this.Large(OfficeIcon.NewNote, "New Note", "New or edit note (Shift+F2)", c => c.ShowDialog(SpreadsheetDialogs.Note(c)));
        this.deleteNote = this.Command(OfficeIcon.DeleteNote, "Delete note", c => c.DeleteNote(), "Delete");
        this.previousNote = this.Command(OfficeIcon.Previous, "Previous note", c => c.NextNote(backwards: true), "Previous");
        this.nextNote = this.Command(OfficeIcon.Next, "Next note", c => c.NextNote(), "Next");
        this.showAllNotes = this.Toggle(OfficeIcon.ShowNotes, "Show all notes", c =>
        {
            c.ShowAllNotes = !c.ShowAllNotes;
            this.Changed?.Invoke(this, EventArgs.Empty);
        });

        foreach (var item in new RibbonItem[] { this.previousNote, this.nextNote, this.showAllNotes })
            this.readOnlySafe.Add(item);

        // ---- View ----

        this.gridlines = this.Toggle(OfficeIcon.Gridlines, "Show gridlines", c => c.ShowGridlines = !c.ShowGridlines);
        this.headings = this.Toggle(OfficeIcon.Headings, "Show headings", c => c.ShowHeadings = !c.ShowHeadings);
        this.formulaBar = OfficeRibbonItems.Toggle(OfficeIcon.FormulaBar, "Show formula bar", () =>
        {
            this.IsFormulaBarVisible = !this.IsFormulaBarVisible;
            this.FormulaBarToggled?.Invoke(this, this.IsFormulaBarVisible);
            this.Refresh();
        }, "SheetToolbarFormulaBarToggle");

        this.viewFormulas = this.Toggle(OfficeIcon.ShowFormulas, "Show formulas (Ctrl+`)", c => c.ToggleShowFormulas());
        this.zoomDialog = this.Large(OfficeIcon.ZoomIn, "Zoom", "Zoom", c => c.ShowDialog(SpreadsheetDialogs.Zoom(c, c.Viewport.Width * c.Zoom, c.Viewport.Height * c.Zoom)));
        this.zoom100 = this.Command(OfficeIcon.Zoom100, "Zoom to 100%", c => c.Zoom = 1, "100%");
        this.zoomSelection = this.Command(OfficeIcon.ZoomIn, "Zoom to selection", this.ZoomToSelection, "Zoom to Selection");

        this.freeze = this.MenuButton(OfficeIcon.FreezePanes, "Freeze Panes", "Freeze Panes");
        this.freeze.Size = RibbonItemSize.Large;
        this.freeze.Menu.Add(this.Entry("Freeze Panes", c => c.FreezePanes()));
        this.freeze.Menu.Add(this.Entry("Freeze Top Row", c => c.FreezeTopRow()));
        this.freeze.Menu.Add(this.Entry("Freeze First Column", c => c.FreezeFirstColumn()));
        this.freeze.Menu.Add(this.Entry("Unfreeze Panes", c => c.UnfreezePanes()));

        foreach (var item in new RibbonItem[] { this.gridlines, this.headings, this.formulaBar, this.viewFormulas, this.zoomDialog, this.zoom100, this.zoomSelection, this.freeze })
            this.readOnlySafe.Add(item);

        // The fill button keeps its own popup - it is a colour surface, which a ribbon button cannot be.
        this.fill = new OfficeToolbarButton(OfficeIcon.FillColor, "Fill colour");
        this.fill.Clicked += async (_, _) => await this.PickFillAsync();

        this.textColor = this.CreateColorPicker();

        this.ribbon = new Ribbon
        {
            // Two rows rather than three: this is a bar above a grid, and the groups divide evenly.
            SmallItemRows = 2,
            SmallItemRowHeight = 32,
            AllowGroupCollapse = true,

            // Below this the bar runs dense instead of folding its groups away.
            SimplifyBelowWidth = 600,

            // Excel's File tab. The backstage is the app's; the bar only says it was asked for.
            ApplicationButtonText = "File",
            ApplicationButtonCommand = new Command(() => this.FileMenuRequested?.Invoke(this, EventArgs.Empty))
        };

        // Explicitly, because a BindableProperty's propertyChanged does not fire for its default.
        this.ApplyAccent();

        this.Content = this.ribbon;
        this.BuildBar();

        // An unset Theme tracks the app's appearance, so a flip has to redraw.
        this.FollowAppTheme(static v => v.Refresh());
    }

    BorderPreset lastBorder = BorderPreset.Bottom;

    public static readonly BindableProperty ThemeProperty = BindableProperty.Create(
        nameof(Theme),
        typeof(SpreadsheetTheme),
        typeof(SpreadsheetToolbar),
        null,
        propertyChanged: (b, _, _) => ((SpreadsheetToolbar)b).OnThemeChanged());

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

    /// <summary>Whether the buttons show a hover tooltip. On everywhere but phones by default.</summary>
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

            this.findBar.Find = this.controller?.Find;
            this.Refresh();
        }
    }

    /// <summary>Raised after a command runs, so a host can repaint and track the dirty state.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when the watermark button picks a picture, or clears the one already set.</summary>
    public event EventHandler<OfficeWatermark?>? WatermarkPicked;

    /// <summary>
    /// Raised when the ribbon's File button is pressed. The bar has no backstage of its own: what File
    /// opens — save, export, recent files — belongs to the app.
    /// </summary>
    public event EventHandler? FileMenuRequested;

    /// <summary>
    /// Raised when View ▸ Formula Bar is toggled, with the new state. The formula bar is the host's
    /// chrome rather than the workbook's, so the bar reports the choice and the view acts on it.
    /// </summary>
    public event EventHandler<bool>? FormulaBarToggled;

    /// <summary>Whether the host is showing its formula bar — what the View tab's toggle reflects.</summary>
    public bool IsFormulaBarVisible { get; set; } = true;

    /// <summary>Whether a mark is currently set, so the button can offer to take it off.</summary>
    public bool HasWatermark { get; set; }

    /// <summary>Extra views appended after the built-in controls.</summary>
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

    /// <summary>What a preset does to a number, so the menu shows the format rather than naming it.</summary>
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

    /// <summary>Detaches from the controller. Called by the hosting view when it is disposed.</summary>
    public void Detach() => this.Controller = null;

    void BuildBar()
    {
        // A propertyChanged from a Style can arrive before the constructor has built the items.
        if (this.ribbon is null)
            return;

        this.ribbon.Tabs.Clear();

        this.fontPicker = this.CreateFontPicker();
        this.sizePicker = this.CreateSizePicker();

        this.ribbon.QuickAccessItems.Clear();
        this.ribbon.QuickAccessItems.Add(this.undo);
        this.ribbon.QuickAccessItems.Add(this.redo);

        this.ribbon.Tabs.Add(this.HomeTab());
        this.ribbon.Tabs.Add(this.InsertTab());
        this.ribbon.Tabs.Add(this.FormulasTab());
        this.ribbon.Tabs.Add(this.DataTab());
        this.ribbon.Tabs.Add(this.ReviewTab());
        this.ribbon.Tabs.Add(this.ViewTab());

        this.RebuildMenus();
        this.Refresh();
    }

    // ---- tabs ----

    RibbonTab HomeTab()
    {
        var home = new RibbonTab { Title = "Home", Key = "home" };

        // Paste is large and the other two stack beside it - Excel's own arrangement.
        var clipboard = new RibbonGroup { Title = "Clipboard", Priority = 110 };
        this.paste.Size = RibbonItemSize.Large;
        this.paste.Text = "Paste";
        clipboard.Items.Add(this.paste);
        clipboard.Items.Add(this.cut);
        clipboard.Items.Add(this.copy);
        home.Groups.Add(clipboard);

        // Excel's Font group: the two boxes on top, the marks underneath with the border, text colour and
        // fill at the end of the row.
        var font = new RibbonGroup { Title = "Font", Priority = 100 };
        font.Items.Add(OfficeRibbonItems.Row(
            OfficeRibbonItems.Host(this.fontPicker!),
            OfficeRibbonItems.Host(this.sizePicker!)));
        font.Items.Add(OfficeRibbonItems.Row(
            this.bold,
            this.italic,
            this.underline,
            this.strike,
            new RibbonSeparator(),
            this.borders,
            OfficeRibbonItems.Host(this.textColor),
            OfficeRibbonItems.Host(this.fill)));
        home.Groups.Add(font);

        // How the text sits top-to-bottom on one row, left-to-right on the other, with wrap and merge -
        // the two things that change the box the text sits in - at the ends.
        var alignment = new RibbonGroup { Title = "Alignment", Priority = 90 };
        alignment.Items.Add(OfficeRibbonItems.Row(
            this.alignTop,
            this.alignMiddle,
            this.alignBottom,
            new RibbonSeparator(),
            this.wrap));
        alignment.Items.Add(OfficeRibbonItems.Row(
            this.alignLeft,
            this.alignCenter,
            this.alignRight,
            new RibbonSeparator(),
            this.outdent,
            this.indent,
            this.merge));
        home.Groups.Add(alignment);

        var number = new RibbonGroup { Title = "Number", Priority = 80 };
        number.Items.Add(OfficeRibbonItems.Row(this.numberFormats));
        number.Items.Add(OfficeRibbonItems.Row(this.currency, this.percent, this.decimalDecrease, this.decimalIncrease));
        home.Groups.Add(number);

        // Styles: the three galleries, one column each, as Excel's Styles group has them.
        var styles = new RibbonGroup { Title = "Styles", Priority = 75 };
        styles.Items.Add(this.conditional);
        styles.Items.Add(this.formatAsTable);
        styles.Items.Add(this.cellStyles);
        home.Groups.Add(styles);

        var cells = new RibbonGroup { Title = "Cells", Priority = 72 };
        cells.Items.Add(this.insertCells);
        cells.Items.Add(this.deleteCells);
        cells.Items.Add(this.formatCells);
        home.Groups.Add(cells);

        // AutoSum heads Editing, as in Excel, with fill, clear, sort-and-filter and go-to beside it.
        var editing = new RibbonGroup { Title = "Editing", Priority = 70 };
        this.sum.Size = RibbonItemSize.Large;
        editing.Items.Add(this.sum);
        editing.Items.Add(this.fillMenu);
        editing.Items.Add(this.clearMenu);
        editing.Items.Add(this.sortFilterMenu);
        editing.Items.Add(this.goTo);
        home.Groups.Add(editing);

        // Its own group: finding changes nothing, and the box is as wide as three buttons.
        var finding = new RibbonGroup { Title = "Find", Priority = 65 };
        finding.Items.Add(OfficeRibbonItems.HostLarge(this.findBar));
        home.Groups.Add(finding);

        if (this.ToolbarItems.Count > 0)
        {
            // Never folds into an overflow: whatever the host added is theirs.
            var extras = new RibbonGroup { Title = this.ToolbarItemsTitle, Priority = 200, CanCollapse = false };
            foreach (var item in this.ToolbarItems)
                extras.Items.Add(OfficeRibbonItems.Host(item));

            home.Groups.Add(extras);
        }

        return home;
    }

    RibbonTab InsertTab()
    {
        var insert = new RibbonTab { Title = "Insert", Key = "insert" };

        var tables = new RibbonGroup { Title = "Tables", Priority = 100 };
        tables.Items.Add(this.insertTable);
        insert.Groups.Add(tables);

        var charts = new RibbonGroup { Title = "Charts", Priority = 90 };
        foreach (var button in this.chartButtons)
            charts.Items.Add(button);

        insert.Groups.Add(charts);

        var links = new RibbonGroup { Title = "Links", Priority = 80 };
        links.Items.Add(this.insertLink);
        insert.Groups.Add(links);

        var notes = new RibbonGroup { Title = "Notes", Priority = 70 };
        notes.Items.Add(this.insertNote);
        insert.Groups.Add(notes);

        // A display watermark is not an Excel feature, so it sits with the other things placed on the
        // sheet rather than in a tab Excel users would search for it on.
        var sheet = new RibbonGroup { Title = "Sheet", Priority = 60 };
        this.watermark.Size = RibbonItemSize.Large;
        this.watermark.Text = "Watermark";
        sheet.Items.Add(this.watermark);
        insert.Groups.Add(sheet);

        return insert;
    }

    RibbonTab FormulasTab()
    {
        var formulas = new RibbonTab { Title = "Formulas", Key = "formulas" };

        var library = new RibbonGroup { Title = "Function Library", Priority = 100 };
        library.Items.Add(this.insertFunction);
        this.formulaSum.Size = RibbonItemSize.Small;
        library.Items.Add(this.formulaSum);

        foreach (var menu in this.libraryMenus)
            library.Items.Add(menu);

        formulas.Groups.Add(library);

        var names = new RibbonGroup { Title = "Defined Names", Priority = 90 };
        names.Items.Add(this.nameManager);
        names.Items.Add(this.defineName);
        names.Items.Add(this.useInFormula);
        formulas.Groups.Add(names);

        var calculation = new RibbonGroup { Title = "Calculation", Priority = 80 };
        calculation.Items.Add(this.calculateNow);
        calculation.Items.Add(this.showFormulas);
        formulas.Groups.Add(calculation);

        return formulas;
    }

    RibbonTab DataTab()
    {
        var data = new RibbonTab { Title = "Data", Key = "data" };

        var sort = new RibbonGroup { Title = "Sort & Filter", Priority = 100 };
        sort.Items.Add(this.sortAscending);
        sort.Items.Add(this.sortDescending);
        sort.Items.Add(this.customSort);
        sort.Items.Add(this.filter);
        sort.Items.Add(this.clearFilter);
        sort.Items.Add(this.reapplyFilter);
        data.Groups.Add(sort);

        var tools = new RibbonGroup { Title = "Data Tools", Priority = 90 };
        tools.Items.Add(this.dataValidation);
        tools.Items.Add(this.clearValidation);
        data.Groups.Add(tools);

        return data;
    }

    RibbonTab ReviewTab()
    {
        var review = new RibbonTab { Title = "Review", Key = "review" };

        var notes = new RibbonGroup { Title = "Notes", Priority = 100 };
        notes.Items.Add(this.newNote);
        notes.Items.Add(this.deleteNote);
        notes.Items.Add(this.previousNote);
        notes.Items.Add(this.nextNote);
        notes.Items.Add(this.showAllNotes);
        review.Groups.Add(notes);

        return review;
    }

    RibbonTab ViewTab()
    {
        var view = new RibbonTab { Title = "View", Key = "view" };

        var show = new RibbonGroup { Title = "Show", Priority = 100 };
        show.Items.Add(OfficeRibbonItems.Row(this.gridlines, this.headings));
        show.Items.Add(OfficeRibbonItems.Row(this.formulaBar, this.viewFormulas));
        view.Groups.Add(show);

        var zoom = new RibbonGroup { Title = "Zoom", Priority = 90 };
        zoom.Items.Add(this.zoomDialog);
        zoom.Items.Add(this.zoom100);
        zoom.Items.Add(this.zoomSelection);
        view.Groups.Add(zoom);

        var window = new RibbonGroup { Title = "Window", Priority = 80 };
        window.Items.Add(this.freeze);
        view.Groups.Add(window);

        return view;
    }

    // ---- menus ----

    void BuildBordersMenu()
    {
        void Add(BorderPreset preset)
            => this.borders.Menu.Add(this.Entry(BorderPresets.NameOf(preset), c =>
            {
                this.lastBorder = preset;
                this.borders.Tooltip = BorderPresets.NameOf(preset);
                c.ApplyBorders(preset);
            }));

        Add(BorderPreset.Bottom);
        Add(BorderPreset.Top);
        Add(BorderPreset.Left);
        Add(BorderPreset.Right);
        this.borders.Menu.Add(new RibbonMenuEntry { IsSeparator = true });
        Add(BorderPreset.None);
        Add(BorderPreset.All);
        Add(BorderPreset.Outside);
        Add(BorderPreset.ThickOutside);
        this.borders.Menu.Add(new RibbonMenuEntry { IsSeparator = true });
        Add(BorderPreset.DoubleBottom);
        Add(BorderPreset.ThickBottom);
        Add(BorderPreset.TopAndBottom);
        this.borders.Menu.Add(new RibbonMenuEntry { IsSeparator = true });

        var style = new RibbonMenuEntry { Text = "Line Style" };
        foreach (var (name, value) in new[]
                 {
                     ("Thin", CellBorderStyle.Thin), ("Medium", CellBorderStyle.Medium), ("Thick", CellBorderStyle.Thick),
                     ("Dashed", CellBorderStyle.Dashed), ("Dotted", CellBorderStyle.Dotted), ("Double", CellBorderStyle.Double)
                 })
        {
            var captured = value;
            style.Children.Add(this.Entry(name, c => c.BorderLine = c.BorderLine with { Style = captured }));
        }

        var color = new RibbonMenuEntry { Text = "Line Color" };
        color.Children.Add(this.Entry("Automatic", c => c.BorderLine = c.BorderLine with { Color = ArgbColor.Transparent }));
        foreach (var (name, value) in SheetField.Palette)
        {
            var captured = value;
            color.Children.Add(this.Entry(name, c => c.BorderLine = c.BorderLine with { Color = captured }));
        }

        this.borders.Menu.Add(style);
        this.borders.Menu.Add(color);
    }

    void BuildConditionalMenu()
    {
        RibbonMenuEntry Dialog(string text, ConditionalDialogKind kind)
            => this.Entry(text, c => c.ShowDialog(SpreadsheetDialogs.Conditional(c, kind)));

        var highlight = new RibbonMenuEntry { Text = "Highlight Cells Rules" };
        highlight.Children.Add(Dialog("Greater Than...", ConditionalDialogKind.GreaterThan));
        highlight.Children.Add(Dialog("Less Than...", ConditionalDialogKind.LessThan));
        highlight.Children.Add(Dialog("Between...", ConditionalDialogKind.Between));
        highlight.Children.Add(Dialog("Equal To...", ConditionalDialogKind.EqualTo));
        highlight.Children.Add(Dialog("Text that Contains...", ConditionalDialogKind.TextContains));
        highlight.Children.Add(Dialog("Duplicate Values...", ConditionalDialogKind.DuplicateValues));

        var topBottom = new RibbonMenuEntry { Text = "Top/Bottom Rules" };
        topBottom.Children.Add(Dialog("Top 10 Items...", ConditionalDialogKind.Top10Items));
        topBottom.Children.Add(Dialog("Top 10%...", ConditionalDialogKind.Top10Percent));
        topBottom.Children.Add(Dialog("Bottom 10 Items...", ConditionalDialogKind.Bottom10Items));
        topBottom.Children.Add(Dialog("Bottom 10%...", ConditionalDialogKind.Bottom10Percent));
        topBottom.Children.Add(Dialog("Above Average...", ConditionalDialogKind.AboveAverage));
        topBottom.Children.Add(Dialog("Below Average...", ConditionalDialogKind.BelowAverage));

        var bars = new RibbonMenuEntry { Text = "Data Bars" };
        foreach (var (name, color) in ConditionalPresets.DataBars)
        {
            var captured = color;
            bars.Children.Add(this.Entry(name, c => c.AddConditionalFormat(ConditionalFormatRule.Bar(captured))));
        }

        var scales = new RibbonMenuEntry { Text = "Color Scales" };
        foreach (var (name, rule) in ConditionalPresets.ColorScales)
        {
            var captured = rule;
            scales.Children.Add(this.Entry(name, c => c.AddConditionalFormat(captured)));
        }

        var clear = new RibbonMenuEntry { Text = "Clear Rules" };
        clear.Children.Add(this.Entry("Clear Rules from Selected Cells", c => c.ClearConditionalFormats()));
        clear.Children.Add(this.Entry("Clear Rules from Entire Sheet", c => c.ClearConditionalFormats(entireSheet: true)));

        this.conditional.Menu.Add(highlight);
        this.conditional.Menu.Add(topBottom);
        this.conditional.Menu.Add(new RibbonMenuEntry { IsSeparator = true });
        this.conditional.Menu.Add(bars);
        this.conditional.Menu.Add(scales);
        this.conditional.Menu.Add(new RibbonMenuEntry { IsSeparator = true });
        this.conditional.Menu.Add(clear);
    }

    RibbonMenuButton LibraryMenu(string text, string category)
    {
        var menu = this.MenuButton(CategoryIcon(category), text, $"{category} functions");
        foreach (var info in FunctionCatalog.InCategory(category))
            menu.Menu.Add(this.FunctionEntry(info));

        return menu;
    }

    RibbonMenuEntry FunctionEntry(FunctionInfo info)
    {
        var name = info.Name;
        return this.Entry(name, c => c.InsertFunction(name));
    }

    /// <summary>
    /// Refreshes the menus whose contents follow the workbook: the number formats (whose ticks and
    /// samples follow the active cell) and Use in Formula (which lists the names).
    /// </summary>
    void RebuildMenus()
    {
        this.numberFormats.Menu.Clear();
        foreach (var preset in SpreadsheetMenus.Formats)
        {
            var captured = preset;
            this.numberFormats.Menu.Add(new RibbonMenuEntry
            {
                Text = $"{NumberFormats.DisplayName(captured)}   {this.SampleOf(captured)}",
                IsChecked = this.ActivePreset == captured,
                Command = new Command(() => this.RunCommand(c => c.SetNumberFormat(captured)))
            });
        }

        this.numberFormats.Menu.Add(new RibbonMenuEntry { IsSeparator = true });
        this.numberFormats.Menu.Add(this.Entry("More Number Formats...", c => c.ShowDialog(SpreadsheetDialogs.FormatCells(c))));

        // Only rebuilt when the names actually change: this runs on every selection move.
        var names = this.controller?.Workbook.VisibleNames.Select(x => x.Name).ToList() ?? [];
        var key = string.Join('\u0001', names);
        if (key == this.namesShown)
            return;

        this.namesShown = key;
        this.useInFormula.Menu.Clear();

        if (names.Count == 0)
        {
            this.useInFormula.Menu.Add(new RibbonMenuEntry { Text = "(No names defined)", IsEnabled = false });
        }
        else
        {
            foreach (var name in names)
            {
                var captured = name;
                this.useInFormula.Menu.Add(this.Entry(captured, c => UseName(c, captured)));
            }
        }

        this.useInFormula.Menu.Add(new RibbonMenuEntry { IsSeparator = true });
        this.useInFormula.Menu.Add(this.Entry("Paste Names...", c => c.ShowDialog(SpreadsheetDialogs.NameManager(c))));
    }

    /// <summary>Puts a name into the formula being written, or starts one with it.</summary>
    static void UseName(SpreadsheetController controller, string name)
    {
        if (controller.EditingCell is not null)
        {
            var text = controller.EditingText;
            controller.CancelEdit();
            controller.BeginEdit(text + name);
            return;
        }

        controller.BeginEdit("=" + name);
    }

    static void FormatAsTableFor(SpreadsheetController controller, string style)
    {
        if (controller.ActiveTable is not null)
            controller.FormatAsTable(style);
        else
            controller.ShowDialog(SpreadsheetDialogs.CreateTable(controller, style));
    }

    void FormatAsTable(SpreadsheetController controller, string style) => FormatAsTableFor(controller, style);

    void ZoomToSelection(SpreadsheetController controller)
    {
        var rect = controller.Viewport.RangeRect(controller.Selection.Range);
        var width = controller.Viewport.Width * controller.Zoom - controller.Metrics.RowHeaderWidth * controller.Zoom;
        var height = controller.Viewport.Height * controller.Zoom - controller.Metrics.ColumnHeaderHeight * controller.Zoom;
        controller.Zoom = Math.Min(width / Math.Max(1, rect.Width), height / Math.Max(1, rect.Height));
        controller.GoTo(controller.Selection.Range.TopLeft);
    }

    static string TableStyleName(string style)
    {
        foreach (var family in new[] { "Light", "Medium", "Dark" })
        {
            var prefix = "TableStyle" + family;
            if (style.StartsWith(prefix, StringComparison.Ordinal))
                return $"{family} {style[prefix.Length..]}";
        }

        return style;
    }

    static OfficeIcon ChartIcon(ChartKind kind) => kind switch
    {
        ChartKind.Bar => OfficeIcon.ChartBar,
        ChartKind.Line => OfficeIcon.ChartLine,
        ChartKind.Pie => OfficeIcon.ChartPie,
        ChartKind.Area => OfficeIcon.ChartArea,
        _ => OfficeIcon.ChartColumn
    };

    static OfficeIcon CategoryIcon(string category) => category switch
    {
        FunctionCatalog.Financial => OfficeIcon.Currency,
        FunctionCatalog.Logical => OfficeIcon.DataValidation,
        FunctionCatalog.Text => OfficeIcon.TextBox,
        FunctionCatalog.DateTime => OfficeIcon.Calculate,
        FunctionCatalog.Lookup => OfficeIcon.Find,
        FunctionCatalog.Math => OfficeIcon.Sum,
        _ => OfficeIcon.Function
    };

    // ---- item factories ----

    RibbonToggleButton Toggle(OfficeIcon icon, string hint, Action<SpreadsheetController> action)
        => this.Track(OfficeRibbonItems.Toggle(icon, hint, () => this.RunCommand(action), this.IdFor(icon)));

    RibbonToggleButton LargeToggle(OfficeIcon icon, string text, string hint, Action<SpreadsheetController> action)
    {
        var toggle = this.Toggle(icon, hint, action);
        toggle.Size = RibbonItemSize.Large;
        toggle.Text = text;
        return toggle;
    }

    RibbonButton Command(OfficeIcon icon, string hint, Action<SpreadsheetController> action, string? text = null)
        => this.Track(OfficeRibbonItems.Command(icon, hint, () => this.RunCommand(action), text, this.IdFor(icon)));

    RibbonButton Large(OfficeIcon icon, string text, string hint, Action<SpreadsheetController> action)
        => this.Track(OfficeRibbonItems.LargeCommand(icon, text, hint, () => this.RunCommand(action), this.IdFor(icon)));

    RibbonMenuButton MenuButton(OfficeIcon icon, string text, string tooltip)
        => this.Track(new RibbonMenuButton
        {
            Text = text,
            Tooltip = tooltip,
            Size = RibbonItemSize.Small,
            AutomationId = $"SheetToolbar{text.Replace(" ", string.Empty, StringComparison.Ordinal)}",
            IconTemplate = OfficeRibbonItems.IconTemplateFor(icon)
        });

    RibbonSplitButton Split(OfficeIcon icon, string text, string tooltip, Action<SpreadsheetController> face, RibbonItemSize size)
        => this.Track(new RibbonSplitButton
        {
            Text = size == RibbonItemSize.Large ? text : null,
            Tooltip = tooltip,
            Size = size,
            AutomationId = $"SheetToolbar{text.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("&", string.Empty, StringComparison.Ordinal)}",
            IconTemplate = OfficeRibbonItems.IconTemplateFor(icon),
            Command = new Command(() => this.RunCommand(face))
        });

    RibbonSplitButton AutoSum(string automationId)
    {
        var split = this.Track(new RibbonSplitButton
        {
            Text = "AutoSum",
            Tooltip = "AutoSum (Alt+=)",
            Size = RibbonItemSize.Small,
            AutomationId = automationId,
            IconTemplate = OfficeRibbonItems.IconTemplateFor(OfficeIcon.Sum),
            Command = new Command(() => this.RunCommand(c => c.ApplyAutoFunction(AutoFunction.Sum)))
        });

        foreach (var function in SpreadsheetMenus.Functions)
        {
            var captured = function;
            split.Menu.Add(new RibbonMenuEntry
            {
                Text = $"{AutoFunctions.DisplayName(captured)}   {AutoFunctions.NameOf(captured)}",
                Command = new Command(() => this.RunCommand(c => c.ApplyAutoFunction(captured)))
            });
        }

        split.Menu.Add(new RibbonMenuEntry { IsSeparator = true });
        split.Menu.Add(this.Entry("More Functions...", c => c.ShowDialog(SpreadsheetDialogs.InsertFunction(c))));
        return split;
    }

    RibbonMenuEntry Entry(string text, Action<SpreadsheetController> action)
        => new() { Text = text, Command = new Command(() => this.RunCommand(action)) };

    readonly HashSet<string> ids = [];

    /// <summary>
    /// Named off the icon, the way the bar always has been — the item models are not in the visual tree,
    /// so the rendered view's id is a UI test's only handle — with a suffix for the icons used twice.
    /// </summary>
    string IdFor(OfficeIcon icon)
    {
        var id = $"SheetToolbar{icon}";
        for (var n = 2; !this.ids.Add(id); n++)
            id = $"SheetToolbar{icon}{n}";

        return id;
    }

    T Track<T>(T item) where T : RibbonItem
    {
        this.items.Add(item);
        return item;
    }

    void RunCommand(Action<SpreadsheetController> action)
    {
        if (this.controller is not { } current)
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

        var (chosen, color) = await OfficeMenus.PickHighlightAsync(OfficeMenus.PageOf(this));
        if (!chosen)
            return;

        current.SetFillColor(color);
        this.AfterCommand();
    }

    void OnControllerChanged(object? sender, EventArgs e) => this.Refresh();

    void AfterCommand()
    {
        this.Refresh();
        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Puts the ribbon on the same appearance as a pinned <see cref="Theme"/>.</summary>
    void OnThemeChanged()
    {
        OfficeScheme.ScopeTokens(this, this.Theme);
        this.Refresh();
    }

    /// <summary>Reflects the active cell's formatting and the sheet's state back into the bar.</summary>
    void Refresh()
    {
        if (this.ribbon is null)
            return;

        var current = this.controller;
        var format = current?.ActiveFormat ?? ResolvedFormat.Default;
        var enabled = current is not null && !this.IsReadOnly;

        this.IsVisible = current is not null;

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

        this.merge.Tooltip = current?.IsActiveCellMerged == true ? "Unmerge Cells" : "Merge & Center";

        this.filter.IsChecked = current?.HasAutoFilter ?? false;
        this.filterEntry.IsChecked = current?.HasAutoFilter ?? false;
        this.convertToRange.IsVisible = current?.ActiveTable is not null;
        this.showFormulas.IsChecked = current?.ShowFormulas ?? false;
        this.viewFormulas.IsChecked = current?.ShowFormulas ?? false;
        this.gridlines.IsChecked = current?.ShowGridlines ?? true;
        this.headings.IsChecked = current?.ShowHeadings ?? true;
        this.formulaBar.IsChecked = this.IsFormulaBarVisible;
        this.showAllNotes.IsChecked = current?.ShowAllNotes ?? false;
        this.deleteNote.IsEnabled = enabled && current?.ActiveNote is not null;
        this.newNote.Text = current?.ActiveNote is null ? "New Note" : "Edit Note";

        this.fill.IsActive = !format.Background.IsTransparent;
        this.fill.SetEnabled(enabled);
        this.fill.SetTooltipEnabled(this.ShowTooltips);

        foreach (var item in this.items)
        {
            if (item == this.deleteNote)
                continue;

            item.IsEnabled = current is not null && (enabled || this.readOnlySafe.Contains(item));
        }

        this.formulaBar.IsEnabled = true;
        this.watermark.IsEnabled = current is not null && !this.IsReadOnly;

        // Paste has a precondition of its own: there has to be something held.
        this.paste.IsEnabled = enabled && (current?.CanPaste ?? false);
        this.undo.IsEnabled = enabled && (current?.CanUndo ?? false);
        this.redo.IsEnabled = enabled && (current?.CanRedo ?? false);

        this.RebuildMenus();

        // Writing a picker's selection raises its change event, which would re-apply the format being shown.
        this.suppressPickerEvents = true;

        if (this.fontPicker is not null)
        {
            this.fontPicker.SelectedFont = format.FontName;
            this.fontPicker.IsEnabled = enabled;
        }

        if (this.sizePicker is not null)
        {
            var sizes = this.FontSizes ?? DefaultFontSizes;
            this.sizePicker.SelectedFontSize = sizes.OrderBy(x => Math.Abs(x - format.FontSize)).FirstOrDefault();
            this.sizePicker.IsEnabled = enabled;
        }

        this.textColor.SelectedColor = Color.FromRgba(format.Foreground.R, format.Foreground.G, format.Foreground.B, format.Foreground.A);
        this.textColor.IsEnabled = enabled;

        this.suppressPickerEvents = false;

        this.findBar.IsEnabled = current is not null;
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
    /// Defaults to <see cref="OfficeAccent.Spreadsheet"/> — Excel green. Set it to take on the app's own
    /// brand instead, or to <c>null</c> to leave the bar on the theme's neutrals.
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

    /// <summary>Picks a picture for the watermark, or clears one already there.</summary>
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
