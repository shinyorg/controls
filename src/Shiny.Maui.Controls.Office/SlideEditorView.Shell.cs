using System.ComponentModel;
using Shiny.Controls.Office.Icons;
using Shiny.Controls.Office.Shell;
using Shiny.Controls.Office.Skia;
using Shiny.Controls.Office.Theming;
using Shiny.Maui.Controls.Ribbons;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// PowerPoint's window around the editor: the title bar, Editing/Viewing and Share at the ribbon's end,
/// the Office status bar (slide counter, language, Notes, the three views, zoom and fit) and the File
/// backstage — the <see cref="OfficeShell"/> parts, fed from the controller through <see cref="SlideShell"/>.
/// </summary>
/// <remarks>
/// <para>
/// On by default (<see cref="ShowShell"/>). Every part is created with the view and switched with
/// <c>IsVisible</c>, never added later: the AppKit head does not realise a child added after the first
/// layout. With the shell off the view is the ribbon over the slide with the plain status line, as before.
/// </para>
/// <para>
/// The shell does no I/O. Save, Save As, Export (the ribbon's too) and Print arrive as a
/// <see cref="SlideFileRequest"/> through <see cref="FileRequested"/>, and the host decides where the
/// bytes go. The one thing done here without a host is opening a built-in template when nobody handles
/// <see cref="TemplateSelected"/>.
/// </para>
/// </remarks>
public partial class SlideEditorView
{
    static readonly OfficeZoomModel ShellZoom = new(0.1, 4.0, 1.0);

    readonly OfficeShell shell = new() { App = OfficeApp.PowerPoint };
    readonly OfficeTitleBar titleBar = new() { DocumentName = OfficeAppInfo.PowerPoint.DefaultDocumentName };
    readonly OfficeRibbonActions ribbonActions = new() { ShowComments = false };
    readonly OfficeStatusBar officeStatusBar = new() { ShowFocus = false, ShowFitToWindow = true };
    readonly OfficeBackstage backstage = new();
    readonly Image printPreview = new() { Aspect = Aspect.AspectFit, HeightRequest = 420 };
    readonly OfficeStatusItem slideItem = new("slide", "Slide 1 of 1") { Tooltip = "The slide being edited" };
    readonly OfficeStatusItem languageItem = new("language", WordShell.LanguageText()) { Tooltip = "Proofing language" };
    readonly OfficeStatusItem notesItem = new("notes", "Notes") { IsClickable = true, Tooltip = "Show the speaker notes under the slide" };

    bool shellBuilt;
    bool syncingShell;
    SlideDeck? ownedDeck;
    SlideDeck? trackedDeck;
    long revision;
    long savedRevision;
    OfficeSaveState lastSaved = OfficeSaveState.None;

    /// <summary>The command list the title bar's search reads: every ribbon command plus PowerPoint's own.</summary>
    public OfficeCommandIndex CommandIndex { get; } = new();

    // ---- parts, for hosts and tests ----

    /// <summary>The Office window this view is dressed in.</summary>
    public OfficeShell Shell => this.shell;

    public OfficeTitleBar TitleBar => this.titleBar;

    /// <summary>The Office status bar (the plain status line of before shows only with the shell off).</summary>
    public OfficeStatusBar StatusBar => this.officeStatusBar;

    public OfficeBackstage Backstage => this.backstage;

    public OfficeRibbonActions RibbonActions => this.ribbonActions;

    /// <summary>The ribbon, for <c>OfficeCommandIndex.AddRibbon</c> and anything the view's own properties do not reach.</summary>
    public Ribbon Ribbon => this.ribbon;

    // ---- bindable properties ----

    static BindableProperty ShellFlag(string name)
        => BindableProperty.Create(name, typeof(bool), typeof(SlideEditorView), true,
            propertyChanged: (b, _, _) => ((SlideEditorView)b).ApplyShell());

    /// <summary>PowerPoint's window — title bar, backstage, ribbon actions, Office status bar. On by default.</summary>
    public static readonly BindableProperty ShowShellProperty = ShellFlag(nameof(ShowShell));
    public static readonly BindableProperty ShowTitleBarProperty = ShellFlag(nameof(ShowTitleBar));
    public static readonly BindableProperty ShowStatusBarProperty = ShellFlag(nameof(ShowStatusBar));
    public static readonly BindableProperty ShowBackstageProperty = ShellFlag(nameof(ShowBackstage));
    public static readonly BindableProperty ShowRibbonActionsProperty = ShellFlag(nameof(ShowRibbonActions));

