using System.ComponentModel;
using System.Text.RegularExpressions;
using Shiny.Controls.Office.Document;
using Shiny.Controls.Office.Shell;
using Shiny.Controls.Office.Skia;
using Shiny.Maui.Controls.Ribbons;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// Word's window around the editor: the title bar, ribbon actions, ruler, navigation and comments
/// panes, status bar and the File backstage — the <see cref="OfficeShell"/> parts, fed from the
/// controller through <see cref="WordShell"/>.
/// </summary>
/// <remarks>
/// <para>
/// On by default (<see cref="ShowShell"/>). Every part is created with the view and switched with
/// <c>IsVisible</c>, never added later: the AppKit head does not realise a child added after the first
/// layout. With the shell off the view is exactly the ribbon over the page, as it was before.
/// </para>
/// <para>
/// The shell does no I/O. New, Open, Save, Save As, Export and Print are events; the one thing done
/// here without a host is opening a built-in template when nobody handles <see cref="NewDocumentRequested"/>,
/// and <see cref="ExportPdf"/>, which writes to a stream the host supplies.
/// </para>
/// </remarks>
public partial class DocumentEditorView
{
    readonly OfficeTitleBar titleBar = new() { DocumentName = "Document1" };
    readonly OfficeRibbonActions ribbonActions = new();
    readonly OfficeRuler ruler = new() { HeightRequest = 24 };
    readonly OfficeNavigationPane navigationPaneView = new();
    readonly VerticalStackLayout commentsList = new() { Spacing = 8, Padding = new Thickness(12, 4, 12, 12) };
    readonly OfficeSidePane commentsPane = new() { Title = "Comments" };
    readonly OfficeStatusBar statusBar = new();
    readonly OfficeBackstage backstage = new();
    readonly OfficeStyleGallery styleGallery = new() { Columns = 5 };
    readonly Image printPreview = new() { Aspect = Aspect.AspectFit, HeightRequest = 520 };
    readonly OfficeStatusItem pageItem = new("page", "Page 1 of 1") { IsClickable = true, Tooltip = "Open the navigation pane" };
    readonly OfficeStatusItem wordsItem = new("words", "0 words") { IsClickable = true, Tooltip = "Word count" };
    readonly OfficeStatusItem languageItem = new("language", WordShell.LanguageText());

    string headingSignature = string.Empty;
    string commentSignature = string.Empty;
    bool syncingShell;
    bool shellBuilt;
    OfficeIndents? pendingIndents;
    IReadOnlyList<OfficeTabStop>? pendingTabs;
    (double Left, double Right)? pendingMargins;
    IDispatcherTimer? rulerTimer;

    /// <summary>The command list the title bar's search reads: every ribbon command plus a few of Word's own.</summary>
    public OfficeCommandIndex CommandIndex { get; } = new();

    // ---- parts, for hosts and tests ----

    /// <summary>The Office window this view is dressed in.</summary>
    public OfficeShell Shell => this.shell;

    public OfficeTitleBar TitleBar => this.titleBar;

    public OfficeStatusBar StatusBar => this.statusBar;

    public OfficeRuler Ruler => this.ruler;

    public OfficeNavigationPane NavigationPane => this.navigationPaneView;

    public OfficeSidePane CommentsPane => this.commentsPane;

    public OfficeBackstage Backstage => this.backstage;

    public OfficeRibbonActions RibbonActions => this.ribbonActions;

    public OfficeStyleGallery StyleGallery => this.styleGallery;

    // ---- bindable properties ----

    static BindableProperty ShellFlag(string name, bool value, BindingMode mode = BindingMode.OneWay)
        => BindableProperty.Create(name, typeof(bool), typeof(DocumentEditorView), value, mode,
            propertyChanged: (b, _, _) => ((DocumentEditorView)b).ApplyShell());

    /// <summary>Word's window — title bar, ruler, panes, status bar, backstage. On by default; off is the bare ribbon over the page.</summary>
    public static readonly BindableProperty ShowShellProperty = BindableProperty.Create(
        nameof(ShowShell), typeof(bool), typeof(DocumentEditorView), true,
        propertyChanged: (b, _, _) =>
        {
            var view = (DocumentEditorView)b;
            view.ApplyShell();
            view.ApplyNavigationPane();
            view.ApplyAccent();
            view.ApplyFileButton(view.ShowFileButton);
        });

