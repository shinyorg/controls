using Microsoft.AspNetCore.Components;
using Shiny.Controls.Office.Shell;
using Shiny.Controls.Office.Skia;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Spreadsheet.View;

namespace Shiny.Blazor.Controls.Office;

/// <summary>
/// The Excel window around the grid: an <see cref="OfficeShell"/> with the title bar, the ribbon, the
/// comments pane, the status bar and the File backstage.
/// </summary>
/// <remarks>
/// On by default (<see cref="ShowShell"/>). Every part is a switch of its own, and what the backstage
/// asks for — save, save as, export, print — arrives at the host as a <see cref="SpreadsheetFileRequest"/>
/// through <see cref="FileRequested"/>. Left unhandled, the browser downloads the file (print opens the
/// sheet as a PDF), so the shell works end to end without any host code.
/// </remarks>
public partial class SpreadsheetView
{
    /// <summary>The zoom range the grid supports (10–400%), so the slider cannot ask for 500%.</summary>
    static readonly OfficeZoomModel ShellZoom = new(0.1, 4.0, 1.0);

    readonly OfficeCommandIndex commands = new();
    readonly OfficeStatusItem modeItem = new("mode", "Ready");
    readonly OfficeStatusItem aggregateItem = new("aggregates") { IsVisible = false };
    readonly List<OfficeStatusItem> statusItems;

    SpreadsheetToolbar? toolbar;
    IDisposable? commandSync;
    bool backstageOpen;
    bool commentsOpen;
    bool simplifiedRibbon;
    OfficeEditMode editMode;
    string? renamedTo;
    OfficeDocumentInfo? documentInfo;

    Workbook? ownedWorkbook;
    long savedRevision = -1;
    OfficeSaveState? transientSaveState;
    OfficeSaveState lastSaved = OfficeSaveState.None;
    CancellationTokenSource? autoSave;

    public SpreadsheetView()
    {
        this.statusItems = [this.modeItem, this.aggregateItem];
        this.AddShellCommands();
    }

    // ---- switches ----

    /// <summary>
    /// Wraps the sheet in Excel's window — title bar, ribbon with File, comments pane, status bar and
    /// backstage. On by default; false gives the bare ribbon + formula bar + grid + tabs of before.
    /// </summary>
    [Parameter] public bool ShowShell { get; set; } = true;

    /// <summary>The green title bar: AutoSave, Save/Undo/Redo, the workbook name and the command search.</summary>
    [Parameter] public bool ShowTitleBar { get; set; } = true;

    /// <summary>Ready/Edit, the selection's Average/Count/Sum, the three views and the zoom slider.</summary>
    [Parameter] public bool ShowStatusBar { get; set; } = true;

    /// <summary>Whether File opens the backstage. Off, File only raises <see cref="FileMenuRequested"/>.</summary>
    [Parameter] public bool ShowBackstage { get; set; } = true;

    /// <summary>The Comments button and the pane listing every note in the workbook.</summary>
    [Parameter] public bool ShowCommentsPane { get; set; } = true;

    /// <summary>Comments / Editing mode / Share at the end of the ribbon's tab strip.</summary>
    [Parameter] public bool ShowRibbonActions { get; set; } = true;

    // ---- document ----

    /// <summary>The name in the title bar and backstage. Defaults to the file's name, or "Book1". Two-way.</summary>
    [Parameter] public string? DocumentName { get; set; }

    [Parameter] public EventCallback<string?> DocumentNameChanged { get; set; }

    /// <summary>
    /// Overrides the title bar's save status. Left null the view tracks it itself: nothing for an untouched
    /// workbook, "Unsaved changes" after an edit, "Saving…", then "Saved" / "Saved locally".
    /// </summary>
    [Parameter] public OfficeSaveState? SaveState { get; set; }

    /// <summary>
    /// The title bar's AutoSave switch. On, and with <see cref="FileRequested"/> handled, edits are saved
    /// two seconds after the last one. (Unhandled, AutoSave does nothing: a download per edit is not a save.)
    /// </summary>
    [Parameter] public bool AutoSave { get; set; }

    [Parameter] public EventCallback<bool> AutoSaveChanged { get; set; }

