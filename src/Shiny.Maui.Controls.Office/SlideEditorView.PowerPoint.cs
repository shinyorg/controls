using System.Collections.ObjectModel;
using System.Globalization;
using Shiny.Controls.Office.Icons;
using Shiny.Controls.Office.Packaging;
using Shiny.Controls.Office.Shapes;
using Shiny.Controls.Office.Text;
using Shiny.Maui.Controls.ColorPicker;
using Shiny.Maui.Controls.Ribbons;
using TextAlignment = Shiny.Controls.Office.Text.TextAlignment;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// The PowerPoint ribbon — Home, Insert, Design, Transitions, Animations, Slide Show, View and the
/// contextual Shape Format, Table, Chart and Slide Master tabs — with its panes, dialogs and status.
/// </summary>
public partial class SlideEditorView
{
    // ---- panes and status (built with the view; toggled by IsVisible, since AppKit never realizes
    // a child added to a laid-out view) ----

    readonly ColumnDefinition paneColumn = new(0);
    readonly ScrollView outlinePane = new() { IsVisible = false };
    readonly VerticalStackLayout outlineList = new() { Spacing = 4, Padding = 8 };
    readonly Grid animationPane = new() { IsVisible = false, Padding = 8, RowSpacing = 6 };
    readonly VerticalStackLayout animationList = new() { Spacing = 2 };
    readonly Label slideCountLabel = new() { FontSize = 12, Padding = new Thickness(10, 4), VerticalTextAlignment = Microsoft.Maui.TextAlignment.Center };
    readonly HorizontalStackLayout statusTools = new() { Spacing = 2, VerticalOptions = LayoutOptions.Center };
    readonly Label zoomLabel = new() { FontSize = 12, WidthRequest = 46, HorizontalTextAlignment = Microsoft.Maui.TextAlignment.Center, VerticalTextAlignment = Microsoft.Maui.TextAlignment.Center };

    bool showAnimationPane;
    bool powerPointWired;
    int? selectedAnimation;

    readonly List<(RibbonItem Item, Func<bool> Enabled)> gated = [];
    readonly List<(RibbonToggleButton Item, Func<bool> Checked)> checks = [];
    readonly List<(RibbonMenuButton Menu, Func<string> Key, Action<IList<RibbonMenuEntry>> Fill)> dynamicMenus = [];
    readonly Dictionary<RibbonMenuButton, string> menuKeys = [];

    RibbonTab? shapeFormatTab;
    RibbonTab? tableDesignTab;
    RibbonTab? tableLayoutTab;
    RibbonTab? chartTab;
    RibbonTab? masterTab;
    RibbonToggleButton? alignJustify;

    Entry? widthEntry;
    Entry? heightEntry;
    Entry? transitionDuration;
    Entry? advanceAfter;
    CheckBox? advanceOnClick;
    CheckBox? advanceAfterOn;
    Entry? animationDuration;
    Entry? animationDelay;
    Picker? animationStart;
    Entry? replaceEntry;
    bool refreshingFields;

    // ---- status bar members ----

    static readonly BindablePropertyKey CurrentSlideIndexPropertyKey = BindableProperty.CreateReadOnly(
        nameof(CurrentSlideIndex), typeof(int), typeof(SlideEditorView), 0);

    /// <summary>The slide being edited, zero-based — a status bar's "Slide 3 of 12".</summary>
    public static readonly BindableProperty CurrentSlideIndexProperty = CurrentSlideIndexPropertyKey.BindableProperty;

    static readonly BindablePropertyKey SlideCountPropertyKey = BindableProperty.CreateReadOnly(
        nameof(SlideCount), typeof(int), typeof(SlideEditorView), 0);

    /// <summary>How many slides the deck has.</summary>
    public static readonly BindableProperty SlideCountProperty = SlideCountPropertyKey.BindableProperty;

    static readonly BindablePropertyKey EffectiveZoomPropertyKey = BindableProperty.CreateReadOnly(
        nameof(EffectiveZoom), typeof(double), typeof(SlideEditorView), 1d);

    /// <summary>The zoom actually drawn at, fitted or not — 1 is 100%.</summary>
    public static readonly BindableProperty EffectiveZoomProperty = EffectiveZoomPropertyKey.BindableProperty;

    /// <summary>The zoom: 1 is 100%, null fits the slide to the window. Two-way.</summary>
    public static readonly BindableProperty ZoomProperty = BindableProperty.Create(
        nameof(Zoom), typeof(double?), typeof(SlideEditorView), null, BindingMode.TwoWay,
        propertyChanged: (b, _, value) =>
        {
            if (((SlideEditorView)b).editor.Controller is { } controller && !Nullable.Equals(controller.Zoom, (double?)value))
                controller.Zoom = (double?)value;
        });

    /// <summary>Normal, Outline, Slide Sorter, Notes Page or Slide Master. Two-way.</summary>
    public static readonly BindableProperty ViewModeProperty = BindableProperty.Create(
        nameof(ViewMode), typeof(SlideEditorViewMode), typeof(SlideEditorView), SlideEditorViewMode.Normal, BindingMode.TwoWay,
        propertyChanged: (b, _, value) => ((SlideEditorView)b).ApplyViewMode((SlideEditorViewMode)value));

    public int CurrentSlideIndex => (int)this.GetValue(CurrentSlideIndexProperty);

    public int SlideCount => (int)this.GetValue(SlideCountProperty);

    public double EffectiveZoom => (double)this.GetValue(EffectiveZoomProperty);

    public double? Zoom
    {
        get => (double?)this.GetValue(ZoomProperty);
        set => this.SetValue(ZoomProperty, value);
    }

    public SlideEditorViewMode ViewMode
    {
        get => (SlideEditorViewMode)this.GetValue(ViewModeProperty);
        set => this.SetValue(ViewModeProperty, value);
    }

    /// <summary>Raised when anything a status bar shows changes: slide, count, zoom, notes or view.</summary>
    public event EventHandler? StatusChanged;

    /// <summary>
    /// The File tab. With the shell on (the default) File opens the backstage and still raises this; with it
    /// off, the ribbon shows File only while this is handled.
    /// </summary>
    public event EventHandler? FileMenuRequested
    {
        add
        {
            this.fileMenuRequested += value;
            this.ApplyFileButton();
        }
        remove
        {
            this.fileMenuRequested -= value;
            this.ApplyFileButton();
        }
    }

    EventHandler? fileMenuRequested;

    (int, int, double, double?, bool, SlideEditorViewMode) lastStatus;