    public static readonly BindableProperty ShowTitleBarProperty = ShellFlag(nameof(ShowTitleBar), true);
    public static readonly BindableProperty ShowStatusBarProperty = ShellFlag(nameof(ShowStatusBar), true);
    public static readonly BindableProperty ShowRulerProperty = ShellFlag(nameof(ShowRuler), true);

    /// <summary>The comments pane down the right. Two-way: the ribbon's Comments button and the pane's close move it.</summary>
    public static readonly BindableProperty ShowCommentsPaneProperty = ShellFlag(nameof(ShowCommentsPane), false, BindingMode.TwoWay);

    public static readonly BindableProperty DocumentNameProperty = BindableProperty.Create(
        nameof(DocumentName), typeof(string), typeof(DocumentEditorView), "Document1", BindingMode.TwoWay,
        propertyChanged: (b, _, n) =>
        {
            var view = (DocumentEditorView)b;
            view.titleBar.DocumentName = (string?)n;
            view.backstage.DocumentName = (string?)n;
        });

    public static readonly BindableProperty DocumentLocationProperty = BindableProperty.Create(
        nameof(DocumentLocation), typeof(string), typeof(DocumentEditorView), null,
        propertyChanged: (b, _, n) => ((DocumentEditorView)b).titleBar.DocumentLocation = (string?)n);

    /// <summary>The title bar's save status. Null (the default) follows the document: Unsaved while dirty, else Saved.</summary>
    public static readonly BindableProperty SaveStateProperty = BindableProperty.Create(
        nameof(SaveState), typeof(OfficeSaveState?), typeof(DocumentEditorView), null,
        propertyChanged: (b, _, _) => ((DocumentEditorView)b).RefreshTitle());

    public static readonly BindableProperty AutoSaveProperty = BindableProperty.Create(
        nameof(AutoSave), typeof(bool), typeof(DocumentEditorView), false, BindingMode.TwoWay,
        propertyChanged: (b, _, n) => ((DocumentEditorView)b).titleBar.AutoSave = (bool)n);

    public static readonly BindableProperty UserNameProperty = BindableProperty.Create(
        nameof(UserName), typeof(string), typeof(DocumentEditorView), null,
        propertyChanged: (b, _, n) =>
        {
            var view = (DocumentEditorView)b;
            view.titleBar.UserName = (string?)n;
            if (!string.IsNullOrWhiteSpace((string?)n) && view.editor.Controller is { } controller)
                controller.Author = (string)n!;
        });

    /// <summary>The backstage's templates. Null (the default) is <see cref="WordTemplates.All"/>: Blank, Report, Letter.</summary>
    public static readonly BindableProperty TemplatesProperty = BindableProperty.Create(
        nameof(Templates), typeof(IEnumerable<OfficeTemplate>), typeof(DocumentEditorView), null,
        propertyChanged: (b, _, n) => ((DocumentEditorView)b).backstage.Templates = (IEnumerable<OfficeTemplate>?)n ?? WordTemplates.All);

    /// <summary>The backstage's recent files — the host's list.</summary>
    public static readonly BindableProperty RecentFilesProperty = BindableProperty.Create(
        nameof(RecentFiles), typeof(IEnumerable<OfficeRecentFile>), typeof(DocumentEditorView), null,
        propertyChanged: (b, _, n) => ((DocumentEditorView)b).backstage.RecentFiles = (IEnumerable<OfficeRecentFile>?)n);

    /// <summary>Editing / Reviewing (track changes on) / Viewing (read-only). Two-way; the ribbon's mode dropdown sets it.</summary>
    public static readonly BindableProperty EditModeProperty = BindableProperty.Create(
        nameof(EditMode), typeof(OfficeEditMode), typeof(DocumentEditorView), OfficeEditMode.Editing, BindingMode.TwoWay,
        propertyChanged: (b, _, _) => ((DocumentEditorView)b).ApplyEditMode());

