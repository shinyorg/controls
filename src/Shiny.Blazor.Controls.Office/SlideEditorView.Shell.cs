using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Shiny.Blazor.Controls;
using Shiny.Controls.Office.Icons;
using Shiny.Controls.Office.Presentation;
using Shiny.Controls.Office.Shell;
using Shiny.Controls.Office.Skia;
using Shiny.Controls.Office.Theming;

namespace Shiny.Blazor.Controls.Office;

/// <summary>
/// The PowerPoint window around the editor: the title bar, the ribbon's Editing/Viewing mode and Share,
/// the status bar (slide counter, language, Notes, the three views, zoom and fit) and the File
/// backstage — the <see cref="OfficeShell"/> parts wired to the controller through <see cref="SlideShell"/>.
/// </summary>
/// <remarks>
/// <para>
/// On by default (<see cref="ShowShell"/>); every part has its own switch. With the shell off the view is
/// what it was before: the ribbon (File only when <see cref="FileMenuRequested"/> is handled), the
/// slide and the plain status line.
/// </para>
/// <para>
/// The shell does no I/O of its own: Save, Save As, Export and Print arrive as a
/// <see cref="SlideFileRequest"/> through <see cref="FileRequested"/>. Left unhandled, the browser
/// downloads the file — the .pptx, a PDF, a PNG of the slide or a .zip of every slide — and Print opens
/// a PDF of the deck in the browser's print dialog, so the window works end to end with no host code.
/// </para>
/// </remarks>
public partial class SlideEditorView : IDisposable
{
    [Inject] IJSRuntime Js { get; set; } = default!;

    // ---- switches ----

    /// <summary>
    /// Dress the editor in PowerPoint's window — title bar, File backstage, Editing/Viewing and Share on
    /// the ribbon, and the Office status bar. On by default; off gives the ribbon over the slide of before.
    /// </summary>
    [Parameter] public bool ShowShell { get; set; } = true;

    /// <summary>The red title bar: AutoSave, Save / Undo / Redo / From Beginning, the name, command search and account.</summary>
    [Parameter] public bool ShowTitleBar { get; set; } = true;

    /// <summary>The Office status bar: "Slide 3 of 12", language, Notes, the views, zoom and fit. <see cref="ShowStatus"/> off hides it too.</summary>
    [Parameter] public bool ShowStatusBar { get; set; } = true;

    /// <summary>Whether File opens the backstage. Off, File only raises <see cref="FileMenuRequested"/>.</summary>
    [Parameter] public bool ShowBackstage { get; set; } = true;

    /// <summary>Editing / Viewing mode and Share at the end of the ribbon's tab strip.</summary>
    [Parameter] public bool ShowRibbonActions { get; set; } = true;

    // ---- document ----

    /// <summary>The name in the title bar and backstage. Defaults to the file's name, or "Presentation1". Two-way — the title bar renames.</summary>
    [Parameter] public string? DocumentName { get; set; }

    [Parameter] public EventCallback<string?> DocumentNameChanged { get; set; }

    /// <summary>Where the deck lives, shown under the name's dropdown. Defaults to the deck's path.</summary>
    [Parameter] public string? DocumentLocation { get; set; }

    /// <summary>
    /// Overrides the title bar's save status. Left null the view tracks it: nothing for an untouched deck,
    /// "Unsaved changes" after an edit, "Saving…", then "Saved" / "Saved locally".
    /// </summary>
    [Parameter] public OfficeSaveState? SaveState { get; set; }

    /// <summary>
    /// The title bar's AutoSave switch. On, and with <see cref="FileRequested"/> handled, edits are saved two
    /// seconds after the last one. (Unhandled, AutoSave does nothing: a download per edit is not a save.)
    /// </summary>
    [Parameter] public bool AutoSave { get; set; }

    [Parameter] public EventCallback<bool> AutoSaveChanged { get; set; }

    /// <summary>The account shown at the title bar's right end, and the author on the backstage's Info page.</summary>
    [Parameter] public string? UserName { get; set; }

    /// <summary>The backstage's New page. Null offers <see cref="SlideTemplates.All"/> — Blank, Project update, Pitch deck, Lesson.</summary>
    [Parameter] public IReadOnlyList<OfficeTemplate>? Templates { get; set; }

