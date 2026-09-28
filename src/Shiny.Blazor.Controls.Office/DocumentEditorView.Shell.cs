using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Shiny.Blazor.Controls;
using Shiny.Controls.Office.Document;
using Shiny.Controls.Office.Shell;
using Shiny.Controls.Office.Skia;
using Shiny.Controls.Office.Theming;

namespace Shiny.Blazor.Controls.Office;

/// <summary>
/// The Word window around the editor: title bar, ribbon actions, ruler, navigation and comments panes,
/// status bar and the File backstage — the <see cref="OfficeShell"/> parts wired to the controller.
/// </summary>
/// <remarks>
/// <para>
/// On by default (<see cref="ShowShell"/>); every part has its own switch. With the shell off the view
/// is exactly what it was before: the ribbon over the page, with its own quick access and navigation pane.
/// </para>
/// <para>
/// The shell does no I/O of its own, and neither does this: Save, Save As, Export, Open and New are
/// events. What happens when the host has not handled one is the browser's natural answer — the file
/// is downloaded, a template is opened in place, Print prints a PDF of the document.
/// </para>
/// </remarks>
public partial class DocumentEditorView
{
    [Inject] IJSRuntime Js { get; set; } = default!;

    // ---- parameters ----

    /// <summary>Dress the editor in the Word window. On by default; off gives the bare ribbon-over-page layout.</summary>
    [Parameter] public bool ShowShell { get; set; } = true;

    /// <summary>The title bar: AutoSave, Save / Undo / Redo, the document name, command search and save status.</summary>
    [Parameter] public bool ShowTitleBar { get; set; } = true;

    /// <summary>The status bar: page, words, language, Focus, view modes and zoom.</summary>
    [Parameter] public bool ShowStatusBar { get; set; } = true;

    /// <summary>The horizontal ruler above the page. Shown in Print Layout only.</summary>
    [Parameter] public bool ShowRuler { get; set; } = true;

    /// <summary>The navigation pane — headings, and search results — down the left. Two-way bindable.</summary>
    [Parameter] public bool ShowNavigationPane { get; set; }

    [Parameter] public EventCallback<bool> ShowNavigationPaneChanged { get; set; }

    /// <summary>The comments pane down the right. Two-way bindable; the ribbon's Comments button toggles it.</summary>
    [Parameter] public bool ShowCommentsPane { get; set; }

    [Parameter] public EventCallback<bool> ShowCommentsPaneChanged { get; set; }

    /// <summary>The name in the title bar. Defaults to "Document1". Two-way bindable — the title bar renames.</summary>
    [Parameter] public string? DocumentName { get; set; }

    [Parameter] public EventCallback<string?> DocumentNameChanged { get; set; }

    /// <summary>Raised with the new name when the user renames the document from the title bar.</summary>
    [Parameter] public EventCallback<string> DocumentRenamed { get; set; }

    /// <summary>Where the document lives, shown under the name's dropdown.</summary>
    [Parameter] public string? DocumentLocation { get; set; }

    /// <summary>The save status the title bar shows. Null works it out: "Unsaved changes" once edited.</summary>
    [Parameter] public OfficeSaveState? SaveState { get; set; }

    /// <summary>The title bar's AutoSave switch. The host does the saving. Two-way bindable.</summary>
    [Parameter] public bool AutoSave { get; set; }

    [Parameter] public EventCallback<bool> AutoSaveChanged { get; set; }

    /// <summary>The account shown at the title bar's right end.</summary>
    [Parameter] public string? UserName { get; set; }

    /// <summary>The backstage's templates. Null offers <see cref="WordTemplates.All"/> — Blank, Report and Letter.</summary>
    [Parameter] public IReadOnlyList<OfficeTemplate>? Templates { get; set; }

    /// <summary>The backstage's recent files. The host keeps the list; picking one raises <see cref="RecentFileSelected"/>.</summary>
    [Parameter] public IReadOnlyList<OfficeRecentFile>? RecentFiles { get; set; }

    /// <summary>Editing, Reviewing (track changes on) or Viewing (read-only). Two-way bindable.</summary>
    [Parameter] public OfficeEditMode EditMode { get; set; }

    [Parameter] public EventCallback<OfficeEditMode> EditModeChanged { get; set; }

    /// <summary>Save — the title bar's disk, the backstage's Save, Ctrl+S. Unhandled, the .docx is downloaded.</summary>
    [Parameter] public EventCallback SaveRequested { get; set; }