    void SyncStatus()
    {
        var controller = this.editor.Controller;
        this.SetValue(CurrentSlideIndexPropertyKey, controller?.Index ?? 0);
        this.SetValue(SlideCountPropertyKey, this.Deck?.Slides.Count ?? 0);
        this.SetValue(EffectiveZoomPropertyKey, controller?.EffectiveZoom ?? 1);

        if (controller is not null)
        {
            if (!Nullable.Equals(controller.Zoom, this.Zoom))
                this.Zoom = controller.Zoom;

            if (controller.ViewMode != this.ViewMode)
                this.ViewMode = controller.ViewMode;
        }

        var now = (this.CurrentSlideIndex, this.SlideCount, this.EffectiveZoom, controller?.Zoom, this.ShowNotes, this.ViewMode);
        if (now == this.lastStatus)
            return;

        this.lastStatus = now;
        this.StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    void ApplyViewMode(SlideEditorViewMode mode)
    {
        if (this.editor.Controller is { } controller && controller.ViewMode != mode)
            controller.ViewMode = mode;

        // A Notes Page has its notes box open under it, as PowerPoint's has the text editable.
        this.notes.IsVisible = this.ShowNotes || mode == SlideEditorViewMode.NotesPage;

        if (this.ribbon.Tabs.Count > 0 && this.masterTab is not null)
        {
            if (mode == SlideEditorViewMode.SlideMaster)
                this.ribbon.SelectedTab = this.masterTab;
            else if (this.ribbon.SelectedTab == this.masterTab)
                this.ribbon.SelectedTab = this.ribbon.Tabs[0];
        }

        this.ApplyRailVisibility();
        this.RebuildOutline();
        this.RefreshBar();
    }

    // ---- export ----

    /// <summary>One slide (the current one by default) as a PNG, <paramref name="width"/> pixels wide.</summary>
    public byte[]? ExportSlidePng(int? slide = null, int width = 1920)
        => this.Deck is { Slides.Count: > 0 } deck ? SlideExporter.ToPng(deck, slide ?? this.CurrentSlideIndex, width, this.Watermark) : null;

    /// <summary>Every slide as a PNG.</summary>
    public IReadOnlyList<byte[]> ExportSlidesPng(int width = 1920)
        => this.Deck is { } deck ? SlideExporter.ToPngs(deck, width, watermark: this.Watermark) : [];

    /// <summary>The deck as a PDF, a page per slide. Hidden slides are left out.</summary>
    public byte[]? ExportPdf() => this.Deck is { } deck ? SlideExporter.ToPdf(deck, watermark: this.Watermark) : null;

    // ---- plumbing ----

    SlideEditorController? C => this.editor.Controller;

    bool Editable => !this.EffectiveReadOnly && this.Deck is not null;

    bool HasShape => this.Editable && (this.C?.HasShapeSelection ?? false);

    bool CanFormat => this.Editable && (this.C?.CanFormatShape ?? false);

    bool HasText => this.Editable && this.C?.IsEditingText == true;

    bool HasTextOrShape => this.HasText || this.HasShape;

    bool CanArrangeShapes => this.Editable && (this.C?.CanArrange ?? false);

    void Run(Action<SlideEditorController> action)
    {
        if (this.C is not { } controller || !this.Editable)
            return;

        action(controller);
        this.editor.Repaint();
        this.AfterCommand();
    }

    static DataTemplate SlideIconTemplate(SlideIcon icon)
        => new(() => new OfficeRibbonIconView(new OfficeToolbarIconDrawable { Shapes = SlideIcons.Shapes(icon) }));

    RibbonButton SlideButton(SlideIcon icon, string text, string tooltip, Action<SlideEditorController> action, Func<bool>? enabled = null, RibbonItemSize size = RibbonItemSize.Small)
    {
        var button = new RibbonButton
        {
            Text = text,
            Tooltip = tooltip,
            Size = size,
            IconTemplate = SlideIconTemplate(icon),
            AutomationId = $"Slide{icon}{text.Replace(" ", string.Empty, StringComparison.Ordinal)}",
            Command = new Command(() => this.Run(action))
        };

        this.gated.Add((button, enabled ?? (() => this.Editable)));
        return button;
    }

    RibbonButton SlideButton(OfficeIcon icon, string text, string tooltip, Action<SlideEditorController> action, Func<bool>? enabled = null, RibbonItemSize size = RibbonItemSize.Small)
    {
        var button = new RibbonButton
        {
            Text = text,
            Tooltip = tooltip,
            Size = size,
            IconTemplate = OfficeRibbonItems.IconTemplateFor(icon),
            AutomationId = $"Slide{icon}{text.Replace(" ", string.Empty, StringComparison.Ordinal)}",
            Command = new Command(() => this.Run(action))
        };

        this.gated.Add((button, enabled ?? (() => this.Editable)));
        return button;
    }

    RibbonToggleButton Toggle(SlideIcon icon, string text, Action action, Func<bool> isChecked, Func<bool>? enabled = null, RibbonItemSize size = RibbonItemSize.Small)
    {
        var toggle = new RibbonToggleButton
        {
            Text = text,
            Tooltip = text,
            Size = size,
            IconTemplate = SlideIconTemplate(icon),
            AutomationId = $"SlideToggle{icon}",
            Command = new Command(() =>
            {
                action();
                this.editor.Repaint();
                this.RefreshBar();
            })
        };

        this.gated.Add((toggle, enabled ?? (() => this.Deck is not null)));
        this.checks.Add((toggle, isChecked));
        return toggle;
    }

    RibbonMenuButton Menu(SlideIcon icon, string text, Func<bool> enabled, Func<string> key, Action<IList<RibbonMenuEntry>> fill, RibbonItemSize size = RibbonItemSize.Small)
    {
        var menu = new RibbonMenuButton
        {
            Text = text,
            Tooltip = text,
            Size = size,
            IconTemplate = SlideIconTemplate(icon),
            AutomationId = $"SlideMenu{icon}{text.Replace(" ", string.Empty, StringComparison.Ordinal)}"
        };

        this.gated.Add((menu, enabled));
        this.dynamicMenus.Add((menu, key, fill));
        return menu;
    }

    RibbonMenuButton Menu(SlideIcon icon, string text, Func<bool> enabled, Action<IList<RibbonMenuEntry>> fill, RibbonItemSize size = RibbonItemSize.Small)
        => this.Menu(icon, text, enabled, () => "static", fill, size);

    RibbonMenuEntry Entry(string text, Action<SlideEditorController> action, bool isChecked = false)
        => new() { Text = text, IsChecked = isChecked, Command = new Command(() => this.Run(action)) };

    static RibbonMenuEntry Separator() => new() { IsSeparator = true };

    RibbonContentItem Field(string label, Entry entry, Action<double> apply, Func<bool> enabled)
    {
        entry.Keyboard = Keyboard.Numeric;
        entry.WidthRequest = 64;
        entry.FontSize = 12;
        entry.HeightRequest = OfficeToolbarButton.ItemHeight;

        void Commit()
        {
            if (this.refreshingFields)
                return;

            if (double.TryParse(entry.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) ||
                double.TryParse(entry.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                apply(value);
            }
        }

        entry.Completed += (_, _) => Commit();
        entry.Unfocused += (_, _) => Commit();

        var host = OfficeRibbonItems.Host(new HorizontalStackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
            Children = { new Label { Text = label, FontSize = 12, VerticalTextAlignment = Microsoft.Maui.TextAlignment.Center }, entry }
        });

        this.gated.Add((host, enabled));
        return host;
    }

    static string Number(double value) => value.ToString("0.##", CultureInfo.CurrentCulture);

    static RibbonGroup Group(string title, int priority, params RibbonItem[] items)
    {
        var group = new RibbonGroup { Title = title, Priority = priority };
        foreach (var item in items)
            group.Items.Add(item);

        return group;
    }

    // ---- building ----

    void BuildBar()
    {
        this.ribbon.Tabs.Clear();
        this.gated.Clear();
        this.checks.Clear();
        this.dynamicMenus.Clear();
        this.menuKeys.Clear();

        this.fontPicker = this.CreateFontPicker();
        this.sizePicker = this.CreateSizePicker();

        this.ribbon.QuickAccessItems.Clear();
        this.ribbon.QuickAccessItems.Add(this.undo);
        this.ribbon.QuickAccessItems.Add(this.redo);

        if (!this.powerPointWired)
        {
            this.powerPointWired = true;
            this.WirePanes();
        }

        this.ribbon.Tabs.Add(this.BuildHome());
        this.ribbon.Tabs.Add(this.BuildInsert());
        this.ribbon.Tabs.Add(this.BuildDesign());
        this.ribbon.Tabs.Add(this.BuildTransitions());
        this.ribbon.Tabs.Add(this.BuildAnimations());
        this.ribbon.Tabs.Add(this.BuildSlideShow());
        this.ribbon.Tabs.Add(this.BuildViewTab());
        this.ribbon.Tabs.Add(OfficeRibbonItems.ShapesTab(this.InsertShape));

        this.shapeFormatTab = this.BuildShapeFormat();
        this.ribbon.Tabs.Add(this.shapeFormatTab);
        (this.tableDesignTab, this.tableLayoutTab) = this.BuildTableTabs();
        this.ribbon.Tabs.Add(this.tableDesignTab);
        this.ribbon.Tabs.Add(this.tableLayoutTab);
        this.chartTab = this.BuildChartTab();
        this.ribbon.Tabs.Add(this.chartTab);
        this.masterTab = this.BuildMasterTab();
        this.ribbon.Tabs.Add(this.masterTab);

        this.AfterBarBuilt();
        this.RefreshBar();
    }

    RibbonTab BuildHome()
    {
        var tab = new RibbonTab { Title = "Home", Key = "home" };

        var clipboard = Group("Clipboard", 95, this.paste, this.cut, this.copy, this.duplicateShape);
        tab.Groups.Add(clipboard);

        var slides = new RibbonGroup { Title = "Slides", Priority = 105 };
        this.newSlide.Size = RibbonItemSize.Large;
        slides.Items.Add(this.newSlide);
        slides.Items.Add(OfficeRibbonItems.Row(
            this.slideLayout,
            this.SlideButton(SlideIcon.FormatBackground, "Reset", "Reset the background to the layout's", c => c.ResetBackground()),
            this.Menu(SlideIcon.Section, "Section", () => this.Editable && (this.C?.Count ?? 0) > 0,
                () => string.Join("|", this.C?.Sections.Select(x => x.Name) ?? []) + this.CurrentSlideIndex,
                this.FillSectionMenu)));
        slides.Items.Add(OfficeRibbonItems.Row(this.duplicateSlide, this.deleteSlide, this.moveSlideEarlier, this.moveSlideLater));
        tab.Groups.Add(slides);

        var font = new RibbonGroup { Title = "Font", Priority = 100 };
        var boxes = new RibbonRow();
        if (this.fontPicker is not null)
            boxes.Items.Add(OfficeRibbonItems.Host(this.fontPicker));
        if (this.sizePicker is not null)
            boxes.Items.Add(OfficeRibbonItems.Host(this.sizePicker));

        boxes.Items.Add(this.SlideButton(SlideIcon.GrowFont, string.Empty, "Increase font size (Ctrl+Shift+>)", c => c.GrowFont(1), () => this.HasTextOrShape));
        boxes.Items.Add(this.SlideButton(SlideIcon.ShrinkFont, string.Empty, "Decrease font size (Ctrl+Shift+<)", c => c.GrowFont(-1), () => this.HasTextOrShape));
        boxes.Items.Add(this.SlideButton(OfficeIcon.ClearFormat, string.Empty, "Clear all formatting", c => c.ClearFormatting(), () => this.HasTextOrShape));
        font.Items.Add(boxes);

        font.Items.Add(OfficeRibbonItems.Row(
            this.bold, this.italic, this.underline, this.strike,
            this.Toggle(SlideIcon.Subscript, "Subscript", () => this.C?.ToggleSubscript(), () => this.C?.CaretFormat.Baseline < 0, () => this.HasText),
            this.Toggle(SlideIcon.Superscript, "Superscript", () => this.C?.ToggleSuperscript(), () => this.C?.CaretFormat.Baseline > 0, () => this.HasText),
            this.Menu(SlideIcon.ChangeCase, string.Empty, () => this.HasText, m =>
            {
                m.Add(this.Entry("Sentence case.", c => c.ChangeCase(TextCase.Sentence)));
                m.Add(this.Entry("lowercase", c => c.ChangeCase(TextCase.Lower)));
                m.Add(this.Entry("UPPERCASE", c => c.ChangeCase(TextCase.Upper)));
                m.Add(this.Entry("Capitalize Each Word", c => c.ChangeCase(TextCase.Capitalize)));
                m.Add(this.Entry("tOGGLE cASE", c => c.ChangeCase(TextCase.Toggle)));
            }),
            this.Menu(SlideIcon.LineSpacing, "AV", () => this.HasText, m =>
            {
                foreach (var (name, points) in new[] { ("Very Tight", -3d), ("Tight", -1.5), ("Normal", 0d), ("Loose", 3d), ("Very Loose", 6d) })
                    m.Add(this.Entry(name, c => c.SetCharacterSpacing(points)));
            }),
            new RibbonSeparator(),
            OfficeRibbonItems.Host(this.textColor),
            this.highlight));
        tab.Groups.Add(font);

        this.alignJustify = this.MakeToggle(OfficeIcon.AlignJustify, "Justify (Ctrl+J)", () => this.C?.SetParagraphAlignment(TextAlignment.Justify));
        var paragraph = new RibbonGroup { Title = "Paragraph", Priority = 90 };
        paragraph.Items.Add(OfficeRibbonItems.Row(
            this.bulletList, this.numberedList, this.outdent, this.indent,
            this.Menu(SlideIcon.LineSpacing, string.Empty, () => this.HasTextOrShape, m =>
            {
                foreach (var multiple in new[] { 1.0, 1.5, 2.0, 2.5, 3.0 })
                    m.Add(this.Entry(multiple.ToString("0.0", CultureInfo.CurrentCulture), c => c.SetLineSpacing(multiple)));
            })));
        paragraph.Items.Add(OfficeRibbonItems.Row(
            this.alignLeft, this.alignCenter, this.alignRight, this.alignJustify,
            this.Menu(SlideIcon.TextDirection, string.Empty, () => this.CanFormat, m =>
            {
                m.Add(this.Entry("Horizontal", c => c.SetTextDirection(ShapeTextDirection.Horizontal)));
                m.Add(this.Entry("Rotate all text 90°", c => c.SetTextDirection(ShapeTextDirection.Rotate90)));
                m.Add(this.Entry("Rotate all text 270°", c => c.SetTextDirection(ShapeTextDirection.Rotate270)));
            }),
            this.Menu(SlideIcon.AlignText, string.Empty, () => this.CanFormat, this.FillAlignTextMenu)));
        tab.Groups.Add(paragraph);

        var drawing = new RibbonGroup { Title = "Drawing", Priority = 80 };
        drawing.Items.Add(this.Menu(SlideIcon.Icons, "Shapes", () => this.Editable, this.FillShapesMenu, RibbonItemSize.Large));
        drawing.Items.Add(this.Menu(SlideIcon.Group, "Arrange", () => this.CanArrangeShapes,
            () => $"{this.C?.CanGroup}{this.C?.CanUngroup}{this.C?.AlignToSlide}", this.FillArrangeMenu, RibbonItemSize.Large));
        drawing.Items.Add(this.Menu(SlideIcon.QuickStyles, "Quick Styles", () => this.CanFormat, () => this.C?.ThemeName ?? string.Empty, this.FillQuickStyles, RibbonItemSize.Large));
        drawing.Items.Add(OfficeRibbonItems.Row(this.Menu(SlideIcon.ShapeFill, "Fill", () => this.CanFormat, () => this.C?.ThemeName ?? string.Empty, this.FillFillMenu)));
        drawing.Items.Add(OfficeRibbonItems.Row(this.Menu(SlideIcon.ShapeOutline, "Outline", () => this.HasShape, () => this.C?.ThemeName ?? string.Empty, this.FillOutlineMenu)));
        tab.Groups.Add(drawing);

        var editing = new RibbonGroup { Title = "Editing", Priority = 60 };
        editing.Items.Add(OfficeRibbonItems.HostLarge(this.findBar));

        this.replaceEntry = new Entry { Placeholder = "Replace with", WidthRequest = 120, FontSize = 12, HeightRequest = OfficeToolbarButton.ItemHeight };
        var replaceHost = OfficeRibbonItems.Host(this.replaceEntry);
        this.gated.Add((replaceHost, () => this.Editable));
        editing.Items.Add(OfficeRibbonItems.Row(
            replaceHost,
            this.SlideButton(SlideIcon.Replace, "Replace", "Replace the current match", c => c.ReplaceCurrent(this.replaceEntry?.Text ?? string.Empty)),
            this.SlideButton(SlideIcon.Replace, "All", "Replace every match", c => c.ReplaceAll(this.replaceEntry?.Text ?? string.Empty))));
        editing.Items.Add(OfficeRibbonItems.Row(
            this.SlideButton(OfficeIcon.Pointer, "Select All", "Select every shape", c => c.SelectAllShapes())));
        tab.Groups.Add(editing);

        return tab;
    }

    RibbonTab BuildInsert()
    {
        var tab = new RibbonTab { Title = "Insert", Key = "insert" };

        tab.Groups.Add(Group("Slides", 100, this.SlideButton(OfficeIcon.NewSlide, "New Slide", "Add a slide after this one", c => c.NewSlide(), size: RibbonItemSize.Large)));

        this.insertTable.Size = RibbonItemSize.Large;
        this.insertTable.Text = "Table";
        this.insertPicture.Size = RibbonItemSize.Large;
        this.insertPicture.Text = "Pictures";
        tab.Groups.Add(Group("Tables & Images", 95, this.insertTable, this.insertPicture));

        tab.Groups.Add(Group("Illustrations", 90,
            this.Menu(SlideIcon.Icons, "Shapes", () => this.Editable, this.FillShapesMenu, RibbonItemSize.Large),
            this.Menu(SlideIcon.Icons, "Icons", () => this.Editable, m =>
            {
                foreach (var geometry in new[] { ShapeGeometry.Star5, ShapeGeometry.Plus, ShapeGeometry.Hexagon, ShapeGeometry.Cloud, ShapeGeometry.RightArrow, ShapeGeometry.Chevron, ShapeGeometry.Diamond, ShapeGeometry.Can })
                    m.Add(this.Entry(ShapeNames.Of(geometry), c => c.AddIcon(geometry)));
            }, RibbonItemSize.Large),
            this.Menu(SlideIcon.Chart, "Chart", () => this.Editable, m =>
            {
                foreach (var kind in Enum.GetValues<SlideChartKind>())
                    m.Add(new RibbonMenuEntry { Text = kind == SlideChartKind.Column ? "Clustered Column" : kind.ToString(), Command = new Command(() => _ = this.EditChartAsync(kind)) });
            }, RibbonItemSize.Large)));

        var link = new RibbonButton
        {
            Text = "Link",
            Tooltip = "Insert a link (Ctrl+K)",
            Size = RibbonItemSize.Large,
            IconTemplate = SlideIconTemplate(SlideIcon.Hyperlink),
            AutomationId = "SlideInsertLink",
            Command = new Command(() => _ = this.EditLinkAsync())
        };
        this.gated.Add((link, () => this.Editable && this.C?.SelectedShape >= 0));
        tab.Groups.Add(Group("Links", 85, link));

        this.addTextBox.Size = RibbonItemSize.Large;
        this.addTextBox.Text = "Text Box";
        var text = Group("Text", 80, this.addTextBox);
        var headerFooter = new RibbonButton
        {
            Text = "Header & Footer",
            Size = RibbonItemSize.Small,
            IconTemplate = SlideIconTemplate(SlideIcon.HeaderFooter),
            AutomationId = "SlideHeaderFooter",
            Command = new Command(() => _ = this.EditHeaderFooterAsync())
        };
        this.gated.Add((headerFooter, () => this.Editable && (this.C?.Count ?? 0) > 0));
        text.Items.Add(OfficeRibbonItems.Row(headerFooter));
        text.Items.Add(OfficeRibbonItems.Row(
            this.Menu(SlideIcon.DateTime, "Date", () => this.Editable && (this.C?.Count ?? 0) > 0, m =>
            {
                foreach (var format in new[] { "d", "D", "MMMM d, yyyy", "yyyy-MM-dd", "t", "g" })
                {
                    m.Add(new RibbonMenuEntry
                    {
                        Text = DateTime.Now.ToString(format, CultureInfo.CurrentCulture),
                        Command = new Command(() =>
                        {
                            if (this.C?.IsEditingText == true)
                                this.Run(c => c.InsertField(SlideFieldKind.DateTime, format));
                            else
                                _ = this.EditHeaderFooterAsync();
                        })
                    });
                }
            }),
            new RibbonButton
            {
                Text = "Number",
                Size = RibbonItemSize.Small,
                IconTemplate = SlideIconTemplate(SlideIcon.SlideNumber),
                AutomationId = "SlideInsertNumber",
                Command = new Command(() =>
                {
                    if (this.C?.IsEditingText == true)
                        this.Run(c => c.InsertField(SlideFieldKind.SlideNumber));
                    else
                        _ = this.EditHeaderFooterAsync();
                })
            }));
        tab.Groups.Add(text);

        var video = new RibbonButton { Text = "Video", Size = RibbonItemSize.Large, IconTemplate = SlideIconTemplate(SlideIcon.Video), AutomationId = "SlideInsertVideo", Command = new Command(() => _ = this.InsertMediaAsync(true)) };
        var audio = new RibbonButton { Text = "Audio", Size = RibbonItemSize.Large, IconTemplate = SlideIconTemplate(SlideIcon.Audio), AutomationId = "SlideInsertAudio", Command = new Command(() => _ = this.InsertMediaAsync(false)) };
        this.gated.Add((video, () => this.Editable));
        this.gated.Add((audio, () => this.Editable));
        tab.Groups.Add(Group("Media", 70, video, audio));

        return tab;
    }

    RibbonTab BuildDesign()
    {
        var tab = new RibbonTab { Title = "Design", Key = "design" };

        tab.Groups.Add(Group("Themes", 100, this.Menu(SlideIcon.Theme, "Themes", () => this.Editable, () => this.C?.ThemeName ?? string.Empty, m =>
        {
            foreach (var theme in SlideThemeDefinition.BuiltIn)
                m.Add(this.Entry(theme.Name, c => c.ApplyTheme(theme), this.C?.ThemeName == theme.Name));
        }, RibbonItemSize.Large)));

        tab.Groups.Add(Group("Variants", 90, this.Menu(SlideIcon.Variants, "Variants", () => this.Editable, () => this.C?.ThemeName ?? string.Empty, m =>
        {
            var theme = SlideThemeDefinition.BuiltIn.FirstOrDefault(x => x.Name == this.C?.ThemeName) ?? SlideThemeDefinition.BuiltIn[0];
            foreach (var variant in theme.Variants)
                m.Add(this.Entry(variant.Name, c => c.ApplyColorVariant(variant)));
        }, RibbonItemSize.Large)));

        var background = new RibbonButton { Text = "Format Background", Size = RibbonItemSize.Large, IconTemplate = SlideIconTemplate(SlideIcon.FormatBackground), AutomationId = "SlideFormatBackground", Command = new Command(() => _ = this.EditBackgroundAsync(master: false)) };
        this.gated.Add((background, () => this.Editable && (this.C?.Count ?? 0) > 0));

        tab.Groups.Add(Group("Customize", 80,
            this.Menu(SlideIcon.SlideSize, "Slide Size", () => this.Editable, m =>
            {
                m.Add(this.Entry("Standard (4:3)", c => c.SetSlideSize(SetSlideSizeCommand.Standard.Width, SetSlideSizeCommand.Standard.Height)));
                m.Add(this.Entry("Widescreen (16:9)", c => c.SetSlideSize(SetSlideSizeCommand.Widescreen.Width, SetSlideSizeCommand.Widescreen.Height)));
                m.Add(Separator());
                m.Add(new RibbonMenuEntry { Text = "Custom Slide Size…", Command = new Command(() => _ = this.EditSlideSizeAsync()) });
            }, RibbonItemSize.Large),
            background));

        this.watermark.Size = RibbonItemSize.Large;
        this.watermark.Text = "Watermark";
        tab.Groups.Add(Group("Watermark", 70, this.watermark));
        return tab;
    }

    RibbonTab BuildTransitions()
    {
        var tab = new RibbonTab { Title = "Transitions", Key = "transitions" };
        var preview = new RibbonButton { Text = "Preview", Size = RibbonItemSize.Large, IconTemplate = SlideIconTemplate(SlideIcon.Preview), AutomationId = "SlideTransitionPreview", Command = new Command(() => this.editor.Preview()) };
        this.gated.Add((preview, () => (this.C?.Count ?? 0) > 0));
        tab.Groups.Add(Group("Preview", 100, preview));

        SlideTransitionKind[] kinds =
        [
            SlideTransitionKind.None, SlideTransitionKind.Fade, SlideTransitionKind.Push, SlideTransitionKind.Wipe, SlideTransitionKind.Split,
            SlideTransitionKind.Reveal, SlideTransitionKind.Cover, SlideTransitionKind.Zoom, SlideTransitionKind.Morph
        ];

        var gallery = new RibbonGroup { Title = "Transition to This Slide", Priority = 95 };
        foreach (var kind in kinds)
        {
            gallery.Items.Add(this.Toggle(kind == SlideTransitionKind.None ? SlideIcon.Close : SlideIcon.Transition, SlideTransition.NameOf(kind),
                () => this.Run(c => c.SetTransitionKind(kind)),
                () => (this.C?.CurrentTransition?.Kind ?? SlideTransitionKind.None) == kind,
                () => this.Editable));
        }

        gallery.Items.Add(this.Menu(SlideIcon.EffectOptions, "Effect Options",
            () => this.Editable && this.C?.CurrentTransition is { } t && SlideTransition.DirectionsFor(t.Kind).Count > 0,
            () => $"{this.C?.CurrentTransition?.Kind}{this.C?.CurrentTransition?.Direction}",
            m =>
            {
                if (this.C?.CurrentTransition is not { } transition)
                    return;

                foreach (var direction in SlideTransition.DirectionsFor(transition.Kind))
                    m.Add(this.Entry(SlideTransition.NameOf(direction), c => c.UpdateTransition(t => t with { Direction = direction }), transition.Direction == direction));
            }, RibbonItemSize.Large));
        tab.Groups.Add(gallery);

        this.transitionDuration = new Entry();
        this.advanceAfter = new Entry();
        this.advanceOnClick = new CheckBox();
        this.advanceAfterOn = new CheckBox();

        this.advanceOnClick.CheckedChanged += (_, e) =>
        {
            if (!this.refreshingFields)
                this.Run(c => c.UpdateTransition(t => t with { AdvanceOnClick = e.Value }));
        };

        this.advanceAfterOn.CheckedChanged += (_, e) =>
        {
            if (!this.refreshingFields)
                this.Run(c => c.UpdateTransition(t => t with { AdvanceAfter = e.Value ? TimeSpan.FromSeconds(5) : null }));
        };

        var timing = new RibbonGroup { Title = "Timing", Priority = 90 };
        timing.Items.Add(OfficeRibbonItems.Row(
            this.Field("Duration", this.transitionDuration, s => this.Run(c => c.UpdateTransition(t => t with { Duration = TimeSpan.FromSeconds(Math.Max(0, s)) })), () => this.Editable && this.C?.CurrentTransition is not null),
            this.SlideButton(SlideIcon.Transition, "Apply To All", "Every slide takes this slide's transition", c => c.ApplyTransitionToAll())));
        timing.Items.Add(OfficeRibbonItems.Row(
            this.Labelled(this.advanceOnClick, "On Mouse Click"),
            this.Labelled(this.advanceAfterOn, "After"),
            this.Field(string.Empty, this.advanceAfter, s => this.Run(c => c.UpdateTransition(t => t with { AdvanceAfter = TimeSpan.FromSeconds(Math.Max(0, s)) })), () => this.Editable && this.C?.CurrentTransition?.AdvanceAfter is not null)));
        tab.Groups.Add(timing);
        return tab;
    }

    RibbonContentItem Labelled(CheckBox box, string label)
    {
        var host = OfficeRibbonItems.Host(new HorizontalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children = { box, new Label { Text = label, FontSize = 12, VerticalTextAlignment = Microsoft.Maui.TextAlignment.Center } }
        });

        this.gated.Add((host, () => this.Editable));
        return host;
    }