    public bool ShowShell { get => (bool)this.GetValue(ShowShellProperty); set => this.SetValue(ShowShellProperty, value); }
    public bool ShowTitleBar { get => (bool)this.GetValue(ShowTitleBarProperty); set => this.SetValue(ShowTitleBarProperty, value); }
    public bool ShowStatusBar { get => (bool)this.GetValue(ShowStatusBarProperty); set => this.SetValue(ShowStatusBarProperty, value); }
    public bool ShowRuler { get => (bool)this.GetValue(ShowRulerProperty); set => this.SetValue(ShowRulerProperty, value); }
    public bool ShowCommentsPane { get => (bool)this.GetValue(ShowCommentsPaneProperty); set => this.SetValue(ShowCommentsPaneProperty, value); }
    public string? DocumentName { get => (string?)this.GetValue(DocumentNameProperty); set => this.SetValue(DocumentNameProperty, value); }
    public string? DocumentLocation { get => (string?)this.GetValue(DocumentLocationProperty); set => this.SetValue(DocumentLocationProperty, value); }
    public OfficeSaveState? SaveState { get => (OfficeSaveState?)this.GetValue(SaveStateProperty); set => this.SetValue(SaveStateProperty, value); }
    public bool AutoSave { get => (bool)this.GetValue(AutoSaveProperty); set => this.SetValue(AutoSaveProperty, value); }
    public string? UserName { get => (string?)this.GetValue(UserNameProperty); set => this.SetValue(UserNameProperty, value); }
    public IEnumerable<OfficeTemplate>? Templates { get => (IEnumerable<OfficeTemplate>?)this.GetValue(TemplatesProperty); set => this.SetValue(TemplatesProperty, value); }
    public IEnumerable<OfficeRecentFile>? RecentFiles { get => (IEnumerable<OfficeRecentFile>?)this.GetValue(RecentFilesProperty); set => this.SetValue(RecentFilesProperty, value); }
    public OfficeEditMode EditMode { get => (OfficeEditMode)this.GetValue(EditModeProperty); set => this.SetValue(EditModeProperty, value); }

    /// <summary>What the title bar is showing as the save status right now.</summary>
    public OfficeSaveState EffectiveSaveState
        => this.SaveState ?? (this.Document?.IsDirty == true ? OfficeSaveState.Unsaved : OfficeSaveState.Saved);

    // ---- events ----

    /// <summary>Save — the title bar's button or the backstage's rail.</summary>
    public event EventHandler? SaveRequested;

    public event EventHandler<OfficeFileFormat>? SaveAsRequested;

    /// <summary>Export in a format. For PDF, <see cref="ExportPdf"/> writes the file.</summary>
    public event EventHandler<OfficeFileFormat>? ExportRequested;

    public event EventHandler? OpenRequested;

    public event EventHandler<OfficeRecentFile>? RecentFileSelected;

    /// <summary>A template was picked in the backstage. With no handler the view opens it itself and raises <see cref="DocumentOpened"/>.</summary>
    public event EventHandler<OfficeTemplate>? NewDocumentRequested;

    /// <summary>The view opened a document itself — a template picked with no <see cref="NewDocumentRequested"/> handler.</summary>
    public event EventHandler<WordDocument>? DocumentOpened;

    /// <summary>Print. <see cref="ExportPdf"/> gives the host something to print.</summary>
    public event EventHandler? PrintRequested;

    public event EventHandler? ShareRequested;

    public event EventHandler<string>? DocumentRenamed;

    // ---- actions ----

    /// <summary>Writes the document to <paramref name="output"/> as a PDF, one page per printed page. Returns the page count.</summary>
    public int ExportPdf(Stream output)
    {
        if (this.Document is not { } document)
            throw new InvalidOperationException("There is no document to export.");

        return DocumentPdfExporter.Export(document, output, new DocumentPdfOptions
        {
            Title = this.DocumentName,
            Author = this.editor.Controller?.Author
        });
    }

    /// <summary>Applies ruler drags that are waiting out their debounce now. Test seam; a drag commits on its own a moment after it stops.</summary>
    public void CommitRulerEdits()
    {
        this.rulerTimer?.Stop();
        var controller = this.editor.Controller;
        var changed = false;

        if (this.pendingIndents is { } indents)
            changed |= WordShell.ApplyIndents(controller, indents);

        if (this.pendingTabs is { } tabs)
            changed |= WordShell.ApplyTabStops(controller, tabs);

        if (this.pendingMargins is { } margins)
            changed |= WordShell.ApplyMargins(controller, margins.Left, margins.Right);

        this.pendingIndents = null;
        this.pendingTabs = null;
        this.pendingMargins = null;

        if (changed)
            this.AfterCommand();
    }