    /// <summary>Save As with the chosen format. Unhandled, .docx, .pdf, .txt and .html are downloaded.</summary>
    [Parameter] public EventCallback<OfficeFileFormat> SaveAsRequested { get; set; }

    /// <summary>Export with the chosen format. Unhandled, .pdf, .txt and .html are downloaded.</summary>
    [Parameter] public EventCallback<OfficeFileFormat> ExportRequested { get; set; }

    /// <summary>The backstage's Open › Browse.</summary>
    [Parameter] public EventCallback OpenRequested { get; set; }

    /// <summary>A recent file was picked in the backstage.</summary>
    [Parameter] public EventCallback<OfficeRecentFile> RecentFileSelected { get; set; }

    /// <summary>
    /// A template was picked. Unhandled, the view opens it itself (<see cref="WordTemplates.OpenAsync"/>)
    /// and raises <see cref="DocumentOpened"/> with the new document.
    /// </summary>
    [Parameter] public EventCallback<OfficeTemplate> NewDocumentRequested { get; set; }

    /// <summary>Raised when the view opened a document itself — a template picked with no handler for it.</summary>
    [Parameter] public EventCallback<WordDocument> DocumentOpened { get; set; }

    /// <summary>Print. Unhandled, a PDF of the document is opened in the browser's print dialog.</summary>
    [Parameter] public EventCallback PrintRequested { get; set; }

    /// <summary>The ribbon's Share button.</summary>
    [Parameter] public EventCallback ShareRequested { get; set; }

    /// <summary>The title bar's command search. Filled from the ribbon as its tabs render, plus a few commands of the view's own.</summary>
    public OfficeCommandIndex CommandIndex => this.commands;

    // ---- state ----

    readonly OfficeCommandIndex commands = new();
    readonly OfficeStatusItem pageItem = new("page", "Page 1 of 1") { IsClickable = true, Tooltip = "Go to a heading or search the document" };
    readonly OfficeStatusItem wordsItem = new("words", "0 words") { IsClickable = true, Tooltip = "Word Count" };
    readonly OfficeStatusItem languageItem = new("language", WordShell.LanguageText()) { Tooltip = "Proofing language" };
    readonly List<OfficeStatusItem> statusItems;

    static readonly OfficeZoomModel StatusZoomModel = new(0.25, 4.0, 1.0);

    Ribbon? ribbonRef;
    Ribbon? syncedRibbon;
    IDisposable? ribbonSync;
    RibbonDisplayMode ribbonMode = RibbonDisplayMode.Expanded;

    bool backstageOpen;
    bool focusMode;
    bool? navigationOverride;
    bool? commentsOverride;
    string? nameOverride;
    bool? autoSaveOverride;
    OfficeEditMode? editModeOverride;
    OfficeBackstagePage backstagePage = OfficeBackstagePage.Home;
    OfficeDocumentInfo? backstageInfo;
    string? printPreview;

    string? navigationSearch;
    IReadOnlyList<OfficeSearchResult>? navigationResults;
    OfficeNavigationTab navigationTab = OfficeNavigationTab.Headings;

    WordDocument? documentOverride;
    WordDocument? lastDocumentParameter;
    string? rulerSignature;
    IJSObjectReference? shellModule;

