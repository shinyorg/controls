using System.ComponentModel;
using Shiny.Controls.Office.Shell;
using Shiny.Maui.Controls.Ribbons;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// The Excel window around the grid: an <see cref="OfficeShell"/> with the title bar, the ribbon, the
/// comments pane, the status bar and the File backstage.
/// </summary>
/// <remarks>
/// <para>
/// On by default (<see cref="ShowShell"/>), and every part has a switch of its own. What the backstage
/// asks for — save, save as, export, print — arrives as a <see cref="SpreadsheetFileRequest"/> through
/// <see cref="FileRequested"/>; the host decides where the bytes go.
/// </para>
/// <para>
/// Everything is built in the constructor and shown or hidden, never added later — the AppKit head
/// only realises children that exist at first layout.
/// </para>
/// </remarks>
public partial class SpreadsheetView
{
    static readonly OfficeZoomModel ShellZoom = new(SpreadsheetController.MinZoom, SpreadsheetController.MaxZoom, 1.0);

    readonly OfficeCommandIndex commands = new();
    readonly OfficeStatusItem modeItem = new("mode", "Ready");
    readonly OfficeStatusItem aggregateItem = new("aggregates") { IsVisible = false };

    OfficeShell shell = null!;
    OfficeTitleBar titleBar = null!;
    OfficeStatusBar statusBar = null!;
    OfficeBackstage backstage = null!;
    OfficeSidePane commentsPane = null!;
    OfficeRibbonActions ribbonActions = null!;
    VerticalStackLayout notesList = null!;
    Label notesEmpty = null!;
    IDisposable? commandSync;

    Workbook? ownedWorkbook;
    long savedRevision = -1;
    OfficeSaveState lastSaved = OfficeSaveState.None;
    OfficeEditMode editMode;
    bool syncingPane;

    // ---- switches ----

    public static readonly BindableProperty ShowShellProperty = ShellFlag(nameof(ShowShell));
    public static readonly BindableProperty ShowTitleBarProperty = ShellFlag(nameof(ShowTitleBar));
    public static readonly BindableProperty ShowStatusBarProperty = ShellFlag(nameof(ShowStatusBar));
    public static readonly BindableProperty ShowBackstageProperty = ShellFlag(nameof(ShowBackstage));
    public static readonly BindableProperty ShowCommentsPaneProperty = ShellFlag(nameof(ShowCommentsPane));

    public static readonly BindableProperty DocumentNameProperty = BindableProperty.Create(
        nameof(DocumentName), typeof(string), typeof(SpreadsheetView), null, BindingMode.TwoWay,
        propertyChanged: (b, _, _) => ((SpreadsheetView)b).UpdateShell());

    public static readonly BindableProperty UserNameProperty = BindableProperty.Create(
        nameof(UserName), typeof(string), typeof(SpreadsheetView), null,
        propertyChanged: (b, _, n) => ((SpreadsheetView)b).OnUserNameChanged((string?)n));

    public static readonly BindableProperty TemplatesProperty = BindableProperty.Create(
        nameof(Templates), typeof(IEnumerable<OfficeTemplate>), typeof(SpreadsheetView), null,
        propertyChanged: (b, _, n) => ((SpreadsheetView)b).backstage.Templates = (IEnumerable<OfficeTemplate>?)n ?? TemplateThumbnailCache.SpreadsheetReady ?? SpreadsheetTemplates.All);

    public static readonly BindableProperty RecentFilesProperty = BindableProperty.Create(
        nameof(RecentFiles), typeof(IEnumerable<OfficeRecentFile>), typeof(SpreadsheetView), null,
        propertyChanged: (b, _, n) => ((SpreadsheetView)b).backstage.RecentFiles = (IEnumerable<OfficeRecentFile>?)n);

    static BindableProperty ShellFlag(string name)
        => BindableProperty.Create(name, typeof(bool), typeof(SpreadsheetView), true,
            propertyChanged: (b, _, _) => ((SpreadsheetView)b).ApplyShellVisibility());