    // ---- building ----

    void BuildShell()
    {
        this.shell.TitleBar = this.titleBar;
        this.shell.Ribbon = this.ribbon;
        this.shell.Ruler = this.ruler;
        this.shell.LeftPane = this.navigationPaneView;
        this.shell.ShellContent = this.root;
        this.shell.RightPane = this.commentsPane;
        this.shell.StatusBar = this.statusBar;
        this.shell.Backstage = this.backstage;
        this.ribbon.HeaderEndContent = this.ribbonActions;

        // Title bar
        this.titleBar.CommandIndex = this.CommandIndex;
        this.titleBar.SaveRequested += (_, _) => this.RaiseSave();
        this.titleBar.UndoRequested += (_, _) => this.Run(c => c.Undo());
        this.titleBar.RedoRequested += (_, _) => this.Run(c => c.Redo());
        this.titleBar.DocumentRenamed += (_, name) => this.DocumentRenamed?.Invoke(this, name);
        this.titleBar.SearchSubmitted += (_, query) => this.SearchDocument(query);
        this.titleBar.PropertyChanged += this.OnTitleBarPropertyChanged;

        // Ribbon actions
        this.ribbonActions.PropertyChanged += this.OnRibbonActionsPropertyChanged;
        this.ribbonActions.ShareClicked += (_, _) => this.ShareRequested?.Invoke(this, EventArgs.Empty);

        // Ruler: a drag reports every step, so the edit waits for the drag to settle - one undo step, not fifty.
        this.ruler.IndentsChanged += (_, indents) => { this.pendingIndents = indents; this.ScheduleRulerCommit(); };
        this.ruler.TabStopsChanged += (_, tabs) => { this.pendingTabs = tabs; this.ScheduleRulerCommit(); };
        this.ruler.MarginsChanged += (_, margins) => { this.pendingMargins = margins; this.ScheduleRulerCommit(); };

        // Navigation pane
        this.navigationPaneView.HeadingSelected += (_, heading) =>
        {
            if (WordShell.GoTo(this.editor.Controller, heading))
                this.editor.FocusEditor();
        };
        this.navigationPaneView.SearchRequested += (_, text) =>
            this.navigationPaneView.SearchResults = WordShell.Search(this.editor.Controller, text);
        this.navigationPaneView.ResultSelected += (_, result) => WordShell.GoTo(this.editor.Controller, result);

        // Comments pane
        var newComment = ShellChrome.TextButton("New", () =>
        {
            if (this.editor.Controller is { } controller)
                _ = this.NewCommentAsync(controller);
        }, Shiny.Controls.Office.Icons.OfficeShellIcon.Comments, null, out _);
        this.commentsPane.HeaderContent = newComment;
        this.commentsPane.PaneContent = new ScrollView { Content = this.commentsList };

        // Status bar
        this.statusBar.Items.Add(this.pageItem);
        this.statusBar.Items.Add(this.wordsItem);
        this.statusBar.Items.Add(this.languageItem);
        this.statusBar.Zoom = this.Zoom;
        this.statusBar.ItemClicked += this.OnStatusItemClicked;
        this.statusBar.ViewModeChanged += (_, mode) => this.ApplyViewMode(mode.Id);
        this.statusBar.PropertyChanged += this.OnStatusBarPropertyChanged;

        // Backstage
        this.backstage.Templates = this.Templates ?? WordTemplates.All;
        this.backstage.DocumentName = this.DocumentName;
        this.backstage.PrintPreview = this.printPreview;
        this.backstage.PropertyChanged += this.OnBackstagePropertyChanged;
        this.backstage.TemplateSelected += (_, template) => _ = this.NewFromTemplateAsync(template);
        this.backstage.OpenRequested += (_, _) => this.OpenRequested?.Invoke(this, EventArgs.Empty);
        this.backstage.RecentFileSelected += (_, file) => this.RecentFileSelected?.Invoke(this, file);
        this.backstage.SaveRequested += (_, _) => this.RaiseSave();
        this.backstage.SaveAsRequested += (_, format) => this.SaveAsRequested?.Invoke(this, format);
        this.backstage.ExportRequested += (_, format) => this.ExportRequested?.Invoke(this, format);
        this.backstage.PrintRequested += (_, _) => this.PrintRequested?.Invoke(this, EventArgs.Empty);

        // Style gallery
        this.styleGallery.StyleSelected += (_, style) => this.Run(c => c.ApplyStyle(style.Id));

        // Shell
        this.shell.PropertyChanged += this.OnShellPropertyChanged;

        this.shellBuilt = true;
        this.ApplyFileButton(this.ShowFileButton);
        this.ApplyShell();
        this.ApplyNavigationPane();
        this.RefreshShell();
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

        this.CommandIndex.Add(new OfficeCommand("Navigation Pane", () => this.ShowNavigationPane = true)
        {
            Category = "View › Show",
            Keywords = ["go to", "headings", "outline", "jump"]
        });
        this.CommandIndex.Add(new OfficeCommand("Comments", () => this.ShowCommentsPane = true)
        {
            Category = "Review › Comments",
            Keywords = ["comment", "pane", "review"]
        });
        this.CommandIndex.Add(new OfficeCommand("Word Count", () =>
        {
            if (this.editor.Controller is { } controller)
                _ = this.ShowWordCountAsync(controller);
        })
        {
            Category = "Review › Proofing",
            Shortcut = "Ctrl+Shift+G",
            Keywords = ["statistics", "count", "characters"]
        });
        this.CommandIndex.Add(new OfficeCommand("Export to PDF", () => this.ExportRequested?.Invoke(this, OfficeFileFormats.Pdf))
        {
            Category = "File › Export",
            Keywords = ["pdf", "save as", "print"]
        });
        this.CommandIndex.Add(new OfficeCommand("Print", () => this.PrintRequested?.Invoke(this, EventArgs.Empty))
        {
            Category = "File",
            Shortcut = "Ctrl+P"
        });
    }