    public static readonly BindableProperty DocumentNameProperty = BindableProperty.Create(
        nameof(DocumentName), typeof(string), typeof(SlideEditorView), null, BindingMode.TwoWay,
        propertyChanged: (b, _, _) => ((SlideEditorView)b).RefreshTitle());

    public static readonly BindableProperty DocumentLocationProperty = BindableProperty.Create(
        nameof(DocumentLocation), typeof(string), typeof(SlideEditorView), null,
        propertyChanged: (b, _, _) => ((SlideEditorView)b).RefreshTitle());

    /// <summary>The title bar's save status. Null (the default) follows the deck: Unsaved after an edit, Saved after a save.</summary>
    public static readonly BindableProperty SaveStateProperty = BindableProperty.Create(
        nameof(SaveState), typeof(OfficeSaveState?), typeof(SlideEditorView), null,
        propertyChanged: (b, _, _) => ((SlideEditorView)b).RefreshTitle());

    public static readonly BindableProperty AutoSaveProperty = BindableProperty.Create(
        nameof(AutoSave), typeof(bool), typeof(SlideEditorView), false, BindingMode.TwoWay,
        propertyChanged: (b, _, n) => ((SlideEditorView)b).titleBar.AutoSave = (bool)n);

    public static readonly BindableProperty UserNameProperty = BindableProperty.Create(
        nameof(UserName), typeof(string), typeof(SlideEditorView), null,
        propertyChanged: (b, _, n) => ((SlideEditorView)b).titleBar.UserName = (string?)n);

    /// <summary>The backstage's templates. Null (the default) is <see cref="SlideTemplates.All"/>.</summary>
    public static readonly BindableProperty TemplatesProperty = BindableProperty.Create(
        nameof(Templates), typeof(IEnumerable<OfficeTemplate>), typeof(SlideEditorView), null,
        propertyChanged: (b, _, n) => ((SlideEditorView)b).backstage.Templates = (IEnumerable<OfficeTemplate>?)n ?? SlideTemplates.All);

    public static readonly BindableProperty RecentFilesProperty = BindableProperty.Create(
        nameof(RecentFiles), typeof(IEnumerable<OfficeRecentFile>), typeof(SlideEditorView), null,
        propertyChanged: (b, _, n) => ((SlideEditorView)b).backstage.RecentFiles = (IEnumerable<OfficeRecentFile>?)n);

    /// <summary>Editing or Viewing (read-only) — the ribbon's mode dropdown. Reviewing edits as Editing does. Two-way.</summary>
    public static readonly BindableProperty EditModeProperty = BindableProperty.Create(
        nameof(EditMode), typeof(OfficeEditMode), typeof(SlideEditorView), OfficeEditMode.Editing, BindingMode.TwoWay,
        propertyChanged: (b, _, _) => ((SlideEditorView)b).ApplyEditMode());

    public bool ShowShell { get => (bool)this.GetValue(ShowShellProperty); set => this.SetValue(ShowShellProperty, value); }
    public bool ShowTitleBar { get => (bool)this.GetValue(ShowTitleBarProperty); set => this.SetValue(ShowTitleBarProperty, value); }

    /// <summary>The Office status bar. <see cref="ShowStatus"/> off hides it too.</summary>
    public bool ShowStatusBar { get => (bool)this.GetValue(ShowStatusBarProperty); set => this.SetValue(ShowStatusBarProperty, value); }

    /// <summary>Whether File opens the backstage. Off, File only raises <see cref="FileMenuRequested"/>.</summary>
    public bool ShowBackstage { get => (bool)this.GetValue(ShowBackstageProperty); set => this.SetValue(ShowBackstageProperty, value); }

    /// <summary>Editing/Viewing and Share at the end of the ribbon's tab strip.</summary>
    public bool ShowRibbonActions { get => (bool)this.GetValue(ShowRibbonActionsProperty); set => this.SetValue(ShowRibbonActionsProperty, value); }

