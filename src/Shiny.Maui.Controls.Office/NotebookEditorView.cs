using Shiny.Controls.Office.Icons;
using Shiny.Controls.Office.Notebook;
using Shiny.Controls.Office.Packaging;
using Shiny.Controls.Office.Shapes;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;
using Shiny.Controls.Office.Theming;
using Shiny.Maui.Controls.ColorPicker;
using Shiny.Maui.Controls.FontPicker;
using Shiny.Maui.Controls.Ribbons;
using TextAlignment = Shiny.Controls.Office.Text.TextAlignment;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// <see cref="NotebookEditor"/> with the notebook chrome around it — the ribbon, the section tabs
/// across the top and the page list down the side.
/// </summary>
/// <remarks>
/// <para>
/// Four ribbon tabs, and the split is the one OneNote makes. <b>Home</b> is the text on the page.
/// <b>Draw</b> is the pen, and it is a set of toggles rather than commands because every one of them
/// puts the pointer into a mode that stays until it is changed. <b>Insert</b> is what goes on the page.
/// <b>View</b> is zoom and the page's rule.
/// </para>
/// <para>
/// The navigation is deliberately two levels shown at once — sections across, pages down — rather than
/// a tree. A tree makes finding a page a two-step expand-then-pick, and the whole point of the section
/// tab is that the pages of the one you are in are always in front of you.
/// </para>
/// </remarks>
public class NotebookEditorView : ContentView, IDisposable
{
    readonly NotebookEditor editor = new();
    readonly Ribbon ribbon;
    readonly Grid root;
    readonly Grid body;
    readonly Label status;
    readonly HorizontalStackLayout sectionTabs;
    readonly VerticalStackLayout pageList;
    readonly ScrollView pageScroller;

    readonly RibbonToggleButton bold;
    readonly RibbonToggleButton italic;
    readonly RibbonToggleButton underline;
    readonly RibbonToggleButton strike;
    readonly RibbonToggleButton alignLeft;
    readonly RibbonToggleButton alignCenter;
    readonly RibbonToggleButton alignRight;
    readonly RibbonToggleButton bulletList;
    readonly RibbonToggleButton numberedList;
    readonly RibbonButton outdent;
    readonly RibbonButton indent;
    readonly RibbonToggleButton highlight;

    readonly RibbonToggleButton selectTool;
    readonly RibbonToggleButton penTool;
    readonly RibbonToggleButton highlighterTool;
    readonly RibbonToggleButton eraserTool;
    readonly RibbonToggleButton lassoTool;
    readonly RibbonToggleButton panTool;
    readonly ColorPickerButton penColor;

    readonly RibbonButton addTextBox;
    readonly RibbonButton insertPicture;
    readonly RibbonButton deleteItem;
    readonly RibbonButton duplicate;
    readonly RibbonButton bringToFront;
    readonly RibbonButton sendToBack;
    readonly RibbonButton newPage;
    readonly RibbonButton newSection;
    readonly RibbonButton pageRule;
    readonly RibbonButton zoomIn;
    readonly RibbonButton zoomOut;
    readonly RibbonButton undo;
    readonly RibbonButton redo;
    readonly ColorPickerButton textColor;
    readonly List<RibbonItem> buttons = [];

    View? fontPicker;
    View? sizePicker;
    bool suppressPickerEvents;
    bool disposed;