    static readonly Regex ShortcutInTooltip = new(@"^(?<name>.+?) \((?<keys>(?:Ctrl|Shift|Alt|Cmd|Esc|F\d{1,2})[^()]*)\)(?<rest>.*)$", RegexOptions.Compiled);

    /// <summary>"Bold (Ctrl+B)" becomes Tooltip "Bold" + Shortcut "Ctrl+B", so the search can show the keys too.</summary>
    static void MoveShortcut(RibbonItem item)
    {
        if (item is RibbonRow row)
        {
            foreach (var child in row.Items)
                MoveShortcut(child);

            return;
        }

        if (!string.IsNullOrWhiteSpace(item.Shortcut) || item.Tooltip is not { } tooltip)
            return;

        var match = ShortcutInTooltip.Match(tooltip);
        if (!match.Success)
            return;

        item.Shortcut = match.Groups["keys"].Value;
        item.Tooltip = match.Groups["name"].Value + match.Groups["rest"].Value;
    }

    // ---- applying ----

    void ApplyShell()
    {
        if (!this.shellBuilt)
            return;

        var on = this.ShowShell;
        this.titleBar.IsVisible = on && this.ShowTitleBar;
        this.statusBar.IsVisible = on && this.ShowStatusBar;
        this.ribbonActions.IsVisible = on;
        this.backstage.IsVisible = on;

        if (!on)
        {
            this.shell.IsBackstageOpen = false;
            this.shell.IsFocusMode = false;
        }

        this.shell.IsRightPaneOpen = on && this.ShowCommentsPane;

        // The title bar owns undo and redo while it shows; otherwise they are the ribbon's quick access.
        this.ribbon.ShowQuickAccess = !(on && this.ShowTitleBar);

        this.styleGallery.IsVisible = on;
        if (this.stylesMenu is not null)
            this.stylesMenu.IsVisible = !on;

        this.syncingShell = true;
        this.ribbonActions.IsCommentsOpen = this.ShowCommentsPane;
        this.syncingShell = false;

        this.RefreshRuler();
    }

    void ApplyEditorReadOnly()
        => this.editor.IsReadOnly = this.IsReadOnly || this.ReadMode || this.EditMode == OfficeEditMode.Viewing;

