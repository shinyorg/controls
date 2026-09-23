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
    readonly RibbonButton newSlide;
    readonly RibbonButton duplicateSlide;
    readonly RibbonButton deleteSlide;
    readonly RibbonButton moveSlideEarlier;
    readonly RibbonButton moveSlideLater;
    readonly RibbonMenuButton slideLayout;
    readonly Button notesButton;
    readonly Grid statusBar;
    readonly RibbonButton paste;
    readonly RibbonButton cut;
    readonly RibbonButton copy;
    readonly RibbonButton duplicateShape;
    readonly RibbonButton toFront;
    readonly RibbonButton forward;
    readonly RibbonButton backward;
    readonly RibbonButton toBack;
    readonly SlideRail rail = new();
    readonly Editor notes;
    readonly ColumnDefinition railColumn = new(180);
    string? notesDraft;
    int notesSlide = -1;
    bool writingNotes;
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

        // The deck's own structure. Labelled, because a slide with a plus, a slide with an arrow and a
        // bin are four guesses in a row without words under them.
        this.newSlide = this.MakeButton(OfficeIcon.NewSlide, "Add a slide after this one", () => this.editor.Controller?.NewSlide(), "New slide");
        this.duplicateSlide = this.MakeButton(OfficeIcon.Duplicate, "Duplicate this slide", () => this.editor.Controller?.DuplicateSlide(), "Duplicate", "SlideToolbarDuplicateSlide");
        this.deleteSlide = this.MakeAsyncButton(OfficeIcon.Delete, "Delete this slide", this.RequestDeleteSlideAsync, "Delete", "SlideToolbarDeleteSlide");
        this.moveSlideEarlier = this.MakeButton(OfficeIcon.MoveSlideEarlier, "Move this slide earlier", () => this.editor.Controller?.MoveSlideEarlier());
        this.moveSlideLater = this.MakeButton(OfficeIcon.MoveSlideLater, "Move this slide later", () => this.editor.Controller?.MoveSlideLater());

        // Layouts are read off the current slide's master each time the menu opens, so it is built
        // empty here and filled in RefreshBar.
        this.slideLayout = this.Track(new RibbonMenuButton
        {
            Text = "Layout",
            Tooltip = "Change this slide's layout, or add a slide with one",
            Size = RibbonItemSize.Small,
            AutomationId = "SlideToolbarLayout",
            IconTemplate = OfficeRibbonItems.IconTemplateFor(OfficeIcon.SlideLayout)
        });

        // In the status bar, where PowerPoint keeps it: the notes are a way of looking at the slide,
        // and a ribbon column for them pushed Paragraph into a dropdown at desktop widths.
        this.notesButton = new Button
        {
            Text = "Notes",
            FontSize = 12,
            Padding = new Thickness(10, 2),
            MinimumHeightRequest = 0,
            BackgroundColor = Colors.Transparent,
            AutomationId = "SlideNotesToggle"
        };
        this.notesButton.Clicked += (_, _) => this.ShowNotes = !this.ShowNotes;
        this.notesButton.SetAppThemeColor(Button.TextColorProperty, Color.FromArgb("#161C23"), Color.FromArgb("#E3E7EE"));

        this.paste = this.MakeButton(OfficeIcon.Paste, "Paste the copied shape", () => this.editor.Controller?.Paste(), "Paste");
        this.paste.Size = RibbonItemSize.Large;
        this.cut = this.MakeButton(OfficeIcon.Cut, "Cut the selected shape", () => this.editor.Controller?.CutShape(), "Cut");
        this.copy = this.MakeButton(OfficeIcon.Copy, "Copy the selected shape", () => this.editor.Controller?.CopyShape(), "Copy");
        this.duplicateShape = this.MakeButton(OfficeIcon.Duplicate, "Duplicate the selected shape", () => this.editor.Controller?.DuplicateShape(), "Duplicate", "SlideToolbarDuplicateShape");

        this.toFront = this.MakeButton(OfficeIcon.BringToFront, "Bring to front", () => this.editor.Controller?.BringToFront(), "To front");
        this.forward = this.MakeButton(OfficeIcon.BringForward, "Bring forward", () => this.editor.Controller?.BringForward(), "Forward");
        this.backward = this.MakeButton(OfficeIcon.SendBackward, "Send backward", () => this.editor.Controller?.SendBackward(), "Backward");
        this.toBack = this.MakeButton(OfficeIcon.SendToBack, "Send to back", () => this.editor.Controller?.SendToBack(), "To back");

        this.notes = new Editor
        {
            Placeholder = "Tap to add notes",
            HeightRequest = 96,
            FontSize = 13,
            AutoSize = EditorAutoSizeOption.Disabled,
            IsVisible = false,
            AutomationId = "SlideNotes"
        };
        this.notes.TextChanged += this.OnNotesChanged;

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

        // The rail beside the slide, the notes under it. Built now rather than added when first shown:
        // AppKit never realizes a child added to a laid-out view, so both are toggled by IsVisible.
        var main = new Grid
        {
            RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) }
        };
        main.Add(this.editor);
        main.Add(this.notes);
        Grid.SetRow(this.notes, 1);

        var body = new Grid
        {
            ColumnDefinitions = { this.railColumn, new ColumnDefinition(GridLength.Star) }
        };
        body.Add(this.rail);
        body.Add(main);
        Grid.SetColumn(main, 1);

        this.rail.Interacted += (_, _) =>
        {
            this.editor.FocusEditor();
            this.RefreshBar();
        };

        this.root.Add(this.ribbon);
        this.root.Add(body);
        this.statusBar = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
        };
        this.statusBar.Add(this.status);
        this.statusBar.Add(this.notesButton);
        Grid.SetColumn(this.notesButton, 1);

        this.root.Add(this.statusBar);
        Grid.SetRow(body, 1);
        Grid.SetRow(this.statusBar, 2);

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
        propertyChanged: (b, _, value) => ((SlideEditorView)b).statusBar.IsVisible = (bool)value);

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
        // The counter belongs between the arrows, not after them: it is the thing the two arrows move,
        // and reading "< > 1/3" makes them look like two commands with an unrelated label beside them.
        // A row is what keeps the three on one line - filling columns stacked the arrows and left the
        // counter alone in the next one.
        var slide = new RibbonGroup { Title = "Slide", Priority = 110 };
        slide.Items.Add(OfficeRibbonItems.Row(
            this.previous,
            OfficeRibbonItems.Host(this.counter),
            this.next
        ));

        // Beside the arrows rather than on a tab of its own: playing the deck is what the slide you
        // are looking at is *for*, and a show that costs a tab switch is one nobody starts to check a
        // build.
        this.present.Size = RibbonItemSize.Large;
        slide.Items.Add(this.present);
        tab.Groups.Add(slide);

        // Beside navigation rather than on Insert: PowerPoint puts New Slide on Home, and that is
        // where people look for it.
        var slides = new RibbonGroup { Title = "Slides", Priority = 105 };

        // Two rows, the Blazor bar's shape: a large New Slide and a column per pair made the group
        // wide enough to push Paragraph into a dropdown. Earlier and Later are icon only.
        this.newSlide.Size = RibbonItemSize.Small;
        slides.Items.Add(OfficeRibbonItems.Row(this.newSlide, this.duplicateSlide, this.deleteSlide));
        slides.Items.Add(OfficeRibbonItems.Row(this.moveSlideEarlier, this.moveSlideLater, this.slideLayout));
        tab.Groups.Add(slides);

        // Shapes, not text. The same commands as Ctrl+C/X/V/D through HandleKey.
        var clipboard = new RibbonGroup { Title = "Clipboard", Priority = 95 };
        clipboard.Items.Add(this.paste);
        clipboard.Items.Add(this.cut);
        clipboard.Items.Add(this.copy);
        clipboard.Items.Add(this.duplicateShape);

        var arrange = new RibbonGroup { Title = "Arrange", Priority = 85 };
        arrange.Items.Add(this.toFront);
        arrange.Items.Add(this.forward);
        arrange.Items.Add(this.backward);
        arrange.Items.Add(this.toBack);

        // Rows: the two boxes on top, the run of marks underneath - the same shape the document editor
        // draws, so the two bars are learned once. Filling columns put bold above italic and underline
        // above strikethrough, and forced the font picker to share a column with a toggle.
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

        // Lists and the indent pair on top, the alignments underneath - the same two rows the document
        // editor draws.
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
            this.alignRight
        ));
        tab.Groups.Add(paragraph);

        // On Home rather than a tab of its own: finding a word is something you do while building the
        // deck, and a search that costs a tab switch is one nobody uses. Last on the tab, because it is
        // reached less often than the formatting beside it - which is what decides the order groups
        // fold into the overflow in on a narrow window. It spans the rows: on a single row it left the
        // row underneath empty for the width of a search box.
        var finding = new RibbonGroup { Title = "Find", Priority = 60 };
        finding.Items.Add(OfficeRibbonItems.HostLarge(this.findBar));
        tab.Groups.Add(finding);

        this.ribbon.Tabs.Add(tab);

        // Two tabs. Home is the slide you are on and the text on it; Insert is what goes on it. The
        // split is only worth making because the second tab holds a real bar - a text box, three ways
        // to place an object and the way to remove one - rather than a token button.
        var insertTab = new RibbonTab { Title = "Insert", Key = "insert" };
        // What goes on the slide, on one row; removing one underneath, which says "this one is
        // different" better than a rule beside it in a column flow that put delete under a text box
        // anyway.
        var insert = new RibbonGroup { Title = "Insert", Priority = 100 };
        insert.Items.Add(OfficeRibbonItems.Row(
            this.addTextBox,
            this.insertTable,
            this.insertPicture
        ));

        this.deleteShape.Text = "Delete";
        insert.Items.Add(OfficeRibbonItems.Row(this.deleteShape));
        insertTab.Groups.Add(insert);

        // Clipboard and Arrange act on objects, so they sit with the other object commands. On Home
        // they pushed Font and Paragraph into dropdowns at ordinary desktop widths.
        insertTab.Groups.Add(clipboard);
        insertTab.Groups.Add(arrange);

        // The watermark is a picture drawn behind the whole slide, not a thing you place on it - so it
        // is about the design of the slide rather than its contents, and sat oddly among the four
        // commands that put an object under the pointer.
        var design = new RibbonGroup { Title = "Design", Priority = 90 };
        this.watermark.Size = RibbonItemSize.Large;
        this.watermark.Text = "Watermark";
        design.Items.Add(this.watermark);
        insertTab.Groups.Add(design);
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


    // AutomationId is set-once in MAUI, so a second button on the same icon names its own here
    // rather than having it reassigned afterwards.
    RibbonButton MakeButton(OfficeIcon icon, string hint, Action action, string? text = null, string? automationId = null)
        => this.Track(OfficeRibbonItems.Command(icon, hint, () =>
        {
            action();
            this.AfterCommand();
        }, text, automationId: automationId ?? $"SlideToolbar{icon}"));

    /// <summary>
    /// A command whose work opens a menu or a file picker first.
    /// </summary>
    /// <remarks>
    /// It does not call <c>AfterCommand</c> itself: each waits for the user to choose something, and
    /// refreshing the bar before then would happen while the menu is still up.
    /// </remarks>
    RibbonButton MakeAsyncButton(OfficeIcon icon, string hint, Func<Task> action, string? text = null, string? automationId = null)
        => this.Track(OfficeRibbonItems.Command(icon, hint, () => _ = action(), text, automationId ?? $"SlideToolbar{icon}"));

    /// <summary>
    /// Ask before the toolbar deletes a slide. On by default.
    /// </summary>
    /// <remarks>
    /// Undo brings a deleted slide back, but a slide is a lot of work to lose to a mis-tap on a button
    /// beside New Slide, and nothing on screen says undo is the way back.
    /// <see cref="SlideEditorController.DeleteSlide"/> itself never asks.
    /// </remarks>
    public static readonly BindableProperty ConfirmSlideDeleteProperty = BindableProperty.Create(
        nameof(ConfirmSlideDelete),
        typeof(bool),
        typeof(SlideEditorView),
        true);

    public bool ConfirmSlideDelete
    {
        get => (bool)this.GetValue(ConfirmSlideDeleteProperty);
        set => this.SetValue(ConfirmSlideDeleteProperty, value);
    }

    /// <summary>
    /// Replaces the built-in confirmation — the page's alert — with the app's own, e.g.
    /// <c>IDialogService.Confirm</c>. Given the index of the slide about to go; return true to delete it.
    /// </summary>
    public Func<int, Task<bool>>? ConfirmDeleteSlide { get; set; }

    /// <summary>
    /// Show the slide rail — thumbnails to tap and drag — beside the slide. On by default; the view
    /// hides it on its own below 600 wide, where there is room for one column.
    /// </summary>
    public static readonly BindableProperty ShowSlideRailProperty = BindableProperty.Create(
        nameof(ShowSlideRail),
        typeof(bool),
        typeof(SlideEditorView),
        true,
        propertyChanged: (b, _, _) => ((SlideEditorView)b).ApplyRailVisibility());

    public bool ShowSlideRail
    {
        get => (bool)this.GetValue(ShowSlideRailProperty);
        set => this.SetValue(ShowSlideRailProperty, value);
    }

    /// <summary>Show the speaker-notes box under the slide. Off by default; the ribbon's Notes toggles it.</summary>
    public static readonly BindableProperty ShowNotesProperty = BindableProperty.Create(
        nameof(ShowNotes),
        typeof(bool),
        typeof(SlideEditorView),
        false,
        BindingMode.TwoWay,
        propertyChanged: (b, _, value) =>
        {
            var view = (SlideEditorView)b;
            view.notes.IsVisible = (bool)value;
            view.notesSlide = -1;
            view.RefreshBar();
        });

    public bool ShowNotes
    {
        get => (bool)this.GetValue(ShowNotesProperty);
        set => this.SetValue(ShowNotesProperty, value);
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        this.ApplyRailVisibility();
    }

    void ApplyRailVisibility()
    {
        var show = this.ShowSlideRail && (this.Width <= 0 || this.Width >= 600);
        this.rail.IsVisible = show;
        this.railColumn.Width = show ? new GridLength(180) : new GridLength(0);
    }

    /// <summary>
    /// Writes the notes box through to the deck as the user types; the command merges a run of these
    /// into one undo step.
    /// </summary>
    void OnNotesChanged(object? sender, TextChangedEventArgs e)
    {
        if (this.writingNotes || this.editor.Controller is not { } controller || this.IsReadOnly)
            return;

        this.notesDraft = e.NewTextValue;
        this.notesSlide = controller.Index;
        controller.SetNotes(this.notesDraft);
        this.DeckChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Refills the notes box when the slide changed, or when the notes changed from anywhere but the
    /// box — an undo. Never while the text matches, which is what keeps a trailing Enter from being
    /// swallowed: the deck trims trailing blank lines, the box must not.
    /// </summary>
    void SyncNotes(SlideEditorController? controller)
    {
        if (controller is null || !this.notes.IsVisible)
            return;

        static string Normal(string? value) => (value ?? string.Empty).Replace("\r\n", "\n").TrimEnd();

        if (controller.Index == this.notesSlide && Normal(this.notesDraft) == Normal(controller.Notes))
            return;

        this.notesDraft = controller.Notes;
        this.notesSlide = controller.Index;

        this.writingNotes = true;
        try
        {
            this.notes.Text = this.notesDraft ?? string.Empty;
        }
        finally
        {
            this.writingNotes = false;
        }
    }

    string? layoutMenuKey;

    /// <summary>Rebuilds the layout menu only when what it would show has changed — not on every caret move.</summary>
    void RebuildLayoutMenu(SlideEditorController? controller, bool enabled)
    {
        var layouts = controller?.Layouts ?? [];
        var key = $"{enabled}|{string.Join("|", layouts.Select(x => $"{x.Name}{(x.IsCurrent ? "*" : "")}"))}";
        if (key == this.layoutMenuKey)
            return;

        this.layoutMenuKey = key;
        this.slideLayout.Menu.Clear();
        if (controller is null)
            return;

        foreach (var layout in layouts)
        {
            var chosen = layout;
            this.slideLayout.Menu.Add(new RibbonMenuEntry
            {
                Text = layout.Name,
                IsChecked = layout.IsCurrent,
                IsEnabled = enabled,
                Command = new Command(() =>
                {
                    this.editor.Controller?.SetLayout(chosen);
                    this.AfterCommand();
                })
            });
        }

        this.slideLayout.Menu.Add(new RibbonMenuEntry { IsSeparator = true });

        var add = new RibbonMenuEntry { Text = "New slide with layout", IsEnabled = enabled };
        foreach (var layout in layouts)
        {
            var chosen = layout;
            add.Children.Add(new RibbonMenuEntry
            {
                Text = layout.Name,
                Command = new Command(() =>
                {
                    this.editor.Controller?.NewSlide(chosen);
                    this.AfterCommand();
                })
            });
        }

        this.slideLayout.Menu.Add(add);
    }

    /// <summary>Deletes the current slide, asking first unless <see cref="ConfirmSlideDelete"/> is off.</summary>
    public async Task RequestDeleteSlideAsync()
    {
        if (this.editor.Controller is not { CanDeleteSlide: true } controller || this.IsReadOnly)
            return;

        var index = controller.Index;

        if (this.ConfirmSlideDelete)
        {
            bool confirmed;

            if (this.ConfirmDeleteSlide is { } ask)
            {
                confirmed = await ask(index);
            }
            else if (OfficeMenus.PageOf(this) is { } page)
            {
                var what = this.Deck?.Slides.ElementAtOrDefault(index)?.Title is { Length: > 0 } title
                    ? $"“{title}” and everything on it"
                    : "This slide and everything on it";

                confirmed = await page.DisplayAlertAsync(
                    $"Delete slide {index + 1}?",
                    $"{what} will be removed from the deck. You can undo this.",
                    "Delete",
                    "Cancel");
            }
            else
            {
                // Nowhere to ask, so nothing is deleted: a confirmation that silently never appears
                // must not turn into a delete that silently always happens.
                return;
            }

            if (!confirmed)
                return;
        }

        controller.DeleteSlide(index);
        this.AfterCommand();
    }

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
        this.rail.Controller = this.editor.Controller;

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

        this.newSlide.IsEnabled = enabled;
        this.duplicateSlide.IsEnabled = enabled && (controller?.Count ?? 0) > 0;
        this.deleteSlide.IsEnabled = enabled && (controller?.CanDeleteSlide ?? false);
        this.moveSlideEarlier.IsEnabled = enabled && (controller?.CanMoveSlideEarlier ?? false);
        this.moveSlideLater.IsEnabled = enabled && (controller?.CanMoveSlideLater ?? false);
        this.slideLayout.IsEnabled = enabled && (controller?.Count ?? 0) > 0;
        this.RebuildLayoutMenu(controller, enabled);

        var canCopy = enabled && (controller?.CanCopyShape ?? false);
        this.paste.IsEnabled = enabled && (controller?.CanPaste ?? false);
        this.cut.IsEnabled = canCopy;
        this.copy.IsEnabled = canCopy;
        this.duplicateShape.IsEnabled = canCopy;

        var canArrange = enabled && (controller?.CanArrange ?? false);
        this.toFront.IsEnabled = canArrange;
        this.forward.IsEnabled = canArrange;
        this.backward.IsEnabled = canArrange;
        this.toBack.IsEnabled = canArrange;

        this.notesButton.FontAttributes = this.ShowNotes ? FontAttributes.Bold : FontAttributes.None;
        this.notesButton.IsEnabled = this.Deck is not null;
        this.notes.IsReadOnly = !enabled;
        this.SyncNotes(controller);

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
            { SelectedShape: >= 0, IsEditingText: true, ActiveCell: not null } => "Editing a cell — Tab moves to the next one",
            { SelectedShape: >= 0, IsEditingText: true } => "Editing text — double-tap a word to select it, Esc to leave",
            { SelectedShape: >= 0, Selection.IsGroup: true } => "Group selected — double-tap to select a shape inside it",
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