    /// <summary>The name in the title bar. Defaults to the deck's file name, or "Presentation1". Two-way: a rename writes it.</summary>
    public string? DocumentName { get => (string?)this.GetValue(DocumentNameProperty); set => this.SetValue(DocumentNameProperty, value); }

    public string? DocumentLocation { get => (string?)this.GetValue(DocumentLocationProperty); set => this.SetValue(DocumentLocationProperty, value); }
    public OfficeSaveState? SaveState { get => (OfficeSaveState?)this.GetValue(SaveStateProperty); set => this.SetValue(SaveStateProperty, value); }

    /// <summary>The title bar's AutoSave switch. On, and with <see cref="FileRequested"/> handled, edits are saved two seconds after the last one.</summary>
    public bool AutoSave { get => (bool)this.GetValue(AutoSaveProperty); set => this.SetValue(AutoSaveProperty, value); }

    /// <summary>The account in the title bar, and the author on the backstage's Info page.</summary>
    public string? UserName { get => (string?)this.GetValue(UserNameProperty); set => this.SetValue(UserNameProperty, value); }

    public IEnumerable<OfficeTemplate>? Templates { get => (IEnumerable<OfficeTemplate>?)this.GetValue(TemplatesProperty); set => this.SetValue(TemplatesProperty, value); }
    public IEnumerable<OfficeRecentFile>? RecentFiles { get => (IEnumerable<OfficeRecentFile>?)this.GetValue(RecentFilesProperty); set => this.SetValue(RecentFilesProperty, value); }
    public OfficeEditMode EditMode { get => (OfficeEditMode)this.GetValue(EditModeProperty); set => this.SetValue(EditModeProperty, value); }

    /// <summary>What the title bar is showing as the save status right now.</summary>
    public OfficeSaveState EffectiveSaveState
        => this.SaveState ?? (this.Deck is null ? OfficeSaveState.None : this.revision != this.savedRevision ? OfficeSaveState.Unsaved : this.lastSaved);

    // ---- events ----

    /// <summary>
    /// Save, Save As, Export (backstage or ribbon) or Print. Write the file where it belongs with
    /// <see cref="SlideFileRequest.WriteToAsync"/>. Unhandled, nothing is written.
    /// </summary>
    public event EventHandler<SlideFileRequest>? FileRequested;

    /// <summary>A template was picked. With no handler the view opens it itself (<see cref="SlideTemplates.OpenAsync"/>) and raises <see cref="DeckReplaced"/>.</summary>
    public event EventHandler<OfficeTemplate>? TemplateSelected;

    /// <summary>The view swapped in a deck of its own — a template from the backstage. The host owns it from then on.</summary>
    public event EventHandler<SlideDeck>? DeckReplaced;

    public event EventHandler? OpenRequested;

    public event EventHandler<OfficeRecentFile>? RecentFileSelected;

    public event EventHandler? ShareRequested;

    // ---- derived ----

    bool EffectiveReadOnly => this.IsReadOnly || (this.ShowShell && this.EditMode == OfficeEditMode.Viewing);

    string EffectiveDocumentName
        => this.DocumentName
           ?? (this.Deck?.Path is { } path ? System.IO.Path.GetFileNameWithoutExtension(path) : null)
           ?? OfficeAppInfo.PowerPoint.DefaultDocumentName;

    // ---- building ----