    /// <summary>The account shown in the title bar's avatar.</summary>
    [Parameter] public string? UserName { get; set; }

    /// <summary>The backstage's New page. Defaults to <see cref="SpreadsheetTemplates.All"/> — blank, budget, invoice, schedule.</summary>
    [Parameter] public IReadOnlyList<OfficeTemplate>? Templates { get; set; }

    /// <summary>The backstage's recent files. The host's; none by default.</summary>
    [Parameter] public IReadOnlyList<OfficeRecentFile>? RecentFiles { get; set; }

    // ---- events ----

    /// <summary>
    /// A template was picked. Unhandled, the view builds it (<see cref="SpreadsheetTemplates.Create"/>, or
    /// the template's own <see cref="OfficeTemplate.Open"/> stream), shows it and raises <see cref="WorkbookChanged"/>.
    /// </summary>
    [Parameter] public EventCallback<OfficeTemplate> TemplateSelected { get; set; }

    /// <summary>The view replaced its workbook — a template from the backstage. Use <c>@bind-Workbook</c>.</summary>
    [Parameter] public EventCallback<Workbook?> WorkbookChanged { get; set; }

    /// <summary>Backstage ▸ Open. The host shows its own picker.</summary>
    [Parameter] public EventCallback OpenRequested { get; set; }

    [Parameter] public EventCallback<OfficeRecentFile> RecentFileSelected { get; set; }

    /// <summary>The Share button beside the comments toggle.</summary>
    [Parameter] public EventCallback ShareRequested { get; set; }

    /// <summary>
    /// Save, Save As, Export or Print. Handle it to put the file somewhere; unhandled, the browser
    /// downloads it (Print opens the PDF in a new tab).
    /// </summary>
    [Parameter] public EventCallback<SpreadsheetFileRequest> FileRequested { get; set; }

    /// <summary>The command search behind the title bar — add the app's own commands to it.</summary>
    public OfficeCommandIndex Commands => this.commands;

    /// <summary>Whether the backstage is showing.</summary>
    public bool IsBackstageOpen => this.backstageOpen;

    /// <summary>Viewing mode (from the ribbon's mode menu) turns the workbook read-only.</summary>
    bool EffectiveReadOnly => this.ReadOnly || this.editMode == OfficeEditMode.Viewing;

    string EffectiveDocumentName
        => this.renamedTo
           ?? this.DocumentName
           ?? (this.Workbook?.Path is { } path ? System.IO.Path.GetFileNameWithoutExtension(path) : null)
           ?? OfficeAppInfo.Excel.DefaultDocumentName;

    OfficeSaveState EffectiveSaveState
    {
        get
        {
            if (this.SaveState is { } pinned)
                return pinned;

            if (this.transientSaveState is { } transient)
                return transient;

            if (this.Workbook is not { } book)
                return OfficeSaveState.None;

            return book.Revision != this.savedRevision ? OfficeSaveState.Unsaved : this.lastSaved;
        }
    }


    /// <summary>Opens the File backstage, as the ribbon's File button does.</summary>
    public Task OpenBackstageAsync() => this.OnBackstageOpenChangedAsync(true);


    // ---- shell state ----

    /// <summary>Called when a controller is built for a workbook: that is the "saved" baseline.</summary>
    void OnWorkbookAttached(Workbook workbook)
    {
        this.savedRevision = workbook.Revision;
        this.transientSaveState = null;
        this.lastSaved = OfficeSaveState.None;

        // A note says who wrote it; the signed-in name beats the machine's user name.
        if (!string.IsNullOrWhiteSpace(this.UserName) && this.controller is { } c)
            c.NoteAuthor = this.UserName;

        this.UpdateStatus();
    }

    void UpdateStatus()
    {
        var controller = this.controller;
        this.modeItem.Text = SpreadsheetShell.ModeText(controller?.EditMode ?? SheetEditMode.Ready);

        var text = controller is null ? null : SpreadsheetShell.Aggregates(controller.SelectionStatistics);
        this.aggregateItem.Text = text;
        this.aggregateItem.IsVisible = text is not null;
    }

