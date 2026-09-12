using Shiny.Controls.Office.Document;
using Shiny.Controls.Office.Icons;
using Shiny.Controls.Office.Packaging;
using Shiny.Controls.Office.Shapes;
using Shiny.Maui.Controls.ColorPicker;
using Shiny.Maui.Controls.Ribbons;
using Shiny.Maui.Controls.FontPicker;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;
using TextAlignment = Shiny.Controls.Office.Text.TextAlignment;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// <see cref="DocumentEditor"/> with a formatting toolbar above it.
/// </summary>
/// <remarks>
/// <para>
/// The toolbar is built from MAUI primitives plus the core package's <c>FontPickerButton</c> and
/// <c>FontSizePickerButton</c>. MAUI has no <c>ShinyToolbar</c> — that control is Blazor-only — so the
/// bar itself is a scrolling row here, while the Blazor <c>DocumentEditorView</c> composes ShinyToolbar
/// for the same two slots. The API and behaviour match; only the internals differ.
/// </para>
/// <para>
/// Every plain button on it is an <see cref="OfficeToolbarButton"/> drawing from the shared
/// <see cref="OfficeIcons"/> set — one monochrome stroked weight, no colour of its own, the same
/// artwork the Blazor toolbar renders. The pickers are the exception, because a font, a size and a
/// colour have to show what they are currently set to.
/// </para>
/// </remarks>
public class DocumentEditorView : ContentView, IDisposable
{
    readonly DocumentEditor editor = new();
    readonly Ribbon ribbon;
    readonly Grid root;

    readonly RibbonToggleButton bold;
    readonly RibbonToggleButton italic;
    readonly RibbonToggleButton underline;
    readonly RibbonToggleButton strike;
    readonly RibbonButton undo;
    readonly RibbonButton redo;
    readonly RibbonToggleButton alignLeft;
    readonly RibbonToggleButton alignCenter;
    readonly RibbonToggleButton alignRight;
    readonly RibbonToggleButton alignJustify;
    readonly RibbonToggleButton highlight;
    readonly RibbonToggleButton bulletList;
    readonly RibbonToggleButton numberedList;
    readonly RibbonButton indent;
    readonly RibbonButton outdent;
    readonly RibbonButton insertTable;
    readonly RibbonButton insertPicture;
    readonly RibbonButton watermark;
    readonly IReadOnlyList<RibbonButton> marginButtons;
    readonly RibbonButton insertHeader;
    readonly RibbonButton insertFooter;
    readonly RibbonMenuButton pageNumber;
    readonly RibbonButton pageBreak;
    readonly RibbonToggleButton printLayout;
    readonly RibbonToggleButton portrait;
    readonly RibbonToggleButton landscape;
    readonly RibbonToggleButton spellCheck;
    readonly RibbonButton zoomIn;
    readonly RibbonButton zoomOut;
    readonly RibbonButton fitWidth;
    readonly Label zoomLabel;
    readonly RibbonButton previousError;
    readonly RibbonButton nextError;
    readonly OfficeFindBar findBar = new();
    readonly ColorPickerButton textColor;
    readonly List<RibbonItem> buttons = [];

    View? fontPicker;
    View? sizePicker;
    bool suppressPickerEvents;
    bool disposed;