    RibbonTab BuildAnimations()
    {
        var tab = new RibbonTab { Title = "Animations", Key = "animations" };
        var preview = new RibbonButton { Text = "Preview", Size = RibbonItemSize.Large, IconTemplate = SlideIconTemplate(SlideIcon.Preview), AutomationId = "SlideAnimationPreview", Command = new Command(() => this.editor.Preview()) };
        this.gated.Add((preview, () => (this.C?.Count ?? 0) > 0));
        tab.Groups.Add(Group("Preview", 100, preview));

        var gallery = new RibbonGroup { Title = "Animation", Priority = 95 };
        gallery.Items.Add(this.SlideButton(SlideIcon.Close, "None", "Remove the selected shapes' animations", c => c.RemoveAnimations(), () => this.HasShape));
        foreach (var effect in SlideAnimation.Gallery)
        {
            gallery.Items.Add(this.Toggle(SlideIcon.Animation, SlideAnimation.NameOf(effect),
                () => this.Run(c => c.Animate(effect)),
                () => this.C?.SelectionAnimations is { Count: > 0 } own && own[0].Animation.Effect == effect,
                () => this.HasShape));
        }

        gallery.Items.Add(this.Menu(SlideIcon.EffectOptions, "Effect Options",
            () => this.Editable && this.SelectedAnimation is { } a && SlideAnimation.HasDirection(a.Effect),
            () => $"{this.SelectedAnimationIndex}{this.SelectedAnimation?.Direction}",
            m =>
            {
                foreach (var direction in new[] { SlideTransitionDirection.FromBottom, SlideTransitionDirection.FromTop, SlideTransitionDirection.FromLeft, SlideTransitionDirection.FromRight })
                    m.Add(this.Entry(SlideTransition.NameOf(direction), c => c.UpdateAnimation(this.SelectedAnimationIndex ?? -1, a => a with { Direction = direction }), this.SelectedAnimation?.Direction == direction));
            }, RibbonItemSize.Large));
        tab.Groups.Add(gallery);

        tab.Groups.Add(Group("Advanced Animation", 90,
            this.Menu(SlideIcon.Animation, "Add Animation", () => this.HasShape, m =>
            {
                foreach (var klass in Enum.GetValues<SlideAnimationClass>())
                {
                    var parent = new RibbonMenuEntry { Text = klass.ToString() };
                    foreach (var effect in SlideAnimation.Gallery.Where(x => SlideAnimation.ClassOf(x) == klass))
                        parent.Children.Add(this.Entry(SlideAnimation.NameOf(effect), c => c.Animate(effect, add: true)));

                    m.Add(parent);
                }
            }, RibbonItemSize.Large),
            this.Toggle(SlideIcon.AnimationPane, "Animation Pane", () =>
            {
                this.showAnimationPane = !this.showAnimationPane;
                this.ApplyRailVisibility();
                this.RebuildAnimationPane();
            }, () => this.showAnimationPane, size: RibbonItemSize.Large)));

        this.animationStart = new Picker { ItemsSource = new[] { "On Click", "With Previous", "After Previous" }, FontSize = 12, WidthRequest = 130 };
        this.animationStart.SelectedIndexChanged += (_, _) =>
        {
            if (this.refreshingFields || this.SelectedAnimationIndex is not { } index || this.animationStart.SelectedIndex < 0)
                return;

            var trigger = (SlideAnimationTrigger)this.animationStart.SelectedIndex;
            this.Run(c => c.UpdateAnimation(index, a => a with { Trigger = trigger }));
        };

        var startHost = OfficeRibbonItems.Host(new HorizontalStackLayout { Spacing = 4, Children = { new Label { Text = "Start", FontSize = 12, VerticalTextAlignment = Microsoft.Maui.TextAlignment.Center }, this.animationStart } });
        this.gated.Add((startHost, () => this.Editable && this.SelectedAnimationIndex is not null));

        this.animationDuration = new Entry();
        this.animationDelay = new Entry();

        var timing = new RibbonGroup { Title = "Timing", Priority = 85 };
        timing.Items.Add(OfficeRibbonItems.Row(
            startHost,
            this.SlideButton(OfficeIcon.MoveSlideEarlier, "Move Earlier", "Play this animation earlier", _ => this.MoveAnimation(-1), () => this.Editable && this.SelectedAnimationIndex > 0)));
        timing.Items.Add(OfficeRibbonItems.Row(
            this.Field("Duration", this.animationDuration, s => this.Run(c => c.UpdateAnimation(this.SelectedAnimationIndex ?? -1, a => a with { Duration = TimeSpan.FromSeconds(Math.Max(0, s)) })), () => this.Editable && this.SelectedAnimationIndex is not null),
            this.Field("Delay", this.animationDelay, s => this.Run(c => c.UpdateAnimation(this.SelectedAnimationIndex ?? -1, a => a with { Delay = TimeSpan.FromSeconds(Math.Max(0, s)) })), () => this.Editable && this.SelectedAnimationIndex is not null),
            this.SlideButton(OfficeIcon.MoveSlideLater, "Move Later", "Play this animation later", _ => this.MoveAnimation(1),
                () => this.Editable && this.SelectedAnimationIndex is { } i && i < (this.C?.Animations.Count ?? 0) - 1)));
        tab.Groups.Add(timing);
        return tab;
    }