    void ApplyEditMode()
    {
        this.syncingShell = true;
        this.ribbonActions.EditMode = this.EditMode;
        this.syncingShell = false;

        if (this.editor.Controller is { } controller && this.EditMode == OfficeEditMode.Reviewing && !controller.IsTrackingChanges)
            controller.IsTrackingChanges = true;

        this.ApplyEditorReadOnly();
        this.RefreshBar();
    }

    void ApplyViewMode(string? id)
    {
        var (readMode, layout) = WordShell.FromViewModeId(id, this.editor.PageLayout);

        if (readMode)
        {
            this.ReadMode = true;
        }
        else
        {
            this.ReadMode = false;
            this.editor.PageLayout = layout;
        }

        this.RefreshShell();
    }

    // ---- refreshing ----

    void OnShellControllerAttached()
    {
        if (!this.shellBuilt)
            return;

        if (this.editor.Controller is { } controller)
        {
            if (!string.IsNullOrWhiteSpace(this.UserName))
                controller.Author = this.UserName!;

            if (this.EditMode == OfficeEditMode.Reviewing)
                controller.IsTrackingChanges = true;
        }

        this.headingSignature = string.Empty;
        this.commentSignature = string.Empty;
        this.navigationPaneView.SearchResults = null;
        this.RefreshShell();
    }

    /// <summary>Everything the shell shows that follows the controller.</summary>
    void RefreshShell()
    {
        if (!this.shellBuilt)
            return;

        this.RefreshTitle();
        this.RefreshStatus();
        this.RefreshRuler();
        this.RefreshNavigation();
        this.RefreshComments();

        if (this.editor.Controller is { } controller)
        {
            this.styleGallery.Styles = WordShell.Styles(controller);
            this.styleGallery.SelectedStyleId = controller.CurrentStyleId;
        }
    }

    void RefreshTitle()
    {
        if (!this.shellBuilt)
            return;

        var controller = this.editor.Controller;
        this.titleBar.CanUndo = controller?.CanUndo ?? false;
        this.titleBar.CanRedo = controller?.CanRedo ?? false;
        this.titleBar.SaveState = this.EffectiveSaveState;
    }

    void RefreshStatus()
    {
        if (!this.shellBuilt)
            return;

        var controller = this.editor.Controller;
        var statistics = controller?.Statistics ?? DocumentStatistics.Empty;

        this.pageItem.Text = WordShell.PageText(statistics);
        this.wordsItem.Text = WordShell.WordsText(statistics);
        this.pageItem.IsVisible = this.editor.PageLayout == DocumentPageLayout.Print && !this.ReadMode;

        this.syncingShell = true;
        this.statusBar.SelectedViewMode = WordShell.ViewModeId(this.ReadMode, this.editor.PageLayout);

        if (Math.Abs(this.statusBar.Zoom - this.editor.Zoom) > 0.0001)
            this.statusBar.Zoom = this.editor.Zoom;

        this.syncingShell = false;

        if (this.Document is { } document)
        {
            this.statusBar.PageWidth = document.Page.Width;
            this.statusBar.PageHeight = document.Page.Height;
            this.statusBar.TextWidth = document.Page.ContentWidth;
        }

        this.statusBar.ViewportWidth = this.editor.Width > 0 ? this.editor.Width : 0;
        this.statusBar.ViewportHeight = this.editor.Height > 0 ? this.editor.Height : 0;
    }

    void RefreshRuler()
    {
        if (!this.shellBuilt)
            return;

        var controller = this.editor.Controller;
        var print = this.editor.PageLayout == DocumentPageLayout.Print;
        this.ruler.IsVisible = this.ShowShell && this.ShowRuler && !this.ReadMode && print && controller is not null;

        if (controller is null || this.Document is not { } document)
            return;

        var scale = controller.ViewScale;
        this.ruler.PageWidth = WordShell.ToPoints(document.Page.Width);
        this.ruler.Zoom = scale;

        // The ruler sits over the content column, so the page's left edge in the editor's own pixels is
        // where the ruler's zero goes - less the hand-built navigation pane, which is gone with the shell on.
        this.ruler.PageOffset = controller.PageX * scale;

        // A drag in progress owns the markers until it is committed.
        if (this.pendingMargins is null)
        {
            this.ruler.LeftMargin = WordShell.ToPoints(controller.PageMargins.Left);
            this.ruler.RightMargin = WordShell.ToPoints(controller.PageMargins.Right);
        }

        if (this.pendingIndents is null)
            this.ruler.Indents = WordShell.Indents(controller.CaretFormat);

        if (this.pendingTabs is null)
            this.ruler.TabStops = WordShell.TabStops(controller);

        this.ruler.IsEnabled = !this.IsReadOnly && !this.ReadMode && this.EditMode != OfficeEditMode.Viewing;
    }