    async Task OnBackstageOpenChangedAsync(bool open)
    {
        if (open && this.Workbook is { } book)
            this.documentInfo = SpreadsheetShell.DocumentInfo(book, this.EffectiveDocumentName);

        this.backstageOpen = open;
        this.StateHasChanged();

        if (!open)
            await this.FocusGridAsync();
    }

    async Task OnFileMenuAsync()
    {
        if (this.ShowBackstage)
            await this.OnBackstageOpenChangedAsync(true);

        await this.FileMenuRequested.InvokeAsync();
    }

    Task OnCommentsOpenChangedAsync(bool open)
    {
        this.commentsOpen = open;
        this.StateHasChanged();
        return Task.CompletedTask;
    }

    void OnShellLayoutChanged(OfficeShellLayout layout)
    {
        this.simplifiedRibbon = layout.SimplifiedRibbon;
        this.StateHasChanged();
    }

    void OnEditModeChanged(OfficeEditMode mode)
    {
        this.editMode = mode;
        this.StateHasChanged();
    }

    async Task OnDocumentNameChangedAsync(string? name)
    {
        this.renamedTo = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        await this.DocumentNameChanged.InvokeAsync(this.renamedTo);
    }

    Task OnAutoSaveChangedAsync(bool on)
    {
        this.AutoSave = on;
        return this.AutoSaveChanged.InvokeAsync(on);
    }

    void OnViewModeChanged(string? id)
    {
        if (this.controller is { } c)
            c.ViewMode = SpreadsheetShell.ParseViewMode(id);

        this.canvas?.Invalidate();
    }

    void OnStatusZoomChanged(double zoom)
    {
        // The controller clamps and raises ZoomChanged, which is what reports it to a @bind-Zoom host.
        if (this.controller is { } c)
            c.Zoom = zoom;
        else
            this.Zoom = zoom;
    }

    void UndoFromShell()
    {
        this.controller?.Undo();
        this.canvas?.Invalidate();
    }

    void RedoFromShell()
    {
        this.controller?.Redo();
        this.canvas?.Invalidate();
    }

    void OnSearchSubmitted(string text)
    {
        if (this.controller is { } c && SpreadsheetShell.Search(c, text))
            this.canvas?.Invalidate();
    }

    async Task GoToNote(SpreadsheetNoteEntry note)
    {
        if (this.controller is not { } c)
            return;

        SpreadsheetShell.GoTo(c, note);
        this.canvas?.Invalidate();
        await this.FocusGridAsync();
    }


    // ---- backstage: new ----