    public DocumentEditorView()
    {
        this.statusItems = [this.pageItem, this.wordsItem, this.languageItem];

        this.commands.Add(new OfficeCommand("Save", () => this.InvokeAsync(this.SaveAsync)) { Category = "File", Shortcut = "Ctrl+S", Keywords = ["store", "download"] });
        this.commands.Add(new OfficeCommand("Export to PDF", () => this.InvokeAsync(() => this.ExportAsync(OfficeFileFormats.Pdf))) { Category = "File › Export", Keywords = ["pdf", "download"] });
        this.commands.Add(new OfficeCommand("Print", () => this.InvokeAsync(this.PrintAsync)) { Category = "File", Shortcut = "Ctrl+P" });
        this.commands.Add(new OfficeCommand("Word Count", () => this.InvokeAsync(() => { this.Open(DialogKind.WordCount); this.StateHasChanged(); })) { Category = "Review › Proofing", Shortcut = "Ctrl+Shift+G", Keywords = ["statistics", "count"] });
        this.commands.Add(new OfficeCommand("Navigation Pane", () => this.InvokeAsync(() => this.SetNavigationOpenAsync(!this.NavigationOpen))) { Category = "View › Show", Keywords = ["headings", "outline", "go to"] });
        this.commands.Add(new OfficeCommand("Comments Pane", () => this.InvokeAsync(() => this.SetCommentsOpenAsync(!this.CommentsOpen))) { Category = "Review › Comments", Keywords = ["comments", "review"] });
        this.commands.Add(new OfficeCommand("Find", () => this.InvokeAsync(() => this.OpenSearchAsync(null))) { Category = "Home › Editing", Shortcut = "Ctrl+F", Keywords = ["search"] });

        // The Blazor ribbon indexes a tab only once it has rendered, so the commands people search for
        // most on the other tabs are listed up front. Each steps aside once the ribbon has indexed its
        // own button (PruneCurated), so a tab that has been opened is not listed twice.
        this.Curate("Table", c => c.InsertTable(3, 3), "Insert › Tables", keywords: ["grid", "insert table"]);
        this.Curate("Page Break", c => c.InsertPageBreak(), "Insert › Pages", "Ctrl+Enter");
        this.Curate("Blank Page", c => c.InsertBlankPage(), "Insert › Pages");
        this.Curate("Link", _ => this.OpenHyperlink(), "Insert › Links", "Ctrl+K", keywords: ["hyperlink", "url"]);
        this.Curate("Bookmark", _ => this.OpenBookmark(), "Insert › Links");
        this.Curate("Comment", _ => this.OpenComment(), "Insert › Comments", "Ctrl+Alt+M", keywords: ["new comment", "note"]);
        this.Curate("Header", _ => this.OpenChrome(header: true), "Insert › Header & Footer");
        this.Curate("Footer", _ => this.OpenChrome(header: false), "Insert › Header & Footer");
        this.Curate("Symbol", _ => this.Open(DialogKind.Symbols), "Insert › Symbols", keywords: ["character", "special"]);
        this.Curate("Horizontal Line", c => c.InsertHorizontalLine(), "Insert › Symbols", keywords: ["rule", "divider"]);
        this.Curate("Table of Contents", c => c.InsertTableOfContents(), "References › Table of Contents", keywords: ["toc", "contents"]);
        this.Curate("Insert Footnote", _ => this.Open(DialogKind.Footnote), "References › Footnotes", keywords: ["footnote", "note"]);
        this.Curate("Track Changes", c => c.IsTrackingChanges = !c.IsTrackingChanges, "Review › Tracking", "Ctrl+Shift+E", keywords: ["revisions", "review"]);
        this.Curate("Accept All Changes", c => c.AcceptAllChanges(), "Review › Changes");
        this.Curate("Reject All Changes", c => c.RejectAllChanges(), "Review › Changes");
        this.Curate("Portrait", c => c.SetPageOrientation(PageOrientation.Portrait), "Layout › Page Setup", keywords: ["orientation"]);
        this.Curate("Landscape", c => c.SetPageOrientation(PageOrientation.Landscape), "Layout › Page Setup", keywords: ["orientation"]);
        this.Curate("Read Mode", c => this.Defer(() => this.SetReadMode(true)), "View › Views", viewOnly: true);
        this.Curate("Print Layout", c => this.Defer(() => this.SetLayout(DocumentPageLayout.Print)), "View › Views", viewOnly: true);
        this.Curate("Web Layout", c => this.Defer(() => this.SetLayout(DocumentPageLayout.Reflow)), "View › Views", viewOnly: true);
        this.Curate("Zoom 100%", c => this.Defer(() => this.SetZoomAsync(1.0)), "View › Zoom", viewOnly: true, keywords: ["actual size", "reset zoom"]);
    }

    readonly List<OfficeCommand> curated = [];

    void Defer(Func<Task> work) => _ = this.InvokeAsync(work);

    void Curate(
        string label,
        Action<DocumentEditorController> action,
        string category,
        string? shortcut = null,
        bool viewOnly = false,
        string[]? keywords = null)
    {
        var command = new OfficeCommand(label, () => this.InvokeAsync(async () =>
        {
            await this.Run(action, requireEditable: !viewOnly);
            this.StateHasChanged();
        }))
        {
            Id = "curated:" + label,
            Category = category,
            Shortcut = shortcut,
            Keywords = keywords ?? [],
            CanExecute = () => this.C is not null && (viewOnly || !this.Disabled)
        };

        this.curated.Add(command);
        this.commands.Add(command);
    }