    /// <summary>The backstage's recent files. The host keeps the list; picking one raises <see cref="RecentFileSelected"/>.</summary>
    [Parameter] public IReadOnlyList<OfficeRecentFile>? RecentFiles { get; set; }

    /// <summary>Editing or Viewing (read-only) — the ribbon's mode dropdown. Reviewing edits as Editing does. Two-way.</summary>
    [Parameter] public OfficeEditMode EditMode { get; set; }

    [Parameter] public EventCallback<OfficeEditMode> EditModeChanged { get; set; }

    // ---- events ----

    /// <summary>
    /// A template was picked. Unhandled, the view opens it itself (<see cref="SlideTemplates.OpenAsync"/>),
    /// shows it and raises <see cref="DeckReplaced"/>.
    /// </summary>
    [Parameter] public EventCallback<OfficeTemplate> TemplateSelected { get; set; }

    /// <summary>The view swapped in a deck of its own — a template from the backstage. The host owns it from then on.</summary>
    [Parameter] public EventCallback<SlideDeck> DeckReplaced { get; set; }

    /// <summary>Backstage ▸ Open. The host shows its own picker.</summary>
    [Parameter] public EventCallback OpenRequested { get; set; }

    [Parameter] public EventCallback<OfficeRecentFile> RecentFileSelected { get; set; }

    /// <summary>The ribbon's Share button.</summary>
    [Parameter] public EventCallback ShareRequested { get; set; }

    /// <summary>
    /// Save, Save As, Export or Print. Handle it to put the file somewhere; unhandled, the browser
    /// downloads it (Print opens the PDF in the browser's print dialog).
    /// </summary>
    [Parameter] public EventCallback<SlideFileRequest> FileRequested { get; set; }

    /// <summary>The title bar's command search. Filled from the ribbon as its tabs render, plus PowerPoint's own commands up front.</summary>
    public OfficeCommandIndex Commands => this.commands;

    /// <summary>Whether the backstage is showing.</summary>
    public bool IsBackstageOpen => this.backstageOpen;

    // ---- state ----

    static readonly OfficeZoomModel StatusZoomModel = new(0.1, 4.0, 1.0);

    readonly OfficeCommandIndex commands = new();
    readonly OfficeStatusItem slideItem = new("slide", "Slide 1 of 1") { Tooltip = "The slide being edited" };
    readonly OfficeStatusItem languageItem = new("language", WordShell.LanguageText()) { Tooltip = "Proofing language" };
    readonly OfficeStatusItem notesItem = new("notes", "Notes") { IsClickable = true, Tooltip = "Show the speaker notes under the slide" };
    readonly List<OfficeStatusItem> statusItems;
    readonly List<OfficeQuickAccessItem> quickAccess;
    readonly List<OfficeCommand> curated = [];

    Ribbon? ribbonRef;
    Ribbon? syncedRibbon;
    IDisposable? ribbonSync;
    RibbonDisplayMode ribbonMode = RibbonDisplayMode.Expanded;

    bool backstageOpen;
    OfficeBackstagePage backstagePage = OfficeBackstagePage.Home;
    OfficeDocumentInfo? backstageInfo;
    string? printPreview;
    string? nameOverride;
    bool? autoSaveOverride;
    OfficeEditMode? editModeOverride;

    SlideDeck? ownedDeck;
    SlideDeck? lastDeckParameter;
    SlideDeck? trackedDeck;
    long revision;
    long savedRevision;
    OfficeSaveState lastSaved = OfficeSaveState.None;
    OfficeSaveState? transientSaveState;
    CancellationTokenSource? autoSaveTimer;
    IJSObjectReference? shellModule;

    /// <summary>The built-in templates with a picture of each one's first slide, drawn once per app.</summary>
    static IReadOnlyList<OfficeTemplate>? builtInTemplates;

    public SlideEditorView()
    {
        this.statusItems = [this.slideItem, this.languageItem, this.notesItem];
        this.quickAccess =
        [
            new OfficeQuickAccessItem("slides:from-beginning", "Start From Beginning", OfficeShellIcon.SlideShow, () => this.InvokeAsync(() => this.StartPresentingAsync(0)))
            {
                Shortcut = "F5"
            }
        ];

        this.AddShellCommands();
    }

    // ---- derived ----

    bool ShellOn => this.ShowShell;