    View BuildShell()
    {
        this.shell.TitleBar = this.titleBar;
        this.shell.Ribbon = this.ribbon;
        this.shell.ShellContent = this.root;
        this.shell.StatusBar = this.officeStatusBar;
        this.shell.Backstage = this.backstage;
        this.ribbon.HeaderEndContent = this.ribbonActions;

        // Title bar
        this.titleBar.CommandIndex = this.CommandIndex;
        this.titleBar.SaveRequested += (_, _) => this.Save();
        this.titleBar.UndoRequested += (_, _) => this.Run(c => c.Undo());
        this.titleBar.RedoRequested += (_, _) => this.Run(c => c.Redo());
        this.titleBar.DocumentRenamed += (_, name) => this.DocumentName = name;
        this.titleBar.SearchSubmitted += (_, query) => this.SearchSlides(query);
        this.titleBar.PropertyChanged += this.OnTitleBarPropertyChanged;
        this.titleBar.QuickAccessItems.Add(new OfficeQuickAccessItem("slides:from-beginning", "Start From Beginning", OfficeShellIcon.SlideShow, () => this.StartPresenting(0))
        {
            Shortcut = "F5"
        });

        // Ribbon actions
        this.ribbonActions.EditModeChanged += (_, mode) =>
        {
            if (!this.syncingShell)
                this.EditMode = mode;
        };
        this.ribbonActions.ShareClicked += (_, _) => this.ShareRequested?.Invoke(this, EventArgs.Empty);

        // Status bar
        this.officeStatusBar.ZoomModel = ShellZoom;
        this.officeStatusBar.Items.Add(this.slideItem);
        this.officeStatusBar.Items.Add(this.languageItem);
        this.officeStatusBar.Items.Add(this.notesItem);
        this.officeStatusBar.ItemClicked += (_, item) =>
        {
            if (item == this.notesItem)
                this.ShowNotes = !this.ShowNotes;
        };
        this.officeStatusBar.ViewModeChanged += (_, mode) => this.SelectViewMode(mode.Id);
        this.officeStatusBar.FitToWindowRequested += (_, _) => this.SetZoom(null);
        this.officeStatusBar.PropertyChanged += this.OnStatusBarPropertyChanged;

        // Backstage
        this.backstage.Templates = SlideTemplates.All;
        this.backstage.SaveAsFormats = SlideExport.SaveAsFormats;
        this.backstage.ExportFormats = SlideExport.ExportFormats;
        this.backstage.PrintPreview = this.printPreview;
        this.backstage.PropertyChanged += this.OnBackstagePropertyChanged;
        this.backstage.TemplateSelected += (_, template) => _ = this.NewFromTemplateAsync(template);
        this.backstage.OpenRequested += (_, _) => this.OpenRequested?.Invoke(this, EventArgs.Empty);
        this.backstage.RecentFileSelected += (_, file) => this.RecentFileSelected?.Invoke(this, file);
        this.backstage.SaveRequested += (_, _) => this.Save();
        this.backstage.SaveAsRequested += (_, format) => this.WriteFile(format, SlideFileAction.SaveAs);
        this.backstage.ExportRequested += (_, format) => this.WriteFile(format, SlideFileAction.Export);
        this.backstage.PrintRequested += (_, _) => this.WriteFile(OfficeFileFormats.Pdf, SlideFileAction.Print);

        // Shell
        this.shell.PropertyChanged += this.OnShellPropertyChanged;

        this.shellBuilt = true;
        this.ApplyShell();
        return this.shell;
    }

    /// <summary>Runs after every <c>BuildBar</c>: shortcut text moves off tooltips, and the command search is refilled.</summary>
    void AfterBarBuilt()
    {
        foreach (var item in this.ribbon.QuickAccessItems)
            MoveShortcut(item);

        foreach (var tab in this.ribbon.Tabs)
        foreach (var group in tab.Groups)
        foreach (var item in group.Items)
            MoveShortcut(item);

        this.CommandIndex.Clear();
        this.CommandIndex.AddRibbon(this.ribbon);

        this.CommandIndex.Add(new OfficeCommand("Save", this.Save) { Id = "slides:save", Category = "File", Shortcut = "Ctrl+S", Keywords = ["store", "pptx"] });
        this.CommandIndex.Add(new OfficeCommand("New Presentation", () => this.shell.OpenBackstage(OfficeBackstagePage.New)) { Id = "slides:new", Category = "File", Keywords = ["template", "blank", "deck"] });
        this.CommandIndex.Add(new OfficeCommand("Export to PDF", () => this.Export(OfficeFileFormats.Pdf)) { Id = "slides:pdf", Category = "File › Export", Keywords = ["pdf"] });
        this.CommandIndex.Add(new OfficeCommand("Export Slide as PNG", () => this.Export(OfficeFileFormats.Png)) { Id = "slides:png", Category = "File › Export", Keywords = ["picture", "image"] });
        this.CommandIndex.Add(new OfficeCommand("Export All Slides as PNG", () => this.Export(SlideExport.AllSlidesPng)) { Id = "slides:png-all", Category = "File › Export", Keywords = ["pictures", "images", "zip"] });
        this.CommandIndex.Add(new OfficeCommand("Print", () => this.WriteFile(OfficeFileFormats.Pdf, SlideFileAction.Print)) { Id = "slides:print", Category = "File", Shortcut = "Ctrl+P" });
        this.CommandIndex.Add(new OfficeCommand("Reading View", () => this.StartPresenting()) { Id = "slides:reading", Category = "View › Presentation Views", Keywords = ["read", "present"] });
        this.CommandIndex.Add(new OfficeCommand("Zoom 100%", () => this.SetZoom(1.0)) { Id = "slides:zoom100", Category = "View › Zoom", Keywords = ["actual size", "reset zoom"] });
    }