    public NotebookEditorView()
    {
        this.bold = this.MakeToggle(OfficeIcon.Bold, "Bold (Ctrl+B)", () => this.editor.Controller?.ToggleBold());
        this.italic = this.MakeToggle(OfficeIcon.Italic, "Italic (Ctrl+I)", () => this.editor.Controller?.ToggleItalic());
        this.underline = this.MakeToggle(OfficeIcon.Underline, "Underline (Ctrl+U)", () => this.editor.Controller?.ToggleUnderline());
        this.strike = this.MakeToggle(OfficeIcon.Strikethrough, "Strikethrough", () => this.editor.Controller?.ToggleStrikethrough());

        this.alignLeft = this.MakeToggle(OfficeIcon.AlignLeft, "Align left", () => this.editor.Controller?.SetAlignment(TextAlignment.Left));
        this.alignCenter = this.MakeToggle(OfficeIcon.AlignCenter, "Centre", () => this.editor.Controller?.SetAlignment(TextAlignment.Center));
        this.alignRight = this.MakeToggle(OfficeIcon.AlignRight, "Align right", () => this.editor.Controller?.SetAlignment(TextAlignment.Right));

        this.bulletList = this.MakeToggle(OfficeIcon.BulletList, "Bulleted list", () => this.editor.Controller?.ToggleBulletList());
        this.numberedList = this.MakeToggle(OfficeIcon.NumberedList, "Numbered list", () => this.editor.Controller?.ToggleNumberedList());
        this.outdent = this.MakeButton(OfficeIcon.Outdent, "Outdent (Shift+Tab)", () => this.editor.Controller?.ShiftLevel(-1));
        this.indent = this.MakeButton(OfficeIcon.Indent, "Indent (Tab)", () => this.editor.Controller?.ShiftLevel(1));
        this.highlight = this.MakeToggle(OfficeIcon.Highlight, "Highlight text", this.ToggleTextHighlight);

        this.selectTool = this.MakeToolToggle(OfficeIcon.Pointer, "Select", NoteTool.Select);
        this.penTool = this.MakeToolToggle(OfficeIcon.Pen, "Pen", NoteTool.Pen);
        this.highlighterTool = this.MakeToolToggle(OfficeIcon.Highlight, "Highlighter", NoteTool.Highlighter);
        this.eraserTool = this.MakeToolToggle(OfficeIcon.Eraser, "Eraser", NoteTool.Eraser);
        this.lassoTool = this.MakeToolToggle(OfficeIcon.Lasso, "Lasso select", NoteTool.Lasso);
        this.panTool = this.MakeToolToggle(OfficeIcon.Hand, "Pan", NoteTool.Pan);

        this.addTextBox = this.MakeButton(OfficeIcon.TextBox, "Add a text container", () => this.editor.InsertTextBox());
        this.insertPicture = this.MakeAsyncButton(OfficeIcon.Picture, "Picture", this.InsertPictureAsync);
        this.deleteItem = this.MakeButton(OfficeIcon.Delete, "Delete the selection", () => this.editor.Controller?.DeleteSelection());
        this.duplicate = this.MakeButton(OfficeIcon.Duplicate, "Duplicate (Ctrl+D)", () => this.editor.Controller?.DuplicateSelection());
        this.bringToFront = this.MakeButton(OfficeIcon.BringToFront, "Bring to front", () => this.editor.Controller?.BringToFront());
        this.sendToBack = this.MakeButton(OfficeIcon.SendToBack, "Send to back", () => this.editor.Controller?.SendToBack());

        this.newPage = this.MakeButton(OfficeIcon.NewPage, "Add a page", () => this.editor.Controller?.AddPage());
        this.newSection = this.MakeButton(OfficeIcon.NewSection, "Add a section", () => this.editor.Controller?.AddSection());
        this.pageRule = this.MakeButton(OfficeIcon.PageRule, "Page rule", this.CyclePageRule);

        this.zoomIn = this.MakeButton(OfficeIcon.ZoomIn, "Zoom in", () => this.editor.ZoomIn());
        this.zoomOut = this.MakeButton(OfficeIcon.ZoomOut, "Zoom out", () => this.editor.ZoomOut());

        this.undo = this.MakeButton(OfficeIcon.Undo, "Undo (Ctrl+Z)", () => this.editor.Controller?.Undo());
        this.redo = this.MakeButton(OfficeIcon.Redo, "Redo (Ctrl+Shift+Z)", () => this.editor.Controller?.Redo());

        this.textColor = this.CreateColorPicker(color => this.editor.Controller?.SetTextColor(color));

        // The pen's colour, not the text's. Two swatches on one bar would be a genuine ambiguity, so
        // they live on different tabs — this one only ever appears beside the pens.
        this.penColor = this.CreateColorPicker(color =>
        {
            if (this.editor.Controller is not { } controller)
                return;

            if (controller.Tool == NoteTool.Highlighter)
                controller.HighlighterColor = color with { A = 110 };
            else
                controller.PenColor = color;

            // Recolouring with ink already selected is the obvious way to fix a stroke drawn in the
            // wrong colour, so the swatch does both jobs rather than only arming the next stroke.
            if (controller.HasSelection)
                controller.SetSelectionInkColor(controller.Tool == NoteTool.Highlighter ? color with { A = 110 } : color);
        });

        this.status = new Label
        {
            FontSize = 12,
            Opacity = 0.7,
            Padding = new Thickness(10, 4),
            LineBreakMode = LineBreakMode.TailTruncation
        };

        this.ribbon = new Ribbon
        {
            SmallItemRows = 2,
            SmallItemRowHeight = 32,
            AllowGroupCollapse = true,
            SimplifyBelowWidth = 600
        };

        this.sectionTabs = new HorizontalStackLayout { Spacing = 2, Padding = new Thickness(6, 4) };

        this.pageList = new VerticalStackLayout { Spacing = 2, Padding = new Thickness(6) };
        this.pageScroller = new ScrollView
        {
            Content = this.pageList,
            WidthRequest = PageListWidth,
            Orientation = ScrollOrientation.Vertical
        };

        this.body = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };

        this.body.Add(this.editor);
        this.body.Add(this.pageScroller);
        Grid.SetColumn(this.pageScroller, 1);

        this.root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            }
        };

        var tabScroller = new ScrollView
        {
            Content = this.sectionTabs,
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never
        };

        this.root.Add(this.ribbon);
        this.root.Add(tabScroller);
        this.root.Add(this.body);
        this.root.Add(this.status);
        Grid.SetRow(tabScroller, 1);
        Grid.SetRow(this.body, 2);
        Grid.SetRow(this.status, 3);

        this.editor.NotebookChanged += this.OnNotebookChanged;
        this.editor.PageChanged += this.OnPageChanged;

        this.Content = this.root;
        this.BuildBar();

        // Last, not beside the ribbon it paints: ApplyAccent ends in RefreshNavigation, which reads
        // the section tabs and the page list. Called before those exist and the constructor throws.
        this.ApplyAccent();
    }

    const double PageListWidth = 190;

    // ---- bindable surface ----

    public static readonly BindableProperty NotebookProperty = BindableProperty.Create(
        nameof(Notebook),
        typeof(NotebookDocument),
        typeof(NotebookEditorView),
        propertyChanged: (b, old, value) =>
        {
            var view = (NotebookEditorView)b;
            view.Detach(old as NotebookDocument);
            view.editor.Notebook = (NotebookDocument?)value;
            view.Attach(value as NotebookDocument);
        });

    public static readonly BindableProperty ThemeProperty = BindableProperty.Create(
        nameof(Theme),
        typeof(NotebookTheme),
        typeof(NotebookEditorView),
        null,
        propertyChanged: (b, _, value) => ((NotebookEditorView)b).editor.Theme = (NotebookTheme?)value);

    public static readonly BindableProperty IsReadOnlyProperty = BindableProperty.Create(
        nameof(IsReadOnly),
        typeof(bool),
        typeof(NotebookEditorView),
        false,
        propertyChanged: (b, _, value) =>
        {
            var view = (NotebookEditorView)b;
            view.editor.IsReadOnly = (bool)value;
            view.RefreshBar();
        });

    public static readonly BindableProperty ShowToolbarProperty = BindableProperty.Create(
        nameof(ShowToolbar),
        typeof(bool),
        typeof(NotebookEditorView),
        true,
        propertyChanged: (b, _, value) => ((NotebookEditorView)b).ribbon.IsVisible = (bool)value);

    /// <summary>The section tabs and the page list. Off leaves the bare canvas plus its toolbar.</summary>
    public static readonly BindableProperty ShowNavigationProperty = BindableProperty.Create(
        nameof(ShowNavigation),
        typeof(bool),
        typeof(NotebookEditorView),
        true,
        propertyChanged: (b, _, _) => ((NotebookEditorView)b).RefreshNavigation());

    public static readonly BindableProperty ShowStatusProperty = BindableProperty.Create(
        nameof(ShowStatus),
        typeof(bool),
        typeof(NotebookEditorView),
        true,
        propertyChanged: (b, _, value) => ((NotebookEditorView)b).status.IsVisible = (bool)value);

    public static readonly BindableProperty AccentProperty = BindableProperty.Create(
        nameof(Accent),
        typeof(OfficeAccent),
        typeof(NotebookEditorView),
        OfficeAccent.Notebook,
        propertyChanged: (b, _, _) => ((NotebookEditorView)b).ApplyAccent());

    public static readonly BindableProperty FontFamiliesProperty = BindableProperty.Create(
        nameof(FontFamilies),
        typeof(IList<string>),
        typeof(NotebookEditorView),
        propertyChanged: (b, _, _) => ((NotebookEditorView)b).BuildBar());

    public static readonly BindableProperty FontSizesProperty = BindableProperty.Create(
        nameof(FontSizes),
        typeof(IList<double>),
        typeof(NotebookEditorView),
        propertyChanged: (b, _, _) => ((NotebookEditorView)b).BuildBar());

    public NotebookDocument? Notebook
    {
        get => (NotebookDocument?)this.GetValue(NotebookProperty);
        set => this.SetValue(NotebookProperty, value);
    }

    public NotebookTheme? Theme
    {
        get => (NotebookTheme?)this.GetValue(ThemeProperty);
        set => this.SetValue(ThemeProperty, value);
    }

    public bool IsReadOnly
    {
        get => (bool)this.GetValue(IsReadOnlyProperty);
        set => this.SetValue(IsReadOnlyProperty, value);
    }

    public bool ShowToolbar
    {
        get => (bool)this.GetValue(ShowToolbarProperty);
        set => this.SetValue(ShowToolbarProperty, value);
    }

    /// <inheritdoc cref="ShowNavigationProperty"/>
    public bool ShowNavigation
    {
        get => (bool)this.GetValue(ShowNavigationProperty);
        set => this.SetValue(ShowNavigationProperty, value);
    }

    public bool ShowStatus
    {
        get => (bool)this.GetValue(ShowStatusProperty);
        set => this.SetValue(ShowStatusProperty, value);
    }

    /// <inheritdoc cref="OfficeAccent"/>
    public OfficeAccent? Accent
    {
        get => (OfficeAccent?)this.GetValue(AccentProperty);
        set => this.SetValue(AccentProperty, value);
    }

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

    /// <summary>The live controller, for a host driving the canvas from its own chrome.</summary>
    public NotebookEditorController? Controller => this.editor.Controller;

    /// <summary>Raised after every edit.</summary>
    public event EventHandler? NotebookChanged;

    /// <summary>Raised when a chosen file could not be inserted, so a host can say so.</summary>
    public event EventHandler<OfficeDropRejected>? DropRejected;

    /// <inheritdoc cref="NotebookEditor.HandleKey(EditorKey, bool, bool)"/>
    public bool HandleKey(EditorKey key, bool shift = false, bool control = false)
    {
        var handled = this.editor.HandleKey(key, shift, control);
        if (handled)
            this.RefreshBar();

        return handled;
    }

    public void FocusEditor() => this.editor.FocusEditor();

    // ---- ribbon ----

    void BuildBar()
    {
        this.ribbon.Tabs.Clear();

        this.fontPicker = this.CreateFontPicker();
        this.sizePicker = this.CreateSizePicker();

        this.ribbon.QuickAccessItems.Clear();
        this.ribbon.QuickAccessItems.Add(this.undo);
        this.ribbon.QuickAccessItems.Add(this.redo);

        var home = new RibbonTab { Title = "Home", Key = "home" };

        var font = new RibbonGroup { Title = "Font", Priority = 100 };

        if (this.fontPicker is not null)
            font.Items.Add(OfficeRibbonItems.Host(this.fontPicker));

        if (this.sizePicker is not null)
            font.Items.Add(OfficeRibbonItems.Host(this.sizePicker));

        font.Items.Add(this.bold);
        font.Items.Add(this.italic);
        font.Items.Add(this.underline);
        font.Items.Add(this.strike);
        font.Items.Add(OfficeRibbonItems.Host(this.textColor));
        font.Items.Add(this.highlight);
        home.Groups.Add(font);

        var paragraph = new RibbonGroup { Title = "Paragraph", Priority = 90 };
        paragraph.Items.Add(this.alignLeft);
        paragraph.Items.Add(this.alignCenter);
        paragraph.Items.Add(this.alignRight);
        paragraph.Items.Add(new RibbonSeparator());
        paragraph.Items.Add(this.bulletList);
        paragraph.Items.Add(this.numberedList);
        paragraph.Items.Add(this.outdent);
        paragraph.Items.Add(this.indent);
        home.Groups.Add(paragraph);

        var arrange = new RibbonGroup { Title = "Arrange", Priority = 70 };
        arrange.Items.Add(this.bringToFront);
        arrange.Items.Add(this.sendToBack);
        arrange.Items.Add(this.duplicate);
        arrange.Items.Add(new RibbonSeparator());
        arrange.Items.Add(this.deleteItem);
        home.Groups.Add(arrange);

        this.ribbon.Tabs.Add(home);

        // A tab of its own rather than a group on Home, because these are the only controls here that
        // change what the *pointer* does. Mixed in among Bold and Centre they read as commands, and a
        // user who clicks the pen and then wonders why clicking a word no longer selects it has been
        // misled by the layout.
        var draw = new RibbonTab { Title = "Draw", Key = "draw" };

        var tools = new RibbonGroup { Title = "Tools", Priority = 100 };
        tools.Items.Add(this.selectTool);
        tools.Items.Add(this.lassoTool);
        tools.Items.Add(this.panTool);
        tools.Items.Add(new RibbonSeparator());
        tools.Items.Add(this.penTool);
        tools.Items.Add(this.highlighterTool);
        tools.Items.Add(this.eraserTool);
        draw.Groups.Add(tools);

        var pen = new RibbonGroup { Title = "Pen", Priority = 90 };
        pen.Items.Add(OfficeRibbonItems.Host(this.penColor));
        pen.Items.Add(OfficeRibbonItems.Host(this.CreateWidthPicker()));
        draw.Groups.Add(pen);

        this.ribbon.Tabs.Add(draw);

        var insertTab = new RibbonTab { Title = "Insert", Key = "insert" };
        var insert = new RibbonGroup { Title = "Insert", Priority = 100 };
        insert.Items.Add(this.addTextBox);
        insert.Items.Add(this.insertPicture);
        insertTab.Groups.Add(insert);

        var pages = new RibbonGroup { Title = "Pages", Priority = 90 };
        pages.Items.Add(this.newPage);
        pages.Items.Add(this.newSection);
        insertTab.Groups.Add(pages);

        this.ribbon.Tabs.Add(insertTab);
        this.ribbon.Tabs.Add(OfficeRibbonItems.ShapesTab(g => this.editor.InsertShape(g)));

        var viewTab = new RibbonTab { Title = "View", Key = "view" };
        var view = new RibbonGroup { Title = "View", Priority = 100 };
        view.Items.Add(this.zoomOut);
        view.Items.Add(this.zoomIn);
        view.Items.Add(new RibbonSeparator());
        view.Items.Add(this.pageRule);
        viewTab.Groups.Add(view);
        this.ribbon.Tabs.Add(viewTab);

        this.RefreshBar();
    }

    RibbonButton MakeButton(OfficeIcon icon, string hint, Action action)
        => this.Track(OfficeRibbonItems.Command(icon, hint, () =>
        {
            action();
            this.AfterCommand();
        }, automationId: $"NotebookToolbar{icon}"));

    /// <summary>
    /// A command whose work opens a menu or a file picker first.
    /// </summary>
    /// <remarks>
    /// It does not call <c>AfterCommand</c> itself: each waits for the user to choose something, and
    /// refreshing the bar before then would happen while the picker is still up.
    /// </remarks>
    RibbonButton MakeAsyncButton(OfficeIcon icon, string hint, Func<Task> action)
        => this.Track(OfficeRibbonItems.Command(icon, hint, () => _ = action(), automationId: $"NotebookToolbar{icon}"));

    RibbonToggleButton MakeToggle(OfficeIcon icon, string hint, Action action)
        => this.Track(OfficeRibbonItems.Toggle(icon, hint, () =>
        {
            action();
            this.AfterCommand();
        }, $"NotebookToolbar{icon}"));

    /// <summary>
    /// A toggle that selects a tool.
    /// </summary>
    /// <remarks>
    /// Clicking the tool that is already active goes back to Select rather than doing nothing, so the
    /// pen has a way out that does not require finding the arrow — which matters because the pen is
    /// the one tool where the way out is not otherwise obvious.
    /// </remarks>
    RibbonToggleButton MakeToolToggle(OfficeIcon icon, string hint, NoteTool tool)
        => this.Track(OfficeRibbonItems.Toggle(icon, hint, () =>
        {
            if (this.editor.Controller is { } controller)
                controller.Tool = controller.Tool == tool ? NoteTool.Select : tool;

            this.AfterCommand();
        }, $"NotebookTool{tool}"));

    T Track<T>(T item) where T : RibbonItem
    {
        this.buttons.Add(item);
        return item;
    }

    void AfterCommand()
    {
        // Focus returns to the canvas after every toolbar action; left on the button the next
        // keystroke goes nowhere, which reads as the editor having stopped working.
        this.editor.FocusEditor();
        this.RefreshBar();
        this.NotebookChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reflects the state under the caret, and the current tool, back into the toolbar.</summary>
    void RefreshBar()
    {
        var controller = this.editor.Controller;
        var format = controller?.CaretFormat ?? NoteCaretFormat.Default;

        var enabled = !this.IsReadOnly && this.Notebook is not null;
        var hasSelection = enabled && controller?.HasSelection == true;

        // Text formatting reaches a selected container as well as a caret inside one, so it stays live
        // for both — a shape with a label should go bold from one click.
        var hasText = enabled && (controller?.IsEditingText == true || hasSelection);

        this.bold.IsChecked = format.Bold;
        this.italic.IsChecked = format.Italic;
        this.underline.IsChecked = format.Underline;
        this.strike.IsChecked = format.Strike;
        this.highlight.IsChecked = format.Highlight is not null;

        this.alignLeft.IsChecked = format.Alignment == TextAlignment.Left;
        this.alignCenter.IsChecked = format.Alignment == TextAlignment.Center;
        this.alignRight.IsChecked = format.Alignment == TextAlignment.Right;

        this.bulletList.IsChecked = format.List == ListStyle.Bullet;
        this.numberedList.IsChecked = format.List == ListStyle.Numbered;

        var tool = controller?.Tool ?? NoteTool.Select;
        this.selectTool.IsChecked = tool == NoteTool.Select;
        this.penTool.IsChecked = tool == NoteTool.Pen;
        this.highlighterTool.IsChecked = tool == NoteTool.Highlighter;
        this.eraserTool.IsChecked = tool == NoteTool.Eraser;
        this.lassoTool.IsChecked = tool == NoteTool.Lasso;
        this.panTool.IsChecked = tool == NoteTool.Pan;

        foreach (var item in this.buttons)
            item.IsEnabled = enabled;

        this.bold.IsEnabled = hasText;
        this.italic.IsEnabled = hasText;
        this.underline.IsEnabled = hasText;
        this.strike.IsEnabled = hasText;
        this.highlight.IsEnabled = hasText;
        this.alignLeft.IsEnabled = hasText;
        this.alignCenter.IsEnabled = hasText;
        this.alignRight.IsEnabled = hasText;
        this.bulletList.IsEnabled = hasText;
        this.numberedList.IsEnabled = hasText;
        this.outdent.IsEnabled = hasText;
        this.indent.IsEnabled = hasText;

        this.deleteItem.IsEnabled = hasSelection;
        this.duplicate.IsEnabled = hasSelection;
        this.bringToFront.IsEnabled = hasSelection;
        this.sendToBack.IsEnabled = hasSelection;

        this.undo.IsEnabled = enabled && controller?.CanUndo == true;
        this.redo.IsEnabled = enabled && controller?.CanRedo == true;

        this.suppressPickerEvents = true;
        this.textColor.SelectedColor = ToColor(format.Color);

        if (controller is { } live)
        {
            this.penColor.SelectedColor = ToColor(
                live.Tool == NoteTool.Highlighter ? live.HighlighterColor with { A = 255 } : live.PenColor);
        }

        this.suppressPickerEvents = false;

        this.status.Text = this.StatusText(controller);
    }

    string StatusText(NotebookEditorController? controller)
    {
        if (controller is not { Page: { } page })
            return string.Empty;

        var tool = controller.Tool switch
        {
            NoteTool.Pen => "Pen — drag to draw",
            NoteTool.Highlighter => "Highlighter — drag to mark",
            NoteTool.Eraser => "Eraser — drag over ink to remove it",
            NoteTool.Lasso => "Lasso — circle what you want to select",
            NoteTool.Pan => "Pan — drag to move the page",
            NoteTool.Text => "Text — click to start writing",
            NoteTool.Shape => "Shape — drag out the shape",
            _ => controller.IsEditingText
                ? "Typing — Escape leaves the container"
                : "Select — click to pick, double-click to write, drag empty space to marquee"
        };

        var count = controller.SelectedIds.Count;
        var selection = count switch
        {
            0 => string.Empty,
            1 => "  ·  1 item selected",
            _ => $"  ·  {count} items selected"
        };

        return $"{page.Title}  ·  {Math.Round(controller.Zoom * 100)}%  ·  {tool}{selection}";
    }

    // ---- navigation ----

    void Attach(NotebookDocument? document)
    {
        if (document is not null)
            document.StructureChanged += this.OnStructureChanged;

        if (this.editor.Controller is { } controller)
            controller.Changed += this.OnControllerChanged;

        this.RefreshNavigation();
        this.RefreshBar();
    }

    void Detach(NotebookDocument? document)
    {
        if (document is not null)
            document.StructureChanged -= this.OnStructureChanged;

        if (this.editor.Controller is { } controller)
            controller.Changed -= this.OnControllerChanged;
    }

    void OnStructureChanged(object? sender, EventArgs e) => this.RefreshNavigation();

    void OnControllerChanged(object? sender, EventArgs e) => this.RefreshBar();

    void OnNotebookChanged(object? sender, EventArgs e)
    {
        this.RefreshBar();
        this.NotebookChanged?.Invoke(this, EventArgs.Empty);
    }

    void OnPageChanged(object? sender, PageAddress address)
    {
        this.RefreshNavigation();
        this.RefreshBar();
    }

    /// <summary>
    /// Rebuilds the section tabs and the page list from the document.
    /// </summary>
    /// <remarks>
    /// Rebuilt wholesale rather than bound to a collection, and not through a <c>CollectionView</c>:
    /// these are short lists that change shape — a rename, a reorder, a delete that refills an empty
    /// section — and a recycling list view is the wrong tool for a set small enough to lay out in full
    /// and structural enough that recycled rows go stale.
    /// </remarks>
    void RefreshNavigation()
    {
        this.sectionTabs.IsVisible = this.ShowNavigation;
        this.pageScroller.IsVisible = this.ShowNavigation;

        this.sectionTabs.Clear();
        this.pageList.Clear();

        if (!this.ShowNavigation || this.editor.Controller is not { } controller)
            return;

        var accent = this.Accent?.Color ?? OfficeAccent.Notebook.Color;

        for (var i = 0; i < controller.Document.Sections.Count; i++)
        {
            var index = i;
            var section = controller.Document.Sections[i];
            var current = index == controller.Address.Section;

            this.sectionTabs.Add(this.MakeTab(
                section.Title,
                current,
                section.Color ?? accent,
                () => controller.Address = new PageAddress(index, 0)));
        }

        if (controller.Section is not { } activeSection)
            return;

        for (var i = 0; i < activeSection.Pages.Count; i++)
        {
            var index = i;
            var page = activeSection.Pages[i];

            this.pageList.Add(this.MakePageRow(
                page,
                index == controller.Address.Page,
                accent,
                () => controller.Address = controller.Address with { Page = index }));
        }
    }

    View MakeTab(string title, bool current, ArgbColor color, Action tapped)
    {
        var label = new Label
        {
            Text = title,
            FontSize = 13,
            FontAttributes = current ? FontAttributes.Bold : FontAttributes.None,
            VerticalTextAlignment = Microsoft.Maui.TextAlignment.Center
        };

        var border = new Border
        {
            Content = label,
            Padding = new Thickness(12, 6),
            StrokeThickness = 0,
            BackgroundColor = current ? ToColor(color with { A = 46 }) : Colors.Transparent,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(6, 6, 0, 0) }
        };

        // The colour bar, which is the only thing carrying a section's identity once the tab is not
        // the current one.
        var strip = new BoxView
        {
            Color = ToColor(color),
            HeightRequest = 3,
            Opacity = current ? 1 : 0.45
        };

        var stack = new VerticalStackLayout { Spacing = 0 };
        stack.Add(border);
        stack.Add(strip);

        AddTap(stack, tapped);

        return stack;
    }

    View MakePageRow(NotebookPage page, bool current, ArgbColor accent, Action tapped)
    {
        var title = new Label
        {
            Text = string.IsNullOrWhiteSpace(page.Title) ? "Untitled page" : page.Title,
            FontSize = 13,
            FontAttributes = current ? FontAttributes.Bold : FontAttributes.None,
            LineBreakMode = LineBreakMode.TailTruncation
        };

        var when = new Label
        {
            Text = page.Modified.ToLocalTime().ToString("d MMM"),
            FontSize = 11,
            Opacity = 0.6
        };

        var stack = new VerticalStackLayout { Spacing = 1 };
        stack.Add(title);
        stack.Add(when);

        var border = new Border
        {
            Content = stack,
            Padding = new Thickness(10, 7),
            StrokeThickness = 0,
            BackgroundColor = current ? ToColor(accent with { A = 38 }) : Colors.Transparent,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(6) }
        };

        AddTap(border, tapped);

        return border;
    }

    /// <summary>
    /// Wires a tap onto a plain view.
    /// </summary>
    /// <remarks>
    /// On <c>Command</c> rather than the <c>Tapped</c> event, because <c>Tapped</c> cannot be raised
    /// from a test and a command can — which is what makes the navigation assertable without a device.
    /// </remarks>
    static void AddTap(View view, Action action)
        => view.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(action) });

    // ---- pickers ----

    ColorPickerButton CreateColorPicker(Action<ArgbColor> apply)
    {
        var picker = new ColorPickerButton
        {
            Text = string.Empty,
            ShowOpacity = false,
            WidthRequest = 44,
            HeightRequest = ToolbarItemHeight,
            VerticalOptions = LayoutOptions.Center
        };

        picker.ColorChanged += (_, color) =>
        {
            if (this.suppressPickerEvents)
                return;

            apply(ToArgb(color));
            this.AfterCommand();
        };

        return picker;
    }

    /// <summary>The pen's nib width. A picker rather than a slider: four named weights is the whole range.</summary>
    View CreateWidthPicker()
    {
        var picker = new Picker
        {
            WidthRequest = 92,
            HeightRequest = ToolbarItemHeight,
            VerticalOptions = LayoutOptions.Center,
            ItemsSource = new List<string> { "Fine", "Medium", "Bold", "Marker" }
        };

        picker.SelectedIndex = 1;
        picker.SelectedIndexChanged += (_, _) =>
        {
            if (this.suppressPickerEvents || this.editor.Controller is not { } controller)
                return;

            var width = picker.SelectedIndex switch { 0 => 1.2, 2 => 3.6, 3 => 6.5, _ => 2.2 };

            if (controller.Tool == NoteTool.Highlighter)
                controller.HighlighterWidth = width * 6;
            else
                controller.PenWidth = width;

            this.AfterCommand();
        };

        return picker;
    }

    View? CreateFontPicker()
    {
        var picker = new FontPickerButton
        {
            AvailableFonts = (this.FontFamilies ?? DefaultFontFamilies).ToList(),
            Placeholder = "Font",
            WidthRequest = 150,
            HeightRequest = ToolbarItemHeight,
            VerticalOptions = LayoutOptions.Center
        };

        picker.FontChanged += (_, family) =>
        {
            if (this.suppressPickerEvents || string.IsNullOrWhiteSpace(family))
                return;

            this.editor.Controller?.SetFontFamily(family);
            this.AfterCommand();
        };

        return picker;
    }

    View? CreateSizePicker()
    {
        var picker = new FontSizePickerButton
        {
            AvailableFontSizes = (this.FontSizes ?? DefaultFontSizes).ToList(),
            WidthRequest = 84,
            HeightRequest = ToolbarItemHeight,
            VerticalOptions = LayoutOptions.Center
        };

        picker.FontSizeChanged += (_, size) =>
        {
            if (this.suppressPickerEvents || size <= 0)
                return;

            this.editor.Controller?.SetFontSize(size);
            this.AfterCommand();
        };

        return picker;
    }

    // Notebook type runs at document sizes rather than deck sizes: a note is read at arm's length,
    // not from the back of a room.
    static readonly IList<double> DefaultFontSizes = [8, 9, 10, 11, 12, 14, 16, 18, 20, 24, 28, 36, 48];

    static readonly IList<string> DefaultFontFamilies =
        ["Calibri", "Cambria", "Arial", "Times New Roman", "Georgia", "Verdana", "Courier New"];

    const double ToolbarItemHeight = OfficeToolbarButton.ItemHeight;

    // ---- commands with a bit of work behind them ----

    void ToggleTextHighlight()
    {
        if (this.editor.Controller is not { } controller)
            return;

        controller.ToggleHighlight(HighlightPalette.Swatches[0].Color);
    }

    /// <summary>
    /// Steps the page's rule.
    /// </summary>
    /// <remarks>
    /// A cycle rather than a menu because there are four of them and the button already shows the
    /// page: clicking through blank, lined, grid and dots is faster than opening a list of four, and
    /// the status line names the one you have landed on.
    /// </remarks>
    void CyclePageRule()
    {
        if (this.editor.Controller is not { Page: { } page } controller)
            return;

        var next = page.Rule switch
        {
            PageRule.Blank => PageRule.Lines,
            PageRule.Lines => PageRule.Grid,
            PageRule.Grid => PageRule.Dots,
            _ => PageRule.Blank
        };

        controller.SetPageRule(next);
    }

    async Task InsertPictureAsync()
    {
        try
        {
            var picked = await MediaPicker.Default.PickPhotoAsync().ConfigureAwait(true);
            if (picked is null)
                return;

            var contentType = ImageContentTypes.Resolve(picked.FileName, picked.ContentType);
            if (contentType is null)
            {
                this.DropRejected?.Invoke(this, new OfficeDropRejected(picked.FileName, "That file is not an image the editor can embed."));
                return;
            }

            await using var stream = await picked.OpenReadAsync().ConfigureAwait(true);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer).ConfigureAwait(true);

            this.editor.InsertImage(buffer.ToArray(), contentType);
        }
        catch (Exception ex)
        {
            // A cancelled or refused pick is the common case and is not an error worth throwing from a
            // toolbar button; the host is told so it can say so if it wants to.
            this.DropRejected?.Invoke(this, new OfficeDropRejected(null, ex.Message));
        }
        finally
        {
            this.AfterCommand();
        }
    }

    // ---- theming ----

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

        this.RefreshNavigation();
    }

    static Color ToColor(ArgbColor value)
        => Color.FromRgba(value.R / 255f, value.G / 255f, value.B / 255f, value.A / 255f);

    /// <summary>MAUI colours are floats in 0..1; the kernel stores bytes.</summary>
    static ArgbColor ToArgb(Color color) => new(
        (byte)Math.Round(color.Alpha * 255),
        (byte)Math.Round(color.Red * 255),
        (byte)Math.Round(color.Green * 255),
        (byte)Math.Round(color.Blue * 255));

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

        this.Detach(this.Notebook);
        this.editor.NotebookChanged -= this.OnNotebookChanged;
        this.editor.PageChanged -= this.OnPageChanged;
        this.editor.Dispose();
    }
}