    RibbonTab BuildSlideShow()
    {
        var tab = new RibbonTab { Title = "Slide Show", Key = "slideshow" };

        RibbonButton Start(SlideIcon icon, string text, Action action)
        {
            var button = new RibbonButton { Text = text, Size = RibbonItemSize.Large, IconTemplate = SlideIconTemplate(icon), AutomationId = $"Slide{icon}", Command = new Command(action) };
            this.gated.Add((button, () => this.Deck is not null));
            return button;
        }

        tab.Groups.Add(Group("Start Slide Show", 100,
            Start(SlideIcon.FromBeginning, "From Beginning", () => this.StartPresenting(0)),
            Start(SlideIcon.FromCurrent, "From Current Slide", () => this.StartPresenting()),
            Start(SlideIcon.PresenterView, "Presenter View", () => this.StartPresenting(null, presenterView: true))));

        tab.Groups.Add(Group("Set Up", 90,
            this.Toggle(SlideIcon.HideSlide, "Hide Slide", () => this.Run(c => c.ToggleHideSlide()), () => this.C?.Current?.IsHidden == true,
                () => this.Editable && (this.C?.Count ?? 0) > 0, RibbonItemSize.Large)));

        // Export: the PDF and the pictures, through FileRequested - the host decides where they go.
        var pdf = new RibbonButton
        {
            Text = "PDF",
            Tooltip = "Export the deck as a PDF, a page per slide",
            Size = RibbonItemSize.Large,
            IconTemplate = SlideIconTemplate(SlideIcon.Export),
            AutomationId = "SlideExportPdf",
            Command = new Command(() => this.Export(Shiny.Controls.Office.Shell.OfficeFileFormats.Pdf))
        };
        this.gated.Add((pdf, () => this.Deck is not null));

        var pictures = this.Menu(SlideIcon.Export, "Pictures", () => (this.Deck?.Slides.Count ?? 0) > 0, m =>
        {
            m.Add(new RibbonMenuEntry { Text = "This slide as PNG", Command = new Command(() => this.Export(Shiny.Controls.Office.Shell.OfficeFileFormats.Png)) });
            m.Add(new RibbonMenuEntry { Text = "This slide as JPEG", Command = new Command(() => this.Export(Shiny.Controls.Office.Shell.OfficeFileFormats.Jpeg)) });
            m.Add(new RibbonMenuEntry { Text = "All slides as PNG (.zip)", Command = new Command(() => this.Export(SlideExport.AllSlidesPng)) });
        }, RibbonItemSize.Large);
        pictures.Tooltip = "Export slides as pictures";

        tab.Groups.Add(Group("Export", 70, pdf, pictures));

        return tab;
    }

    RibbonTab BuildViewTab()
    {
        var tab = new RibbonTab { Title = "View", Key = "view" };

        RibbonToggleButton View(SlideIcon icon, string text, SlideEditorViewMode mode)
            => this.Toggle(icon, text, () => this.ViewMode = mode, () => this.ViewMode == mode, size: RibbonItemSize.Large);

        tab.Groups.Add(Group("Presentation Views", 100,
            View(SlideIcon.NormalView, "Normal", SlideEditorViewMode.Normal),
            View(SlideIcon.OutlineView, "Outline View", SlideEditorViewMode.Outline),
            View(SlideIcon.SlideSorter, "Slide Sorter", SlideEditorViewMode.SlideSorter),
            View(SlideIcon.NotesPage, "Notes Page", SlideEditorViewMode.NotesPage)));

        tab.Groups.Add(Group("Master Views", 95, View(SlideIcon.SlideMaster, "Slide Master", SlideEditorViewMode.SlideMaster)));

        var show = new RibbonGroup { Title = "Show", Priority = 90 };
        show.Items.Add(OfficeRibbonItems.Row(
            this.Toggle(SlideIcon.Ruler, "Ruler", () => { if (this.C is { } c) c.ShowRuler = !c.ShowRuler; }, () => this.C?.ShowRuler == true),
            this.Toggle(SlideIcon.Gridlines, "Gridlines", () => { if (this.C is { } c) c.ShowGridlines = !c.ShowGridlines; }, () => this.C?.ShowGridlines == true)));
        show.Items.Add(OfficeRibbonItems.Row(
            this.Toggle(SlideIcon.Guides, "Guides", () => { if (this.C is { } c) c.ShowGuides = !c.ShowGuides; }, () => this.C?.ShowGuides == true),
            this.Toggle(SlideIcon.NotesPage, "Notes", () => this.ShowNotes = !this.ShowNotes, () => this.ShowNotes)));
        tab.Groups.Add(show);

        tab.Groups.Add(Group("Zoom", 85,
            this.Menu(SlideIcon.FitToWindow, "Zoom", () => this.Deck is not null, m =>
            {
                foreach (var zoom in new[] { 0.25, 0.33, 0.5, 0.66, 0.75, 1, 1.5, 2, 3, 4 })
                    m.Add(new RibbonMenuEntry { Text = $"{Math.Round(zoom * 100)}%", Command = new Command(() => this.SetZoom(zoom)) });

                m.Add(Separator());
                m.Add(new RibbonMenuEntry { Text = "Fit", Command = new Command(() => this.SetZoom(null)) });
            }, RibbonItemSize.Large),
            this.Toggle(SlideIcon.FitToWindow, "Fit to Window", () => this.SetZoom(null), () => this.C?.Zoom is null, size: RibbonItemSize.Large)));

        return tab;
    }

    void SetZoom(double? zoom)
    {
        if (this.C is not { } controller)
            return;

        controller.Zoom = zoom;
        this.Zoom = controller.Zoom;
        this.editor.Repaint();
        this.RefreshBar();
    }

    RibbonTab BuildShapeFormat()
    {
        var tab = new RibbonTab { Title = "Shape Format", Key = "shapeformat", ContextTitle = "Drawing Tools", ContextColor = Color.FromArgb("#C43E1C"), IsVisible = false };

        var styles = new RibbonGroup { Title = "Shape Styles", Priority = 100 };
        styles.Items.Add(this.Menu(SlideIcon.QuickStyles, "Quick Styles", () => this.CanFormat, () => this.C?.ThemeName ?? string.Empty, this.FillQuickStyles, RibbonItemSize.Large));

        var fillPicker = new ColorPickerButton { Text = string.Empty, ShowOpacity = false, WidthRequest = 44, HeightRequest = OfficeToolbarButton.ItemHeight };
        fillPicker.ColorChanged += (_, color) => this.Run(c => c.SetShapeFill(ToArgb(color)));
        var fillHost = OfficeRibbonItems.Host(fillPicker);
        this.gated.Add((fillHost, () => this.CanFormat));

        var outlinePicker = new ColorPickerButton { Text = string.Empty, ShowOpacity = false, WidthRequest = 44, HeightRequest = OfficeToolbarButton.ItemHeight };
        outlinePicker.ColorChanged += (_, color) => this.Run(c => c.SetShapeOutlineColor(ToArgb(color)));
        var outlineHost = OfficeRibbonItems.Host(outlinePicker);
        this.gated.Add((outlineHost, () => this.HasShape));

        styles.Items.Add(OfficeRibbonItems.Row(this.Menu(SlideIcon.ShapeFill, "Shape Fill", () => this.CanFormat, () => this.C?.ThemeName ?? string.Empty, this.FillFillMenu), fillHost));
        styles.Items.Add(OfficeRibbonItems.Row(this.Menu(SlideIcon.ShapeOutline, "Shape Outline", () => this.HasShape, () => this.C?.ThemeName ?? string.Empty, this.FillOutlineMenu), outlineHost));
        styles.Items.Add(this.Toggle(SlideIcon.ShapeEffects, "Shadow", () => this.Run(c => c.SetShapeShadow(!c.SelectionHasShadow)), () => this.C?.SelectionHasShadow == true, () => this.HasShape, RibbonItemSize.Large));
        tab.Groups.Add(styles);

        var arrange = new RibbonGroup { Title = "Arrange", Priority = 90 };
        arrange.Items.Add(OfficeRibbonItems.Row(this.toFront, this.forward,
            this.Menu(SlideIcon.Align, "Align", () => this.CanArrangeShapes, () => $"{this.C?.AlignToSlide}", this.FillAlignMenu)));
        arrange.Items.Add(OfficeRibbonItems.Row(this.toBack, this.backward,
            this.Menu(SlideIcon.Group, "Group", () => this.Editable && (this.C?.CanGroup == true || this.C?.CanUngroup == true),
                () => $"{this.C?.CanGroup}{this.C?.CanUngroup}", m =>
                {
                    m.Add(new RibbonMenuEntry { Text = "Group (Ctrl+G)", IsEnabled = this.C?.CanGroup == true, Command = new Command(() => this.Run(c => c.Group())) });
                    m.Add(new RibbonMenuEntry { Text = "Ungroup (Ctrl+Shift+G)", IsEnabled = this.C?.CanUngroup == true, Command = new Command(() => this.Run(c => c.Ungroup())) });
                })));
        arrange.Items.Add(this.Menu(SlideIcon.Rotate, "Rotate", () => this.CanArrangeShapes, this.FillRotateMenu, RibbonItemSize.Large));
        tab.Groups.Add(arrange);

        this.widthEntry = new Entry();
        this.heightEntry = new Entry();
        var size = new RibbonGroup { Title = "Size (in)", Priority = 80 };
        size.Items.Add(OfficeRibbonItems.Row(this.Field("Height", this.heightEntry, v => this.Run(c => c.SetShapeSize(null, v * 96)), () => this.HasShape)));
        size.Items.Add(OfficeRibbonItems.Row(this.Field("Width", this.widthEntry, v => this.Run(c => c.SetShapeSize(v * 96, null)), () => this.HasShape)));
        tab.Groups.Add(size);

        return tab;
    }