    /// <summary>"Bold (Ctrl+B)" becomes Tooltip "Bold" + Shortcut "Ctrl+B", so the search can show the keys too.</summary>
    static void MoveShortcut(RibbonItem item)
    {
        if (item is RibbonRow row)
        {
            foreach (var child in row.Items)
                MoveShortcut(child);

            return;
        }

        if (!string.IsNullOrWhiteSpace(item.Shortcut) || SlideShell.SplitShortcut(item.Tooltip) is not { } split)
            return;

        item.Tooltip = split.Tooltip;
        item.Shortcut = split.Shortcut;
    }

    // ---- applying ----

    void ApplyShell()
    {
        if (!this.shellBuilt)
            return;

        var on = this.ShowShell;
        this.titleBar.IsVisible = on && this.ShowTitleBar;
        this.officeStatusBar.IsVisible = on && this.ShowStatusBar && this.ShowStatus;
        this.statusBar.IsVisible = !on && this.ShowStatus;
        this.ribbonActions.IsVisible = on && this.ShowRibbonActions;
        this.backstage.IsVisible = on && this.ShowBackstage;

        if (!(on && this.ShowBackstage))
            this.shell.IsBackstageOpen = false;

        if (!on)
            this.shell.IsFocusMode = false;

        // The title bar owns undo and redo while it shows; otherwise they are the ribbon's quick access.
        this.ribbon.ShowQuickAccess = !(on && this.ShowTitleBar);

        this.ApplyAccent();
        this.ApplyFileButton();
        this.editor.IsReadOnly = this.EffectiveReadOnly;
        this.RefreshShell();
    }

    /// <summary>File: the backstage's door while the shell is on (and FileMenuRequested still fires), or the host's hook.</summary>
    void ApplyFileButton()
    {
        var on = (this.shellBuilt && this.ShowShell) || this.fileMenuRequested is not null;
        this.ribbon.ApplicationButtonText = on ? "File" : null;
        this.ribbon.ApplicationButtonCommand = on ? new Command(() => this.fileMenuRequested?.Invoke(this, EventArgs.Empty)) : null;
    }

    void ApplyEditMode()
    {
        this.syncingShell = true;
        this.ribbonActions.EditMode = this.EditMode;
        this.syncingShell = false;

        if (this.EditMode == OfficeEditMode.Viewing)
            this.editor.Controller?.ClearSelection();

        this.editor.IsReadOnly = this.EffectiveReadOnly;
        this.editor.Repaint();
        this.RefreshBar();
    }

    // ---- refreshing ----

    /// <summary>Everything the shell shows that follows the controller.</summary>
    void RefreshShell()
    {
        this.RefreshTitle();
        this.SyncShellStatus();
    }

    void RefreshTitle()
    {
        if (!this.shellBuilt)
            return;

        this.TrackDeck(this.Deck);

        var controller = this.editor.Controller;
        this.titleBar.DocumentName = this.EffectiveDocumentName;
        this.titleBar.DocumentLocation = this.DocumentLocation ?? this.Deck?.Path;
        this.titleBar.CanUndo = !this.EffectiveReadOnly && (controller?.CanUndo ?? false);
        this.titleBar.CanRedo = !this.EffectiveReadOnly && (controller?.CanRedo ?? false);
        this.titleBar.AccentColor = this.Accent is { } accent && accent != OfficeAccent.Presentation ? ToColor(accent.Color) : null;

        if (this.titleBar.SaveState != OfficeSaveState.Saving)
            this.titleBar.SaveState = this.EffectiveSaveState;

        this.backstage.DocumentName = this.EffectiveDocumentName;
    }