    /// <summary>
    /// Wraps the sheet in Excel's window — title bar, ribbon with File, comments pane, status bar and
    /// backstage. On by default; false leaves the ribbon, formula bar, grid and tabs of before.
    /// </summary>
    public bool ShowShell { get => (bool)this.GetValue(ShowShellProperty); set => this.SetValue(ShowShellProperty, value); }

    /// <summary>The green title bar: AutoSave, Save/Undo/Redo, the workbook name and the command search.</summary>
    public bool ShowTitleBar { get => (bool)this.GetValue(ShowTitleBarProperty); set => this.SetValue(ShowTitleBarProperty, value); }

    /// <summary>Ready/Edit, the selection's Average/Count/Sum, the three views and the zoom slider.</summary>
    public bool ShowStatusBar { get => (bool)this.GetValue(ShowStatusBarProperty); set => this.SetValue(ShowStatusBarProperty, value); }

    /// <summary>Whether File opens the backstage. Off, File only raises <see cref="FileMenuRequested"/>.</summary>
    public bool ShowBackstage { get => (bool)this.GetValue(ShowBackstageProperty); set => this.SetValue(ShowBackstageProperty, value); }

    /// <summary>The Comments button and the pane listing every note in the workbook.</summary>
    public bool ShowCommentsPane { get => (bool)this.GetValue(ShowCommentsPaneProperty); set => this.SetValue(ShowCommentsPaneProperty, value); }

    /// <summary>The name in the title bar and backstage. Defaults to the file's name, or "Book1". Two-way: a rename writes it.</summary>
    public string? DocumentName { get => (string?)this.GetValue(DocumentNameProperty); set => this.SetValue(DocumentNameProperty, value); }

    /// <summary>The account in the title bar's avatar, and the author of new notes.</summary>
    public string? UserName { get => (string?)this.GetValue(UserNameProperty); set => this.SetValue(UserNameProperty, value); }

    /// <summary>The backstage's New page. Defaults to <see cref="SpreadsheetTemplates.All"/>.</summary>
    public IEnumerable<OfficeTemplate>? Templates { get => (IEnumerable<OfficeTemplate>?)this.GetValue(TemplatesProperty); set => this.SetValue(TemplatesProperty, value); }

    /// <summary>The backstage's recent files. The host's; none by default.</summary>
    public IEnumerable<OfficeRecentFile>? RecentFiles { get => (IEnumerable<OfficeRecentFile>?)this.GetValue(RecentFilesProperty); set => this.SetValue(RecentFilesProperty, value); }

    /// <summary>The shell itself, for anything the switches above do not reach.</summary>
    public OfficeShell Shell => this.shell;

    public OfficeTitleBar TitleBar => this.titleBar;

    public OfficeStatusBar StatusBar => this.statusBar;

    public OfficeBackstage Backstage => this.backstage;

    /// <summary>The command search behind the title bar — add the app's own commands to it.</summary>
    public OfficeCommandIndex Commands => this.commands;

    // ---- events ----

    /// <summary>
    /// Save, Save As, Export or Print. Write the file where it belongs with
    /// <see cref="SpreadsheetFileRequest.WriteToAsync"/>. Unhandled, nothing is written.
    /// </summary>
    public event EventHandler<SpreadsheetFileRequest>? FileRequested;

    /// <summary>
    /// A template was picked. With no handler the view builds it (<see cref="SpreadsheetTemplates.Create"/>
    /// or the template's own stream), shows it and raises <see cref="WorkbookReplaced"/>.
    /// </summary>
    public event EventHandler<OfficeTemplate>? TemplateSelected;

    /// <summary>The view swapped in a workbook of its own — a template from the backstage.</summary>
    public event EventHandler<Workbook>? WorkbookReplaced;

    public event EventHandler? OpenRequested;

    public event EventHandler<OfficeRecentFile>? RecentFileSelected;

    public event EventHandler? ShareRequested;


    bool EffectiveReadOnly => this.IsReadOnly || this.editMode == OfficeEditMode.Viewing;

