using Shiny.Controls.Office.Icons;
using Shiny.Controls.Office.Packaging;
using Shiny.Controls.Office.Presentation;
using Shiny.Controls.Office.Shapes;
using Shiny.Maui.Controls.ColorPicker;
using Shiny.Maui.Controls.Ribbons;
using Shiny.Maui.Controls.FontPicker;
using Shiny.Controls.Office.Text;
using TextAlignment = Shiny.Controls.Office.Text.TextAlignment;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// <see cref="SlideEditor"/> with an editing toolbar above it.
/// </summary>
/// <remarks>
/// <para>
/// Built from MAUI primitives plus the core package's <c>FontPickerButton</c> and
/// <c>FontSizePickerButton</c>. MAUI has no <c>ShinyToolbar</c> — that control is Blazor-only — so the
/// bar is a scrolling row here, while the Blazor <c>SlideEditorView</c> composes ShinyToolbar for the
/// same slots. The API and behaviour match; only the internals differ.
/// </para>
/// <para>
/// Every plain button on it is an <see cref="OfficeToolbarButton"/> drawing from the shared
/// <see cref="OfficeIcons"/> set — the same artwork, at the same weight, as the document toolbar and
/// as both Blazor toolbars. The pickers are the exception: a font, a size and a colour have to show
/// what they are currently set to.
/// </para>
/// </remarks>
public class SlideEditorView : ContentView, IDisposable
{
    readonly SlideEditor editor = new();
    readonly Ribbon ribbon;
    readonly Grid root;
    readonly Label status;
    readonly Label counter;

    readonly RibbonButton previous;
    readonly RibbonButton next;
    readonly RibbonButton present;
    readonly RibbonToggleButton bold;
    readonly RibbonToggleButton italic;
    readonly RibbonToggleButton underline;
    readonly RibbonToggleButton strike;
    readonly RibbonToggleButton alignLeft;
    readonly RibbonToggleButton alignCenter;
    readonly RibbonToggleButton alignRight;
    readonly RibbonButton outdent;
    readonly RibbonButton indent;
    readonly RibbonToggleButton bulletList;
    readonly RibbonToggleButton numberedList;
    readonly RibbonButton addTextBox;
    readonly RibbonToggleButton highlight;
    readonly RibbonButton insertTable;
    readonly RibbonButton insertPicture;
    readonly RibbonButton watermark;
    readonly RibbonButton deleteShape;
    readonly RibbonButton undo;
    readonly RibbonButton redo;
    readonly OfficeFindBar findBar = new();
    readonly ColorPickerButton textColor;
    readonly List<RibbonItem> buttons = [];

    SlideView? show;
    View? fontPicker;
    View? sizePicker;
    bool suppressPickerEvents;
    bool disposed;