    OfficeEditMode EditModeValue => this.editModeOverride ?? this.EditMode;

    /// <summary>Viewing mode, from the ribbon's dropdown, makes the deck read-only.</summary>
    bool EffectiveReadOnly => this.ReadOnly || (this.ShellOn && this.EditModeValue == OfficeEditMode.Viewing);

    string EffectiveDocumentName
        => this.nameOverride
           ?? this.DocumentName
           ?? (this.Deck?.Path is { } path ? System.IO.Path.GetFileNameWithoutExtension(path) : null)
           ?? OfficeAppInfo.PowerPoint.DefaultDocumentName;

    OfficeSaveState EffectiveSaveState
    {
        get
        {
            if (this.SaveState is { } pinned)
                return pinned;

            if (this.transientSaveState is { } transient)
                return transient;

            if (this.Deck is null)
                return OfficeSaveState.None;

            return this.revision != this.savedRevision ? OfficeSaveState.Unsaved : this.lastSaved;
        }
    }

    bool EffectiveAutoSave => this.autoSaveOverride ?? this.AutoSave;

    /// <summary>A custom accent overrides the shell's PowerPoint red; the default leaves it alone.</summary>
    string? ShellAccent => this.Accent is { } a && a != OfficeAccent.Presentation ? Css(a.Color) : null;

    // ---- the deck the view shows ----

    /// <summary>
    /// A deck the view opened itself (a template) stays on screen until the host hands in a different
    /// one: a re-render that passes the old parameter back must not undo the swap.
    /// </summary>
    void ApplyDeckParameter()
    {
        if (!ReferenceEquals(this.Deck, this.lastDeckParameter))
        {
            this.lastDeckParameter = this.Deck;

            if (this.ownedDeck is { } mine && !ReferenceEquals(mine, this.Deck))
            {
                this.ownedDeck = null;
                if (!this.DeckReplaced.HasDelegate)
                    mine.Dispose();
            }
        }
        else if (this.ownedDeck is { } mine)
        {
            this.Deck = mine;
        }

        this.TrackDeck(this.Deck);
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
        this.transientSaveState = null;

        if (deck is not null)
            deck.ContentChanged += this.OnDeckContentChanged;
    }

    void OnDeckContentChanged(object? sender, EventArgs e)
    {
        this.revision++;
        this.ScheduleAutoSave();
    }

    // ---- lifecycle hooks the main file calls ----

    void AfterShellRender()
    {
        if (!this.ShellOn)
            return;

        // The Blazor ribbon only indexes the tabs it has rendered, so the search follows whichever
        // ribbon instance is on screen and the curated list steps aside as tabs are visited.
        if (!ReferenceEquals(this.ribbonRef, this.syncedRibbon))
        {
            this.ribbonSync?.Dispose();
            this.ribbonSync = this.ribbonRef is { } ribbon ? this.commands.SyncRibbon(ribbon) : null;
            this.syncedRibbon = this.ribbonRef;
        }

        this.PruneCurated();
        this.UpdateShellStatus();
    }

    void UpdateShellStatus()
    {
        this.slideItem.Text = SlideShell.SlideText(this.CurrentSlideIndex, this.SlideCount, this.ViewMode);
        this.notesItem.Tooltip = this.ShowNotes ? "Hide the speaker notes" : "Show the speaker notes under the slide";
        this.notesItem.IsClickable = this.Deck is not null;
    }

    public void Dispose()
    {
        this.ribbonSync?.Dispose();
        this.ribbonSync = null;
        this.autoSaveTimer?.Cancel();

        if (this.trackedDeck is { } deck)
            deck.ContentChanged -= this.OnDeckContentChanged;

        // A host handed the deck through DeckReplaced may outlive this view with it.
        if (!this.DeckReplaced.HasDelegate)
            this.ownedDeck?.Dispose();

        if (this.shellModule is { } module)
            _ = DisposeModuleAsync(module);

        static async Task DisposeModuleAsync(IJSObjectReference module)
        {
            try { await module.DisposeAsync(); }
            catch (JSDisconnectedException) { }
            catch (ObjectDisposedException) { }
        }
    }

    // ---- title bar, ribbon actions ----

    async Task OnDocumentNameChangedAsync(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name == this.EffectiveDocumentName)
            return;