    void RefreshNavigation()
    {
        if (!this.shellBuilt)
            return;

        var controller = this.editor.Controller;
        var headings = WordShell.Headings(controller);
        var signature = string.Join("\n", headings.Select(x => $"{x.Id}|{x.Level}|{x.Text}"));

        if (signature != this.headingSignature)
        {
            this.headingSignature = signature;
            this.navigationPaneView.Headings = headings;
        }

        this.navigationPaneView.CurrentHeadingId = WordShell.CurrentHeadingId(controller, headings);
    }

    void RefreshComments()
    {
        var controller = this.editor.Controller;
        var comments = controller?.Comments ?? [];
        var current = controller?.CurrentComment?.Id;
        var signature = string.Join("\n", comments.Select(x => $"{x.Id}|{x.Author}|{x.Date:O}|{x.Text}")) + "#" + current + "#" + this.IsReadOnly;

        if (signature == this.commentSignature)
            return;

        this.commentSignature = signature;
        this.commentsList.Children.Clear();

        if (controller is null || comments.Count == 0)
        {
            var empty = ShellChrome.Text("No comments yet. Select some text and choose New to add one.", 13);
            empty.LineBreakMode = LineBreakMode.WordWrap;
            empty.Opacity = 0.75;
            this.commentsList.Children.Add(empty);
            return;
        }

        var now = DateTimeOffset.Now;
        foreach (var comment in comments)
            this.commentsList.Children.Add(this.CommentCard(controller, comment, comment.Id == current, now));
    }