    /// <summary>Drops a curated command once the ribbon has indexed a button of the same name.</summary>
    void PruneCurated()
    {
        if (this.curated.Count == 0)
            return;

        var harvested = this.commands.Commands
            .Where(x => x.Id.StartsWith(OfficeRibbonCommands.IdPrefix, StringComparison.Ordinal))
            .Select(x => x.Label)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var command in this.curated.Where(x => harvested.Contains(x.Label)).ToList())
        {
            this.commands.Remove(command.Id);
            this.curated.Remove(command);
        }
    }

    // ---- derived ----

    bool ShellOn => this.ShowShell;

    /// <summary>The document on screen: the host's, or one this view opened from a template.</summary>
    WordDocument? CurrentDocument => this.documentOverride ?? this.Document;

    bool IsViewing => this.EditModeValue == OfficeEditMode.Viewing;

    OfficeEditMode EditModeValue => this.editModeOverride ?? this.EditMode;

    bool NavigationOpen => this.navigationOverride ?? this.ShowNavigationPane;

    bool CommentsOpen => this.commentsOverride ?? this.ShowCommentsPane;

    bool RulerVisible => this.ShowRuler && !this.ReadModeOn && this.C is { IsPaginated: true };

    string EffectiveDocumentName => this.nameOverride ?? this.DocumentName ?? "Document1";

    OfficeSaveState EffectiveSaveState
        => this.SaveState ?? (this.CurrentDocument is { IsDirty: true } ? OfficeSaveState.Unsaved : OfficeSaveState.None);

    bool EffectiveAutoSave => this.autoSaveOverride ?? this.AutoSave;

    /// <summary>A custom accent overrides the shell's Word blue; the default leaves it alone.</summary>
    string? ShellAccent => this.Accent is { } a && a != OfficeAccent.Document ? Css(a.Color) : null;

    protected override void OnParametersSet()
    {
        // A host that hands in a different document takes over from one this view opened itself.
        if (!ReferenceEquals(this.Document, this.lastDocumentParameter))
        {
            this.lastDocumentParameter = this.Document;
            if (this.documentOverride is { } mine && !ReferenceEquals(mine, this.Document))
            {
                this.documentOverride = null;
                mine.Dispose();
            }
        }
    }

    // ---- lifecycle hooks the main file calls ----

    void AfterShellRender()
    {
        // The Blazor ribbon only indexes the tabs it has rendered, and a ribbon dropped for read mode
        // comes back as a new instance - so the search follows whichever ribbon is on screen.
        if (!ReferenceEquals(this.ribbonRef, this.syncedRibbon))
        {
            this.ribbonSync?.Dispose();
            this.ribbonSync = this.ribbonRef is { } ribbon ? this.commands.SyncRibbon(ribbon) : null;
            this.syncedRibbon = this.ribbonRef;
        }

        this.PruneCurated();
        this.UpdateStatus();
        _ = this.FocusOpenedDialogAsync();
    }

    ElementReference dialogRef;
    DialogKind focusedDialog;

    /// <summary>
    /// A dialog opened from a pane or the command search would otherwise leave focus where it was,
    /// so the first keystrokes of a comment went nowhere. Its first field takes focus once, on open.
    /// </summary>
    async Task FocusOpenedDialogAsync()
    {
        var current = this.dialog;
        if (current == this.focusedDialog)
            return;

        this.focusedDialog = current;
        if (current == DialogKind.None)
            return;

        try
        {
            if (await this.ShellModuleAsync() is { } module)
                await module.InvokeVoidAsync("focusFirstField", this.dialogRef);
        }
        catch (JSException) { }
        catch (JSDisconnectedException) { }
    }

    void UpdateStatus()
    {
        var statistics = this.C?.Statistics ?? DocumentStatistics.Empty;
        this.pageItem.Text = WordShell.PageText(statistics);
        this.wordsItem.Text = WordShell.WordsText(statistics);

        // No page count to speak of in one continuous column.
        this.pageItem.IsVisible = this.C is null || this.C.IsPaginated;
    }

    /// <summary>
    /// The ruler follows the page: it re-renders when the page moves under it (a resize, a zoom, a
    /// sideways scroll) or the caret lands in a paragraph with different indents — and not on every
    /// caret blink, which would re-render the whole ribbon with it.
    /// </summary>
    void OnControllerChangedForShell(object? sender, EventArgs e)
    {
        if (!this.ShellOn || sender is not DocumentEditorController c)
            return;

        var format = c.CaretFormat;
        var signature = FormattableString.Invariant(
            $"{c.PageX * c.ViewScale:0.#}|{c.ViewScale:0.###}|{c.PageMargins.Left:0.#}|{c.PageMargins.Right:0.#}|{c.Document.Page.Width:0}|{format.IndentLeft:0.#}|{format.IndentRight:0.#}|{format.IndentFirstLine:0.#}|{c.IsPaginated}|{c.Comments.Count}|{c.CanUndo}|{c.CanRedo}|{c.Document.IsDirty}");

        if (signature == this.rulerSignature)
            return;

        this.rulerSignature = signature;
        _ = this.InvokeAsync(this.StateHasChanged);
    }

    void DisposeShell()
    {
        this.ribbonSync?.Dispose();
        this.ribbonSync = null;
        this.documentOverride?.Dispose();
        this.documentOverride = null;

        if (this.shellModule is { } module)
            _ = DisposeModuleAsync(module);

        static async Task DisposeModuleAsync(IJSObjectReference module)
        {
            try { await module.DisposeAsync(); }
            catch (JSDisconnectedException) { }
            catch (ObjectDisposedException) { }
        }
    }

    // ---- panes ----

    async Task SetNavigationOpenAsync(bool open)
    {
        if (open == this.NavigationOpen)
            return;

        this.navigationOverride = open;
        await this.ShowNavigationPaneChanged.InvokeAsync(open);
        this.StateHasChanged();
    }

    async Task SetCommentsOpenAsync(bool open)
    {
        if (open == this.CommentsOpen)
            return;

        this.commentsOverride = open;
        await this.ShowCommentsPaneChanged.InvokeAsync(open);
        this.StateHasChanged();
    }

    async Task GoToHeadingAsync(OfficeHeading heading)
    {
        if (WordShell.GoTo(this.C, heading) && this.editor is { } surface)
            await surface.FocusAsync();
    }

    Task SearchDocumentAsync(string? query)
    {
        this.navigationSearch = query;
        this.navigationResults = WordShell.Search(this.C, query);
        this.navigationTab = string.IsNullOrEmpty(query) ? OfficeNavigationTab.Headings : OfficeNavigationTab.Results;
        this.StateHasChanged();
        return Task.CompletedTask;
    }

    async Task GoToResultAsync(OfficeSearchResult result)
    {
        if (WordShell.GoTo(this.C, result) && this.editor is { } surface)
            await surface.FocusAsync();
    }

    /// <summary>Opens the navigation pane on its search — with a query, straight onto its results.</summary>
    async Task OpenSearchAsync(string? query)
    {
        if (!this.ShellOn)
        {
            this.selectedTab = "home";
            this.StateHasChanged();
            return;
        }

        await this.SetNavigationOpenAsync(true);

        if (!string.IsNullOrWhiteSpace(query))
            await this.SearchDocumentAsync(query);
        else
            this.navigationTab = OfficeNavigationTab.Results;
    }

    /// <summary>A title bar search that matched no command searches the document instead.</summary>
    Task OnCommandSearchSubmittedAsync(string query) => this.OpenSearchAsync(query);

    async Task GoToCommentAsync(DocumentComment comment)
    {
        if (this.C?.GoToComment(comment.Id) == true && this.editor is { } surface)
            await surface.FocusAsync();
    }

    async Task DeleteCommentAsync(DocumentComment comment)
    {
        if (this.C is not { } controller || this.Disabled)
            return;

        controller.DeleteComment(comment.Id);
        await this.RaiseChangedAsync();
    }

    // ---- title bar, ribbon actions ----

    async Task OnDocumentNameChangedAsync(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name == this.EffectiveDocumentName)
            return;

        this.nameOverride = name;
        await this.DocumentNameChanged.InvokeAsync(name);
        await this.DocumentRenamed.InvokeAsync(name);
    }

    async Task OnAutoSaveChangedAsync(bool value)
    {
        this.autoSaveOverride = value;
        await this.AutoSaveChanged.InvokeAsync(value);
    }

    async Task SetEditModeAsync(OfficeEditMode mode)
    {
        var previous = this.EditModeValue;
        this.editModeOverride = mode;

        // Word's Reviewing is Editing with Track Changes on; leaving it turns tracking back off.
        if (this.C is { } controller)
        {
            if (mode == OfficeEditMode.Reviewing)
                controller.IsTrackingChanges = true;
            else if (previous == OfficeEditMode.Reviewing)
                controller.IsTrackingChanges = false;
        }

        await this.EditModeChanged.InvokeAsync(mode);
        this.StateHasChanged();
    }

    Task OnShareAsync() => this.ShareRequested.InvokeAsync();

    async Task OnFileButtonAsync()
    {
        if (this.ShellOn)
            await this.OpenBackstageAsync();

        await this.FileClicked.InvokeAsync();
    }

    async Task OnStyleSelected(OfficeStyleDescriptor style) => await this.Run(c => c.ApplyStyle(style.Id));

    // ---- status bar ----

    async Task OnStatusItemClickedAsync(OfficeStatusItem item)
    {
        if (item == this.wordsItem)
            this.Open(DialogKind.WordCount);
        else if (item == this.pageItem)
            await this.SetNavigationOpenAsync(true);

        this.StateHasChanged();
    }

    async Task OnViewModeSelectedAsync(string? id)
    {
        var (read, layout) = WordShell.FromViewModeId(id, this.LayoutMode);

        if (read)
        {
            if (!this.ReadModeOn)
                await this.SetReadMode(true);

            return;
        }

        await this.SetLayout(layout);
    }

    // ---- ruler ----

    async Task OnRulerIndentsAsync(OfficeIndents indents)
    {
        if (!this.Disabled && WordShell.ApplyIndents(this.C, indents))
            await this.RaiseChangedAsync();
    }

    async Task OnRulerTabStopsAsync(IReadOnlyList<OfficeTabStop> stops)
    {
        if (!this.Disabled && WordShell.ApplyTabStops(this.C, stops))
            await this.RaiseChangedAsync();
    }

    async Task OnRulerLeftMarginAsync(double left)
    {
        if (this.C is { } c && !this.Disabled && WordShell.ApplyMargins(c, left, WordShell.ToPoints(c.PageMargins.Right)))
            await this.RaiseChangedAsync();
    }

    async Task OnRulerRightMarginAsync(double right)
    {
        if (this.C is { } c && !this.Disabled && WordShell.ApplyMargins(c, WordShell.ToPoints(c.PageMargins.Left), right))
            await this.RaiseChangedAsync();
    }

    // ---- backstage ----

    /// <summary>Opens the File backstage, refreshing its Info page.</summary>
    public Task OpenBackstageAsync()
    {
        this.backstageInfo = WordShell.DocumentInfo(this.C, this.EffectiveDocumentName, this.DocumentLocation);
        this.backstageOpen = true;
        this.StateHasChanged();
        return Task.CompletedTask;
    }

    Task OnBackstageOpenChanged(bool open)
    {
        this.backstageOpen = open;

        if (!open && this.editor is { } surface)
            return surface.FocusAsync();

        return Task.CompletedTask;
    }

    async Task OnBackstagePageChangedAsync(OfficeBackstagePage page)
    {
        this.backstagePage = page;

        if (page == OfficeBackstagePage.Info)
            this.backstageInfo = WordShell.DocumentInfo(this.C, this.EffectiveDocumentName, this.DocumentLocation);

        if (page == OfficeBackstagePage.Print && this.CurrentDocument is { } document)
        {
            // Rendered once per visit: laying out the whole document for a preview is not free.
            await Task.Yield();
            try
            {
                var png = DocumentPdfExporter.RenderPagePng(document, 0, 0.75f);
                this.printPreview = "data:image/png;base64," + Convert.ToBase64String(png);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Shiny.Office] Print preview failed: {ex.Message}");
                this.printPreview = null;
            }

            this.StateHasChanged();
        }
    }

    async Task NewFromTemplateAsync(OfficeTemplate template)
    {
        if (this.NewDocumentRequested.HasDelegate)
        {
            await this.NewDocumentRequested.InvokeAsync(template);
            return;
        }

        var opened = await WordTemplates.OpenAsync(template);
        var previous = this.documentOverride;
        this.documentOverride = opened;
        previous?.Dispose();

        this.nameOverride = template.IsBlank ? "Document" + (++untitled + 1) : template.Name;
        this.backstageOpen = false;
        this.StateHasChanged();

        await this.DocumentOpened.InvokeAsync(opened);
    }

    int untitled;

    /// <summary>Save: the host's, or a .docx download.</summary>
    public async Task SaveAsync()
    {
        if (this.SaveRequested.HasDelegate)
        {
            await this.SaveRequested.InvokeAsync();
            return;
        }

        await this.DownloadAsync(OfficeFileFormats.Docx);
    }

    async Task SaveAsAsync(OfficeFileFormat format)
    {
        if (this.SaveAsRequested.HasDelegate)
            await this.SaveAsRequested.InvokeAsync(format);
        else
            await this.DownloadAsync(format);
    }

    /// <summary>Export: the host's, or a download of the chosen format.</summary>
    public async Task ExportAsync(OfficeFileFormat format)
    {
        if (this.ExportRequested.HasDelegate)
            await this.ExportRequested.InvokeAsync(format);
        else
            await this.DownloadAsync(format);
    }

    /// <summary>
    /// Writes the document as a PDF — one page per printed page, drawn by the same painter as the
    /// screen. Returns the page count.
    /// </summary>
    public int ExportPdf(Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var document = this.CurrentDocument ?? throw new InvalidOperationException("There is no document to export.");

        return DocumentPdfExporter.Export(document, output, new DocumentPdfOptions
        {
            Title = this.EffectiveDocumentName,
            Author = this.C?.Author
        });
    }

    /// <summary>Print: the host's, or the browser's print dialog over a PDF of the document.</summary>
    public async Task PrintAsync()
    {
        if (this.PrintRequested.HasDelegate)
        {
            await this.PrintRequested.InvokeAsync();
            return;
        }

        if (this.CurrentDocument is null)
            return;

        using var pdf = new MemoryStream();
        this.ExportPdf(pdf);
        pdf.Position = 0;

        var module = await this.ShellModuleAsync();
        if (module is null)
            return;

        using var stream = new DotNetStreamReference(pdf, leaveOpen: true);
        await module.InvokeVoidAsync("printPdf", stream);
    }

    /// <summary>Downloads the document in a format the view can write itself: .docx, .pdf, .txt or .html.</summary>
    public async Task DownloadAsync(OfficeFileFormat format)
    {
        if (this.CurrentDocument is not { } document)
            return;

        using var buffer = new MemoryStream();

        if (format.Id == OfficeFileFormats.Pdf.Id)
        {
            this.ExportPdf(buffer);
        }
        else if (format.Id == OfficeFileFormats.PlainText.Id)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(document.PlainText);
            buffer.Write(bytes);
        }
        else if (format.Id == OfficeFileFormats.Html.Id)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(WordShell.ToHtml(document, this.EffectiveDocumentName));
            buffer.Write(bytes);
        }
        else if (format.Id == OfficeFileFormats.Docx.Id)
        {
            var bytes = document.ToArray();
            buffer.Write(bytes);
        }
        else
        {
            Console.Error.WriteLine($"[Shiny.Office] No built-in writer for {format.Extension}; handle SaveAsRequested/ExportRequested to support it.");
            return;
        }

        buffer.Position = 0;

        var module = await this.ShellModuleAsync();
        if (module is null)
            return;

        using var stream = new DotNetStreamReference(buffer, leaveOpen: true);
        await module.InvokeVoidAsync("saveFile", format.FileNameFor(this.EffectiveDocumentName), format.MimeType, stream);
    }

    async Task<IJSObjectReference?> ShellModuleAsync()
    {
        try
        {
            return this.shellModule ??= await this.Js.InvokeAsync<IJSObjectReference>(
                "import", "./_content/Shiny.Blazor.Controls.Office/officeShell.js");
        }
        catch (JSException ex)
        {
            Console.Error.WriteLine($"[Shiny.Office] The shell script could not load: {ex.Message}");
            return null;
        }
    }

    // ---- layout ----

    Task OnShellLayoutChanged(OfficeShellLayout layout)
    {
        var mode = layout.SimplifiedRibbon ? RibbonDisplayMode.Simplified : RibbonDisplayMode.Expanded;

        // A collapsed ribbon stays collapsed; only the dense/full choice follows the width.
        if (this.ribbonMode != RibbonDisplayMode.Collapsed)
            this.ribbonMode = mode;

        return Task.CompletedTask;
    }
}