    (RibbonTab Design, RibbonTab Layout) BuildTableTabs()
    {
        var context = Color.FromArgb("#C43E1C");
        var design = new RibbonTab { Title = "Table Design", Key = "tabledesign", ContextTitle = "Table Tools", ContextColor = context, IsVisible = false };

        SlideTableStyleFlags Flags() => this.C?.SelectedTable?.StyleFlags ?? default;

        var options = new RibbonGroup { Title = "Table Style Options", Priority = 100 };
        options.Items.Add(OfficeRibbonItems.Row(
            this.Toggle(SlideIcon.InsertRowAbove, "Header Row", () => this.Run(c => c.SetTableStyleFlags(Flags() with { HeaderRow = !Flags().HeaderRow })), () => Flags().HeaderRow, () => this.Editable),
            this.Toggle(SlideIcon.InsertColumnLeft, "First Column", () => this.Run(c => c.SetTableStyleFlags(Flags() with { FirstColumn = !Flags().FirstColumn })), () => Flags().FirstColumn, () => this.Editable)));
        options.Items.Add(OfficeRibbonItems.Row(
            this.Toggle(SlideIcon.Gridlines, "Banded Rows", () => this.Run(c => c.SetTableStyleFlags(Flags() with { BandedRows = !Flags().BandedRows })), () => Flags().BandedRows, () => this.Editable),
            this.Toggle(SlideIcon.InsertRowBelow, "Total Row", () => this.Run(c => c.SetTableStyleFlags(Flags() with { TotalRow = !Flags().TotalRow })), () => Flags().TotalRow, () => this.Editable)));
        design.Groups.Add(options);

        var shading = new ColorPickerButton { Text = string.Empty, ShowOpacity = false, WidthRequest = 44, HeightRequest = OfficeToolbarButton.ItemHeight };
        shading.ColorChanged += (_, color) => this.Run(c => c.SetCellFill(ToArgb(color)));
        var shadingHost = OfficeRibbonItems.Host(shading);
        this.gated.Add((shadingHost, () => this.Editable));

        design.Groups.Add(Group("Table Styles", 90,
            this.Menu(SlideIcon.QuickStyles, "Styles", () => this.Editable, () => this.C?.SelectedTable?.StyleId ?? string.Empty, m =>
            {
                foreach (var style in SlideTableStyles.Gallery)
                    m.Add(this.Entry(style.Name, c => c.SetTableStyle(style), this.C?.SelectedTable?.StyleId == style.Id));
            }, RibbonItemSize.Large),
            shadingHost));

        var layout = new RibbonTab { Title = "Table Layout", Key = "tablelayout", ContextTitle = "Table Tools", ContextColor = context, IsVisible = false };
        var rows = new RibbonGroup { Title = "Rows & Columns", Priority = 100 };
        rows.Items.Add(OfficeRibbonItems.Row(
            this.SlideButton(SlideIcon.InsertRowAbove, "Insert Above", "Insert a row above", c => c.EditTable(SlideTableEdit.InsertRowAbove)),
            this.SlideButton(SlideIcon.InsertRowBelow, "Insert Below", "Insert a row below", c => c.EditTable(SlideTableEdit.InsertRowBelow)),
            this.SlideButton(OfficeIcon.DeleteRow, "Delete Row", "Delete the row", c => c.EditTable(SlideTableEdit.DeleteRow))));
        rows.Items.Add(OfficeRibbonItems.Row(
            this.SlideButton(SlideIcon.InsertColumnLeft, "Insert Left", "Insert a column to the left", c => c.EditTable(SlideTableEdit.InsertColumnLeft)),
            this.SlideButton(SlideIcon.InsertColumnRight, "Insert Right", "Insert a column to the right", c => c.EditTable(SlideTableEdit.InsertColumnRight)),
            this.SlideButton(OfficeIcon.DeleteColumn, "Delete Column", "Delete the column", c => c.EditTable(SlideTableEdit.DeleteColumn))));
        layout.Groups.Add(rows);
        layout.Groups.Add(Group("Merge", 90,
            this.SlideButton(SlideIcon.MergeCells, "Merge Cells", "Merge the selected block of cells", c => c.EditTable(SlideTableEdit.MergeCells), size: RibbonItemSize.Large),
            this.SlideButton(SlideIcon.SplitCells, "Split Cells", "Undo a merge", c => c.EditTable(SlideTableEdit.SplitCells), size: RibbonItemSize.Large)));

        return (design, layout);
    }

    RibbonTab BuildChartTab()
    {
        var tab = new RibbonTab { Title = "Chart Design", Key = "chartdesign", ContextTitle = "Chart Tools", ContextColor = Color.FromArgb("#C43E1C"), IsVisible = false };
        var edit = new RibbonButton { Text = "Edit Data", Size = RibbonItemSize.Large, IconTemplate = OfficeRibbonItems.IconTemplateFor(OfficeIcon.Table), AutomationId = "SlideChartEditData", Command = new Command(() => _ = this.EditChartAsync(null)) };
        this.gated.Add((edit, () => this.Editable));
        tab.Groups.Add(Group("Data", 100, edit));
        tab.Groups.Add(Group("Type", 90, this.Menu(SlideIcon.Chart, "Change Chart Type", () => this.Editable, () => $"{this.C?.SelectedChart?.Kind}", m =>
        {
            foreach (var kind in Enum.GetValues<SlideChartKind>())
                m.Add(this.Entry(kind == SlideChartKind.Column ? "Clustered Column" : kind.ToString(), c => c.SetChartData(c.SelectedChart! with { Kind = kind }), this.C?.SelectedChart?.Kind == kind));
        }, RibbonItemSize.Large)));

        return tab;
    }

    RibbonTab BuildMasterTab()
    {
        var tab = new RibbonTab { Title = "Slide Master", Key = "slidemaster", ContextTitle = "Master View", ContextColor = Color.FromArgb("#C43E1C"), IsVisible = false };

        var background = new RibbonButton { Text = "Background Styles", Size = RibbonItemSize.Large, IconTemplate = SlideIconTemplate(SlideIcon.FormatBackground), AutomationId = "SlideMasterBackground", Command = new Command(() => _ = this.EditBackgroundAsync(master: true)) };
        this.gated.Add((background, () => this.Editable));
        tab.Groups.Add(Group("Background", 100, background));

        bool HasMasterSelection() => this.Editable && this.C?.Master.Selection is not null;

        void Style(SlideMasterTextStyle style)
        {
            this.C?.Master.SetTextStyle(style);
            this.editor.Repaint();
            this.AfterCommand();
        }

        var masterFont = new FontPicker.FontPickerButton { AvailableFonts = (this.FontFamilies ?? DefaultFontFamilies).ToList(), Placeholder = "Font", WidthRequest = 150, HeightRequest = OfficeToolbarButton.ItemHeight };
        masterFont.FontChanged += (_, family) => { if (!string.IsNullOrEmpty(family)) Style(new SlideMasterTextStyle { FontFamily = family }); };
        var masterSize = new FontPicker.FontSizePickerButton { AvailableFontSizes = (this.FontSizes ?? DefaultFontSizes).ToList(), WidthRequest = 84, HeightRequest = OfficeToolbarButton.ItemHeight };
        masterSize.FontSizeChanged += (_, size) => Style(new SlideMasterTextStyle { FontSize = size });
        var masterColor = new ColorPickerButton { Text = string.Empty, ShowOpacity = false, WidthRequest = 44, HeightRequest = OfficeToolbarButton.ItemHeight };
        masterColor.ColorChanged += (_, color) => Style(new SlideMasterTextStyle { Color = ToArgb(color) });

        var fontHost = OfficeRibbonItems.Host(masterFont);
        var sizeHost = OfficeRibbonItems.Host(masterSize);
        var colorHost = OfficeRibbonItems.Host(masterColor);
        this.gated.Add((fontHost, HasMasterSelection));
        this.gated.Add((sizeHost, HasMasterSelection));
        this.gated.Add((colorHost, HasMasterSelection));

        var textStyles = new RibbonGroup { Title = "Text Styles", Priority = 90 };
        textStyles.Items.Add(OfficeRibbonItems.Row(fontHost, sizeHost));
        textStyles.Items.Add(OfficeRibbonItems.Row(
            this.SlideButton(OfficeIcon.Bold, string.Empty, "Bold", _ => Style(new SlideMasterTextStyle { Bold = true }), HasMasterSelection),
            this.SlideButton(OfficeIcon.Italic, string.Empty, "Italic", _ => Style(new SlideMasterTextStyle { Italic = true }), HasMasterSelection),
            colorHost));
        tab.Groups.Add(textStyles);

        var close = new RibbonButton { Text = "Close Master View", Size = RibbonItemSize.Large, IconTemplate = SlideIconTemplate(SlideIcon.Close), AutomationId = "SlideMasterClose", Command = new Command(() => this.ViewMode = SlideEditorViewMode.Normal) };
        tab.Groups.Add(Group("Close", 80, close));
        return tab;
    }

    // ---- menu contents ----

    void FillSectionMenu(IList<RibbonMenuEntry> m)
    {
        var sections = this.C?.Sections ?? [];
        var current = sections.ToList().FindIndex(x => x.Contains(this.CurrentSlideIndex));

        m.Add(new RibbonMenuEntry { Text = "Add Section", Command = new Command(() => _ = this.PromptSectionAsync(-1)) });
        m.Add(new RibbonMenuEntry { Text = "Rename Section", IsEnabled = current >= 0, Command = new Command(() => _ = this.PromptSectionAsync(current)) });
        m.Add(new RibbonMenuEntry { Text = "Remove Section", IsEnabled = current >= 0, Command = new Command(() => this.Run(c => c.RemoveSection(current))) });
        m.Add(new RibbonMenuEntry { Text = "Remove Section & Slides", IsEnabled = current >= 0, Command = new Command(() => this.Run(c => c.RemoveSection(current, withSlides: true))) });
        m.Add(new RibbonMenuEntry { Text = "Move Section Up", IsEnabled = current > 0, Command = new Command(() => this.Run(c => c.MoveSection(current, -1))) });
        m.Add(new RibbonMenuEntry { Text = "Move Section Down", IsEnabled = current >= 0 && current < sections.Count - 1, Command = new Command(() => this.Run(c => c.MoveSection(current, 1))) });

        if (this.rail.Rail is { } rail)
        {
            m.Add(Separator());
            m.Add(new RibbonMenuEntry { Text = "Collapse All", Command = new Command(() => rail.SetAllCollapsed(true)) });
            m.Add(new RibbonMenuEntry { Text = "Expand All", Command = new Command(() => rail.SetAllCollapsed(false)) });
        }
    }