    View CommentCard(DocumentEditorController controller, DocumentComment comment, bool isCurrent, DateTimeOffset now)
    {
        var id = comment.Id;
        var author = ShellChrome.Text(comment.Author, 13, attributes: FontAttributes.Bold);
        var date = ShellChrome.Text(WordShell.CommentDate(comment, now), 11);
        date.Opacity = 0.7;

        var quote = ShellChrome.Text(controller.CommentedText(comment), 12);
        quote.FontAttributes = FontAttributes.Italic;
        quote.Opacity = 0.75;
        quote.LineBreakMode = LineBreakMode.TailTruncation;
        quote.MaxLines = 2;

        var text = ShellChrome.Text(comment.Text, 13);
        text.LineBreakMode = LineBreakMode.WordWrap;

        var delete = ShellChrome.IconButton(Shiny.Controls.Office.Icons.OfficeShellIcon.Close, "Delete comment", () =>
        {
            if (this.IsReadOnly || this.ReadMode || this.editor.Controller is not { } c)
                return;

            c.DeleteComment(id);
            this.AfterCommand();
        }, size: 12);
        delete.IsVisible = !this.IsReadOnly;

        var head = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
        head.Add(new VerticalStackLayout { Spacing = 0, Children = { author, date } }, 0);
        head.Add(delete, 1);

        var card = new Border
        {
            Padding = new Thickness(10, 8),
            StrokeThickness = isCurrent ? 2 : 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
            AutomationId = $"DocComment{id}",
            Content = new VerticalStackLayout { Spacing = 4, Children = { head, quote, text } }
        };
        card.SetDynamicResource(BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerHighest);
        card.SetDynamicResource(Border.StrokeProperty, isCurrent ? ShinyThemeKeys.Brush.Primary : ShinyThemeKeys.Brush.OutlineVariant);

        card.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() =>
            {
                if (this.editor.Controller?.GoToComment(id) == true)
                    this.editor.FocusEditor();
            })
        });

        return card;
    }

    void RefreshPrintPreview()
    {
        if (this.Document is not { } document)
        {
            this.printPreview.Source = null;
            return;
        }

        try
        {
            var png = DocumentPdfExporter.RenderPagePng(document, 0, 1f);
            this.printPreview.Source = ImageSource.FromStream(() => new MemoryStream(png));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Shiny.Office] Print preview failed: {ex.Message}");
            this.printPreview.Source = null;
        }
    }

    // ---- reacting ----

    void RaiseSave() => this.SaveRequested?.Invoke(this, EventArgs.Empty);

    void SearchDocument(string query)
    {
        this.ShowNavigationPane = true;
        this.navigationPaneView.SearchText = query;
        this.navigationPaneView.SearchResults = WordShell.Search(this.editor.Controller, query);
        this.navigationPaneView.SelectedTab = OfficeNavigationTab.Results;
    }

    async Task NewFromTemplateAsync(OfficeTemplate template)
    {
        if (this.NewDocumentRequested is { } handler)
        {
            handler(this, template);
            return;
        }

        try
        {
            var document = await WordTemplates.OpenAsync(template);
            this.Document = document;
            this.DocumentName = template.IsBlank ? "Document1" : template.Name;
            this.DocumentLocation = null;
            this.DocumentOpened?.Invoke(this, document);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Shiny.Office] Could not open template '{template.Id}': {ex.Message}");
        }
    }

    void OnStatusItemClicked(object? sender, OfficeStatusItem item)
    {
        if (item == this.pageItem)
        {
            this.ShowNavigationPane = true;
            this.navigationPaneView.SelectedTab = OfficeNavigationTab.Headings;
        }
        else if (item == this.wordsItem && this.editor.Controller is { } controller)
        {
            _ = this.ShowWordCountAsync(controller);
        }
    }

    void OnStatusBarPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (this.syncingShell || e.PropertyName != nameof(OfficeStatusBar.Zoom))
            return;

        if (Math.Abs(this.Zoom - this.statusBar.Zoom) > 0.0001)
            this.Zoom = this.statusBar.Zoom;
    }

    void OnTitleBarPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(OfficeTitleBar.DocumentName) when this.titleBar.DocumentName != this.DocumentName:
                this.DocumentName = this.titleBar.DocumentName;
                break;

            case nameof(OfficeTitleBar.AutoSave) when this.titleBar.AutoSave != this.AutoSave:
                this.AutoSave = this.titleBar.AutoSave;
                break;
        }
    }

    void OnRibbonActionsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (this.syncingShell)
            return;

        switch (e.PropertyName)
        {
            case nameof(OfficeRibbonActions.IsCommentsOpen):
                this.ShowCommentsPane = this.ribbonActions.IsCommentsOpen;
                break;

            case nameof(OfficeRibbonActions.EditMode):
                this.EditMode = this.ribbonActions.EditMode;
                break;
        }
    }

    void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            // The navigation pane's own close, or the compact layout, moved the pane.
            case nameof(OfficeShell.IsLeftPaneOpen) when this.ShowShell && this.shell.IsLeftPaneOpen != this.ShowNavigationPane:
                this.ShowNavigationPane = this.shell.IsLeftPaneOpen;
                break;

            case nameof(OfficeShell.IsRightPaneOpen) when this.ShowShell && this.shell.IsRightPaneOpen != this.ShowCommentsPane:
                this.ShowCommentsPane = this.shell.IsRightPaneOpen;
                break;

            case nameof(OfficeShell.IsBackstageOpen) when this.shell.IsBackstageOpen:
                if (!this.ShowShell)
                {
                    this.shell.IsBackstageOpen = false;
                    break;
                }

                this.backstage.DocumentInfo = WordShell.DocumentInfo(this.editor.Controller, this.DocumentName, this.DocumentLocation);
                break;
        }
    }

    void OnBackstagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OfficeBackstage.SelectedPage) && this.backstage.SelectedPage == OfficeBackstagePage.Print)
            this.RefreshPrintPreview();
    }

    void ScheduleRulerCommit()
    {
        if (this.Dispatcher is not { } dispatcher)
        {
            this.CommitRulerEdits();
            return;
        }

        if (this.rulerTimer is null)
        {
            this.rulerTimer = dispatcher.CreateTimer();
            this.rulerTimer.Interval = TimeSpan.FromMilliseconds(350);
            this.rulerTimer.IsRepeating = false;
            this.rulerTimer.Tick += (_, _) => this.CommitRulerEdits();
        }

        this.rulerTimer.Stop();
        this.rulerTimer.Start();
    }
}