    public SlideEditorView()
    {
        this.previous = this.MakeButton(OfficeIcon.Previous, "Previous slide", () => this.editor.Previous());
        this.next = this.MakeButton(OfficeIcon.Next, "Next slide", () => this.editor.Next());

        // Not MakeButton: that one runs AfterCommand, which raises DeckChanged - and starting a show
        // changes nothing about the deck. It would tell a host to save a file nobody edited. It also
        // pulls focus back to the editor, which is the one control that must not have it while a show
        // is up.
        this.present = this.Track(OfficeRibbonItems.Command(
            OfficeIcon.SlideShow,
            "Play the deck full screen, from this slide",
            () => this.StartPresenting(),
            text: "Slide show",
            automationId: "SlideToolbarSlideShow"));

        // Large, like the Blazor bar's: the label is what says "show" where the icon beside two
        // chevrons could still read as one more way to move a slide.
        this.present.Size = RibbonItemSize.Large;

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

        this.addTextBox = this.MakeButton(OfficeIcon.TextBox, "Add a text box", this.AddTextBox);
        this.deleteShape = this.MakeButton(OfficeIcon.Delete, "Delete the selected shape", () => this.editor.Controller?.DeleteSelectedShape());

        this.highlight = this.MakeToggle(OfficeIcon.Highlight, "Highlight", () => _ = this.PickHighlightAsync());
        this.insertTable = this.MakeAsyncButton(OfficeIcon.Table, "Table", this.InsertTableAsync);
        this.insertPicture = this.MakeAsyncButton(OfficeIcon.Picture, "Picture", this.InsertPictureAsync);
        this.watermark = this.MakeAsyncButton(OfficeIcon.Watermark, "Watermark", this.PickWatermarkAsync);

        this.undo = this.MakeButton(OfficeIcon.Undo, "Undo (Ctrl+Z)", () => this.editor.Controller?.Undo());
        this.redo = this.MakeButton(OfficeIcon.Redo, "Redo (Ctrl+Shift+Z)", () => this.editor.Controller?.Redo());

        this.counter = new Label
        {
            FontSize = 13,
            WidthRequest = 54,
            HorizontalTextAlignment = Microsoft.Maui.TextAlignment.Center,

            // VerticalTextAlignment, not just VerticalOptions. The ribbon pins every small-item row to
            // one height, so the label *is* the row - there is no spare room for VerticalOptions to
            // centre it in, and the text falls to the top of its own box while the arrows beside it
            // centre their glyphs in theirs. That reads as the counter sitting too high.
            VerticalTextAlignment = Microsoft.Maui.TextAlignment.Center,
            VerticalOptions = LayoutOptions.Center
        };

        this.status = new Label
        {
            FontSize = 12,
            Opacity = 0.7,
            Padding = new Thickness(10, 4),
            LineBreakMode = LineBreakMode.TailTruncation
        };

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
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            }
        };

        this.root.Add(this.ribbon);
        this.root.Add(this.editor);
        this.root.Add(this.status);
        Grid.SetRow(this.editor, 1);
        Grid.SetRow(this.status, 2);

        this.editor.DeckChanged += this.OnDeckChanged;
        this.AttachDrop();
        this.editor.SlideChanged += this.OnSlideChanged;
        this.Content = this.root;

        this.BuildBar();
    }

    public static readonly BindableProperty DeckProperty = BindableProperty.Create(
        nameof(Deck),
        typeof(SlideDeck),
        typeof(SlideEditorView),
        propertyChanged: (b, _, value) =>
        {
            var view = (SlideEditorView)b;
            view.editor.Deck = (SlideDeck?)value;
            view.AttachController();
        });

    public static readonly BindableProperty ThemeProperty = BindableProperty.Create(
        nameof(Theme),
        typeof(SlideTheme),
        typeof(SlideEditorView),
        SlideTheme.Light,
        propertyChanged: (b, _, value) => ((SlideEditorView)b).editor.Theme = (SlideTheme)value);

    public static readonly BindableProperty SlideIndexProperty = BindableProperty.Create(
        nameof(SlideIndex),
        typeof(int),
        typeof(SlideEditorView),
        0,
        BindingMode.TwoWay,
        propertyChanged: (b, _, value) => ((SlideEditorView)b).editor.SlideIndex = (int)value);

    public static readonly BindableProperty IsReadOnlyProperty = BindableProperty.Create(
        nameof(IsReadOnly),
        typeof(bool),
        typeof(SlideEditorView),
        false,
        propertyChanged: (b, _, value) =>
        {
            var view = (SlideEditorView)b;
            view.editor.IsReadOnly = (bool)value;
            view.RefreshBar();
        });

    public static readonly BindableProperty ShowToolbarProperty = BindableProperty.Create(
        nameof(ShowToolbar),
        typeof(bool),
        typeof(SlideEditorView),
        true,
        propertyChanged: (b, _, value) => ((SlideEditorView)b).ribbon.IsVisible = (bool)value);

    /// <summary>A one-line hint under the canvas saying what the current gesture will do.</summary>
    public static readonly BindableProperty ShowStatusProperty = BindableProperty.Create(
        nameof(ShowStatus),
        typeof(bool),
        typeof(SlideEditorView),
        true,
        propertyChanged: (b, _, value) => ((SlideEditorView)b).status.IsVisible = (bool)value);

    /// <summary>
    /// Whether the icon-only toolbar buttons carry a hover tooltip naming what they do.
    /// </summary>
    /// <remarks>
    /// On for desktop, off for phones and tablets. Every button on this bar is icon only, and an icon
    /// with no label is a guess until something names it — but the tooltip that names it opens on
    /// hover, and there is no hover on a touch screen. A long-press tooltip is not the answer either:
    /// it would compete with the tap the button exists for. Touch hosts get the semantic description
    /// instead, which is what a screen reader reads on any platform.
    /// </remarks>
    public static readonly BindableProperty ShowToolbarTooltipsProperty = BindableProperty.Create(
        nameof(ShowToolbarTooltips),
        typeof(bool),
        typeof(SlideEditorView),
        // The ribbon decides for itself whether to show a tooltip, from the same hover-capability
        // rule - so this now only reaches the pickers the bar hosts, which draw their own.
        OfficeToolbarButton.TooltipsByDefault);

    public static readonly BindableProperty FontFamiliesProperty = BindableProperty.Create(
        nameof(FontFamilies),
        typeof(IList<string>),
        typeof(SlideEditorView),
        propertyChanged: (b, _, _) => ((SlideEditorView)b).BuildBar());

    public static readonly BindableProperty FontSizesProperty = BindableProperty.Create(
        nameof(FontSizes),
        typeof(IList<double>),
        typeof(SlideEditorView),
        propertyChanged: (b, _, _) => ((SlideEditorView)b).BuildBar());

    /// <summary>The deck to edit. Must have been opened with <c>editable: true</c>.</summary>
    public SlideDeck? Deck
    {
        get => (SlideDeck?)this.GetValue(DeckProperty);
        set => this.SetValue(DeckProperty, value);
    }

    public SlideTheme Theme
    {
        get => (SlideTheme)this.GetValue(ThemeProperty);
        set => this.SetValue(ThemeProperty, value);
    }

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

    public bool ShowToolbar
    {
        get => (bool)this.GetValue(ShowToolbarProperty);
        set => this.SetValue(ShowToolbarProperty, value);
    }

    public bool ShowStatus
    {
        get => (bool)this.GetValue(ShowStatusProperty);
        set => this.SetValue(ShowStatusProperty, value);
    }

    /// <summary>Hover tooltips on the icon-only toolbar buttons. Desktop only by default.</summary>
    public bool ShowToolbarTooltips
    {
        get => (bool)this.GetValue(ShowToolbarTooltipsProperty);
        set => this.SetValue(ShowToolbarTooltipsProperty, value);
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

    /// <summary>Raised after every edit.</summary>
    public event EventHandler? DeckChanged;

    /// <summary>Raised when a dropped or chosen file could not be inserted, so a host can say so.</summary>
    public event EventHandler<OfficeDropRejected>? DropRejected;

    public static readonly BindableProperty ShapeWidthProperty = BindableProperty.Create(
        nameof(ShapeWidth), typeof(double), typeof(SlideEditorView), 240d);

    public static readonly BindableProperty ShapeHeightProperty = BindableProperty.Create(
        nameof(ShapeHeight), typeof(double), typeof(SlideEditorView), 180d);

    public static readonly BindableProperty PictureWidthProperty = BindableProperty.Create(
        nameof(PictureWidth), typeof(double), typeof(SlideEditorView), 400d);

    /// <summary>The size of shape the toolbar inserts, in slide pixels.</summary>
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

    /// <summary>How wide an inserted picture is, in slide pixels.</summary>
    public double PictureWidth
    {
        get => (double)this.GetValue(PictureWidthProperty);
        set => this.SetValue(PictureWidthProperty, value);
    }

    public SlideEditorController? Controller => this.editor.Controller;

    /// <summary>Routes a physical key press to the editor. See <see cref="SlideEditor.HandleKey"/>.</summary>
    public bool HandleKey(EditorKey key, bool shift = false, bool control = false)
    {
        var handled = this.editor.HandleKey(key, shift, control);
        this.RefreshBar();
        return handled;
    }

    public void FocusEditor() => this.editor.FocusEditor();

    // ---- the show ----

    /// <inheritdoc cref="SlideView.ShowPresenterControlsProperty"/>
    public static readonly BindableProperty ShowPresenterControlsProperty = BindableProperty.Create(
        nameof(ShowPresenterControls),
        typeof(bool),
        typeof(SlideEditorView),
        true,
        propertyChanged: (b, _, value) =>
        {
            if (((SlideEditorView)b).show is { } show)
                show.ShowPresenterControls = (bool)value;
        });

    /// <inheritdoc cref="SlideView.KeepScreenOnWhilePresentingProperty"/>
    public static readonly BindableProperty KeepScreenOnWhilePresentingProperty = BindableProperty.Create(
        nameof(KeepScreenOnWhilePresenting),
        typeof(bool),
        typeof(SlideEditorView),
        true,
        propertyChanged: (b, _, value) =>
        {
            if (((SlideEditorView)b).show is { } show)
                show.KeepScreenOnWhilePresenting = (bool)value;
        });

    /// <inheritdoc cref="ShowPresenterControlsProperty"/>
    public bool ShowPresenterControls
    {
        get => (bool)this.GetValue(ShowPresenterControlsProperty);
        set => this.SetValue(ShowPresenterControlsProperty, value);
    }

    /// <inheritdoc cref="KeepScreenOnWhilePresentingProperty"/>
    public bool KeepScreenOnWhilePresenting
    {
        get => (bool)this.GetValue(KeepScreenOnWhilePresentingProperty);
        set => this.SetValue(KeepScreenOnWhilePresentingProperty, value);
    }

    /// <summary>Whether the deck is playing full screen.</summary>
    public bool IsPresenting => this.show?.IsPresenting == true;

    /// <summary>
    /// Raised when the show starts or ends, however it ended — the Exit button, the back gesture, a
    /// swipe-down on the modal.
    /// </summary>
    public event EventHandler<bool>? PresentingChanged;

    /// <summary>
    /// Play the deck full screen, from <paramref name="from"/> or from the slide being edited.
    /// </summary>
    /// <remarks>
    /// From the current slide rather than from the top, because that is what the button is for while a
    /// deck is being built: a show started to see how the slide in front of you actually lands. Pass 0
    /// for the run-through.
    /// </remarks>
    public void StartPresenting(int? from = null)
    {
        if (this.Deck is null || this.IsPresenting)
            return;

        // The caret and the drag handles are editing state, and a show is not editing. Left standing,
        // they are what the editor paints the instant the show ends — over whichever slide the
        // presenter walked to, where the shape they belonged to is not.
        this.editor.Controller?.ClearSelection();

        if (from is { } index)
            this.SlideIndex = index;

        var view = this.EnsureShow();

        // Assigned every time rather than once: the deck, the watermark and the slide are all things a
        // host can have changed since the last show, and the surface is kept between them.
        view.Deck = this.Deck;
        view.Watermark = this.Watermark;
        view.SlideIndex = this.SlideIndex;
        view.StartPresenting();

        this.RefreshBar();
    }

    /// <summary>End the show and go back to editing. A no-op when no show is running.</summary>
    public void StopPresenting() => this.show?.StopPresenting();

    /// <summary>
    /// The viewer that carries the show, built on the first play and kept.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <see cref="SlideView"/> over the same deck rather than a presenting mode grown on the editing
    /// surface: everything a show needs already lives there — the modal page, the black surround, the
    /// presenter bar that fades out, tap-to-advance and the screen lock — and a second copy of it in
    /// the editor would be a second copy to keep right. The deck is the shared object, so the show
    /// paints the edits made a moment ago without a save or a reload.
    /// </para>
    /// <para>
    /// It goes into the tree, invisible, rather than being held as a loose object: a
    /// <see cref="SlideView"/> finds the navigation to push its show onto by walking up its own
    /// parents, and one with no parent falls back to the application's first window — the wrong page
    /// in a multi-window desktop app, and nothing at all in a test. Invisible costs nothing to lay
    /// out, and the surface the audience sees is the show page's own.
    /// </para>
    /// </remarks>
    SlideView EnsureShow()
    {
        if (this.show is not null)
            return this.show;

        var view = new SlideView
        {
            IsVisible = false,
            ShowPresenterControls = this.ShowPresenterControls,
            KeepScreenOnWhilePresenting = this.KeepScreenOnWhilePresenting
        };

        view.PresentingChanged += this.OnShowPresentingChanged;

        this.root.Add(view);
        Grid.SetRow(view, 1);

        this.show = view;
        return view;
    }

    void OnShowPresentingChanged(object? sender, bool value)
    {
        if (!value && this.show is { } view)
        {
            // The show page hands its owner the slide it ended on before this fires, so the editor is
            // left where the presenter left it rather than where the show began.
            this.SlideIndex = view.SlideIndex;
            this.editor.FocusEditor();
        }

        this.RefreshBar();
        this.PresentingChanged?.Invoke(this, value);
    }

    // ---- toolbar ----

    void BuildBar()
    {
        this.ribbon.Tabs.Clear();

        this.fontPicker = this.CreateFontPicker();
        this.sizePicker = this.CreateSizePicker();

        this.ribbon.QuickAccessItems.Clear();
        this.ribbon.QuickAccessItems.Add(this.undo);
        this.ribbon.QuickAccessItems.Add(this.redo);

        var tab = new RibbonTab { Title = "Home", Key = "home" };

        // Which slide you are on is navigation, not formatting, so it leads rather than sitting among
        // the text commands.
        var slide = new RibbonGroup { Title = "Slide", Priority = 110 };
        // The counter belongs between the arrows, not after them: it is the thing the two arrows move,
        // and reading "< > 1/3" makes them look like two commands with an unrelated label beside them.
        slide.Items.Add(this.previous);
        slide.Items.Add(OfficeRibbonItems.Host(this.counter));
        slide.Items.Add(this.next);

        // Beside the arrows rather than on a tab of its own: playing the deck is what the slide you
        // are looking at is *for*, and a show that costs a tab switch is one nobody starts to check a
        // build.
        slide.Items.Add(this.present);
        tab.Groups.Add(slide);

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
        tab.Groups.Add(font);

        var paragraph = new RibbonGroup { Title = "Paragraph", Priority = 90 };
        paragraph.Items.Add(this.alignLeft);
        paragraph.Items.Add(this.alignCenter);
        paragraph.Items.Add(this.alignRight);
        paragraph.Items.Add(new RibbonSeparator());
        paragraph.Items.Add(this.bulletList);
        paragraph.Items.Add(this.numberedList);
        paragraph.Items.Add(this.outdent);
        paragraph.Items.Add(this.indent);
        tab.Groups.Add(paragraph);

        // On Home rather than a tab of its own: finding a word is something you do while building the
        // deck, and a search that costs a tab switch is one nobody uses. Last on the tab, because it is
        // reached less often than the formatting beside it - which is what decides the order groups
        // fold into the overflow in on a narrow window.
        var finding = new RibbonGroup { Title = "Find", Priority = 60 };
        finding.Items.Add(OfficeRibbonItems.Host(this.findBar));
        tab.Groups.Add(finding);

        this.ribbon.Tabs.Add(tab);

        // Two tabs. Home is the slide you are on and the text on it; Insert is what goes on it. The
        // split is only worth making because the second tab holds a real bar - a text box, three ways
        // to place an object and the way to remove one - rather than a token button.
        var insertTab = new RibbonTab { Title = "Insert", Key = "insert" };
        var insert = new RibbonGroup { Title = "Insert", Priority = 100 };
        insert.Items.Add(this.addTextBox);
        insert.Items.Add(this.insertTable);
        insert.Items.Add(this.insertPicture);
        insert.Items.Add(this.watermark);
        insert.Items.Add(new RibbonSeparator());

        // Removing is the opposite of the four beside it, so it belongs here rather than among the
        // text commands - with a rule in front, because it is destructive and the others are not.
        insert.Items.Add(this.deleteShape);
        insertTab.Groups.Add(insert);
        this.ribbon.Tabs.Add(insertTab);
        this.ribbon.Tabs.Add(OfficeRibbonItems.ShapesTab(this.InsertShape));

        this.RefreshBar();
    }

    /// <summary>
    /// The core package's colour picker, in its button form.
    /// </summary>
    /// <remarks>
    /// Not a row of preset swatches: a deck's text can be any colour, and a fixed palette is a promise
    /// the format does not make. The button shows the colour at the caret and opens the full spectrum —
    /// the same control the Blazor toolbar puts in this slot.
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

    void AddTextBox()
    {
        if (this.editor.Controller is not { } controller)
            return;

        controller.AddTextBox(
            Math.Max(0, controller.Deck.SlideWidth / 2 - 160),
            Math.Max(0, controller.Deck.SlideHeight / 2 - 32));
    }

    // ---- insert ----

    /// <summary>
    /// Where a new object goes: the middle of the slide.
    /// </summary>
    /// <remarks>
    /// Not the origin, which is under the title placeholder — an object inserted there is both hidden
    /// and awkward to grab.
    /// </remarks>
    static (double X, double Y) Centred(SlideEditorController controller, double width, double height)
        => (Math.Max(0, (controller.Deck.SlideWidth - width) / 2),
            Math.Max(0, (controller.Deck.SlideHeight - height) / 2));

    async Task PickHighlightAsync()
    {
        var (chosen, color) = await OfficeMenus.PickHighlightAsync(OfficeMenus.PageOf(this));
        if (!chosen)
            return;

        this.editor.Controller?.SetHighlight(color);
        this.AfterCommand();
    }

    /// <summary>Drops a shape on the current slide, centred.</summary>
    void InsertShape(ShapeGeometry geometry)
    {
        if (this.editor.Controller is not { } controller)
            return;

        var (x, y) = Centred(controller, this.ShapeWidth, this.ShapeHeight);
        controller.AddShape(geometry, x, y, this.ShapeWidth, this.ShapeHeight);
        this.AfterCommand();
    }

    async Task InsertTableAsync()
    {
        if (this.editor.Controller is not { } controller)
            return;

        if (await OfficeMenus.PickTableAsync(OfficeMenus.PageOf(this)) is not { } size)
            return;

        // Sized to the slide rather than to the grid: a 2x2 and a 6x4 both want to be a table on a
        // slide, not a postage stamp and something that overflows the edge.
        var width = controller.Deck.SlideWidth * 0.7;
        var height = Math.Min(controller.Deck.SlideHeight * 0.6, size.Rows * 44);
        var (x, y) = Centred(controller, width, height);

        controller.AddTable(size.Rows, size.Columns, x, y, width, height);
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

        if (image is not null)
            this.InsertImage(image, null);
    }

    /// <summary>
    /// Places a picture, at a point when one was given and in the middle otherwise.
    /// </summary>
    /// <remarks>
    /// A drop knows where it landed and should use it; the toolbar button has no such point, and
    /// centring is the honest answer rather than a guess at where the user was looking.
    /// </remarks>
    void InsertImage(OfficePickedImage image, (double X, double Y)? at)
    {
        if (this.editor.Controller is not { } controller)
            return;

        var width = Math.Min(controller.Deck.SlideWidth / 2, this.PictureWidth);
        var height = width * 0.75;

        var (x, y) = at is { } point
            ? (point.X - (width / 2), point.Y - (height / 2))
            : Centred(controller, width, height);

        controller.AddPicture(
            image.Data,
            image.ContentType,
            x,
            y,
            width,
            height,
            Path.GetFileNameWithoutExtension(image.FileName));

        this.AfterCommand();
    }

    // ---- file drop ----

    void AttachDrop()
    {
        var drop = new DropGestureRecognizer { AllowDrop = true };
        drop.Drop += this.OnDropAsync;
        this.editor.GestureRecognizers.Add(drop);
    }

    async void OnDropAsync(object? sender, DropEventArgs e)
    {
        if (this.IsReadOnly || this.Deck is null || this.editor.Controller is not { } controller)
            return;

        // Where the drop landed, in slide coordinates. Read before the await, while the gesture's
        // position still means something.
        var point = e.GetPosition(this.editor) is { } position
            ? controller.ToSlide(position.X, position.Y)
            : null;

        try
        {
            foreach (var image in await OfficeFileDrop.ReadImagesAsync(e))
                this.InsertImage(image, point);
        }
        catch (Exception ex)
        {
            this.DropRejected?.Invoke(this, new OfficeDropRejected(string.Empty, ex.Message));
        }
    }

    static readonly IList<string> DefaultFontFamilies =
        ["Calibri", "Cambria", "Arial", "Times New Roman", "Georgia", "Verdana", "Courier New"];

    // Slide type runs large: 18pt is a small body size on a deck, where a document's is 11.
    static readonly IList<double> DefaultFontSizes =
        [8, 10, 12, 14, 16, 18, 20, 24, 28, 32, 36, 44, 54, 66, 88];

    /// <summary>One height for every control in the bar. See <see cref="OfficeToolbarButton.ItemHeight"/>.</summary>
    const double ToolbarItemHeight = OfficeToolbarButton.ItemHeight;


    RibbonButton MakeButton(OfficeIcon icon, string hint, Action action)
        => this.Track(OfficeRibbonItems.Command(icon, hint, () =>
        {
            action();
            this.AfterCommand();
        }, automationId: $"SlideToolbar{icon}"));

    /// <summary>
    /// A command whose work opens a menu or a file picker first.
    /// </summary>
    /// <remarks>
    /// It does not call <c>AfterCommand</c> itself: each waits for the user to choose something, and
    /// refreshing the bar before then would happen while the menu is still up.
    /// </remarks>
    RibbonButton MakeAsyncButton(OfficeIcon icon, string hint, Func<Task> action)
        => this.Track(OfficeRibbonItems.Command(icon, hint, () => _ = action(), automationId: $"SlideToolbar{icon}"));

    RibbonToggleButton MakeToggle(OfficeIcon icon, string hint, Action action)
        => this.Track(OfficeRibbonItems.Toggle(icon, hint, () =>
        {
            action();
            this.AfterCommand();
        }, $"SlideToolbar{icon}"));

    /// <summary>Remembers an item so <c>RefreshBar</c> can drive the set in one pass.</summary>
    T Track<T>(T item) where T : RibbonItem
    {
        this.buttons.Add(item);
        return item;
    }

    void AfterCommand()
    {
        // Focus returns to the editor after every toolbar action; leaving it on the button means the
        // next keystroke goes nowhere, which reads as the editor having stopped working.
        this.editor.FocusEditor();
        this.RefreshBar();
        this.DeckChanged?.Invoke(this, EventArgs.Empty);
    }

    void AttachController()
    {
        if (this.editor.Controller is { } controller)
            controller.Changed += this.OnControllerChanged;

        // A new deck is a new controller and therefore a new finder; a bar left holding the old one
        // would count matches in a deck that is no longer on screen.
        this.findBar.Find = this.editor.Controller?.Find;

        this.RefreshBar();
    }

    void OnControllerChanged(object? sender, EventArgs e) => this.RefreshBar();

    void OnDeckChanged(object? sender, EventArgs e)
    {
        this.RefreshBar();
        this.DeckChanged?.Invoke(this, EventArgs.Empty);
    }

    void OnSlideChanged(object? sender, int index)
    {
        this.SlideIndex = index;
        this.RefreshBar();
    }

    /// <summary>Reflects the state under the caret back into the toolbar.</summary>
    void RefreshBar()
    {
        var controller = this.editor.Controller;
        var format = controller?.CaretFormat ?? SlideCaretFormat.Default;

        var enabled = !this.IsReadOnly && this.Deck is not null;
        var hasSelection = enabled && controller?.SelectedShape >= 0;

        // Text formatting only means something while a caret is inside a shape's text. A live Bold
        // button with nothing to embolden is worse than a disabled one: it says the click did
        // something.
        var hasText = enabled && controller?.IsEditingText == true;

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

        foreach (RibbonItem item in new RibbonItem[] { this.bold, this.italic, this.underline, this.strike, this.highlight,
                                                       this.alignLeft, this.alignCenter, this.alignRight,
                                                       this.bulletList, this.numberedList, this.indent })
        {
            item.IsEnabled = hasText;
        }

        // The top level is as far out as a paragraph can come, so the button is off there rather than
        // clamping silently.
        this.outdent.IsEnabled = hasText && format.Level > 0;

        this.addTextBox.IsEnabled = enabled;
        this.insertTable.IsEnabled = enabled;
        this.insertPicture.IsEnabled = enabled;
        this.deleteShape.IsEnabled = hasSelection;

        this.previous.IsEnabled = controller?.CanGoPrevious ?? false;
        this.next.IsEnabled = controller?.CanGoNext ?? false;

        // Playing works in a read-only deck - it changes nothing - so it follows whether a deck is
        // open rather than whether it can be edited.
        this.present.IsEnabled = this.Deck is not null;

        this.undo.IsEnabled = enabled && (controller?.CanUndo ?? false);
        this.redo.IsEnabled = enabled && (controller?.CanRedo ?? false);

        this.counter.Text = controller is null ? "—" : $"{controller.Index + 1}/{controller.Count}";

        this.status.Text = controller switch
        {
            { SelectedShape: >= 0, IsEditingText: true } => "Editing text — double-tap a word to select it, Esc to leave",
            { SelectedShape: >= 0 } => "Shape selected — double-tap to edit its text",
            _ => "Tap a shape to select it; double-tap to edit its text."
        };

        // Writing the pickers' selection raises their change events, which would immediately re-apply
        // the format that was only being displayed - so the handlers are muted while they update.
        this.suppressPickerEvents = true;

        if (this.fontPicker is FontPickerButton font)
            font.SelectedFont = format.FontFamily;

        if (this.sizePicker is FontSizePickerButton size)
        {
            // Snap to the nearest offered size: a deck can hold any value, the picker only some.
            var sizes = this.FontSizes ?? DefaultFontSizes;
            size.SelectedFontSize = sizes.OrderBy(x => Math.Abs(x - format.FontSize)).FirstOrDefault();
        }

        this.textColor.SelectedColor = FromArgb(format.Color);
        this.textColor.IsEnabled = hasText;

        this.suppressPickerEvents = false;

        // Finding works in a read-only deck - it changes nothing - so it follows whether a deck is
        // open rather than whether it can be edited.
        this.findBar.IsEnabled = this.Deck is not null;
        this.findBar.SetTooltipsEnabled(this.ShowToolbarTooltips);
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

        this.editor.DeckChanged -= this.OnDeckChanged;
        this.editor.SlideChanged -= this.OnSlideChanged;

        if (this.editor.Controller is { } controller)
            controller.Changed -= this.OnControllerChanged;

        // Drops the bar's subscription to the finder, which outlives this view: the finder belongs to
        // the controller and the controller to the deck, and a host can keep both open.
        this.findBar.Find = null;

        if (this.show is not null)
        {
            this.show.PresentingChanged -= this.OnShowPresentingChanged;

            // Disposing it stops a show that is still up: a modal page left on screen over a view that
            // has gone is a deck nobody can get out of.
            this.show.Dispose();
            this.show = null;
        }

        this.editor.Dispose();
    }

    /// <summary>
    /// The colour this control wears: its ribbon's header band and tab underline.
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="OfficeAccent.Presentation"/> — the colour Microsoft's own PowerPoint wears,
    /// because that is what a user reads as "slides" before any label has been looked at. Set it to
    /// take on the app's own brand instead, or to <c>null</c> to leave the bar on the theme's neutrals
    /// like the rest of the chrome.
    /// </remarks>
    public static readonly BindableProperty AccentProperty = BindableProperty.Create(
        nameof(Accent),
        typeof(OfficeAccent),
        typeof(SlideEditorView),
        OfficeAccent.Presentation,
        propertyChanged: (b, _, _) => ((SlideEditorView)b).ApplyAccent());

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
