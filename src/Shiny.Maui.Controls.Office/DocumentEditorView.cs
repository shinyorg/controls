using Shiny.Controls.Office.Document;
using Shiny.Controls.Office.Icons;
using Shiny.Controls.Office.Packaging;
using Shiny.Maui.Controls.ColorPicker;
using Shiny.Maui.Controls.Ribbons;
using Shiny.Maui.Controls.FontPicker;
using Shiny.Controls.Office.Spreadsheet;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// <see cref="DocumentEditor"/> with Word's ribbon above it.
/// </summary>
/// <remarks>
/// <para>
/// The tabs are Word's: Home (Clipboard, Font, Paragraph, Styles, Editing), Insert, Design, Layout,
/// References, Review and View, plus a contextual Table tab that appears while the caret is in a
/// table. The tab strip is on by default — with eight tabs, turning it off hides most of the editor.
/// </para>
/// <para>
/// Every ribbon item is declared with the state it reflects (checked, enabled) as a function of the
/// controller, and one refresh pass applies them all. That keeps a toggle's pressed state and a
/// button's availability next to the command they belong to, rather than in a second list that has to
/// be kept in step by hand.
/// </para>
/// <para>
/// Plain buttons draw from the shared <see cref="OfficeIcons"/> set (the Word-only marks are
/// <see cref="WordIcons"/>) — one monochrome stroked weight, the same artwork the Blazor ribbon renders.
/// The pickers are the exception, because a font, a size and a colour have to show what they are set to.
/// </para>
/// </remarks>
public partial class DocumentEditorView : ContentView, IDisposable
{
    readonly DocumentEditor editor = new();
    readonly Ribbon ribbon;
    readonly Grid root;
    readonly Grid body;
    readonly Grid overlay;
    readonly OfficeShell shell;
    readonly OfficeFindBar findBar = new();
    readonly ColorPickerButton textColor;
    readonly List<ItemBinding> bindings = [];
    readonly List<MenuBinding> menuBindings = [];

    RibbonTab? tableTab;
    RibbonMenuButton? stylesMenu;
    View? fontPicker;
    View? sizePicker;
    bool suppressPickerEvents;
    bool disposed;
    DocumentEditorController? attached;