    string EffectiveDocumentName
        => this.DocumentName
           ?? (this.Workbook?.Path is { } path ? System.IO.Path.GetFileNameWithoutExtension(path) : null)
           ?? OfficeAppInfo.Excel.DefaultDocumentName;


    // ---- construction ----

    View BuildShell(View sheetBody)
    {
        this.titleBar = new OfficeTitleBar { CommandIndex = this.commands };
        this.titleBar.SaveRequested += (_, _) => _ = this.SaveAsync();
        this.titleBar.UndoRequested += (_, _) => this.Undo();
        this.titleBar.RedoRequested += (_, _) => this.Redo();
        this.titleBar.DocumentRenamed += (_, name) => this.DocumentName = name;
        this.titleBar.SearchSubmitted += (_, text) =>
        {
            if (this.controller is { } c && SpreadsheetShell.Search(c, text))
                this.Invalidate();
        };

        this.statusBar = new OfficeStatusBar { ZoomModel = ShellZoom };
        this.statusBar.Items.Add(this.modeItem);
        this.statusBar.Items.Add(this.aggregateItem);
        this.statusBar.ViewModeChanged += (_, mode) =>
        {
            if (this.controller is { } c)
                c.ViewMode = SpreadsheetShell.ParseViewMode(mode.Id);
        };
        this.statusBar.PropertyChanged += this.OnStatusBarPropertyChanged;

        this.backstage = new OfficeBackstage { Templates = TemplateThumbnailCache.SpreadsheetReady ?? SpreadsheetTemplates.All };
        this.backstage.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OfficeBackstage.IsOpen) && this.backstage.IsOpen)
                _ = this.LoadTemplateThumbnailsAsync();
        };
        this.backstage.TemplateSelected += (_, t) => _ = this.OnTemplateSelectedAsync(t);
        this.backstage.RecentFileSelected += (_, f) => this.RecentFileSelected?.Invoke(this, f);
        this.backstage.OpenRequested += (_, _) => this.OpenRequested?.Invoke(this, EventArgs.Empty);
        this.backstage.SaveRequested += (_, _) => _ = this.SaveAsync();
        this.backstage.SaveAsRequested += (_, f) => _ = this.WriteFileAsync(f, SpreadsheetFileAction.SaveAs);
        this.backstage.ExportRequested += (_, f) => _ = this.WriteFileAsync(f, SpreadsheetFileAction.Export);
        this.backstage.PrintRequested += (_, _) => _ = this.WriteFileAsync(OfficeFileFormats.Pdf, SpreadsheetFileAction.Print);

        this.notesList = new VerticalStackLayout { Spacing = 6, Padding = new Thickness(8, 4, 8, 12) };
        this.notesEmpty = new Label
        {
            Text = "No notes in this workbook. Add one from Review ▸ New Note or the right-click menu.",
            FontSize = 13,
            Margin = new Thickness(12),
            LineBreakMode = LineBreakMode.WordWrap
        };
        this.notesEmpty.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);

        var notes = new VerticalStackLayout();
        notes.Children.Add(this.notesEmpty);
        notes.Children.Add(this.notesList);
        this.commentsPane = new OfficeSidePane { Title = "Comments", PaneContent = new ScrollView { Content = notes } };

        this.ribbonActions = new OfficeRibbonActions();
        this.ribbonActions.PropertyChanged += this.OnRibbonActionsPropertyChanged;
        this.ribbonActions.ShareClicked += (_, _) => this.ShareRequested?.Invoke(this, EventArgs.Empty);
        this.toolbar.Ribbon.HeaderEndContent = this.ribbonActions;

        this.shell = new OfficeShell
        {
            App = OfficeApp.Excel,
            TitleBar = this.titleBar,
            Ribbon = this.toolbar,
            ShellContent = sheetBody,
            RightPane = this.commentsPane,
            StatusBar = this.statusBar,
            Backstage = this.backstage
        };
        this.shell.PropertyChanged += this.OnShellPropertyChanged;

        this.commandSync = this.commands.SyncRibbon(this.toolbar.Ribbon);
        this.AddShellCommands();
        this.ApplyShellVisibility();
        return this.shell;
    }

    void AddShellCommands()
    {
        this.commands.Add(new OfficeCommand("Save", this.SaveAsync) { Id = "sheet:save", Category = "File", Shortcut = "Ctrl+S" });
        this.commands.Add(new OfficeCommand("New Workbook", () => this.shell.OpenBackstage(OfficeBackstagePage.New)) { Id = "sheet:new", Category = "File", Keywords = ["template", "blank"] });
        this.commands.Add(new OfficeCommand("Export to PDF", () => this.WriteFileAsync(OfficeFileFormats.Pdf, SpreadsheetFileAction.Export)) { Id = "sheet:pdf", Category = "File › Export" });
        this.commands.Add(new OfficeCommand("Export to CSV", () => this.WriteFileAsync(OfficeFileFormats.Csv, SpreadsheetFileAction.Export)) { Id = "sheet:csv", Category = "File › Export", Keywords = ["comma"] });
        this.commands.Add(new OfficeCommand("Print", () => this.WriteFileAsync(OfficeFileFormats.Pdf, SpreadsheetFileAction.Print)) { Id = "sheet:print", Category = "File", Shortcut = "Ctrl+P" });
        this.commands.Add(new OfficeCommand("Comments", () => this.shell.IsRightPaneOpen = !this.shell.IsRightPaneOpen) { Id = "sheet:comments", Category = "Review", Keywords = ["notes"] });
        this.commands.Add(new OfficeCommand("Normal View", () => this.statusBar.SelectViewMode(OfficeViewModes.Normal.Id)) { Id = "sheet:normal", Category = "View" });
        this.commands.Add(new OfficeCommand("Page Layout", () => this.statusBar.SelectViewMode(OfficeViewModes.PageLayout.Id)) { Id = "sheet:pagelayout", Category = "View", Keywords = ["print", "pages"] });
        this.commands.Add(new OfficeCommand("Page Break Preview", () => this.statusBar.SelectViewMode(OfficeViewModes.PageBreak.Id)) { Id = "sheet:pagebreak", Category = "View", Keywords = ["print", "pages"] });
    }

    void ApplyShellVisibility()
    {
        if (this.shell is null)
            return;

        var on = this.ShowShell;
        this.titleBar.IsVisible = on && this.ShowTitleBar;
        this.statusBar.IsVisible = on && this.ShowStatusBar;
        this.ribbonActions.IsVisible = on;
        this.ribbonActions.ShowComments = on && this.ShowCommentsPane;

        // Undo/Redo live in the title bar when there is one, and back on the ribbon when there is not.
        this.toolbar.IsInShell = on && this.ShowTitleBar;

        if (!(on && this.ShowCommentsPane))
            this.shell.IsRightPaneOpen = false;

        if (!(on && this.ShowBackstage))
            this.shell.IsBackstageOpen = false;
    }


    // ---- state ----

    void OnWorkbookAttached(Workbook workbook)
    {
        this.savedRevision = workbook.Revision;
        this.lastSaved = OfficeSaveState.None;

        if (!string.IsNullOrWhiteSpace(this.UserName) && this.controller is { } c)
            c.NoteAuthor = this.UserName;

        this.UpdateShell();
        this.UpdateStatus();
        this.RebuildNotes();
    }

    void OnUserNameChanged(string? name)
    {
        this.titleBar.UserName = name;
        if (!string.IsNullOrWhiteSpace(name) && this.controller is { } c)
            c.NoteAuthor = name;
    }

    /// <summary>Title bar state: name, save status, undo/redo.</summary>
    void UpdateShell()
    {
        if (this.titleBar is null)
            return;

        this.titleBar.DocumentName = this.EffectiveDocumentName;
        this.titleBar.DocumentLocation = this.Workbook?.Path;
        this.titleBar.CanUndo = this.controller?.CanUndo ?? false;
        this.titleBar.CanRedo = this.controller?.CanRedo ?? false;

        if (this.titleBar.SaveState != OfficeSaveState.Saving)
        {
            this.titleBar.SaveState = this.Workbook is { } book && book.Revision != this.savedRevision
                ? OfficeSaveState.Unsaved
                : this.lastSaved;
        }

        this.backstage.DocumentName = this.EffectiveDocumentName;

        // The notes list follows edits while it is showing; rebuilt only when the workbook changed.
        if (this.shell.IsRightPaneOpen && this.Workbook is { } current && current.Revision != this.notesRevision)
            this.RebuildNotes();
    }

    long notesRevision = -1;

    /// <summary>The status bar's left end: the mode and, for a meaningful selection, Average/Count/Sum.</summary>
    void UpdateStatus()
    {
        var c = this.controller;
        this.modeItem.Text = SpreadsheetShell.ModeText(c?.EditMode ?? SheetEditMode.Ready);

        var text = c is null ? null : SpreadsheetShell.Aggregates(c.SelectionStatistics);
        this.aggregateItem.Text = text;
        this.aggregateItem.IsVisible = text is not null;
    }

    void RebuildNotes()
    {
        if (this.notesList is null)
            return;

        this.notesList.Children.Clear();
        this.notesRevision = this.Workbook?.Revision ?? -1;
        var notes = this.Workbook is { } book ? SpreadsheetShell.Notes(book) : [];
        this.notesEmpty.IsVisible = notes.Count == 0;

        foreach (var note in notes)
        {
            var where = new Label { Text = note.Address, FontAttributes = FontAttributes.Bold, FontSize = 13 };
            var author = new Label { Text = note.Author, FontSize = 12, HorizontalOptions = LayoutOptions.End };
            author.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
            var text = new Label { Text = note.Text, FontSize = 13, LineBreakMode = LineBreakMode.WordWrap };

            var grid = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto)], RowSpacing = 2 };
            grid.Add(where, 0, 0);
            grid.Add(author, 1, 0);
            grid.Add(text, 0, 1);
            Grid.SetColumnSpan(text, 2);

            var card = new Border { Content = grid, Padding = new Thickness(10, 8), StrokeThickness = 1, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 } };
            card.SetDynamicResource(Border.StrokeProperty, ShinyThemeKeys.Brush.OutlineVariant);
            card.SetDynamicResource(BackgroundColorProperty, ShinyThemeKeys.Color.Surface);

            var target = note;
            var tap = new TapGestureRecognizer { Command = new Command(() => this.GoToNote(target)) };
            card.GestureRecognizers.Add(tap);
            SemanticProperties.SetDescription(card, $"Note at {note.Address}: {note.Text}");

            this.notesList.Children.Add(card);
        }
    }

    /// <summary>Selects a note's cell, switching sheets if needed — the comments pane's tap. Test seam.</summary>
    public void GoToNote(SpreadsheetNoteEntry note)
    {
        if (this.controller is not { } c)
            return;

        SpreadsheetShell.GoTo(c, note);
        this.Invalidate();
    }

    void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OfficeShell.IsBackstageOpen) && this.shell.IsBackstageOpen && this.Workbook is { } book)
            this.backstage.DocumentInfo = SpreadsheetShell.DocumentInfo(book, this.EffectiveDocumentName);

        if (e.PropertyName == nameof(OfficeShell.IsRightPaneOpen) && !this.syncingPane)
        {
            this.syncingPane = true;
            this.ribbonActions.IsCommentsOpen = this.shell.IsRightPaneOpen;
            this.syncingPane = false;

            if (this.shell.IsRightPaneOpen)
                this.RebuildNotes();
        }
    }

    void OnRibbonActionsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OfficeRibbonActions.IsCommentsOpen) && !this.syncingPane)
        {
            this.syncingPane = true;
            this.shell.IsRightPaneOpen = this.ribbonActions.IsCommentsOpen;
            this.syncingPane = false;

            if (this.shell.IsRightPaneOpen)
                this.RebuildNotes();
        }
        else if (e.PropertyName == nameof(OfficeRibbonActions.EditMode))
        {
            this.editMode = this.ribbonActions.EditMode;
            this.UpdateChrome();
        }
    }

    void OnStatusBarPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(OfficeStatusBar.Zoom))
            return;

        var zoom = this.statusBar.Zoom;
        if (this.controller is { } c && Math.Abs(c.Zoom - zoom) > 0.0005)
            c.Zoom = zoom;
    }

    void OnFileMenu()
    {
        if (this.ShowShell && this.ShowBackstage)
            this.shell.OpenBackstage();

        this.FileMenuRequested?.Invoke(this, EventArgs.Empty);
    }


    // ---- backstage ----

    /// <summary>
    /// Gives the built-in templates a picture of their first sheet, as PowerPoint's have of their first
    /// slide. Drawn once per process the first time a backstage opens; a host's own list is left alone.
    /// </summary>
    async Task LoadTemplateThumbnailsAsync()
    {
        if (this.Templates is not null)
            return;

        var templates = await TemplateThumbnailCache.Spreadsheet;
        if (this.Dispatcher is { IsDispatchRequired: true } dispatcher)
            dispatcher.Dispatch(() => this.backstage.Templates = this.Templates ?? templates);
        else
            this.backstage.Templates = this.Templates ?? templates;
    }

    async Task OnTemplateSelectedAsync(OfficeTemplate template)
    {
        if (this.TemplateSelected is { } handler)
        {
            handler(this, template);
            return;
        }

        Workbook next;
        if (template.Open is { } open)
        {
            await using var stream = await open(CancellationToken.None);
            next = await Workbook.OpenAsync(stream);
        }
        else
        {
            next = SpreadsheetTemplates.Create(template);
        }

        var previous = this.ownedWorkbook;
        this.ownedWorkbook = next;
        this.DocumentName = template.IsBlank ? OfficeAppInfo.Excel.DefaultDocumentName : template.Name;
        this.SheetName = null;
        this.Workbook = next;

        this.WorkbookReplaced?.Invoke(this, next);

        if (previous is not null && !ReferenceEquals(previous, next))
            previous.Dispose();
    }

    /// <summary>Title bar ▸ Save, Backstage ▸ Save: the workbook as xlsx, through <see cref="FileRequested"/>.</summary>
    public Task SaveAsync() => this.WriteFileAsync(OfficeFileFormats.Xlsx, SpreadsheetFileAction.Save);

    Task WriteFileAsync(OfficeFileFormat format, SpreadsheetFileAction action)
    {
        if (this.controller is not { } c || this.FileRequested is not { } handler)
            return Task.CompletedTask;

        var request = new SpreadsheetFileRequest(c.Workbook, c.Sheet, format, format.FileNameFor(this.EffectiveDocumentName), action);
        var saving = action is SpreadsheetFileAction.Save or SpreadsheetFileAction.SaveAs && format.IsNative;
        var revision = c.Workbook.Revision;

        try
        {
            if (saving)
                this.titleBar.SaveState = OfficeSaveState.Saving;

            handler(this, request);

            if (saving)
            {
                this.savedRevision = revision;
                this.lastSaved = OfficeSaveState.Saved;
            }
        }
        catch (Exception)
        {
            if (saving)
                this.lastSaved = OfficeSaveState.Error;
        }
        finally
        {
            if (saving)
                this.titleBar.SaveState = OfficeSaveState.None;

            this.UpdateShell();
        }

        return Task.CompletedTask;
    }

    void DisposeShell()
    {
        this.commandSync?.Dispose();
        this.statusBar.PropertyChanged -= this.OnStatusBarPropertyChanged;
        this.shell.PropertyChanged -= this.OnShellPropertyChanged;
        this.ribbonActions.PropertyChanged -= this.OnRibbonActionsPropertyChanged;

        // A host handed the workbook through WorkbookReplaced may outlive this view with it.
        if (this.WorkbookReplaced is null)
            this.ownedWorkbook?.Dispose();
    }
}