    void FillAlignTextMenu(IList<RibbonMenuEntry> m)
    {
        m.Add(this.Entry("Top", c => c.SetTextAnchor(TextAnchor.Top)));
        m.Add(this.Entry("Middle", c => c.SetTextAnchor(TextAnchor.Middle)));
        m.Add(this.Entry("Bottom", c => c.SetTextAnchor(TextAnchor.Bottom)));
        m.Add(Separator());
        m.Add(this.Entry("Do Not Autofit", c => c.SetAutofit(TextAutofit.None)));
        m.Add(this.Entry("Shrink Text on Overflow", c => c.SetAutofit(TextAutofit.ShrinkOnOverflow)));
        m.Add(this.Entry("Resize Shape to Fit Text", c => c.SetAutofit(TextAutofit.ResizeShape)));
    }

    void FillShapesMenu(IList<RibbonMenuEntry> m)
    {
        foreach (var (geometry, name) in ShapeNames.All)
            m.Add(new RibbonMenuEntry { Text = name, Command = new Command(() => this.InsertShape(geometry)) });
    }

    void FillArrangeMenu(IList<RibbonMenuEntry> m)
    {
        m.Add(this.Entry("Bring to Front", c => c.BringToFront()));
        m.Add(this.Entry("Send to Back", c => c.SendToBack()));
        m.Add(this.Entry("Bring Forward", c => c.BringForward()));
        m.Add(this.Entry("Send Backward", c => c.SendBackward()));
        m.Add(Separator());
        m.Add(new RibbonMenuEntry { Text = "Group", IsEnabled = this.C?.CanGroup == true, Command = new Command(() => this.Run(c => c.Group())) });
        m.Add(new RibbonMenuEntry { Text = "Ungroup", IsEnabled = this.C?.CanUngroup == true, Command = new Command(() => this.Run(c => c.Ungroup())) });
        m.Add(Separator());

        var align = new RibbonMenuEntry { Text = "Align" };
        this.FillAlignMenu(align.Children);
        m.Add(align);

        var rotate = new RibbonMenuEntry { Text = "Rotate" };
        this.FillRotateMenu(rotate.Children);
        m.Add(rotate);
    }

    void FillAlignMenu(IList<RibbonMenuEntry> m)
    {
        m.Add(this.Entry("Align Left", c => c.Align(ShapeAlignment.Left)));
        m.Add(this.Entry("Align Center", c => c.Align(ShapeAlignment.Center)));
        m.Add(this.Entry("Align Right", c => c.Align(ShapeAlignment.Right)));
        m.Add(this.Entry("Align Top", c => c.Align(ShapeAlignment.Top)));
        m.Add(this.Entry("Align Middle", c => c.Align(ShapeAlignment.Middle)));
        m.Add(this.Entry("Align Bottom", c => c.Align(ShapeAlignment.Bottom)));
        m.Add(Separator());
        m.Add(this.Entry("Distribute Horizontally", c => c.Distribute(horizontally: true)));
        m.Add(this.Entry("Distribute Vertically", c => c.Distribute(horizontally: false)));
        m.Add(Separator());
        m.Add(this.Entry("Align to Slide", c => c.AlignToSlide = true, this.C?.AlignToSlide == true));
        m.Add(this.Entry("Align Selected Objects", c => c.AlignToSlide = false, this.C?.AlignToSlide == false));
    }

    void FillRotateMenu(IList<RibbonMenuEntry> m)
    {
        m.Add(this.Entry("Rotate Right 90°", c => c.RotateBy(90)));
        m.Add(this.Entry("Rotate Left 90°", c => c.RotateBy(-90)));
        m.Add(this.Entry("Flip Vertical", c => c.Flip(horizontal: false)));
        m.Add(this.Entry("Flip Horizontal", c => c.Flip(horizontal: true)));
    }

    void FillQuickStyles(IList<RibbonMenuEntry> m)
    {
        foreach (var kind in Enum.GetValues<SlideQuickStyleKind>())
        {
            var parent = new RibbonMenuEntry { Text = new SlideQuickStyle(kind, 1).Name.Split(" - ")[0] };
            for (var accent = 1; accent <= 6; accent++)
            {
                var style = new SlideQuickStyle(kind, accent);
                parent.Children.Add(this.Entry($"Accent {accent}", c => c.ApplyQuickStyle(style)));
            }

            m.Add(parent);
        }
    }

    void AddThemeColours(IList<RibbonMenuEntry> m, Action<SlideEditorController, ArgbColor> apply)
    {
        if (this.C?.ThemeColorScheme is not { } colors)
            return;

        var names = new[] { "Background 1", "Text 1", "Background 2", "Text 2", "Accent 1", "Accent 2", "Accent 3", "Accent 4", "Accent 5", "Accent 6" };
        ArgbColor[] palette = [colors.Light1, colors.Dark1, colors.Light2, colors.Dark2, .. colors.Accents];

        for (var i = 0; i < palette.Length; i++)
        {
            var color = palette[i];
            m.Add(this.Entry(names[i], c => apply(c, color)));
        }
    }

    void FillFillMenu(IList<RibbonMenuEntry> m)
    {
        var themeColours = new RibbonMenuEntry { Text = "Theme Colors" };
        this.AddThemeColours(themeColours.Children, (c, color) => c.SetShapeFill(color));
        m.Add(themeColours);
        m.Add(this.Entry("No Fill", c => c.SetShapeFill(SlideFillSpec.None)));

        var accent = this.C?.ThemeColorScheme?.Accent1 ?? new ArgbColor(255, 0x44, 0x72, 0xC4);
        var accent2 = this.C?.ThemeColorScheme?.Accent2 ?? accent;
        var white = new ArgbColor(255, 255, 255, 255);

        static ArgbColor Mix(ArgbColor a, ArgbColor b, double amount) => new(255,
            (byte)Math.Round(a.R + (b.R - a.R) * amount), (byte)Math.Round(a.G + (b.G - a.G) * amount), (byte)Math.Round(a.B + (b.B - a.B) * amount));

        var gradient = new RibbonMenuEntry { Text = "Gradient" };
        gradient.Children.Add(this.Entry("Light Linear Down", c => c.SetShapeFill(SlideFillSpec.LinearGradient(Mix(accent, white, 0.7), accent, 90))));
        gradient.Children.Add(this.Entry("Dark Linear Down", c => c.SetShapeFill(SlideFillSpec.LinearGradient(accent, Mix(accent, new ArgbColor(255, 0, 0, 0), 0.5), 90))));
        gradient.Children.Add(this.Entry("Linear Right", c => c.SetShapeFill(SlideFillSpec.LinearGradient(Mix(accent, white, 0.6), accent, 0))));
        gradient.Children.Add(this.Entry("Linear Diagonal", c => c.SetShapeFill(SlideFillSpec.LinearGradient(Mix(accent, white, 0.6), accent, 45))));
        gradient.Children.Add(this.Entry("Two Accents", c => c.SetShapeFill(SlideFillSpec.LinearGradient(accent, accent2, 90))));
        m.Add(gradient);
    }

    void FillOutlineMenu(IList<RibbonMenuEntry> m)
    {
        var themeColours = new RibbonMenuEntry { Text = "Theme Colors" };
        this.AddThemeColours(themeColours.Children, (c, color) => c.SetShapeOutlineColor(color));
        m.Add(themeColours);
        m.Add(this.Entry("No Outline", c => c.RemoveShapeOutline()));

        var weight = new RibbonMenuEntry { Text = "Weight" };
        foreach (var points in new[] { 0.25, 0.5, 0.75, 1, 1.5, 2.25, 3, 4.5, 6 })
            weight.Children.Add(this.Entry($"{points.ToString("0.##", CultureInfo.CurrentCulture)} pt", c => c.SetShapeOutlineWeight(points)));

        m.Add(weight);

        var dashes = new RibbonMenuEntry { Text = "Dashes" };
        foreach (var (dash, name) in new (LineDash, string)[]
                 {
                     (LineDash.Solid, "Solid"), (LineDash.SystemDot, "Round Dot"), (LineDash.SystemDash, "Square Dot"), (LineDash.Dash, "Dash"),
                     (LineDash.DashDot, "Dash Dot"), (LineDash.LargeDash, "Long Dash"), (LineDash.LongDashDot, "Long Dash Dot"), (LineDash.LongDashDotDot, "Long Dash Dot Dot")
                 })
        {
            dashes.Children.Add(this.Entry(name, c => c.SetShapeOutlineDash(dash)));
        }

        m.Add(dashes);
    }

    // ---- animations ----

    int? SelectedAnimationIndex
    {
        get
        {
            var animations = this.C?.Animations ?? [];
            if (this.selectedAnimation is { } picked && picked < animations.Count)
                return picked;

            return this.C?.SelectionAnimations is { Count: > 0 } own ? own[0].Index : null;
        }
    }

    SlideAnimation? SelectedAnimation
        => this.SelectedAnimationIndex is { } index ? this.C?.Animations.ElementAtOrDefault(index) : null;

    void MoveAnimation(int direction)
    {
        if (this.SelectedAnimationIndex is not { } index || this.C is not { } controller)
            return;

        this.selectedAnimation = index + direction;
        controller.MoveAnimation(index, direction);
    }

    string animationPaneKey = string.Empty;

    void RebuildAnimationPane()
    {
        if (!this.showAnimationPane || this.C is not { } controller)
            return;

        var key = $"{controller.Index}|{string.Join(",", controller.Animations.Select(x => $"{x.ShapeId}{x.Effect}{x.Trigger}"))}|{this.SelectedAnimationIndex}";
        if (key == this.animationPaneKey)
            return;

        this.animationPaneKey = key;
        this.animationList.Children.Clear();

        foreach (var item in SlideAnimationTimeline.Schedule(controller.Animations))
        {
            var index = item.Index;
            var target = controller.Current?.Shapes.FirstOrDefault(x => x.Id == item.Animation.ShapeId);
            var mark = item.Animation.Class switch
            {
                SlideAnimationClass.Entrance => Color.FromArgb("#3E8E41"),
                SlideAnimationClass.Emphasis => Color.FromArgb("#C9A227"),
                _ => Color.FromArgb("#C0392B")
            };

            var remove = new Button { Text = "✕", FontSize = 11, Padding = new Thickness(4, 0), BackgroundColor = Colors.Transparent, MinimumHeightRequest = 0, HeightRequest = 24 };
            remove.SetAppThemeColor(Button.TextColorProperty, Color.FromArgb("#161C23"), Color.FromArgb("#E3E7EE"));
            remove.Clicked += (_, _) => this.Run(c => c.RemoveAnimation(index));

            var row = new Grid
            {
                ColumnSpacing = 6,
                Padding = new Thickness(4, 2),
                BackgroundColor = this.SelectedAnimationIndex == index ? Color.FromArgb("#330055D9") : Colors.Transparent,
                ColumnDefinitions = { new ColumnDefinition(16), new ColumnDefinition(8), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) }
            };

            row.Add(new Label { Text = item.Animation.Trigger == SlideAnimationTrigger.OnClick ? item.Click.ToString(CultureInfo.CurrentCulture) : string.Empty, FontSize = 11, VerticalTextAlignment = Microsoft.Maui.TextAlignment.Center }, 0, 0);
            row.Add(new BoxView { Color = mark, WidthRequest = 8, HeightRequest = 8, VerticalOptions = LayoutOptions.Center }, 1, 0);
            row.Add(new Label { Text = target?.Name ?? $"Shape {item.Animation.ShapeId}", FontSize = 12, LineBreakMode = LineBreakMode.TailTruncation, VerticalTextAlignment = Microsoft.Maui.TextAlignment.Center }, 2, 0);
            row.Add(new Label { Text = SlideAnimation.NameOf(item.Animation.Effect), FontSize = 11, Opacity = 0.75, VerticalTextAlignment = Microsoft.Maui.TextAlignment.Center }, 3, 0);
            row.Add(remove, 4, 0);

            var tap = new TapGestureRecognizer { Command = new Command(() => this.SelectAnimationRow(index)) };
            row.GestureRecognizers.Add(tap);
            this.animationList.Children.Add(row);
        }
    }

    void SelectAnimationRow(int index)
    {
        this.selectedAnimation = index;

        if (this.C is { } controller && controller.Animations.ElementAtOrDefault(index) is { } animation &&
            controller.Current?.Shapes.ToList().FindIndex(x => x.Id == animation.ShapeId) is >= 0 and var shape)
        {
            controller.Select(shape);
            this.editor.Repaint();
        }

        this.RefreshBar();
    }

    // ---- outline ----

    string outlineKey = string.Empty;

    void RebuildOutline()
    {
        if (this.C is not { ViewMode: SlideEditorViewMode.Outline } controller)
            return;

        var outline = controller.Outline;
        var key = string.Join("\u0001", outline.Select(x => x.Title + "\u0002" + x.BodyText)) + "|" + controller.Index;
        if (key == this.outlineKey)
            return;

        this.outlineKey = key;
        this.outlineList.Children.Clear();

        foreach (var entry in outline)
        {
            var slide = entry.Slide;
            var title = new Entry { Text = entry.Title, FontSize = 14, FontAttributes = FontAttributes.Bold, IsReadOnly = !this.Editable, Placeholder = "Title" };
            var body = new Editor { Text = entry.BodyText, FontSize = 13, AutoSize = EditorAutoSizeOption.TextChanges, IsReadOnly = !this.Editable, Placeholder = "Text" };

            title.Unfocused += (_, _) =>
            {
                if (title.Text != entry.Title)
                    this.Run(c => c.SetOutline(slide, title.Text ?? string.Empty, null));
            };

            body.Unfocused += (_, _) =>
            {
                if (body.Text != entry.BodyText)
                    this.Run(c => c.SetOutline(slide, null, body.Text ?? string.Empty));
            };

            var number = new Label { Text = (slide + 1).ToString(CultureInfo.CurrentCulture), FontSize = 12, Opacity = 0.7, WidthRequest = 22, HorizontalTextAlignment = Microsoft.Maui.TextAlignment.End };
            var tap = new TapGestureRecognizer { Command = new Command(() => { controller.ClearSelection(); controller.Index = slide; }) };
            number.GestureRecognizers.Add(tap);

            var row = new Grid
            {
                ColumnSpacing = 8,
                BackgroundColor = slide == controller.Index ? Color.FromArgb("#1A0055D9") : Colors.Transparent,
                ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }
            };

            row.Add(number, 0, 0);
            row.Add(new VerticalStackLayout { Spacing = 0, Children = { title, body } }, 1, 0);
            this.outlineList.Children.Add(row);
        }
    }