        this.nameOverride = name.Trim();
        await this.DocumentNameChanged.InvokeAsync(this.nameOverride);
    }

    async Task OnAutoSaveChangedAsync(bool value)
    {
        this.autoSaveOverride = value;
        await this.AutoSaveChanged.InvokeAsync(value);
        this.ScheduleAutoSave();
    }

    async Task SetEditModeAsync(OfficeEditMode mode)
    {
        this.editModeOverride = mode;

        // Viewing: nothing selected, nothing half-typed - the deck is only looked at.
        if (mode == OfficeEditMode.Viewing)
            this.Controller?.ClearSelection();

        this.editor?.Invalidate();
        await this.EditModeChanged.InvokeAsync(mode);
        this.StateHasChanged();
    }

    Task OnShareAsync() => this.ShareRequested.InvokeAsync();

    /// <summary>A title bar search that matched no command finds the text across the slides.</summary>
    async Task OnCommandSearchSubmittedAsync(string query)
    {
        if (this.Controller is not { } controller)
            return;

        if (!SlideShell.Search(controller, query))
        {
            // Nothing found: the Home tab's Find box shows the query and its "No results".
            this.selectedTab = "home";
            this.StateHasChanged();
            return;
        }

        // The hit is selected on its slide; the keyboard goes with it, as PowerPoint's search does.
        if (this.editor is { } surface)
        {
            surface.Invalidate();
            await surface.FocusAsync();
        }

        await this.OnEditorChanged();
    }

    // ---- status bar ----

    async Task OnStatusItemClickedAsync(OfficeStatusItem item)
    {
        if (item == this.notesItem)
            await this.ToggleNotesAsync();

        this.StateHasChanged();
    }

    async Task OnViewModeSelectedAsync(string? id)
    {
        if (SlideShell.ParseViewMode(id) is { } mode)
        {
            await this.SetViewModeAsync(mode);
            return;
        }

        // Reading View: the deck plays from the slide being edited.
        await this.StartPresentingAsync();
    }

    Task OnShellLayoutChanged(OfficeShellLayout layout)
    {
        var mode = layout.SimplifiedRibbon ? RibbonDisplayMode.Simplified : RibbonDisplayMode.Expanded;

        // A collapsed ribbon stays collapsed; only the dense/full choice follows the width.
        if (this.ribbonMode != RibbonDisplayMode.Collapsed)
            this.ribbonMode = mode;

        return Task.CompletedTask;
    }

    // ---- backstage ----

    /// <summary>Opens the File backstage, refreshing its Info page.</summary>
    public Task OpenBackstageAsync(OfficeBackstagePage? page = null)
    {
        this.backstageInfo = this.Deck is { } deck ? SlideShell.DocumentInfo(deck, this.EffectiveDocumentName, this.UserName) : null;
        this.EnsureTemplateThumbnails();

        if (page is { } p)
            this.backstagePage = p;

        this.backstageOpen = true;
        this.StateHasChanged();
        return Task.CompletedTask;
    }

    async Task OnFileClicked()
    {
        if (this.ShellOn && this.ShowBackstage)
            await this.OpenBackstageAsync();

        if (this.FileMenuRequested.HasDelegate)
            await this.FileMenuRequested.InvokeAsync();
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

        if (page == OfficeBackstagePage.Info && this.Deck is { } deck)
            this.backstageInfo = SlideShell.DocumentInfo(deck, this.EffectiveDocumentName, this.UserName);

        if (page == OfficeBackstagePage.Print && this.Deck is { Slides.Count: > 0 } printed)
        {
            // Rendered once per visit: the slide being edited, the way PowerPoint's preview opens on it.
            await Task.Yield();
            try
            {
                var png = SlideExporter.ToPng(printed, Math.Clamp(this.CurrentSlideIndex, 0, printed.Slides.Count - 1), 960, this.Watermark);
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

    /// <summary>
    /// Gives the built-in templates a picture of their title slide, as PowerPoint's New page has. Drawn
    /// once per app, on the first visit to the backstage — never at start-up.
    /// </summary>
    void EnsureTemplateThumbnails()
    {
        if (this.Templates is not null || builtInTemplates is not null)
            return;

        try
        {
            var list = new List<OfficeTemplate>();
            foreach (var template in SlideTemplates.All)
            {
                if (template.IsBlank)
                {
                    list.Add(template);
                    continue;
                }

                using var stream = SlideTemplates.Create(template.Id);
                using var deck = SlideDeck.OpenAsync(stream).GetAwaiter().GetResult();
                var png = SlideExporter.ToPng(deck, 0, 320);

                list.Add(new OfficeTemplate(template.Id, template.Name)
                {
                    Description = template.Description,
                    Category = template.Category,
                    Open = template.Open,
                    Tag = template.Tag,
                    Thumbnail = "data:image/png;base64," + Convert.ToBase64String(png)
                });
            }

            builtInTemplates = list;
        }
        catch (Exception ex)
        {
            // A picture is a nicety; the named tiles still work without one.
            Console.Error.WriteLine($"[Shiny.Office] Template thumbnails failed: {ex.Message}");
            builtInTemplates = SlideTemplates.All;
        }
    }

    async Task NewFromTemplateAsync(OfficeTemplate template)
    {
        if (this.TemplateSelected.HasDelegate)
        {
            await this.TemplateSelected.InvokeAsync(template);
            return;
        }

        SlideDeck next;
        try
        {
            next = await SlideTemplates.OpenAsync(template);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Shiny.Office] Could not open template '{template.Id}': {ex.Message}");
            return;
        }

        var previous = this.ownedDeck;
        this.ownedDeck = next;
        this.Deck = next;
        this.SlideIndex = 0;
        this.nameOverride = template.IsBlank ? "Presentation" + (++untitled + 1) : template.Name;
        this.backstageOpen = false;
        this.TrackDeck(next);
        this.StateHasChanged();

        await this.DeckReplaced.InvokeAsync(next);
        await this.DocumentNameChanged.InvokeAsync(this.nameOverride);
        await this.OnSlideIndexChanged(0);

        // Only a deck this view opened is its to dispose; the host's are the host's.
        if (previous is not null && !ReferenceEquals(previous, next) && !this.DeckReplaced.HasDelegate)
            previous.Dispose();
    }

    static int untitled;

    // ---- save, save as, export, print ----

    /// <summary>Save: the host's (<see cref="FileRequested"/>), or a .pptx download.</summary>
    public Task SaveAsync() => this.WriteFileAsync(OfficeFileFormats.Pptx, SlideFileAction.Save);

    /// <summary>Export in a format — the host's, or a download of it.</summary>
    public Task ExportAsync(OfficeFileFormat format) => this.WriteFileAsync(format, SlideFileAction.Export);

    /// <summary>Print: the host's, or the browser's print dialog over a PDF of the deck.</summary>
    public Task PrintAsync() => this.WriteFileAsync(OfficeFileFormats.Pdf, SlideFileAction.Print);

    List<RibbonMenuEntry> ExportPictureMenu =>
    [
        new RibbonMenuEntry { Text = "This slide as PNG", OnClick = EventCallback.Factory.Create(this, () => this.ExportAsync(OfficeFileFormats.Png)) },
        new RibbonMenuEntry { Text = "This slide as JPEG", OnClick = EventCallback.Factory.Create(this, () => this.ExportAsync(OfficeFileFormats.Jpeg)) },
        new RibbonMenuEntry { Text = "All slides as PNG (.zip)", OnClick = EventCallback.Factory.Create(this, () => this.ExportAsync(SlideExport.AllSlidesPng)) }
    ];

    async Task WriteFileAsync(OfficeFileFormat format, SlideFileAction action)
    {
        if (this.Deck is not { } deck)
            return;

        var request = new SlideFileRequest(deck, this.CurrentSlideIndex, format, format.FileNameFor(this.EffectiveDocumentName), action, this.Watermark);
        var saving = action is SlideFileAction.Save or SlideFileAction.SaveAs && format.IsNative;
        var at = this.revision;

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
            else if (SlideExport.CanWrite(format))
            {
                using var buffer = new MemoryStream();
                await request.WriteToAsync(buffer);
                buffer.Position = 0;

                if (await this.ShellModuleAsync() is { } module)
                {
                    using var stream = new DotNetStreamReference(buffer, leaveOpen: true);
                    if (action == SlideFileAction.Print)
                        await module.InvokeVoidAsync("printPdf", stream);
                    else
                        await module.InvokeVoidAsync("saveFile", request.FileName, format.MimeType, stream);
                }
            }
            else
            {
                Console.Error.WriteLine($"[Shiny.Office] No built-in writer for {format.Extension}; handle FileRequested to support it.");
            }

            if (saving)
            {
                this.savedRevision = at;
                this.lastSaved = this.FileRequested.HasDelegate ? OfficeSaveState.Saved : OfficeSaveState.SavedLocally;
            }

            if (action != SlideFileAction.Print)
                this.backstageOpen = false;
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
        if (!this.EffectiveAutoSave || !this.FileRequested.HasDelegate || this.Deck is null || this.revision == this.savedRevision)
            return;

        this.autoSaveTimer?.Cancel();
        var cts = this.autoSaveTimer = new CancellationTokenSource();

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

    // ---- command search ----

    void AddShellCommands()
    {
        this.commands.Add(new OfficeCommand("Save", () => this.InvokeAsync(this.SaveAsync)) { Id = "slides:save", Category = "File", Shortcut = "Ctrl+S", Keywords = ["store", "download", "pptx"] });
        this.commands.Add(new OfficeCommand("New Presentation", () => this.InvokeAsync(() => this.OpenBackstageAsync(OfficeBackstagePage.New))) { Id = "slides:new", Category = "File", Keywords = ["template", "blank", "deck"] });
        this.commands.Add(new OfficeCommand("Export to PDF", () => this.InvokeAsync(() => this.ExportAsync(OfficeFileFormats.Pdf))) { Id = "slides:pdf", Category = "File › Export", Keywords = ["pdf", "download"] });
        this.commands.Add(new OfficeCommand("Export Slide as PNG", () => this.InvokeAsync(() => this.ExportAsync(OfficeFileFormats.Png))) { Id = "slides:png", Category = "File › Export", Keywords = ["picture", "image", "png"] });
        this.commands.Add(new OfficeCommand("Export All Slides as PNG", () => this.InvokeAsync(() => this.ExportAsync(SlideExport.AllSlidesPng))) { Id = "slides:png-all", Category = "File › Export", Keywords = ["pictures", "images", "zip"] });
        this.commands.Add(new OfficeCommand("Print", () => this.InvokeAsync(this.PrintAsync)) { Id = "slides:print", Category = "File", Shortcut = "Ctrl+P" });
        this.commands.Add(new OfficeCommand("Reading View", () => this.InvokeAsync(() => this.StartPresentingAsync())) { Id = "slides:reading", Category = "View › Presentation Views", Keywords = ["read", "present"] });
        this.commands.Add(new OfficeCommand("Fit Slide to Window", () => this.InvokeAsync(() => this.SetZoomAsync(null))) { Id = "slides:fit", Category = "View › Zoom", Keywords = ["zoom", "fit"] });
        this.commands.Add(new OfficeCommand("Zoom 100%", () => this.InvokeAsync(() => this.SetZoomAsync(1.0))) { Id = "slides:zoom100", Category = "View › Zoom", Keywords = ["actual size", "reset zoom"] });
        this.commands.Add(new OfficeCommand("Find", () => this.InvokeAsync(() => this.OnTabChanged("home"))) { Id = "slides:find", Category = "Home › Editing", Shortcut = "Ctrl+F", Keywords = ["search"] });

        // The Blazor ribbon indexes a tab only once it has rendered, so the commands people look for on
        // the other tabs are listed up front. Each steps aside once the ribbon has indexed its own button
        // of the same name (PruneCurated), so a visited tab is not listed twice.
        this.Curate("New Slide", () => this.Run(c => c.NewSlide()), "Home › Slides", "Ctrl+M", ["add slide", "insert slide"]);
        this.Curate("Text Box", this.AddTextBox, "Insert › Text", keywords: ["insert text"]);
        this.Curate("Chart", () => this.Tab("insert"), "Insert › Illustrations", keywords: ["graph", "insert chart"], viewOnly: true);
        this.Curate("Shapes", () => this.Tab("insert"), "Insert › Illustrations", keywords: ["rectangle", "arrow", "insert shape"], viewOnly: true);
        this.Curate("Link", () => { this.OpenLinkDialog(); return Task.CompletedTask; }, "Insert › Links", "Ctrl+K", ["hyperlink", "url"]);
        this.Curate("Header & Footer", () => { this.OpenHeaderFooter(); return Task.CompletedTask; }, "Insert › Text", keywords: ["footer", "slide number", "date"]);
        this.Curate("Themes", () => this.Tab("design"), "Design › Themes", keywords: ["design", "colours", "colors", "theme"], viewOnly: true);
        this.Curate("Format Background", () => { this.OpenBackground(master: false); return Task.CompletedTask; }, "Design › Customize", keywords: ["background"]);
        this.Curate("Slide Size", () => { this.OpenSlideSize(); return Task.CompletedTask; }, "Design › Customize", keywords: ["widescreen", "4:3", "16:9"]);
        this.Curate("Transitions", () => this.Tab("transitions"), "Transitions", keywords: ["fade", "push", "morph"], viewOnly: true);
        this.Curate("Apply To All", () => this.Run(c => c.ApplyTransitionToAll()), "Transitions › Timing", keywords: ["transition all slides"]);
        this.Curate("Animations", () => this.Tab("animations"), "Animations", keywords: ["animate", "effect", "fly in"], viewOnly: true);
        this.Curate("Animation Pane", () => { this.showAnimationPane = !this.showAnimationPane; this.StateHasChanged(); return Task.CompletedTask; }, "Animations › Advanced Animation", viewOnly: true);
        this.Curate("From Beginning", () => this.StartPresentingAsync(0), "Slide Show › Start Slide Show", "F5", ["present", "slideshow", "play"], viewOnly: true);
        this.Curate("From Current Slide", () => this.StartPresentingAsync(), "Slide Show › Start Slide Show", "Shift+F5", ["present", "slideshow", "play"], viewOnly: true);
        this.Curate("Presenter View", () => this.StartPresentingAsync(null, presenterView: true), "Slide Show › Start Slide Show", keywords: ["speaker", "notes", "timer"], viewOnly: true);
        this.Curate("Hide Slide", () => this.Run(c => c.ToggleHideSlide()), "Slide Show › Set Up", keywords: ["skip"]);
        this.Curate("Normal", () => this.SetViewModeAsync(SlideEditorViewMode.Normal), "View › Presentation Views", keywords: ["normal view"], viewOnly: true);
        this.Curate("Outline View", () => this.SetViewModeAsync(SlideEditorViewMode.Outline), "View › Presentation Views", keywords: ["outline"], viewOnly: true);
        this.Curate("Slide Sorter", () => this.SetViewModeAsync(SlideEditorViewMode.SlideSorter), "View › Presentation Views", keywords: ["sorter", "thumbnails", "reorder"], viewOnly: true);
        this.Curate("Notes Page", () => this.SetViewModeAsync(SlideEditorViewMode.NotesPage), "View › Presentation Views", viewOnly: true);
        this.Curate("Slide Master", () => this.SetViewModeAsync(SlideEditorViewMode.SlideMaster), "View › Master Views", keywords: ["master", "layouts"], viewOnly: true);
        this.Curate("Notes", this.ToggleNotesAsync, "View › Show", keywords: ["speaker notes"], viewOnly: true);
        this.Curate("Ruler", () => { this.Toggle(c => c.ShowRuler = !c.ShowRuler); this.StateHasChanged(); return Task.CompletedTask; }, "View › Show", viewOnly: true);
        this.Curate("Gridlines", () => { this.Toggle(c => c.ShowGridlines = !c.ShowGridlines); this.StateHasChanged(); return Task.CompletedTask; }, "View › Show", viewOnly: true);
        this.Curate("Guides", () => { this.Toggle(c => c.ShowGuides = !c.ShowGuides); this.StateHasChanged(); return Task.CompletedTask; }, "View › Show", viewOnly: true);
    }

    Task Tab(string key)
    {
        this.StateHasChanged();
        return this.OnTabChanged(key);
    }

    void Curate(string label, Func<Task> action, string category, string? shortcut = null, string[]? keywords = null, bool viewOnly = false)
    {
        var command = new OfficeCommand(label, () => this.InvokeAsync(async () =>
        {
            await action();
            this.StateHasChanged();
        }))
        {
            Id = "curated:" + label,
            Category = category,
            Shortcut = shortcut,
            Keywords = keywords ?? [],
            CanExecute = () => this.Deck is not null && (viewOnly || !this.Disabled)
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
}