    public DocumentEditorView()
    {
        this.textColor = this.CreateColorPicker(color =>
        {
            this.editor.Controller?.SetTextColor(color);
            this.AfterCommand();
        });

        this.ribbon = new Ribbon
        {
            SmallItemRows = 2,

            // These bars mix 32px pickers with icon buttons, and every group sizes its own rows - so
            // without one height the groups stop lining up with one another.
            SmallItemRowHeight = 32,
            AllowGroupCollapse = true,

            // Below this the bar runs dense rather than folding its groups away: at phone width there
            // is room for no group at all, so collapsing puts every command behind a dropdown.
            SimplifyBelowWidth = 600
        };

        // Explicitly, because a BindableProperty's propertyChanged does not fire for its default.
        this.ApplyAccent();

        this.overlay = new Grid { InputTransparent = false, CascadeInputTransparent = false };
        this.overlay.InputTransparent = true;

        this.body = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            }
        };

        this.body.Add(this.editor);
        Grid.SetColumn(this.editor, 1);

        // The editor and the panels that float over it (replace, symbols, the read-mode exit) - the
        // shell's content slot.
        this.root = new Grid();
        this.root.Add(this.body);
        this.root.Add(this.overlay);

        this.editor.DocumentChanged += this.OnDocumentChanged;
        this.editor.ShortcutRequested += this.OnShortcutRequested;

        // Word's window around it all. Built whole, up front, and switched part by part with
        // IsVisible: the AppKit head never realises a child added after the first layout.
        this.shell = new OfficeShell { App = Shiny.Controls.Office.Shell.OfficeApp.Word };
        this.Content = this.shell;

        this.BuildBar();
        this.BuildShell();
        this.AttachDrop();
    }

    // ---- bindable properties ----

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
        BindingMode.TwoWay,
        propertyChanged: (b, _, value) =>
        {
            var view = (DocumentEditorView)b;
            view.editor.Zoom = (double)value;
            view.RefreshBar();
        });

    public static readonly BindableProperty IsReadOnlyProperty = BindableProperty.Create(
        nameof(IsReadOnly),
        typeof(bool),
        typeof(DocumentEditorView),
        false,
        propertyChanged: (b, _, value) =>
        {
            var view = (DocumentEditorView)b;
            view.ApplyEditorReadOnly();
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
        propertyChanged: (b, _, _) => ((DocumentEditorView)b).ApplyChromeVisibility());

    /// <summary>
    /// Draw the ribbon's tab strip. On by default.
    /// </summary>
    /// <remarks>
    /// With eight tabs — nine with Table — turning the strip off leaves only Home reachable. It is
    /// offered for a host that shows a single tab of its own choosing.
    /// </remarks>
    public static readonly BindableProperty ShowRibbonTabsProperty = BindableProperty.Create(
        nameof(ShowRibbonTabs),
        typeof(bool),
        typeof(DocumentEditorView),
        true,
        propertyChanged: (b, _, value) =>
        {
            var view = (DocumentEditorView)b;
            view.ribbon.ShowTabStrip = (bool)value;
            view.ribbon.AllowCollapse = (bool)value;
        });

    /// <summary>
    /// Whether the icon-only toolbar buttons carry a hover tooltip naming what they do.
    /// </summary>
    /// <remarks>
    /// The ribbon decides for itself whether to show a tooltip, from the hover-capability rule — so
    /// this only reaches the pickers the bar hosts, which draw their own.
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

    /// <summary>
    /// Read Mode: the ribbon goes away and the document reflows into one column you can read but not
    /// edit, with a button to come back out.
    /// </summary>
    public static readonly BindableProperty ReadModeProperty = BindableProperty.Create(
        nameof(ReadMode),
        typeof(bool),
        typeof(DocumentEditorView),
        false,
        BindingMode.TwoWay,
        propertyChanged: (b, _, _) => ((DocumentEditorView)b).ApplyReadMode());

    /// <summary>Show the navigation pane — the document's headings, down the left, to jump between.</summary>
    public static readonly BindableProperty ShowNavigationPaneProperty = BindableProperty.Create(
        nameof(ShowNavigationPane),
        typeof(bool),
        typeof(DocumentEditorView),
        false,
        BindingMode.TwoWay,
        propertyChanged: (b, _, _) => ((DocumentEditorView)b).ApplyNavigationPane());

    /// <summary>
    /// Show a File button at the ribbon's left, raising <see cref="FileRequested"/> — the hook for a
    /// backstage view. Off by default: the editor has no File menu of its own.
    /// </summary>
    public static readonly BindableProperty ShowFileButtonProperty = BindableProperty.Create(
        nameof(ShowFileButton),
        typeof(bool),
        typeof(DocumentEditorView),
        false,
        propertyChanged: (b, _, value) => ((DocumentEditorView)b).ApplyFileButton((bool)value));

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

    /// <inheritdoc cref="ShowRibbonTabsProperty"/>
    public bool ShowRibbonTabs
    {
        get => (bool)this.GetValue(ShowRibbonTabsProperty);
        set => this.SetValue(ShowRibbonTabsProperty, value);
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

    /// <inheritdoc cref="ReadModeProperty"/>
    public bool ReadMode
    {
        get => (bool)this.GetValue(ReadModeProperty);
        set => this.SetValue(ReadModeProperty, value);
    }

    /// <inheritdoc cref="ShowNavigationPaneProperty"/>
    public bool ShowNavigationPane
    {
        get => (bool)this.GetValue(ShowNavigationPaneProperty);
        set => this.SetValue(ShowNavigationPaneProperty, value);
    }

    /// <inheritdoc cref="ShowFileButtonProperty"/>
    public bool ShowFileButton
    {
        get => (bool)this.GetValue(ShowFileButtonProperty);
        set => this.SetValue(ShowFileButtonProperty, value);
    }

    /// <summary>The underlying editor, for focus and direct key routing.</summary>
    public DocumentEditor Editor => this.editor;

    public DocumentEditorController? Controller => this.editor.Controller;

    /// <summary>Word count, characters, paragraphs, pages and the caret's page — for a status bar.</summary>
    public DocumentStatistics Statistics => this.editor.Controller?.Statistics ?? DocumentStatistics.Empty;

    public event EventHandler? DocumentChanged;

    /// <summary>Raised when a dropped or chosen file could not be inserted, so a host can say so.</summary>
    public event EventHandler<OfficeDropRejected>? DropRejected;

    /// <summary>Raised when <see cref="Statistics"/> may have changed — an edit, or the caret moving to another page.</summary>
    public event EventHandler? StatisticsChanged;

    /// <summary>Raised when the zoom changes from anywhere — the View tab, a pinch, a binding.</summary>
    public event EventHandler<double>? ZoomChanged;

    /// <summary>Raised when the caret moves onto a paragraph of a different style, with the new style id.</summary>
    public event EventHandler<string>? CurrentStyleChanged;

    /// <summary>Raised by the File button. See <see cref="ShowFileButton"/>.</summary>
    public event EventHandler? FileRequested;

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

    /// <summary>Routes a key combination through Word's shortcuts. See <see cref="DocumentEditor.HandleShortcut"/>.</summary>
    public bool HandleShortcut(string key, bool command, bool shift = false, bool alt = false)
    {
        var handled = this.editor.HandleShortcut(key, command, shift, alt);
        this.RefreshBar();
        return handled;
    }

    // ---- item bindings ----

    /// <summary>An item and the state it shows, both worked out from the controller.</summary>
    sealed record ItemBinding(RibbonItem Item, Func<DocumentEditorController, bool>? Enabled, Func<DocumentEditorController, bool>? Checked, bool ViewOnly);

    /// <summary>A menu line whose tick follows the controller.</summary>
    sealed record MenuBinding(RibbonMenuEntry Entry, Func<DocumentEditorController, bool> Checked);

    /// <summary>
    /// A command button.
    /// </summary>
    /// <param name="viewOnly">True for a command that changes nothing — zoom, find, navigation —
    /// which stays live in a read-only document.</param>
    RibbonButton Button(
        OfficeIcon icon,
        string tooltip,
        Action<DocumentEditorController> action,
        string? text = null,
        bool large = false,
        Func<DocumentEditorController, bool>? enabled = null,
        bool viewOnly = false)
    {
        var button = large
            ? OfficeRibbonItems.LargeCommand(icon, text ?? tooltip, tooltip, () => this.Run(action), $"DocToolbar{icon}")
            : OfficeRibbonItems.Command(icon, tooltip, () => this.Run(action), text, $"DocToolbar{icon}");

        this.bindings.Add(new ItemBinding(button, enabled, null, viewOnly));
        return button;
    }

    /// <summary>A command whose work opens a prompt or a picker first, so it refreshes the bar itself.</summary>
    RibbonButton AsyncButton(
        OfficeIcon icon,
        string tooltip,
        Func<DocumentEditorController, Task> action,
        string? text = null,
        bool large = false,
        Func<DocumentEditorController, bool>? enabled = null,
        bool viewOnly = false)
    {
        void Start()
        {
            if (this.editor.Controller is { } controller)
                _ = action(controller);
        }

        var button = large
            ? OfficeRibbonItems.LargeCommand(icon, text ?? tooltip, tooltip, Start, $"DocToolbar{icon}")
            : OfficeRibbonItems.Command(icon, tooltip, Start, text, $"DocToolbar{icon}");

        this.bindings.Add(new ItemBinding(button, enabled, null, viewOnly));
        return button;
    }

    RibbonToggleButton Toggle(
        OfficeIcon icon,
        string tooltip,
        Action<DocumentEditorController> action,
        Func<DocumentEditorController, bool> isChecked,
        string? text = null,
        bool large = false,
        Func<DocumentEditorController, bool>? enabled = null,
        bool viewOnly = false)
    {
        var toggle = OfficeRibbonItems.Toggle(icon, tooltip, () => this.Run(action), $"DocToolbar{icon}");
        toggle.Text = text;

        if (large)
            toggle.Size = RibbonItemSize.Large;

        this.bindings.Add(new ItemBinding(toggle, enabled, isChecked, viewOnly));
        return toggle;
    }

    /// <summary>A dropdown of commands.</summary>
    RibbonMenuButton Menu(
        OfficeIcon icon,
        string tooltip,
        string? text,
        IEnumerable<(string Text, Action<DocumentEditorController> Action, Func<DocumentEditorController, bool>? Checked)> entries,
        bool large = false,
        Func<DocumentEditorController, bool>? enabled = null,
        bool viewOnly = false)
    {
        var menu = new RibbonMenuButton
        {
            Text = text,
            Tooltip = tooltip,
            Size = large ? RibbonItemSize.Large : RibbonItemSize.Small,
            AutomationId = $"DocToolbar{icon}Menu",
            IconTemplate = OfficeRibbonItems.IconTemplateFor(icon)
        };

        foreach (var (label, action, isChecked) in entries)
        {
            if (label == "-")
            {
                menu.Menu.Add(new RibbonMenuEntry { IsSeparator = true });
                continue;
            }

            var entry = new RibbonMenuEntry { Text = label, Command = new Command(() => this.Run(action)) };
            menu.Menu.Add(entry);

            if (isChecked is not null)
                this.menuBindings.Add(new MenuBinding(entry, isChecked));
        }

        this.bindings.Add(new ItemBinding(menu, enabled, null, viewOnly));
        return menu;
    }

    static (string, Action<DocumentEditorController>, Func<DocumentEditorController, bool>?) Entry(
        string text,
        Action<DocumentEditorController> action,
        Func<DocumentEditorController, bool>? isChecked = null)
        => (text, action, isChecked);

    static readonly (string, Action<DocumentEditorController>, Func<DocumentEditorController, bool>?) Separator = ("-", _ => { }, null);

    /// <summary>Runs a command against the controller and settles the view afterwards.</summary>
    void Run(Action<DocumentEditorController> action)
    {
        if (this.editor.Controller is not { } controller)
            return;

        action(controller);
        this.AfterCommand();
    }

    // ---- pickers ----

    /// <summary>
    /// The core package's colour picker, in its button form.
    /// </summary>
    /// <remarks>
    /// Not a row of preset swatches: a document's text can be any colour, and a fixed palette is a
    /// promise the format does not make.
    /// </remarks>
    ColorPickerButton CreateColorPicker(Action<ArgbColor> picked)
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

            picked(ToArgb(color));
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

    // ---- lifecycle ----

    void AfterCommand()
    {
        // Focus returns to the editor after every toolbar action; leaving it on the button means the
        // next keystroke goes nowhere, which reads as the editor having stopped working.
        this.editor.RestoreFocus();
        this.RefreshBar();
        this.RefreshShell();
        this.DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    void AttachController()
    {
        if (this.attached is { } previous)
        {
            previous.Changed -= this.OnControllerChanged;
            previous.StatisticsChanged -= this.OnStatisticsChanged;
            previous.ZoomChanged -= this.OnZoomChanged;
            previous.CurrentStyleChanged -= this.OnCurrentStyleChanged;
        }

        this.attached = this.editor.Controller;

        if (this.attached is { } controller)
        {
            controller.Changed += this.OnControllerChanged;
            controller.StatisticsChanged += this.OnStatisticsChanged;
            controller.ZoomChanged += this.OnZoomChanged;
            controller.CurrentStyleChanged += this.OnCurrentStyleChanged;
        }

        // A new document is a new controller and therefore a new finder; a bar left holding the old
        // one would count matches in a document that is no longer on screen.
        this.findBar.Find = this.editor.Controller?.Find;

        this.RebuildStylesMenu();
        this.RefreshNavigationPane();
        this.OnShellControllerAttached();
        this.RefreshBar();
    }

    void OnControllerChanged(object? sender, EventArgs e)
    {
        this.RefreshBar();
        this.RefreshNavigationPane();
        this.RefreshShell();
    }

    void OnStatisticsChanged(object? sender, EventArgs e)
    {
        this.RefreshStatus();
        this.StatisticsChanged?.Invoke(this, EventArgs.Empty);
    }

    void OnZoomChanged(object? sender, EventArgs e)
    {
        if (this.editor.Controller is not { } controller)
            return;

        // Written back so a two-way binding sees a pinch; the property's own handler is a no-op for the
        // value the editor already has.
        if (Math.Abs(this.Zoom - controller.Zoom) > 0.0001)
            this.Zoom = controller.Zoom;

        this.RefreshStatus();
        this.RefreshRuler();
        this.ZoomChanged?.Invoke(this, controller.Zoom);
    }

    void OnCurrentStyleChanged(object? sender, EventArgs e)
    {
        if (this.editor.Controller is { } controller)
        {
            this.styleGallery.SelectedStyleId = controller.CurrentStyleId;
            this.CurrentStyleChanged?.Invoke(this, controller.CurrentStyleId);
        }
    }

    void OnDocumentChanged(object? sender, EventArgs e)
    {
        this.RefreshBar();
        this.RefreshTitle();
        this.DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reflects the controller's state — the caret's formatting, what is enabled — into the ribbon.</summary>
    void RefreshBar()
    {
        var controller = this.editor.Controller;
        var format = controller?.CaretFormat ?? CaretFormat.Default;
        var loaded = controller is not null;
        var editable = loaded && !this.IsReadOnly && !this.ReadMode && this.EditMode != Shiny.Controls.Office.Shell.OfficeEditMode.Viewing;

        foreach (var binding in this.bindings)
        {
            var enabled = binding.ViewOnly ? loaded : editable;
            if (enabled && controller is not null && binding.Enabled is { } rule)
                enabled = rule(controller);

            binding.Item.IsEnabled = enabled;

            if (binding.Checked is { } isChecked && binding.Item is RibbonToggleButton toggle)
                toggle.IsChecked = controller is not null && isChecked(controller);
        }

        if (controller is not null)
        {
            foreach (var binding in this.menuBindings)
                binding.Entry.IsChecked = binding.Checked(controller);
        }

        // The contextual tab follows the caret into and out of a table, as Word's does.
        if (this.tableTab is not null)
            this.tableTab.IsVisible = format.IsInTable && editable;

        if (this.stylesMenu is not null)
            this.stylesMenu.Text = this.StyleDisplayName(format.StyleId);

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
        this.textColor.IsEnabled = editable;

        foreach (var picker in this.extraPickers)
            picker.IsEnabled = editable;

        // Finding works in a read-only view - it changes nothing.
        this.findBar.IsEnabled = loaded;
        this.findBar.SetTooltipsEnabled(this.ShowToolbarTooltips);

        if (this.zoomLabel is not null)
            this.zoomLabel.Text = $"{this.editor.Zoom * 100:0}%";

        this.suppressPickerEvents = false;
    }

    readonly List<View> extraPickers = [];

    public void Dispose()
    {
        if (this.disposed)
            return;

        this.disposed = true;
        this.editor.DocumentChanged -= this.OnDocumentChanged;
        this.editor.ShortcutRequested -= this.OnShortcutRequested;

        if (this.attached is { } controller)
        {
            controller.Changed -= this.OnControllerChanged;
            controller.StatisticsChanged -= this.OnStatisticsChanged;
            controller.ZoomChanged -= this.OnZoomChanged;
            controller.CurrentStyleChanged -= this.OnCurrentStyleChanged;
        }

        // Drops the bar's subscription to the finder, which outlives this view.
        this.findBar.Find = null;
        this.editor.Dispose();

        GC.SuppressFinalize(this);
    }

    // ---- accent ----

    /// <summary>
    /// The colour this control wears: its ribbon's header band and tab underline.
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="OfficeAccent.Document"/> — the colour Microsoft's own Word wears. Set it to
    /// take on the app's own brand instead, or to <c>null</c> to leave the bar on the theme's neutrals.
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

        // In the shell the title bar carries the accent, so the tab strip sits on the theme's surface
        // and the accent is the File button and the underline - Word on the web, and what the Blazor
        // host does. Painting the band as well puts the ink (white) behind a File button whose label is
        // also white, which leaves an empty white pill where "File" should be.
        if (this.ShowShell && this.shellBuilt)
        {
            this.ribbon.HeaderBackgroundColor = null;
            this.ribbon.HeaderForegroundColor = null;
            this.ribbon.AccentColor = this.Accent is { } a ? ToColor(a.Color) : null;
            return;
        }

        if (this.Accent is not { } accent)
        {
            this.ribbon.HeaderBackgroundColor = null;
            this.ribbon.HeaderForegroundColor = null;
            this.ribbon.AccentColor = null;
            return;
        }

        // Inside the shell the title bar above already carries the accent, so the tab strip stays on
        // the theme and the accent marks only the File button and the selected tab (as on Blazor).
        // Painting the strip too put the File button - accent-ink on an ink background - out of sight.
        if (this.ShowShell)
        {
            this.ribbon.HeaderBackgroundColor = null;
            this.ribbon.HeaderForegroundColor = null;
            this.ribbon.AccentColor = ToColor(accent.Color);
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
    /// The File button: the backstage's door while the shell is on (and <see cref="FileRequested"/> still
    /// fires), or the host's hook when only <see cref="ShowFileButton"/> asks for it.
    /// </summary>
    void ApplyFileButton(bool show)
    {
        var on = show || this.ShowShell;
        this.ribbon.ApplicationButtonText = on ? "File" : null;
        this.ribbon.ApplicationButtonCommand = on ? new Command(() => this.FileRequested?.Invoke(this, EventArgs.Empty)) : null;
    }

    /// <summary>
    /// A picture drawn behind the content. Forwarded to the surface.
    /// </summary>
    /// <remarks>
    /// A display watermark — drawn, not written into the file. For one Word shows too, use Design ▸
    /// Watermark's text marks, which <see cref="DocumentEditorController.SetWatermarkText"/> writes into the
    /// document's header.
    /// </remarks>
    public OfficeWatermark? Watermark
    {
        get => this.editor.Watermark;
        set => this.editor.Watermark = value;
    }
}