    // ---- wiring ----

    void WirePanes()
    {
        this.outlinePane.Content = this.outlineList;

        var close = new Button { Text = "✕", FontSize = 12, BackgroundColor = Colors.Transparent, Padding = new Thickness(6, 0), MinimumHeightRequest = 0, HeightRequest = 26 };
        close.SetAppThemeColor(Button.TextColorProperty, Color.FromArgb("#161C23"), Color.FromArgb("#E3E7EE"));
        close.Clicked += (_, _) =>
        {
            this.showAnimationPane = false;
            this.ApplyRailVisibility();
            this.RefreshBar();
        };

        var play = new Button { Text = "▶ Play", FontSize = 12, Padding = new Thickness(8, 0), MinimumHeightRequest = 0, HeightRequest = 28 };
        play.Clicked += (_, _) => this.editor.Preview();

        this.animationPane.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        this.animationPane.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        this.animationPane.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        this.animationPane.Add(new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            Children = { new Label { Text = "Animation Pane", FontAttributes = FontAttributes.Bold, FontSize = 13, VerticalTextAlignment = Microsoft.Maui.TextAlignment.Center } }
        }, 0, 0);
        ((Grid)this.animationPane.Children[0]).Add(close, 1, 0);
        this.animationPane.Add(play, 0, 1);
        this.animationPane.Add(new ScrollView { Content = this.animationList }, 0, 2);
        this.animationPane.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb("#EFF4FB"), Color.FromArgb("#1F2126"));

        Button StatusButton(string text, string automationId, Action action)
        {
            var button = new Button
            {
                Text = text,
                FontSize = 12,
                Padding = new Thickness(8, 2),
                MinimumHeightRequest = 0,
                MinimumWidthRequest = 0,
                BackgroundColor = Colors.Transparent,
                AutomationId = automationId
            };

            button.SetAppThemeColor(Button.TextColorProperty, Color.FromArgb("#161C23"), Color.FromArgb("#E3E7EE"));
            button.Clicked += (_, _) => action();
            return button;
        }

        this.statusTools.Children.Add(StatusButton("Normal", "SlideStatusNormal", () => this.ViewMode = SlideEditorViewMode.Normal));
        this.statusTools.Children.Add(StatusButton("Sorter", "SlideStatusSorter", () => this.ViewMode = SlideEditorViewMode.SlideSorter));
        this.statusTools.Children.Add(StatusButton("▶", "SlideStatusShow", () => this.StartPresenting()));
        this.statusTools.Children.Add(StatusButton("−", "SlideStatusZoomOut", () => { this.C?.ZoomStep(-1); this.SetZoom(this.C?.Zoom); }));
        this.statusTools.Children.Add(this.zoomLabel);
        this.statusTools.Children.Add(StatusButton("+", "SlideStatusZoomIn", () => { this.C?.ZoomStep(1); this.SetZoom(this.C?.Zoom); }));
        this.statusTools.Children.Add(StatusButton("Fit", "SlideStatusFit", () => this.SetZoom(null)));

        this.ApplyFileButton();

        // The numbered click markers are the Animations tab's, as in PowerPoint.
        this.ribbon.TabChanged += (_, _) =>
        {
            if (this.C is { } controller)
            {
                controller.ShowAnimationMarkers = this.ribbon.SelectedTab?.Key == "animations";
                this.editor.Repaint();
            }
        };
    }

    void OnShortcutRequested(object? sender, SlideShortcut shortcut)
    {
        switch (shortcut)
        {
            case SlideShortcut.SlideShowFromBeginning: this.StartPresenting(0); break;
            case SlideShortcut.SlideShowFromCurrent: this.StartPresenting(); break;
            case SlideShortcut.InsertLink: _ = this.EditLinkAsync(); break;
            case SlideShortcut.Replace: this.replaceEntry?.Focus(); break;
        }
    }

    // ---- refresh ----

    void RefreshPowerPoint()
    {
        var controller = this.C;

        foreach (var (item, enabled) in this.gated)
            item.IsEnabled = enabled();

        foreach (var (item, isChecked) in this.checks)
            item.IsChecked = isChecked();

        foreach (var (menu, key, fill) in this.dynamicMenus)
        {
            var now = key();
            if (this.menuKeys.TryGetValue(menu, out var previous) && previous == now)
                continue;

            this.menuKeys[menu] = now;
            menu.Menu.Clear();
            fill(menu.Menu);
        }

        if (this.alignJustify is not null)
        {
            this.alignJustify.IsChecked = controller?.CaretFormat.Alignment == TextAlignment.Justify;
            this.alignJustify.IsEnabled = this.HasTextOrShape;
        }

        // Paragraph alignment also applies to a selected shape's whole text.
        foreach (var item in new RibbonItem[] { this.alignLeft, this.alignCenter, this.alignRight })
            item.IsEnabled = this.HasTextOrShape;

        if (this.shapeFormatTab is not null)
            this.shapeFormatTab.IsVisible = controller?.HasShapeSelection == true && !this.EffectiveReadOnly;

        if (this.tableDesignTab is not null && this.tableLayoutTab is not null)
            this.tableDesignTab.IsVisible = this.tableLayoutTab.IsVisible = controller?.SelectedTable is not null && !this.EffectiveReadOnly;

        if (this.chartTab is not null)
            this.chartTab.IsVisible = controller?.SelectedChart is not null && !this.EffectiveReadOnly;

        if (this.masterTab is not null)
            this.masterTab.IsVisible = controller?.ViewMode == SlideEditorViewMode.SlideMaster;

        this.refreshingFields = true;
        try
        {
            if (this.widthEntry is not null && !this.widthEntry.IsFocused)
                this.widthEntry.Text = controller?.Selection is { } s ? Number(s.Width / 96) : string.Empty;

            if (this.heightEntry is not null && !this.heightEntry.IsFocused)
                this.heightEntry.Text = controller?.Selection is { } s2 ? Number(s2.Height / 96) : string.Empty;

            var transition = controller?.CurrentTransition;
            if (this.transitionDuration is not null && !this.transitionDuration.IsFocused)
                this.transitionDuration.Text = Number((transition?.Duration ?? TimeSpan.Zero).TotalSeconds);

            if (this.advanceAfter is not null && !this.advanceAfter.IsFocused)
                this.advanceAfter.Text = transition?.AdvanceAfter is { } after ? Number(after.TotalSeconds) : string.Empty;

            if (this.advanceOnClick is not null)
                this.advanceOnClick.IsChecked = transition?.AdvanceOnClick ?? true;

            if (this.advanceAfterOn is not null)
                this.advanceAfterOn.IsChecked = transition?.AdvanceAfter is not null;

            var animation = this.SelectedAnimation;
            if (this.animationDuration is not null && !this.animationDuration.IsFocused)
                this.animationDuration.Text = animation is null ? string.Empty : Number(animation.Duration.TotalSeconds);

            if (this.animationDelay is not null && !this.animationDelay.IsFocused)
                this.animationDelay.Text = animation is null ? string.Empty : Number(animation.Delay.TotalSeconds);

            if (this.animationStart is not null)
                this.animationStart.SelectedIndex = animation is null ? -1 : (int)animation.Trigger;
        }
        finally
        {
            this.refreshingFields = false;
        }

        this.slideCountLabel.Text = controller is null ? string.Empty
            : controller.IsEditingMaster ? "Slide Master"
            : $"Slide {controller.Index + 1} of {controller.Count}";

        this.zoomLabel.Text = $"{Math.Round((controller?.EffectiveZoom ?? 1) * 100)}%";

        if (controller is not null && controller.ViewMode != SlideEditorViewMode.Normal)
        {
            this.status.Text = controller.ViewMode switch
            {
                SlideEditorViewMode.SlideSorter => "Drag a slide to move it; double-tap to open it",
                SlideEditorViewMode.SlideMaster => "Slide Master — changes here apply to every slide using this layout",
                SlideEditorViewMode.Outline => "Outline — edit titles and text; a tab at the start of a line indents it",
                _ => this.status.Text
            };
        }
        else if (controller is { HasMultipleSelection: true })
        {
            this.status.Text = "Shapes selected — Ctrl+G groups them";
        }

        this.RebuildAnimationPane();
        this.RebuildOutline();
        this.SyncStatus();
        this.SyncShellStatus();
    }

    // ---- dialogs ----

    async Task PromptSectionAsync(int section)
    {
        if (OfficeMenus.PageOf(this) is not { } page || this.C is not { } controller)
            return;

        var current = section >= 0 ? controller.Sections.ElementAtOrDefault(section)?.Name : null;
        var name = await page.DisplayPromptAsync(section < 0 ? "Add Section" : "Rename Section", "Section name", "OK", "Cancel", initialValue: current ?? "Untitled Section");
        if (string.IsNullOrWhiteSpace(name))
            return;

        this.Run(c =>
        {
            if (section < 0)
                c.AddSection(name.Trim());
            else
                c.RenameSection(section, name.Trim());
        });
    }

    async Task EditLinkAsync()
    {
        if (OfficeMenus.PageOf(this) is not { } page || this.C is not { SelectedShape: >= 0 } controller || !this.Editable)
            return;

        const string web = "Web page or file";
        const string slide = "Slide in this presentation";
        const string action = "Show action";
        const string remove = "Remove Link";

        var choice = await page.DisplayActionSheetAsync("Insert Link", "Cancel", controller.CurrentHyperlink is null ? null : remove, web, slide, action);

        switch (choice)
        {
            case remove:
                this.Run(c => c.RemoveHyperlink());
                break;

            case web:
                var url = await page.DisplayPromptAsync("Insert Link", "Address", "OK", "Cancel", "https://", initialValue: controller.CurrentHyperlink?.Url ?? string.Empty, keyboard: Keyboard.Url);
                if (!string.IsNullOrWhiteSpace(url))
                    this.Run(c => c.SetHyperlink(new SlideHyperlink(url.Trim())));
                break;

            case slide:
                var titles = Enumerable.Range(0, controller.Count).Select(i => $"{i + 1}. {controller.Deck.Slides[i].Title ?? $"Slide {i + 1}"}").ToArray();
                var picked = await page.DisplayActionSheetAsync("Slide", "Cancel", null, titles);
                var index = Array.IndexOf(titles, picked);
                if (index >= 0)
                    this.Run(c => c.SetHyperlink(new SlideHyperlink(null, index)));
                break;

            case action:
                var actions = new[] { ("Next Slide", SlideShowJumps.NextSlide), ("Previous Slide", SlideShowJumps.PreviousSlide), ("First Slide", SlideShowJumps.FirstSlide), ("Last Slide", SlideShowJumps.LastSlide), ("End Show", SlideShowJumps.EndShow) };
                var jump = await page.DisplayActionSheetAsync("Show action", "Cancel", null, actions.Select(x => x.Item1).ToArray());
                var found = actions.FirstOrDefault(x => x.Item1 == jump);
                if (found.Item2 is not null)
                    this.Run(c => c.SetHyperlink(new SlideHyperlink(null, null, found.Item2)));
                break;
        }
    }

    async Task EditHeaderFooterAsync()
    {
        if (this.C is not { } controller || !this.Editable)
            return;

        var current = controller.CurrentHeaderFooter;
        var date = new Switch { IsToggled = current.DateAndTime };
        var number = new Switch { IsToggled = current.SlideNumber };
        var footer = new Switch { IsToggled = current.Footer };
        var footerText = new Entry { Text = current.FooterText, Placeholder = "Footer text" };
        var notOnTitle = new Switch();

        static View Row(string label, View control) => new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            Children = { new Label { Text = label, VerticalTextAlignment = Microsoft.Maui.TextAlignment.Center }, control }
        }.WithColumn(control, 1);

        var result = await SlideDialogPage.ShowAsync(this, "Header & Footer", new VerticalStackLayout
        {
            Spacing = 10,
            Children = { Row("Date and time", date), Row("Slide number", number), Row("Footer", footer), footerText, Row("Don't show on title slide", notOnTitle) }
        }, "Apply to All", "Apply");

        if (result is null)
            return;

        var settings = new SlideHeaderFooter
        {
            DateAndTime = date.IsToggled,
            SlideNumber = number.IsToggled,
            Footer = footer.IsToggled,
            FooterText = footerText.Text ?? string.Empty,
            NotOnTitleSlide = notOnTitle.IsToggled
        };

        this.Run(c => c.ApplyHeaderFooter(settings, applyToAll: result == "Apply to All"));
    }

    async Task EditSlideSizeAsync()
    {
        if (this.Deck is not { } deck || !this.Editable)
            return;

        var width = new Entry { Text = Number(deck.SlideWidth / 96), Keyboard = Keyboard.Numeric };
        var height = new Entry { Text = Number(deck.SlideHeight / 96), Keyboard = Keyboard.Numeric };
        var scale = new Switch { IsToggled = true };

        var result = await SlideDialogPage.ShowAsync(this, "Slide Size", new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new Label { Text = "Width (in)" }, width,
                new Label { Text = "Height (in)" }, height,
                new HorizontalStackLayout { Spacing = 8, Children = { scale, new Label { Text = "Scale content to fit (Ensure Fit)", VerticalTextAlignment = Microsoft.Maui.TextAlignment.Center } } }
            }
        }, "OK");

        if (result is null ||
            !double.TryParse(width.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var w) ||
            !double.TryParse(height.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var h))
            return;

        this.Run(c => c.SetSlideSize(w * 96, h * 96, scale.IsToggled));
    }

    async Task EditBackgroundAsync(bool master)
    {
        if (OfficeMenus.PageOf(this) is not { } page || this.C is not { } controller || !this.Editable)
            return;

        const string solid = "Solid fill";
        const string gradient = "Gradient fill";
        const string picture = "Picture fill";
        const string reset = "Reset Background";

        var choice = await page.DisplayActionSheetAsync("Format Background", "Cancel", reset, solid, gradient, picture);
        if (choice is null or "Cancel")
            return;

        if (choice == reset)
        {
            this.Run(c =>
            {
                if (master)
                    c.Master.SetBackground(null);
                else
                    c.ResetBackground();
            });
            return;
        }

        SlideBackgroundSpec? spec = null;

        if (choice == picture)
        {
            var (image, rejected) = await OfficeMenus.PickImageAsync(page);
            if (rejected is not null)
            {
                this.DropRejected?.Invoke(this, rejected);
                return;
            }

            if (image is not null)
                spec = SlideBackgroundSpec.Image(image.Data, image.ContentType);
        }
        else
        {
            var first = new ColorPickerButton { Text = "Colour", ShowOpacity = false, SelectedColor = Colors.White };
            var second = new ColorPickerButton { Text = "To", ShowOpacity = false, SelectedColor = FromArgb(controller.ThemeColorScheme?.Accent1 ?? new ArgbColor(255, 0x44, 0x72, 0xC4)) };
            var body = new VerticalStackLayout { Spacing = 8, Children = { first } };
            if (choice == gradient)
                body.Children.Add(second);

            var result = await SlideDialogPage.ShowAsync(this, choice, body, master ? "Apply" : "Apply to All", master ? null : "Apply");
            if (result is null)
                return;

            spec = choice == gradient
                ? SlideBackgroundSpec.Gradient(ToArgb(first.SelectedColor), ToArgb(second.SelectedColor))
                : SlideBackgroundSpec.Solid(ToArgb(first.SelectedColor));

            if (!master && result == "Apply to All")
            {
                this.Run(c => c.SetBackground(spec, applyToAll: true));
                return;
            }
        }

        if (spec is null)
            return;

        this.Run(c =>
        {
            if (master)
                c.Master.SetBackground(spec);
            else
                c.SetBackground(spec);
        });
    }

    async Task InsertMediaAsync(bool video)
    {
        if (this.C is null || !this.Editable)
            return;

        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = video ? "Insert Video" : "Insert Audio",
                FileTypes = video ? FilePickerFileType.Videos : null
            });

            if (result is null)
                return;

            await using var stream = await result.OpenReadAsync();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);

            var contentType = string.IsNullOrEmpty(result.ContentType) ? (video ? "video/mp4" : "audio/mpeg") : result.ContentType;
            this.Run(c => c.AddMedia(buffer.ToArray(), contentType, video, name: Path.GetFileNameWithoutExtension(result.FileName)));
        }
        catch (Exception ex)
        {
            this.DropRejected?.Invoke(this, new OfficeDropRejected(string.Empty, ex.Message));
        }
    }

    /// <summary>The chart data sheet: a grid of categories by series, and the chart type.</summary>
    async Task EditChartAsync(SlideChartKind? insert)
    {
        if (this.C is not { } controller || !this.Editable)
            return;

        var chart = insert is { } kind ? SlideChart.Sample(kind) : controller.SelectedChart;
        if (chart is null)
            return;

        var categories = new ObservableCollection<string>(chart.Categories);
        var seriesNames = chart.Series.Select(x => x.Name).ToList();
        var values = chart.Series.Select(s => Enumerable.Range(0, chart.Categories.Count).Select(i => i < s.Values.Count ? s.Values[i] : 0).ToList()).ToList();

        var type = new Picker { ItemsSource = Enum.GetNames<SlideChartKind>(), SelectedIndex = (int)chart.Kind };
        var title = new Entry { Text = chart.Title, Placeholder = "Chart title" };
        var grid = new Grid { ColumnSpacing = 4, RowSpacing = 4 };

        void Rebuild()
        {
            grid.Children.Clear();
            grid.RowDefinitions.Clear();
            grid.ColumnDefinitions.Clear();

            for (var c = 0; c <= seriesNames.Count; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition(110));

            for (var r = 0; r <= categories.Count; r++)
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            for (var s = 0; s < seriesNames.Count; s++)
            {
                var column = s;
                var header = new Entry { Text = seriesNames[column], FontAttributes = FontAttributes.Bold };
                header.TextChanged += (_, e) => seriesNames[column] = e.NewTextValue ?? string.Empty;
                grid.Add(header, column + 1, 0);
            }

            for (var r = 0; r < categories.Count; r++)
            {
                var row = r;
                var name = new Entry { Text = categories[row] };
                name.TextChanged += (_, e) => categories[row] = e.NewTextValue ?? string.Empty;
                grid.Add(name, 0, row + 1);

                for (var s = 0; s < seriesNames.Count; s++)
                {
                    var column = s;
                    var cell = new Entry { Text = values[column][row].ToString(CultureInfo.CurrentCulture), Keyboard = Keyboard.Numeric };
                    cell.TextChanged += (_, e) =>
                    {
                        if (double.TryParse(e.NewTextValue, NumberStyles.Float, CultureInfo.CurrentCulture, out var v))
                            values[column][row] = v;
                    };

                    grid.Add(cell, column + 1, row + 1);
                }
            }
        }

        Rebuild();

        var addRow = new Button { Text = "+ Category" };
        addRow.Clicked += (_, _) =>
        {
            categories.Add($"Category {categories.Count + 1}");
            foreach (var series in values)
                series.Add(0);

            Rebuild();
        };

        var addSeries = new Button { Text = "+ Series" };
        addSeries.Clicked += (_, _) =>
        {
            seriesNames.Add($"Series {seriesNames.Count + 1}");
            values.Add([.. Enumerable.Repeat(0d, categories.Count)]);
            Rebuild();
        };

        var removeRow = new Button { Text = "− Category" };
        removeRow.Clicked += (_, _) =>
        {
            if (categories.Count <= 1)
                return;

            categories.RemoveAt(categories.Count - 1);
            foreach (var series in values)
                series.RemoveAt(series.Count - 1);

            Rebuild();
        };

        var removeSeries = new Button { Text = "− Series" };
        removeSeries.Clicked += (_, _) =>
        {
            if (seriesNames.Count <= 1)
                return;

            seriesNames.RemoveAt(seriesNames.Count - 1);
            values.RemoveAt(values.Count - 1);
            Rebuild();
        };

        var result = await SlideDialogPage.ShowAsync(this, insert is null ? "Edit Chart Data" : "Insert Chart", new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                type,
                title,
                new ScrollView { Orientation = ScrollOrientation.Both, Content = grid, MaximumHeightRequest = 360 },
                new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, Children = { addRow, removeRow, addSeries, removeSeries } }
            }
        }, "OK");

        if (result is null)
            return;

        var edited = new SlideChart(
            (SlideChartKind)Math.Max(0, type.SelectedIndex),
            [.. categories],
            [.. seriesNames.Select((name, i) => new SlideChartSeries(name, [.. values[i]]))])
        {
            Title = string.IsNullOrWhiteSpace(title.Text) ? null : title.Text
        };

        this.Run(c =>
        {
            if (insert is not null)
                c.AddChart(edited);
            else
                c.SetChartData(edited);
        });
    }
}