    public DocumentEditorView()
    {
        this.bold = this.MakeToggle(OfficeIcon.Bold, "Bold (Ctrl+B)", () => this.editor.Controller?.ToggleBold());
        this.italic = this.MakeToggle(OfficeIcon.Italic, "Italic (Ctrl+I)", () => this.editor.Controller?.ToggleItalic());
        this.underline = this.MakeToggle(OfficeIcon.Underline, "Underline (Ctrl+U)", () => this.editor.Controller?.ToggleUnderline());
        this.strike = this.MakeToggle(OfficeIcon.Strikethrough, "Strikethrough", () => this.editor.Controller?.ToggleStrikethrough());

        this.alignLeft = this.MakeToggle(OfficeIcon.AlignLeft, "Align left", () => this.editor.Controller?.SetAlignment(TextAlignment.Left));
        this.alignCenter = this.MakeToggle(OfficeIcon.AlignCenter, "Centre", () => this.editor.Controller?.SetAlignment(TextAlignment.Center));
        this.alignRight = this.MakeToggle(OfficeIcon.AlignRight, "Align right", () => this.editor.Controller?.SetAlignment(TextAlignment.Right));
        this.alignJustify = this.MakeToggle(OfficeIcon.AlignJustify, "Justify", () => this.editor.Controller?.SetAlignment(TextAlignment.Justify));

        this.bulletList = this.MakeToggle(OfficeIcon.BulletList, "Bulleted list", () => this.editor.Controller?.ToggleBulletList());
        this.numberedList = this.MakeToggle(OfficeIcon.NumberedList, "Numbered list", () => this.editor.Controller?.ToggleNumberedList());
        this.outdent = this.MakeButton(OfficeIcon.Outdent, "Outdent (Shift+Tab)", () => this.editor.Controller?.ChangeListLevel(-1));
        this.indent = this.MakeButton(OfficeIcon.Indent, "Indent (Tab)", () => this.editor.Controller?.ChangeListLevel(1));

        this.highlight = this.MakeToggle(OfficeIcon.Highlight, "Highlight", () => _ = this.PickHighlightAsync());
        this.insertTable = this.MakeAsyncButton(OfficeIcon.Table, "Table", this.InsertTableAsync);
        this.insertPicture = this.MakeAsyncButton(OfficeIcon.Picture, "Picture", this.InsertPictureAsync);
        this.watermark = this.MakeAsyncButton(OfficeIcon.Watermark, "Watermark", this.PickWatermarkAsync);

        // A button per preset rather than one that opens a sheet of four. Four is few enough to show,
        // and the whole reason to have a ribbon is that the choices are on it.
        this.marginButtons =
        [
            .. PageMarginPresets.All.Select(preset => this.Track(new RibbonButton
            {
                // No Text: four captions are most of a phone's width, and they pushed everything after
                // this group off the right-hand edge of the bar. The icon draws the inset instead.
                Tooltip = $"{preset.Name} — {preset.Description}",
                Size = RibbonItemSize.Small,
                AutomationId = "DocToolbarMargins" + preset.Name,
                IconTemplate = OfficeRibbonItems.IconTemplateFor(MarginIcon(preset.Name)),
                Command = new Command(() => this.editor.Controller?.SetPageMargins(preset.Margins))
            }))
        ];

        this.insertHeader = this.MakeAsyncButton(OfficeIcon.Header, "Header", () => this.EditChromeAsync(header: true));
        this.insertFooter = this.MakeAsyncButton(OfficeIcon.Footer, "Footer", () => this.EditChromeAsync(header: false));
        this.pageBreak = this.MakeButton(OfficeIcon.PageBreak, "Page break (Ctrl+Enter)", () => this.editor.Controller?.InsertPageBreak());

        // A menu, not a button: a page number has a place and a form, and picking them afterwards
        // means finding the header you just wrote into. Six entries is small enough to show at once.
        this.pageNumber = this.Track(new RibbonMenuButton
        {
            Tooltip = "Page number",
            Size = RibbonItemSize.Small,
            AutomationId = "DocToolbarPageNumber",
            IconTemplate = OfficeRibbonItems.IconTemplateFor(OfficeIcon.PageNumber)
        });

        foreach (var placement in new[] { PageNumberPlacement.Footer, PageNumberPlacement.Header })
        {
            foreach (var position in new[] { PageNumberPosition.Left, PageNumberPosition.Center, PageNumberPosition.Right })
            {
                var where = placement;
                var side = position;

                this.pageNumber.Menu.Add(new RibbonMenuEntry
                {
                    Text = $"{where} — {side}",
                    Command = new Command(() =>
                    {
                        this.editor.Controller?.InsertPageNumber(where, side);
                        this.RefreshBar();
                    })
                });
            }
        }

        // Print layout is a way of looking at the document, so it is a toggle rather than two buttons:
        // the pressed state is what says which of the two you are in.
        this.printLayout = this.MakeToggle(OfficeIcon.PrintLayout, "Print layout", () =>
        {
            this.editor.PageLayout = this.editor.PageLayout == DocumentPageLayout.Print
                ? DocumentPageLayout.Reflow
                : DocumentPageLayout.Print;

            this.RefreshBar();
        });

        // Two toggles rather than one, because a page is one of two things rather than on or off -
        // a single "Landscape" button leaves the user reading its pressed state backwards to work out
        // what portrait would be.
        this.portrait = this.MakeToggle(OfficeIcon.Portrait, "Portrait", () => this.SetOrientation(PageOrientation.Portrait));
        this.landscape = this.MakeToggle(OfficeIcon.Landscape, "Landscape", () => this.SetOrientation(PageOrientation.Landscape));

        this.zoomOut = this.MakeButton(OfficeIcon.ZoomOut, "Zoom out", () => this.StepZoom(-1));
        this.zoomIn = this.MakeButton(OfficeIcon.ZoomIn, "Zoom in", () => this.StepZoom(1));
        this.fitWidth = this.MakeButton(OfficeIcon.FitWidth, "Fit the page to the window", this.FitToWidth);

        this.zoomLabel = new Label
        {
            FontSize = 13,
            MinimumWidthRequest = 42,
            HorizontalTextAlignment = Microsoft.Maui.TextAlignment.Center,
            VerticalTextAlignment = Microsoft.Maui.TextAlignment.Center
        };

        this.zoomLabel.SetDynamicResource(
            Label.TextColorProperty,
            Shiny.Maui.Controls.Themes.ShinyThemeKeys.Color.OnSurfaceVariant);

        this.spellCheck = this.MakeToggle(OfficeIcon.SpellCheck, "Check spelling", this.ToggleSpellCheck);
        this.previousError = this.MakeButton(OfficeIcon.Previous, "Previous misspelling", () => this.GoToSpellingError(backwards: true));
        this.nextError = this.MakeButton(OfficeIcon.Next, "Next misspelling", () => this.GoToSpellingError(backwards: false));

        this.undo = this.MakeButton(OfficeIcon.Undo, "Undo (Ctrl+Z)", () => this.editor.Controller?.Undo());
        this.redo = this.MakeButton(OfficeIcon.Redo, "Redo (Ctrl+Shift+Z)", () => this.editor.Controller?.Redo());

        this.textColor = this.CreateColorPicker();

        this.ribbon = new Ribbon
        {
            SmallItemRows = 2,

            // These bars mix 32px pickers with icon buttons, and every group sizes its own rows - so
            // without one height the groups stop lining up with one another and the titles under them
            // land on different baselines.
            SmallItemRowHeight = 32,
            AllowGroupCollapse = true,

            // Below this the bar runs dense rather than folding its groups away: at phone width there
            // is room for no group at all, so collapsing puts every command behind a dropdown.
            SimplifyBelowWidth = 600
        };

        // Explicitly, because a BindableProperty's propertyChanged does not fire for its default -
        // so the accent every one of these ships with would never have been applied at all.
        this.ApplyAccent();

        this.root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            }
        };

        this.root.Add(this.ribbon);
        this.root.Add(this.editor);
        Grid.SetRow(this.editor, 1);

        this.editor.DocumentChanged += this.OnDocumentChanged;
        this.Content = this.root;

        this.BuildBar();
        this.AttachDrop();
    }

    public static readonly BindableProperty DocumentProperty = BindableProperty.Create(
        nameof(Document),
        typeof(WordDocument),
        typeof(DocumentEditorView),
        propertyChanged: (b, _, value) =>
        {
            var view = (DocumentEditorView)b;
            view.editor.Document = (WordDocument?)value;
            view.AttachController();
        });

    public static readonly BindableProperty ThemeProperty = BindableProperty.Create(
        nameof(Theme),
        typeof(DocumentTheme),
        typeof(DocumentEditorView),
        DocumentTheme.Light,
        propertyChanged: (b, _, value) => ((DocumentEditorView)b).editor.Theme = (DocumentTheme)value);

    public static readonly BindableProperty ZoomProperty = BindableProperty.Create(
        nameof(Zoom),
        typeof(double),
        typeof(DocumentEditorView),
        1.0,
        propertyChanged: (b, _, value) => ((DocumentEditorView)b).editor.Zoom = (double)value);

    public static readonly BindableProperty IsReadOnlyProperty = BindableProperty.Create(
        nameof(IsReadOnly),
        typeof(bool),
        typeof(DocumentEditorView),
        false,
        propertyChanged: (b, _, value) =>
        {
            var view = (DocumentEditorView)b;
            view.editor.IsReadOnly = (bool)value;
            view.RefreshBar();
        });

    /// <summary>Passed straight through to the inner <see cref="DocumentEditor"/>.</summary>
    public static readonly BindableProperty SpellCheckerProperty = BindableProperty.Create(
        nameof(SpellChecker),
        typeof(ISpellChecker),
        typeof(DocumentEditorView),
        propertyChanged: (b, _, value) => ((DocumentEditorView)b).editor.SpellChecker = (ISpellChecker?)value);

    public static readonly BindableProperty IsSpellCheckEnabledProperty = BindableProperty.Create(
        nameof(IsSpellCheckEnabled),
        typeof(bool),
        typeof(DocumentEditorView),
        true,
        propertyChanged: (b, _, value) => ((DocumentEditorView)b).editor.IsSpellCheckEnabled = (bool)value);

    public static readonly BindableProperty ShowToolbarProperty = BindableProperty.Create(
        nameof(ShowToolbar),
        typeof(bool),
        typeof(DocumentEditorView),
        true,
        propertyChanged: (b, _, value) => ((DocumentEditorView)b).ribbon.IsVisible = (bool)value);

    /// <summary>
    /// Whether the icon-only toolbar buttons carry a hover tooltip naming what they do.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On for desktop, off for phones and tablets. Every button on this bar is icon only, and an icon
    /// with no label is a guess until something names it — but the tooltip that names it opens on
    /// hover, and there is no hover on a touch screen. A long-press tooltip is not the answer either:
    /// it would compete with the tap the button exists for. Touch hosts get the semantic description
    /// instead, which is what a screen reader reads on any platform.
    /// </para>
    /// <para>
    /// The ribbon decides for itself whether to show a tooltip, from the same hover-capability rule -
    /// so this now only reaches the pickers the bar hosts, which draw their own.
    /// </para>
    /// </remarks>
    public static readonly BindableProperty ShowToolbarTooltipsProperty = BindableProperty.Create(
        nameof(ShowToolbarTooltips),
        typeof(bool),
        typeof(DocumentEditorView),
        OfficeToolbarButton.TooltipsByDefault);

    public static readonly BindableProperty FontFamiliesProperty = BindableProperty.Create(
        nameof(FontFamilies),
        typeof(IList<string>),
        typeof(DocumentEditorView),
        propertyChanged: (b, _, _) => ((DocumentEditorView)b).BuildBar());

    public static readonly BindableProperty FontSizesProperty = BindableProperty.Create(
        nameof(FontSizes),
        typeof(IList<double>),
        typeof(DocumentEditorView),
        propertyChanged: (b, _, _) => ((DocumentEditorView)b).BuildBar());

    public WordDocument? Document
    {
        get => (WordDocument?)this.GetValue(DocumentProperty);
        set => this.SetValue(DocumentProperty, value);
    }

    public DocumentTheme Theme
    {
        get => (DocumentTheme)this.GetValue(ThemeProperty);
        set => this.SetValue(ThemeProperty, value);
    }

    public double Zoom
    {
        get => (double)this.GetValue(ZoomProperty);
        set => this.SetValue(ZoomProperty, value);
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

    public bool ShowToolbar
    {
        get => (bool)this.GetValue(ShowToolbarProperty);
        set => this.SetValue(ShowToolbarProperty, value);
    }

    /// <summary>Hover tooltips on the icon-only toolbar buttons. Desktop only by default.</summary>
    public bool ShowToolbarTooltips
    {
        get => (bool)this.GetValue(ShowToolbarTooltipsProperty);
        set => this.SetValue(ShowToolbarTooltipsProperty, value);
    }

    /// <summary>Font families offered by the picker. Defaults to a small cross-platform set.</summary>
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

    /// <summary>The underlying editor, for focus and direct key routing.</summary>
    public DocumentEditor Editor => this.editor;

    public DocumentEditorController? Controller => this.editor.Controller;

    public event EventHandler? DocumentChanged;

    /// <summary>Raised when a dropped or chosen file could not be inserted, so a host can say so.</summary>
    public event EventHandler<OfficeDropRejected>? DropRejected;

    public static readonly BindableProperty ShapeWidthProperty = BindableProperty.Create(
        nameof(ShapeWidth), typeof(double), typeof(DocumentEditorView), 160d);

    public static readonly BindableProperty ShapeHeightProperty = BindableProperty.Create(
        nameof(ShapeHeight), typeof(double), typeof(DocumentEditorView), 120d);

    public static readonly BindableProperty PictureWidthProperty = BindableProperty.Create(
        nameof(PictureWidth), typeof(double), typeof(DocumentEditorView), 240d);

    /// <summary>The size of shape the toolbar inserts, in pixels.</summary>
    public double ShapeWidth
    {
        get => (double)this.GetValue(ShapeWidthProperty);
        set => this.SetValue(ShapeWidthProperty, value);
    }

    public double ShapeHeight
    {
        get => (double)this.GetValue(ShapeHeightProperty);
        set => this.SetValue(ShapeHeightProperty, value);
    }

    /// <summary>How wide an inserted picture is, in pixels. Its height follows the image's own ratio.</summary>
    public double PictureWidth
    {
        get => (double)this.GetValue(PictureWidthProperty);
        set => this.SetValue(PictureWidthProperty, value);
    }

    /// <summary>Routes a physical key press to the editor. See <see cref="DocumentEditor.HandleKey"/>.</summary>
    public bool HandleKey(EditorKey key, bool shift = false, bool control = false)
    {
        var handled = this.editor.HandleKey(key, shift, control);
        this.RefreshBar();
        return handled;
    }

    void BuildBar()
    {
        this.ribbon.Tabs.Clear();

        this.fontPicker = this.CreateFontPicker();
        this.sizePicker = this.CreateSizePicker();

        // Undo and redo apply whatever the caret is in, so they sit outside the groups.
        this.ribbon.QuickAccessItems.Clear();
        this.ribbon.QuickAccessItems.Add(this.undo);
        this.ribbon.QuickAccessItems.Add(this.redo);

        var tab = new RibbonTab { Title = "Home", Key = "home" };

        // Rows, which is what Word actually draws: the two boxes on top and the run of marks
        // underneath. Filling columns instead put bold above italic and underline above strikethrough -
        // a 2x2 block of a run people read across - and forced the font picker to share a column with a
        // toggle, where the column took the picker's width and stretched the 16px B across all of it.
        var font = new RibbonGroup { Title = "Font", Priority = 100 };

        var fontBoxes = new RibbonRow();

        if (this.fontPicker is not null)
            fontBoxes.Items.Add(OfficeRibbonItems.Host(this.fontPicker));

        if (this.sizePicker is not null)
            fontBoxes.Items.Add(OfficeRibbonItems.Host(this.sizePicker));

        font.Items.Add(fontBoxes);
        font.Items.Add(OfficeRibbonItems.Row(
            this.bold,
            this.italic,
            this.underline,
            this.strike,
            new RibbonSeparator(),
            OfficeRibbonItems.Host(this.textColor),
            this.highlight
        ));
        tab.Groups.Add(font);

        // Lists and the indent pair on top, the four alignments underneath - Word's own arrangement,
        // and two full rows of four rather than a 2x4 grid that read align-left / align-right down the
        // first column.
        var paragraph = new RibbonGroup { Title = "Paragraph", Priority = 90 };
        paragraph.Items.Add(OfficeRibbonItems.Row(
            this.bulletList,
            this.numberedList,
            this.outdent,
            this.indent
        ));
        paragraph.Items.Add(OfficeRibbonItems.Row(
            this.alignLeft,
            this.alignCenter,
            this.alignRight,
            this.alignJustify
        ));
        tab.Groups.Add(paragraph);

        // Proofing rides on Home rather than a Review tab of its own. Spelling is something you do
        // while writing, not a separate pass, and a tab holding three buttons costs a click to reach
        // and leaves most of a bar empty when you get there.
        //
        // The toggle is large and labelled: three icon-only buttons left a hole where the third row
        // would be, and "check spelling" is not a mark anyone reads off a 16px glyph. The two steppers
        // stack beside it, which is the column the hole used to be.
        var proofing = new RibbonGroup { Title = "Proofing", Priority = 70 };
        this.spellCheck.Size = RibbonItemSize.Large;
        this.spellCheck.Text = "Spelling";
        proofing.Items.Add(this.spellCheck);
        proofing.Items.Add(this.previousError);
        proofing.Items.Add(this.nextError);
        tab.Groups.Add(proofing);

        // On Home, and last on it. Finding a word is something you do while writing rather than a
        // separate pass, so it sits on the tab that opens - but it is reached less often than the
        // formatting beside it, which is what the priority orders and what decides which group folds
        // into the overflow first on a narrow window.
        //
        // The bar spans the rows: it is one control as tall as the group, and on a single row it left
        // the row underneath empty for the width of a search box.
        var finding = new RibbonGroup { Title = "Find", Priority = 60 };
        finding.Items.Add(OfficeRibbonItems.HostLarge(this.findBar));
        tab.Groups.Add(finding);

        this.ribbon.Tabs.Add(tab);

        // Two tabs, not four. Home is what you do to the text under the caret; Layout is what you do
        // to the page it sits on. Splitting further gave Insert and Layout one group each, which is a
        // click to reach a bar with a single button on it.
        var layoutTab = new RibbonTab { Title = "Layout", Key = "layout" };

        // The four presets on one row. Two deep in columns they came out as two pairs, and which
        // preset was which then depended on counting down a column rather than along a row.
        var page = new RibbonGroup { Title = "Margins", Priority = 100 };
        page.Items.Add(OfficeRibbonItems.Row([.. this.marginButtons]));
        layoutTab.Groups.Add(page);

        var pageSetup = new RibbonGroup { Title = "Page", Priority = 90 };
        pageSetup.Items.Add(this.portrait);
        pageSetup.Items.Add(this.landscape);
        pageSetup.Items.Add(new RibbonSeparator());
        pageSetup.Items.Add(this.printLayout);
        pageSetup.Items.Add(this.watermark);
        layoutTab.Groups.Add(pageSetup);

        // Minus, readout, plus - on one row, in that order. Filling columns put the readout under the
        // minus and the plus above "fit", which is a stepper whose two halves are on different lines
        // with the number wedged between them.
        var zoom = new RibbonGroup { Title = "Zoom", Priority = 80 };
        zoom.Items.Add(OfficeRibbonItems.Row(
            this.zoomOut,
            OfficeRibbonItems.Host(this.zoomLabel),
            this.zoomIn
        ));

        this.fitWidth.Text = "Fit width";
        zoom.Items.Add(OfficeRibbonItems.Row(this.fitWidth));
        layoutTab.Groups.Add(zoom);

        this.ribbon.Tabs.Add(layoutTab);

        // Insert stands on its own again now that it has something to hold: what goes *in* the
        // document (a table, a picture) and what goes *around* it (the running head and foot, the
        // number on every page, and the break between two of them).
        var insertTab = new RibbonTab { Title = "Insert", Key = "insert" };

        // Labelled: these are things you insert by name, not marks you recognise - nobody reads
        // "footer" off a 16px glyph.
        var objects = new RibbonGroup { Title = "Objects", Priority = 100 };
        this.insertTable.Text = "Table";
        this.insertPicture.Text = "Picture";
        objects.Items.Add(OfficeRibbonItems.Row(this.insertTable, this.insertPicture));
        insertTab.Groups.Add(objects);

        // Two rows rather than three items filling columns two deep, which left the page-number menu
        // alone in a second column with a hole under it.
        var chrome = new RibbonGroup { Title = "Header & Footer", Priority = 90 };
        this.insertHeader.Text = "Header";
        this.insertFooter.Text = "Footer";
        this.pageNumber.Text = "Page number";
        chrome.Items.Add(OfficeRibbonItems.Row(this.insertHeader, this.insertFooter));
        chrome.Items.Add(OfficeRibbonItems.Row(this.pageNumber));
        insertTab.Groups.Add(chrome);

        // One command, so it is drawn the way a single command should be: large, with its name on it.
        // A lone 16px glyph under a caption reading "Breaks" said nothing.
        var breaks = new RibbonGroup { Title = "Pages", Priority = 80 };
        this.pageBreak.Size = RibbonItemSize.Large;
        this.pageBreak.Text = "Page break";
        breaks.Items.Add(this.pageBreak);
        insertTab.Groups.Add(breaks);

        this.ribbon.Tabs.Add(insertTab);
        this.ribbon.Tabs.Add(OfficeRibbonItems.ShapesTab(g => this.editor.Controller?.InsertShape(g)));

        this.RefreshBar();
    }

    /// <summary>
    /// The core package's colour picker, in its button form.
    /// </summary>
    /// <remarks>
    /// Not a row of preset swatches: a document's text can be any colour, and a fixed palette is a
    /// promise the format does not make. The button shows the colour at the caret and opens the full
    /// spectrum — the same control the Blazor toolbar puts in this slot.
    /// </remarks>
    ColorPickerButton CreateColorPicker()
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

            this.editor.Controller?.SetTextColor(ToArgb(color));
            this.AfterCommand();
        };

        return picker;
    }

    /// <summary>MAUI colours are floats in 0..1; the document kernel stores bytes.</summary>
    static ArgbColor ToArgb(Color color) => new(
        (byte)Math.Round(color.Alpha * 255),
        (byte)Math.Round(color.Red * 255),
        (byte)Math.Round(color.Green * 255),
        (byte)Math.Round(color.Blue * 255));

    static Color FromArgb(ArgbColor color) => Color.FromRgba(color.R, color.G, color.B, color.A);

    /// <summary>The core package's font picker, which renders each family in its own typeface.</summary>
    FontPickerButton CreateFontPicker()
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
            if (this.suppressPickerEvents || string.IsNullOrEmpty(family))
                return;

            this.editor.Controller?.SetFontFamily(family);
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
            HeightRequest = ToolbarItemHeight,
            VerticalOptions = LayoutOptions.Center
        };

        picker.FontSizeChanged += (_, size) =>
        {
            if (this.suppressPickerEvents)
                return;

            this.editor.Controller?.SetFontSize(size);
            this.AfterCommand();
        };

        return picker;
    }

    static readonly IList<string> DefaultFontFamilies =
        ["Calibri", "Cambria", "Arial", "Times New Roman", "Georgia", "Verdana", "Courier New"];

    static readonly IList<double> DefaultFontSizes =
        [8, 9, 10, 11, 12, 14, 16, 18, 20, 24, 28, 32, 36, 48, 72];

    /// <summary>One height for every control in the bar. See <see cref="OfficeToolbarButton.ItemHeight"/>.</summary>
    const double ToolbarItemHeight = OfficeToolbarButton.ItemHeight;


    RibbonButton MakeButton(OfficeIcon icon, string hint, Action action)
        => this.Track(OfficeRibbonItems.Command(icon, hint, () =>
        {
            action();
            this.AfterCommand();
        }, automationId: $"DocToolbar{icon}"));

    /// <summary>
    /// A command whose work opens a menu or a file picker first.
    /// </summary>
    /// <remarks>
    /// It does not call <c>AfterCommand</c> itself: each of these has to wait for the user to choose
    /// something, and refreshing the bar and stealing focus back before then would happen while the
    /// menu is still up.
    /// </remarks>
    RibbonButton MakeAsyncButton(OfficeIcon icon, string hint, Func<Task> action)
        => this.Track(OfficeRibbonItems.Command(icon, hint, () => _ = action(), automationId: $"DocToolbar{icon}"));

    RibbonToggleButton MakeToggle(OfficeIcon icon, string hint, Action action)
        => this.Track(OfficeRibbonItems.Toggle(icon, hint, () =>
        {
            action();
            this.AfterCommand();
        }, $"DocToolbar{icon}"));

    /// <summary>Remembers an item so <c>RefreshBar</c> can enable and disable the set in one pass.</summary>
    T Track<T>(T item) where T : RibbonItem
    {
        this.buttons.Add(item);
        return item;
    }

    // ---- insert ----

    async Task PickHighlightAsync()
    {
        var (chosen, color) = await OfficeMenus.PickHighlightAsync(OfficeMenus.PageOf(this));
        if (!chosen)
            return;

        this.editor.Controller?.SetHighlight(color);
        this.AfterCommand();
    }

    /// <summary>
    /// Applies a margin preset to the whole document.
    /// </summary>
    /// <remarks>
    /// Enabled in both layouts, though only <see cref="DocumentPageLayout.Print"/> can show the
    /// result: the margins are the document's own and are written and saved either way, exactly as a
    /// page break is. Disabling it in reflow would hide a setting the file still has.
    /// </remarks>
    async Task PickPageMarginsAsync()
    {
        if (await OfficeMenus.PickPageMarginsAsync(OfficeMenus.PageOf(this)) is not { } margins)
            return;

        this.editor.Controller?.SetPageMargins(margins);
        this.AfterCommand();
    }


    async Task InsertTableAsync()
    {
        if (await OfficeMenus.PickTableAsync(OfficeMenus.PageOf(this)) is not { } size)
            return;

        this.editor.Controller?.InsertTable(size.Rows, size.Columns);
        this.AfterCommand();
    }

    async Task InsertPictureAsync()
    {
        var (image, rejected) = await OfficeMenus.PickImageAsync(OfficeMenus.PageOf(this));

        if (rejected is not null)
        {
            this.DropRejected?.Invoke(this, rejected);
            return;
        }

        if (image is null)
            return;

        this.InsertImage(image);
    }

    void InsertImage(OfficePickedImage image)
    {
        this.editor.Controller?.InsertImage(
            image.Data,
            image.ContentType,
            this.PictureWidth,
            name: Path.GetFileNameWithoutExtension(image.FileName));

        this.AfterCommand();
    }

    // ---- file drop ----

    /// <summary>
    /// Attaches the drop gesture to the editor surface.
    /// </summary>
    /// <remarks>
    /// On the editor rather than on this view, so a drop onto the toolbar is not a drop into the
    /// document — the toolbar is chrome, and dropping a picture on the Bold button should do nothing.
    /// </remarks>
    void AttachDrop()
    {
        var drop = new DropGestureRecognizer { AllowDrop = true };
        drop.Drop += this.OnDropAsync;
        this.editor.GestureRecognizers.Add(drop);
    }

    async void OnDropAsync(object? sender, DropEventArgs e)
    {
        if (this.IsReadOnly || this.Document is null)
            return;

        try
        {
            // The deferral that keeps the payload alive across the await is taken inside, on the one
            // platform that needs one — it lives on the platform args, not on these.
            foreach (var image in await OfficeFileDrop.ReadImagesAsync(e))
                this.InsertImage(image);
        }
        catch (Exception ex)
        {
            this.DropRejected?.Invoke(this, new OfficeDropRejected(string.Empty, ex.Message));
        }
    }

    void AfterCommand()
    {
        // Focus returns to the editor after every toolbar action; leaving it on the button means the
        // next keystroke goes nowhere, which reads as the editor having stopped working.
        this.editor.FocusEditor();
        this.RefreshBar();
        this.DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    void AttachController()
    {
        if (this.editor.Controller is { } controller)
            controller.Changed += (_, _) => this.RefreshBar();

        // A new document is a new controller and therefore a new finder; a bar left holding the old
        // one would count matches in a document that is no longer on screen.
        this.findBar.Find = this.editor.Controller?.Find;

        this.RefreshBar();
    }

    void OnDocumentChanged(object? sender, EventArgs e)
    {
        this.RefreshBar();
        this.DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reflects the formatting under the caret back into the toolbar.</summary>
    /// <summary>
    /// Writes the running head or foot, seeded with whatever is there now.
    /// </summary>
    /// <remarks>
    /// A prompt rather than editing it in place on the page. Header and footer are separate stories in
    /// the document — their own parts, laid out per page and repeated — so making them editable in the
    /// canvas means a second caret, a second selection and a way to get in and out of them. Asking for
    /// the line is the whole of what most documents need from them, and it is undoable like any other
    /// command.
    /// </remarks>
    async Task EditChromeAsync(bool header)
    {
        if (this.editor.Controller is not { } controller || OfficeMenus.PageOf(this) is not { } page)
            return;

        var existing = controller.ChromeText(header);

        var typed = await page.DisplayPromptAsync(
            header ? "Header" : "Footer",
            header ? "Shown at the top of every page" : "Shown at the bottom of every page",
            "Set",
            "Cancel",
            initialValue: existing ?? string.Empty);

        if (typed is null)
            return;

        // An empty line removes it, which is the only way back out of having one.
        var text = string.IsNullOrWhiteSpace(typed) ? null : typed;

        if (header)
            controller.SetHeaderText(text);
        else
            controller.SetFooterText(text);

        this.RefreshBar();
    }

    /// <summary>The icon whose drawn inset matches the preset.</summary>
    static OfficeIcon MarginIcon(string presetName) => presetName switch
    {
        "Narrow" => OfficeIcon.MarginsNarrow,
        "Moderate" => OfficeIcon.MarginsModerate,
        "Wide" => OfficeIcon.MarginsWide,
        _ => OfficeIcon.MarginsNormal
    };

    void SetOrientation(PageOrientation orientation)
    {
        this.editor.Controller?.SetPageOrientation(orientation);
        this.RefreshBar();
    }

    /// <summary>The zoom stops the buttons step through.</summary>
    /// <remarks>
    /// Fixed stops rather than a multiplier: a percentage that lands on 87% is a worse answer than one
    /// that lands on 100%, and these are the ones a reader recognises from every other document app.
    /// </remarks>
    static readonly double[] ZoomStops = [0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 3.0];

    void StepZoom(int direction)
    {
        var current = this.editor.Zoom;

        var next = direction > 0
            ? ZoomStops.FirstOrDefault(z => z > current + 0.001, ZoomStops[^1])
            : ZoomStops.LastOrDefault(z => z < current - 0.001, ZoomStops[0]);

        this.editor.Zoom = next;
        this.RefreshBar();
    }

    /// <summary>
    /// Sets the zoom so the page exactly spans the window.
    /// </summary>
    /// <remarks>
    /// The answer to "I cannot see the whole line" on a phone. Panning reaches the right-hand side,
    /// but a page is about twice a phone's width, so reading anything means panning every line.
    /// </remarks>
    void FitToWidth()
    {
        // Only in print. Reflow already fits the window by construction - it re-wraps to the measure -
        // so there is nothing for this to do there.
        if (this.editor.Controller is not { IsPaginated: true } controller)
            return;

        var available = this.editor.Width;
        var page = controller.PageWidth;

        if (available <= 0 || page <= 0)
            return;

        // Viewport.Width is controlWidth / zoom, so a zoom of controlWidth / pageWidth is exactly the
        // one at which the page spans the window.
        this.editor.Zoom = available / page;
        this.RefreshBar();
    }

    /// <summary>
    /// Turns the spelling pass on or off, and clears the underlines when it goes off.
    /// </summary>
    void ToggleSpellCheck()
    {
        if (this.editor.Controller is not { } controller)
            return;

        controller.IsSpellCheckEnabled = !controller.IsSpellCheckEnabled;
        this.RefreshBar();
    }

    /// <summary>
    /// Steps to the next misspelling and selects it, then offers what can be done about it.
    /// </summary>
    /// <remarks>
    /// The menu is the point. Stepping to an error and stopping there leaves the user with a selected
    /// word and the same problem they started with — on a phone the only way to act on it is the
    /// long-press menu, which is the gesture this button exists to avoid needing.
    /// </remarks>
    async void GoToSpellingError(bool backwards)
    {
        if (this.editor.Controller is not { } controller)
            return;

        if (await controller.GoToNextSpellingErrorAsync(backwards) is null)
            return;

        await this.editor.ShowSpellingMenuForCaretAsync();
    }

    void RefreshBar()
    {
        var format = this.editor.Controller?.CaretFormat ?? CaretFormat.Default;
        var enabled = !this.IsReadOnly && this.Document is not null;

        this.bold.IsChecked = format.Bold;
        this.italic.IsChecked = format.Italic;
        this.underline.IsChecked = format.Underline;
        this.strike.IsChecked = format.Strike;
        this.highlight.IsChecked = format.Highlight is not null;

        this.alignLeft.IsChecked = format.Alignment == TextAlignment.Left;
        this.alignCenter.IsChecked = format.Alignment == TextAlignment.Center;
        this.alignRight.IsChecked = format.Alignment == TextAlignment.Right;
        this.alignJustify.IsChecked = format.Alignment == TextAlignment.Justify;

        this.bulletList.IsChecked = format.List == ListStyle.Bullet;
        this.numberedList.IsChecked = format.List == ListStyle.Numbered;

        var spelling = this.editor.Controller?.IsSpellCheckEnabled ?? false;
        this.spellCheck.IsChecked = spelling;

        this.zoomLabel.Text = $"{this.editor.Zoom * 100:0}%";
        this.printLayout.IsChecked = this.editor.PageLayout == DocumentPageLayout.Print;

        var orientation = this.editor.Controller?.PageOrientation ?? PageOrientation.Portrait;
        this.portrait.IsChecked = orientation == PageOrientation.Portrait;
        this.landscape.IsChecked = orientation == PageOrientation.Landscape;

        foreach (var button in this.buttons)
            button.IsEnabled = enabled;

        // Stepping through misspellings means nothing while the pass that finds them is off.
        this.previousError.IsEnabled = enabled && spelling;
        this.nextError.IsEnabled = enabled && spelling;

        // Zoom is a way of looking at the document, not a way of changing it, so it stays live in a
        // read-only view - where being able to make the text bigger matters more, not less.
        var loaded = this.Document is not null;
        this.zoomIn.IsEnabled = loaded && this.editor.Zoom < ZoomStops[^1] - 0.001;
        this.zoomOut.IsEnabled = loaded && this.editor.Zoom > ZoomStops[0] + 0.001;
        this.fitWidth.IsEnabled = loaded;

        this.undo.IsEnabled = enabled && (this.editor.Controller?.CanUndo ?? false);
        this.redo.IsEnabled = enabled && (this.editor.Controller?.CanRedo ?? false);

        // The nesting buttons only move list items, so they are off everywhere else rather than
        // quietly doing nothing — and outdent is off at the top level, which is as far out as an item
        // can come without leaving the list.
        this.indent.IsEnabled = enabled && format.List != ListStyle.None;
        this.outdent.IsEnabled = enabled && format.List != ListStyle.None && format.ListLevel > 0;

        // Writing the pickers' selection raises their change events, which would immediately re-apply
        // the format that was only being displayed - so the handlers are muted while they are updated.
        this.suppressPickerEvents = true;

        if (this.fontPicker is FontPickerButton font)
            font.SelectedFont = format.FontFamily;

        if (this.sizePicker is FontSizePickerButton size)
        {
            // Snap to the nearest offered size: a document can hold any value, the picker only some.
            var sizes = this.FontSizes ?? DefaultFontSizes;
            size.SelectedFontSize = sizes.OrderBy(x => Math.Abs(x - format.FontSize)).FirstOrDefault();
        }

        this.textColor.SelectedColor = FromArgb(format.Color);
        this.textColor.IsEnabled = enabled;

        // Finding works in a read-only view - it changes nothing - so it follows whether a document is
        // open rather than whether it can be edited.
        this.findBar.IsEnabled = this.Document is not null;
        this.findBar.SetTooltipsEnabled(this.ShowToolbarTooltips);

        this.suppressPickerEvents = false;
    }

    public void Dispose()
    {
        if (this.disposed)
            return;

        this.disposed = true;
        this.editor.DocumentChanged -= this.OnDocumentChanged;

        // Drops the bar's subscription to the finder, which outlives this view: the finder belongs to
        // the controller and the controller to the document, and a host can keep both open.
        this.findBar.Find = null;
        this.editor.Dispose();

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The colour this control wears: its ribbon's header band and tab underline.
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="OfficeAccent.Document"/> — the colour Microsoft's own Word wears,
    /// because that is what a user reads as "a document" before any label has been looked at. Set it to
    /// take on the app's own brand instead, or to <c>null</c> to leave the bar on the theme's neutrals
    /// like the rest of the chrome.
    /// </remarks>
    public static readonly BindableProperty AccentProperty = BindableProperty.Create(
        nameof(Accent),
        typeof(OfficeAccent),
        typeof(DocumentEditorView),
        OfficeAccent.Document,
        propertyChanged: (b, _, _) => ((DocumentEditorView)b).ApplyAccent());

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
    /// A picture drawn behind the content. Forwarded to the surface.
    /// </summary>
    /// <remarks>
    /// A display watermark - drawn, not written into the file. See <see cref="OfficeWatermark"/>.
    /// </remarks>
    public OfficeWatermark? Watermark
    {
        get => this.editor.Watermark;
        set => this.editor.Watermark = value;
    }

    /// <summary>
    /// Picks a picture and sets it as the watermark, or clears one already there.
    /// </summary>
    /// <remarks>
    /// The button toggles rather than always asking: once a mark is set, the next thing anyone wants
    /// from that button is to take it off, and a picker that reopens on a document already stamped is
    /// a dead end with no way back.
    /// </remarks>
    async Task PickWatermarkAsync()
    {
        if (this.Watermark is not null)
        {
            this.Watermark = null;
            this.RefreshBar();
            return;
        }

        var (image, rejected) = await OfficeMenus.PickImageAsync(OfficeMenus.PageOf(this));

        if (rejected is not null || image is null)
            return;

        // Turned onto the diagonal, which is where a stamp goes and what stops it being mistaken for
        // content someone placed on the page.
        this.Watermark = new OfficeWatermark
        {
            Image = image.Data,
            RotationDegrees = 315
        };

        this.RefreshBar();
    }

}