    async Task OnTemplateSelectedAsync(OfficeTemplate template)
    {
        if (this.TemplateSelected.HasDelegate)
        {
            await this.TemplateSelected.InvokeAsync(template);
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
        this.renamedTo = template.IsBlank ? NextBookName() : template.Name;
        this.Workbook = next;
        this.SheetName = null;

        this.OnParametersSet();
        this.canvas?.Invalidate();
        this.StateHasChanged();

        await this.WorkbookChanged.InvokeAsync(next);
        await this.DocumentNameChanged.InvokeAsync(this.renamedTo);

        // Only a workbook this view built is its to dispose; the host's are the host's.
        if (previous is not null && !ReferenceEquals(previous, next))
            previous.Dispose();
    }

    static int bookNumber = 1;

    static string NextBookName() => $"Book{Interlocked.Increment(ref bookNumber)}";


    // ---- backstage: save, save as, export, print ----

    Task SaveAsync() => this.WriteFileAsync(OfficeFileFormats.Xlsx, SpreadsheetFileAction.Save);

    Task PrintAsync() => this.WriteFileAsync(OfficeFileFormats.Pdf, SpreadsheetFileAction.Print);

    async Task WriteFileAsync(OfficeFileFormat format, SpreadsheetFileAction action)
    {
        if (this.controller is not { } c)
            return;

        var request = new SpreadsheetFileRequest(c.Workbook, c.Sheet, format, format.FileNameFor(this.EffectiveDocumentName), action);
        var revision = c.Workbook.Revision;
        var saving = action is SpreadsheetFileAction.Save or SpreadsheetFileAction.SaveAs && format.IsNative;

        if (saving)
        {
            this.transientSaveState = OfficeSaveState.Saving;
            this.StateHasChanged();
        }

        try
        {
            if (this.FileRequested.HasDelegate)
            {
                await this.FileRequested.InvokeAsync(request);
            }
            else if (SpreadsheetExport.CanWrite(format) && this.script is { } s)
            {
                var bytes = await request.ToBytesAsync();
                await s.InvokeAsync(action == SpreadsheetFileAction.Print ? "openFile" : "downloadFile", request.FileName, format.MimeType, bytes);
            }

            if (saving)
            {
                this.savedRevision = revision;
                this.lastSaved = this.FileRequested.HasDelegate ? OfficeSaveState.Saved : OfficeSaveState.SavedLocally;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Shiny.Office] {action} failed: {ex.Message}");
            if (saving)
                this.lastSaved = OfficeSaveState.Error;
        }
        finally
        {
            if (saving)
                this.transientSaveState = null;

            this.StateHasChanged();
        }
    }

    /// <summary>AutoSave: two seconds after the last edit, when the host is there to take the file.</summary>
    void ScheduleAutoSave()
    {
        if (!this.AutoSave || !this.FileRequested.HasDelegate || this.Workbook is not { } book || book.Revision == this.savedRevision)
            return;

        this.autoSave?.Cancel();
        var cts = this.autoSave = new CancellationTokenSource();

        _ = this.InvokeAsync(async () =>
        {
            try
            {
                await Task.Delay(2000, cts.Token);
                await this.SaveAsync();
            }
            catch (TaskCanceledException)
            {
            }
        });
    }


    // ---- command search ----

    void AddShellCommands()
    {
        this.commands.Add(new OfficeCommand("Save", this.SaveAsync) { Id = "sheet:save", Category = "File", Shortcut = "Ctrl+S" });
        this.commands.Add(new OfficeCommand("New Workbook", () => this.OnBackstageOpenChangedAsync(true)) { Id = "sheet:new", Category = "File", Keywords = ["template", "blank"] });
        this.commands.Add(new OfficeCommand("Export to PDF", () => this.WriteFileAsync(OfficeFileFormats.Pdf, SpreadsheetFileAction.Export)) { Id = "sheet:pdf", Category = "File › Export" });
        this.commands.Add(new OfficeCommand("Export to CSV", () => this.WriteFileAsync(OfficeFileFormats.Csv, SpreadsheetFileAction.Export)) { Id = "sheet:csv", Category = "File › Export", Keywords = ["comma"] });
        this.commands.Add(new OfficeCommand("Print", this.PrintAsync) { Id = "sheet:print", Category = "File", Shortcut = "Ctrl+P" });
        this.commands.Add(new OfficeCommand("Comments", () => this.OnCommentsOpenChangedAsync(!this.commentsOpen)) { Id = "sheet:comments", Category = "Review", Keywords = ["notes"] });
        this.commands.Add(new OfficeCommand("Normal View", () => this.OnViewModeChanged(OfficeViewModes.Normal.Id)) { Id = "sheet:normal", Category = "View" });
        this.commands.Add(new OfficeCommand("Page Layout", () => this.OnViewModeChanged(OfficeViewModes.PageLayout.Id)) { Id = "sheet:pagelayout", Category = "View", Keywords = ["print", "pages"] });
        this.commands.Add(new OfficeCommand("Page Break Preview", () => this.OnViewModeChanged(OfficeViewModes.PageBreak.Id)) { Id = "sheet:pagebreak", Category = "View", Keywords = ["print", "pages"] });
    }

    /// <summary>Indexes the ribbon once it exists — its tabs add their commands as they render.</summary>
    void SyncCommands()
    {
        if (this.commandSync is null && this.toolbar?.Ribbon is { } ribbon)
            this.commandSync = this.commands.SyncRibbon(ribbon);
    }

    void DisposeShell()
    {
        this.commandSync?.Dispose();
        this.autoSave?.Cancel();

        // A host bound to WorkbookChanged was handed the workbook and may outlive this view with it.
        if (!this.WorkbookChanged.HasDelegate)
            this.ownedWorkbook?.Dispose();
    }
}