/// <summary>
/// A small modal form the slide editor's dialogs are built from: a title, the fields, and buttons.
/// </summary>
sealed class SlideDialogPage : ContentPage
{
    readonly TaskCompletionSource<string?> result = new();

    SlideDialogPage(string title, View body, string primary, string? secondary)
    {
        this.Title = title;
        this.SetAppThemeColor(BackgroundColorProperty, Colors.White, Color.FromArgb("#1F2126"));

        var buttons = new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.End };

        Button Add(string text, string? value)
        {
            var button = new Button { Text = text };
            button.Clicked += async (_, _) =>
            {
                this.result.TrySetResult(value);
                await this.Navigation.PopModalAsync();
            };

            buttons.Children.Add(button);
            return button;
        }

        Add("Cancel", null);
        if (secondary is not null)
            Add(secondary, secondary);

        Add(primary, primary);

        this.Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 20,
                Spacing = 14,
                Children =
                {
                    new Label { Text = title, FontSize = 18, FontAttributes = FontAttributes.Bold },
                    body,
                    buttons
                }
            }
        };
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        this.result.TrySetResult(null);
    }

    /// <summary>Shows the form and returns the pressed button's text, or null for Cancel.</summary>
    public static async Task<string?> ShowAsync(Element owner, string title, View body, string primary, string? secondary = null)
    {
        if (OfficeMenus.PageOf(owner) is not { } page)
            return null;

        var dialog = new SlideDialogPage(title, body, primary, secondary);
        await page.Navigation.PushModalAsync(dialog);
        return await dialog.result.Task;
    }
}

static class SlideGridExtensions
{
    public static Grid WithColumn(this Grid grid, View view, int column)
    {
        Grid.SetColumn(view, column);
        return grid;
    }
}