    /// <summary>The status bar: the slide counter, the Notes toggle, the pressed view and the zoom.</summary>
    void SyncShellStatus()
    {
        if (!this.shellBuilt)
            return;

        var controller = this.editor.Controller;
        this.slideItem.Text = SlideShell.SlideText(controller?.Index ?? 0, this.Deck?.Slides.Count ?? 0, this.ViewMode);
        this.notesItem.Tooltip = this.ShowNotes ? "Hide the speaker notes" : "Show the speaker notes under the slide";
        this.notesItem.IsClickable = this.Deck is not null;

        this.syncingShell = true;
        try
        {
            this.officeStatusBar.SelectedViewMode = SlideShell.ViewModeId(this.ViewMode, this.IsPresenting);

            var zoom = controller?.EffectiveZoom ?? 1;
            if (Math.Abs(this.officeStatusBar.Zoom - zoom) > 0.0005)
                this.officeStatusBar.Zoom = zoom;

            this.officeStatusBar.IsFitted = controller is { Zoom: null };

            if (this.Deck is { } deck)
            {
                this.officeStatusBar.PageWidth = deck.SlideWidth;
                this.officeStatusBar.PageHeight = deck.SlideHeight;
                this.officeStatusBar.TextWidth = deck.SlideWidth;
            }

            this.officeStatusBar.ViewportWidth = this.editor.Width > 0 ? this.editor.Width : 0;
            this.officeStatusBar.ViewportHeight = this.editor.Height > 0 ? this.editor.Height : 0;
        }
        finally
        {
            this.syncingShell = false;
        }

        this.RefreshTitle();
    }

    void TrackDeck(SlideDeck? deck)
    {
        if (ReferenceEquals(deck, this.trackedDeck))
            return;

        if (this.trackedDeck is { } old)
            old.ContentChanged -= this.OnDeckContentChanged;

        this.trackedDeck = deck;
        this.revision = 0;
        this.savedRevision = 0;
        this.lastSaved = OfficeSaveState.None;

        if (deck is not null)
            deck.ContentChanged += this.OnDeckContentChanged;
    }

    IDispatcherTimer? autoSaveTimer;

    void OnDeckContentChanged(object? sender, EventArgs e)
    {
        this.revision++;

        // AutoSave: two seconds after the last edit, when the host is there to take the file.
        if (!this.AutoSave || this.FileRequested is null || this.Dispatcher is not { } dispatcher)
            return;

        if (this.autoSaveTimer is null)
        {
            this.autoSaveTimer = dispatcher.CreateTimer();
            this.autoSaveTimer.Interval = TimeSpan.FromSeconds(2);
            this.autoSaveTimer.IsRepeating = false;
            this.autoSaveTimer.Tick += (_, _) =>
            {
                if (this.revision != this.savedRevision)
                    this.Save();
            };
        }

        this.autoSaveTimer.Stop();
        this.autoSaveTimer.Start();
    }

    // ---- reacting ----

    /// <summary>The status bar's view buttons: Normal, Slide Sorter, or Reading View — the show from this slide. Test seam.</summary>
    public void SelectViewMode(string? id)
    {
        if (SlideShell.ParseViewMode(id) is { } mode)
            this.ViewMode = mode;
        else
            this.StartPresenting();

        this.SyncShellStatus();
    }

    /// <summary>The title bar's search when no command matched: finds the text across the slides. Test seam.</summary>
    public bool SearchSlides(string? query)
    {
        if (this.editor.Controller is not { } controller || !SlideShell.Search(controller, query))
            return false;

        this.editor.Repaint();
        this.RefreshBar();
        return true;
    }

    void OnStatusBarPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (this.syncingShell || e.PropertyName != nameof(OfficeStatusBar.Zoom) || this.editor.Controller is not { } controller)
            return;

        var zoom = this.officeStatusBar.Zoom;
        if (Math.Abs(controller.EffectiveZoom - zoom) > 0.0005)
            this.SetZoom(zoom);
    }

    void OnTitleBarPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OfficeTitleBar.AutoSave) && this.titleBar.AutoSave != this.AutoSave)
            this.AutoSave = this.titleBar.AutoSave;
    }

    void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(OfficeShell.IsBackstageOpen) || !this.shell.IsBackstageOpen)
            return;

        if (!this.ShowShell || !this.ShowBackstage)
        {
            this.shell.IsBackstageOpen = false;
            return;
        }

        this.backstage.DocumentInfo = this.Deck is { } deck ? SlideShell.DocumentInfo(deck, this.EffectiveDocumentName, this.UserName) : null;
    }

    void OnBackstagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(OfficeBackstage.SelectedPage))
            return;

        if (this.backstage.SelectedPage == OfficeBackstagePage.Info && this.Deck is { } deck)
            this.backstage.DocumentInfo = SlideShell.DocumentInfo(deck, this.EffectiveDocumentName, this.UserName);

        if (this.backstage.SelectedPage == OfficeBackstagePage.Print)
            this.RefreshPrintPreview();
    }

    void RefreshPrintPreview()
    {
        if (this.Deck is not { Slides.Count: > 0 } deck)
        {
            this.printPreview.Source = null;
            return;
        }

        try
        {
            var png = SlideExporter.ToPng(deck, Math.Clamp(this.CurrentSlideIndex, 0, deck.Slides.Count - 1), 960, this.Watermark);
            this.printPreview.Source = ImageSource.FromStream(() => new MemoryStream(png));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Shiny.Office] Print preview failed: {ex.Message}");
            this.printPreview.Source = null;
        }
    }

    async Task NewFromTemplateAsync(OfficeTemplate template)
    {
        if (this.TemplateSelected is { } handler)
        {
            handler(this, template);
            return;
        }

        try
        {
            var deck = await SlideTemplates.OpenAsync(template);
            var previous = this.ownedDeck;
            this.ownedDeck = deck;

            this.Deck = deck;
            this.SlideIndex = 0;
            this.DocumentName = template.IsBlank ? OfficeAppInfo.PowerPoint.DefaultDocumentName : template.Name;
            this.DocumentLocation = null;
            this.shell.IsBackstageOpen = false;
            this.DeckReplaced?.Invoke(this, deck);

            if (previous is not null && !ReferenceEquals(previous, deck) && this.DeckReplaced is null)
                previous.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Shiny.Office] Could not open template '{template.Id}': {ex.Message}");
        }
    }

    // ---- save, export, print ----

    /// <summary>Title bar ▸ Save, Backstage ▸ Save: the deck as pptx, through <see cref="FileRequested"/>.</summary>
    public void Save() => this.WriteFile(OfficeFileFormats.Pptx, SlideFileAction.Save);

    /// <summary>Export in a format — the ribbon's Export group and the backstage raise this. Through <see cref="FileRequested"/>.</summary>
    public void Export(OfficeFileFormat format) => this.WriteFile(format, SlideFileAction.Export);

    void WriteFile(OfficeFileFormat format, SlideFileAction action)
    {
        if (this.Deck is not { } deck || this.FileRequested is not { } handler)
            return;

        var request = new SlideFileRequest(deck, this.CurrentSlideIndex, format, format.FileNameFor(this.EffectiveDocumentName), action, this.Watermark);
        var saving = action is SlideFileAction.Save or SlideFileAction.SaveAs && format.IsNative;
        var at = this.revision;

        try
        {
            if (saving)
                this.titleBar.SaveState = OfficeSaveState.Saving;

            handler(this, request);

            if (saving)
            {
                this.savedRevision = at;
                this.lastSaved = OfficeSaveState.Saved;
            }

            if (action != SlideFileAction.Print)
                this.shell.IsBackstageOpen = false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Shiny.Office] {action} failed: {ex.Message}");
            if (saving)
                this.lastSaved = OfficeSaveState.Error;
        }
        finally
        {
            if (saving)
                this.titleBar.SaveState = OfficeSaveState.None;

            this.RefreshTitle();
        }
    }

    void DisposeShell()
    {
        this.autoSaveTimer?.Stop();
        this.officeStatusBar.PropertyChanged -= this.OnStatusBarPropertyChanged;
        this.titleBar.PropertyChanged -= this.OnTitleBarPropertyChanged;
        this.shell.PropertyChanged -= this.OnShellPropertyChanged;
        this.backstage.PropertyChanged -= this.OnBackstagePropertyChanged;

        if (this.trackedDeck is { } deck)
            deck.ContentChanged -= this.OnDeckContentChanged;

        // A host handed the deck through DeckReplaced may outlive this view with it.
        if (this.DeckReplaced is null)
            this.ownedDeck?.Dispose();
    }
}
