using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Shiny.Blazor.Controls;
using Shiny.Controls.Office.Icons;
using Shiny.Controls.Office.Packaging;
using Shiny.Controls.Office.Presentation;
using Shiny.Controls.Office.Shapes;
using Shiny.Controls.Office.Skia;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;
using Shiny.Controls.Office.Theming;

namespace Shiny.Blazor.Controls.Office;

/// <summary>The dialogs the slide editor's ribbon opens.</summary>
enum SlideDialog
{
    None,
    Chart,
    HeaderFooter,
    Link,
    Background,
    SlideSize,
    Section
}

/// <summary>
/// <see cref="SlideEditor"/> with PowerPoint's chrome: the ribbon with every tab, the rail or outline,
/// the animation pane, speaker notes and a status bar.
/// </summary>
public partial class SlideEditorView
{
    SlideEditor? editor;
    SlideView? show;
    bool presenting;
    bool presenterView;

    string? selectedTab = "home";
    bool showReplace;
    string replacement = string.Empty;
    bool showAnimationPane;
    int? selectedAnimation;

    SlideDialog dialog;

    /// <summary>The slide the built-in confirmation is asking about, or null when it is closed.</summary>
    int? pendingDelete;
    bool focusConfirm;
    ElementReference confirmCancel;
    readonly string confirmId = $"slide-confirm-{Guid.NewGuid():N}";

    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    // ---- parameters ----

    /// <summary>The deck to edit. Must have been opened with <c>editable: true</c>.</summary>
    [Parameter] public SlideDeck? Deck { get; set; }

    /// <summary>Forwarded to the surface. Unset there means "follow the page's scheme".</summary>
    [Parameter] public SlideTheme? Theme { get; set; }

    [Parameter] public int SlideIndex { get; set; }

    [Parameter] public EventCallback<int> SlideIndexChanged { get; set; }

    [Parameter] public bool ReadOnly { get; set; }

    [Parameter] public bool ShowToolbar { get; set; } = true;

    /// <summary>Whether the icon-only toolbar buttons carry a tooltip naming what they do.</summary>
    [Parameter] public bool ShowToolbarTooltips { get; set; } = true;

    /// <summary>The status bar: slide count, hint, notes, views and zoom.</summary>
    [Parameter] public bool ShowStatus { get; set; } = true;

    /// <summary>Show the auto-hiding presenter bar over a running show.</summary>
    [Parameter] public bool ShowPresenterControls { get; set; } = true;

    /// <summary>Raised when the show starts or ends, however it ended.</summary>
    [Parameter] public EventCallback<bool> PresentingChanged { get; set; }

    /// <summary>Whether dragging an image file onto the slide inserts it. On by default.</summary>
    [Parameter] public bool AllowFileDrop { get; set; } = true;

    /// <summary>Raised when a dropped or chosen file could not be inserted.</summary>
    [Parameter] public EventCallback<OfficeDropRejected> DropRejected { get; set; }

    /// <summary>Extra items appended to the Home tab, after the built-in groups.</summary>
    [Parameter] public RenderFragment? ToolbarContent { get; set; }

    /// <summary>Group title for <see cref="ToolbarContent"/>.</summary>
    [Parameter] public string ToolbarContentTitle { get; set; } = "Actions";

    /// <summary>Draw the ribbon's tab strip. On: the bar has PowerPoint's full set of tabs.</summary>
    [Parameter] public bool ShowRibbonTabs { get; set; } = true;

    /// <summary>
    /// The File tab. With the shell on (the default) File opens the backstage and still raises this; with
    /// it off, the ribbon shows File only when this is handled, and the host opens its own backstage.
    /// </summary>
    [Parameter] public EventCallback FileMenuRequested { get; set; }

    [Parameter] public IReadOnlyList<string> FontFamilies { get; set; } =
    [
        "Calibri", "Calibri Light", "Cambria", "Arial", "Century Gothic", "Georgia", "Garamond", "Times New Roman", "Trebuchet MS", "Verdana", "Courier New"
    ];

    [Parameter] public IReadOnlyList<double> FontSizes { get; set; } =
    [
        8, 9, 10, 11, 12, 14, 16, 18, 20, 24, 28, 32, 36, 40, 44, 48, 54, 60, 66, 72, 80, 88, 96
    ];

    /// <summary>The size of shape the toolbar inserts, in slide pixels.</summary>
    [Parameter] public double ShapeWidth { get; set; } = 240;

    [Parameter] public double ShapeHeight { get; set; } = 180;

    /// <summary>How wide an inserted picture is, in slide pixels.</summary>
    [Parameter] public double PictureWidth { get; set; } = 400;

    [Parameter] public double ToolbarHeight { get; set; } = 44;

    internal const string DefaultToolbarBackground = "var(--shiny-color-surface-container-low, #EFF4FB)";

    [Parameter] public string ToolbarBackground { get; set; } = DefaultToolbarBackground;

    string? ChosenToolbarBackground => this.ToolbarBackground == DefaultToolbarBackground ? null : this.ToolbarBackground;

    [Parameter] public string ToolbarForeground { get; set; } = "var(--shiny-color-on-surface, #161C23)";

    [Parameter] public string ToolbarBorder { get; set; } = "var(--shiny-color-outline-variant, #BBC7DF)";

    [Parameter] public string? Class { get; set; }

    [Parameter] public string? Style { get; set; }

    [Parameter] public EventCallback DeckChanged { get; set; }

    /// <summary>Ask before the toolbar deletes a slide. On by default.</summary>
    [Parameter] public bool ConfirmSlideDelete { get; set; } = true;

    /// <summary>Replaces the built-in delete confirmation. Return true to delete.</summary>
    [Parameter] public Func<int, Task<bool>>? ConfirmDeleteSlide { get; set; }

    /// <summary>Show the slide rail beside the slide. On by default; hidden below 600px wide.</summary>
    [Parameter] public bool ShowSlideRail { get; set; } = true;

    /// <summary>Show the speaker-notes box under the slide.</summary>
    [Parameter] public bool ShowNotes { get; set; }

    [Parameter] public EventCallback<bool> ShowNotesChanged { get; set; }

    /// <summary>The zoom: 1 is 100%, null fits the slide to the window. Two-way.</summary>
    [Parameter] public double? Zoom { get; set; }

    [Parameter] public EventCallback<double?> ZoomChanged { get; set; }

    /// <summary>Normal, Outline, Slide Sorter, Notes Page or Slide Master. Two-way.</summary>
    [Parameter] public SlideEditorViewMode ViewMode { get; set; }

    [Parameter] public EventCallback<SlideEditorViewMode> ViewModeChanged { get; set; }

    /// <summary>
    /// Raised whenever something a status bar shows changes: the slide, the count, the zoom, the notes
    /// toggle or the view.
    /// </summary>
    [Parameter] public EventCallback StatusChanged { get; set; }

    /// <summary>The same notification as <see cref="StatusChanged"/>, for code holding a reference.</summary>
    public event EventHandler? StatusUpdated;

    /// <summary>The colour this control wears — its ribbon's header band.</summary>
    [Parameter] public OfficeAccent? Accent { get; set; } = OfficeAccent.Presentation;

    /// <summary>A picture drawn behind the slide. Forwarded to the surface.</summary>
    [Parameter] public OfficeWatermark? Watermark { get; set; }

    // ---- status ----

    public SlideEditor? Editor => this.editor;

    public SlideEditorController? Controller => this.editor?.Controller;

    /// <summary>The slide being edited, zero-based — a status bar's "Slide 3 of 12".</summary>
    public int CurrentSlideIndex => this.Controller?.Index ?? this.SlideIndex;

    /// <summary>How many slides the deck has.</summary>
    public int SlideCount => this.Deck?.Slides.Count ?? 0;

    /// <summary>The zoom actually drawn at, fitted or not — 1 is 100%.</summary>
    public double EffectiveZoom => this.Controller?.EffectiveZoom ?? 1;

    /// <summary>Whether the deck is playing full screen.</summary>
    public bool IsPresenting => this.presenting;

    (int Index, int Count, double Zoom, double? SetZoom, bool Notes, SlideEditorViewMode Mode) lastStatus;

    async Task SyncStatusAsync()
    {
        if (this.Controller is { } controller)
        {
            // The zoom a host set reaches the controller, and a zoom the controller took (Ctrl+wheel)
            // reaches the host.
            if (!Nullable.Equals(controller.Zoom, this.Zoom) && !Nullable.Equals(this.lastStatus.SetZoom, controller.Zoom))
            {
                this.Zoom = controller.Zoom;
                if (this.ZoomChanged.HasDelegate)
                    await this.ZoomChanged.InvokeAsync(this.Zoom);
            }

            if (controller.ViewMode != this.ViewMode)
            {
                this.ViewMode = controller.ViewMode;
                if (this.ViewModeChanged.HasDelegate)
                    await this.ViewModeChanged.InvokeAsync(this.ViewMode);
            }
        }

        var now = (this.CurrentSlideIndex, this.SlideCount, this.EffectiveZoom, this.Controller?.Zoom, this.ShowNotes, this.ViewMode);
        if (now == this.lastStatus)
            return;

        this.lastStatus = now;
        this.StatusUpdated?.Invoke(this, EventArgs.Empty);
        if (this.StatusChanged.HasDelegate)
            await this.StatusChanged.InvokeAsync();
    }

    protected override void OnParametersSet()
    {
        this.ApplyDeckParameter();

        if (this.Controller is { } controller)
        {
            if (!Nullable.Equals(controller.Zoom, this.Zoom))
                controller.Zoom = this.Zoom;

            if (controller.ViewMode != this.ViewMode)
                controller.ViewMode = this.ViewMode;
        }
    }

    // ---- plumbing ----

    static string Ico(OfficeIcon icon) => OfficeToolbarIcons.Get(icon).Value;

    static string Ico(SlideIcon icon) => OfficeToolbarIcons.Get(SlideIcons.Shapes(icon)).Value;

    bool Disabled => this.EffectiveReadOnly || this.Deck is null;

    bool HasShape => !this.Disabled && (this.Controller?.HasShapeSelection ?? false);

    bool CanFormat => !this.Disabled && (this.Controller?.CanFormatShape ?? false);

    bool CanCopyShape => !this.Disabled && (this.Controller?.CanCopyShape ?? false);

    bool CanArrange => !this.Disabled && (this.Controller?.CanArrange ?? false);

    /// <summary>A caret inside text: the character commands apply.</summary>
    bool HasText => !this.Disabled && this.Controller?.IsEditingText == true;

    /// <summary>Text, or a shape whose whole text a command can reach.</summary>
    bool HasTextOrShape => this.HasText || this.HasShape;

    bool HasMasterSelection => !this.Disabled && this.ViewMode == SlideEditorViewMode.SlideMaster && this.Controller?.Master.Selection is not null;

    SlideCaretFormat Format => this.Controller?.CaretFormat ?? SlideCaretFormat.Default;

    string ColorHex => OfficeColors.ToHex(this.Format.Color);

    string FillHex => this.Controller?.Selection?.Fill.Solid is { } fill ? OfficeColors.ToHex(fill) : "#FFFFFF";

    string OutlineHex => this.Controller?.Selection?.Outline is { } outline ? OfficeColors.ToHex(outline.Color) : "#000000";

    SlideColorScheme? ThemeColors => this.Controller?.ThemeColorScheme;

    IReadOnlyList<SlideColorScheme> CurrentVariants
        => (SlideThemeDefinition.BuiltIn.FirstOrDefault(x => x.Name == this.Controller?.ThemeName) ?? SlideThemeDefinition.BuiltIn[0]).Variants;

    SlideTableStyleFlags TableFlags => this.Controller?.SelectedTable?.StyleFlags ?? default;

    SlideTransition? Transition => this.Controller?.CurrentTransition;

    static readonly SlideTransitionKind[] TransitionKinds =
    [
        SlideTransitionKind.None, SlideTransitionKind.Fade, SlideTransitionKind.Push, SlideTransitionKind.Wipe, SlideTransitionKind.Split,
        SlideTransitionKind.Reveal, SlideTransitionKind.Cover, SlideTransitionKind.Zoom, SlideTransitionKind.Morph
    ];

    IReadOnlyList<SlideTransitionDirection> TransitionDirections
        => this.Transition is { } t ? SlideTransition.DirectionsFor(t.Kind) : [];

    /// <summary>The animation the Timing group edits: the one picked in the pane, or the selected shape's first.</summary>
    int? SelectedAnimationIndex
    {
        get
        {
            var animations = this.Controller?.Animations ?? [];
            if (this.selectedAnimation is { } picked && picked < animations.Count)
                return picked;

            return this.Controller?.SelectionAnimations is { Count: > 0 } own ? own[0].Index : null;
        }
    }

    SlideAnimation? SelectedAnimation
        => this.SelectedAnimationIndex is { } index ? this.Controller?.Animations.ElementAtOrDefault(index) : null;

    SlideAnimationEffect? SelectedEffect => this.Controller?.SelectionAnimations is { Count: > 0 } own ? own[0].Animation.Effect : null;

    string StatusHint => this.Controller switch
    {
        null => string.Empty,
        { IsEditingMaster: true } => "Slide Master — changes here apply to every slide using this layout",
        { Mode: SlideViewMode.Grid } => "Drag a slide to move it; double-click to open it",
        { SelectedShape: >= 0, IsEditingText: true, ActiveCell: not null } => "Editing a cell — Tab moves on, Shift+click selects a block",
        { SelectedShape: >= 0, IsEditingText: true } => "Editing text — Shift+Enter for a line break, Esc to leave",
        { HasMultipleSelection: true } => "Shapes selected — Ctrl+G groups them, the arrows nudge them",
        { SelectedShape: >= 0, Selection.IsGroup: true } => "Group selected — double-click to select a shape inside it",
        { SelectedShape: >= 0 } => "Shape selected — drag the round handle to rotate (Shift for 15°), double-click to edit its text",
        _ => "Click a shape to select it; drag on the slide to select several."
    };

    static string Css(ArgbColor value)
        => $"rgba({value.R},{value.G},{value.B},{(value.A / 255d).ToString("0.###", Invariant)})";

    string? AccentBackground => this.Accent is { } a ? Css(a.Color) : null;

    string? AccentInk => this.Accent is { } a ? Css(a.Ink) : null;

    static string Seconds(TimeSpan? value) => (value ?? TimeSpan.Zero).TotalSeconds.ToString("0.##", Invariant);

    static string Inches(double? pixels) => ((pixels ?? 0) / 96).ToString("0.00", Invariant);

    static bool TryNumber(ChangeEventArgs e, out double value)
        => double.TryParse(e.Value?.ToString(), NumberStyles.Float, Invariant, out value);

    async Task Run(Action<SlideEditorController> action)
    {
        if (this.Controller is not { } controller || this.Disabled)
            return;

        action(controller);

        // Focus goes back to the editor after every toolbar action: leaving it on the button means the
        // next keystroke goes nowhere, which reads as the editor having silently stopped working.
        if (this.editor is not null)
        {
            this.editor.Invalidate();
            await this.editor.FocusAsync();
        }

        await this.RaiseChangedAsync();
    }

    /// <summary>A view setting — ruler, gridlines, guides — that changes no content, so works read-only too.</summary>
    void Toggle(Action<SlideEditorController> action)
    {
        if (this.Controller is not { } controller)
            return;

        action(controller);
        this.editor?.Invalidate();
    }

    async Task RaiseChangedAsync()
    {
        this.StateHasChanged();
        await this.SyncStatusAsync();

        if (this.DeckChanged.HasDelegate)
            await this.DeckChanged.InvokeAsync();
    }

    async Task OnEditorChanged()
    {
        this.StateHasChanged();
        await this.SyncStatusAsync();
    }

    async Task OnDeckChanged()
    {
        this.SyncNotesDraft();
        await this.RaiseChangedAsync();
    }

    async Task OnSlideIndexChanged(int index)
    {
        this.SlideIndex = index;
        this.selectedAnimation = null;
        this.SyncNotesDraft();

        if (this.SlideIndexChanged.HasDelegate)
            await this.SlideIndexChanged.InvokeAsync(index);

        await this.SyncStatusAsync();
        this.StateHasChanged();
    }

    async Task OnTabChanged(string? key)
    {
        this.selectedTab = key;

        // The numbered click markers are the Animations tab's, as in PowerPoint.
        if (this.Controller is { } controller)
        {
            controller.ShowAnimationMarkers = key == "animations";
            this.editor?.Invalidate();
        }

        await Task.CompletedTask;
    }


    async Task OnEditorCommand(string command)
    {
        switch (command)
        {
            case "show-beginning": await this.StartPresentingAsync(0); break;
            case "show-current": await this.StartPresentingAsync(); break;
            case "link": this.OpenLinkDialog(); break;
            case "find": this.selectedTab = "home"; break;
            case "replace": this.selectedTab = "home"; this.showReplace = true; break;
            case "save" when this.ShellOn: await this.SaveAsync(); break;
            case "print" when this.ShellOn: await this.PrintAsync(); break;
        }

        this.StateHasChanged();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // The editor makes its controller while it renders, after this view already has; one more
        // render lets the rail and the controller-driven buttons see it.
        if (firstRender && this.Controller is { } controller)
        {
            controller.Zoom = this.Zoom;
            controller.ViewMode = this.ViewMode;
            this.StateHasChanged();
        }

        this.AfterShellRender();

        // Cancel takes the focus, not Delete: Enter on a dialog that opened under the user's hands
        // should be the harmless answer.
        if (this.focusConfirm && this.pendingDelete is not null)
        {
            this.focusConfirm = false;
            await this.confirmCancel.FocusAsync();
        }
    }

    // ---- views and zoom ----

    async Task SetViewModeAsync(SlideEditorViewMode mode)
    {
        if (this.Controller is not { } controller)
            return;

        controller.ViewMode = mode;
        this.ViewMode = mode;
        if (mode == SlideEditorViewMode.SlideMaster)
            this.selectedTab = "slidemaster";
        else if (this.selectedTab == "slidemaster")
            this.selectedTab = "home";

        this.editor?.Invalidate();
        if (this.ViewModeChanged.HasDelegate)
            await this.ViewModeChanged.InvokeAsync(mode);

        await this.SyncStatusAsync();
    }

    async Task SetZoomAsync(double? zoom)
    {
        if (this.Controller is not { } controller)
            return;

        controller.Zoom = zoom;
        this.Zoom = controller.Zoom;
        this.editor?.Invalidate();

        if (this.ZoomChanged.HasDelegate)
            await this.ZoomChanged.InvokeAsync(this.Zoom);

        await this.SyncStatusAsync();
    }

    async Task ZoomStepAsync(int direction)
    {
        if (this.Controller is not { } controller)
            return;

        controller.ZoomStep(direction);
        await this.SetZoomAsync(controller.Zoom);
    }

    List<RibbonMenuEntry> ZoomMenu =>
    [
        .. new[] { 0.25, 0.33, 0.5, 0.66, 0.75, 1, 1.5, 2, 3, 4 }.Select(z => new RibbonMenuEntry
        {
            Text = $"{Math.Round(z * 100)}%",
            IsChecked = this.Controller?.Zoom is { } current && Math.Abs(current - z) < 0.001,
            OnClick = EventCallback.Factory.Create(this, () => this.SetZoomAsync(z))
        }),
        new RibbonMenuEntry { IsSeparator = true },
        new RibbonMenuEntry { Text = "Fit", IsChecked = this.Controller?.Zoom is null, OnClick = EventCallback.Factory.Create(this, () => this.SetZoomAsync(null)) }
    ];

    Task GoToSlide(int index) => this.Run(c =>
    {
        c.ClearSelection();
        c.Index = index;
    });

    Task OnOutlineTitle(int slide, ChangeEventArgs e) => this.Run(c => c.SetOutline(slide, e.Value?.ToString() ?? string.Empty, null));

    Task OnOutlineBody(int slide, ChangeEventArgs e) => this.Run(c => c.SetOutline(slide, null, e.Value?.ToString() ?? string.Empty));

    // ---- the show ----

    /// <summary>
    /// Play the deck full screen, from <paramref name="from"/> or from the slide being edited —
    /// optionally in Presenter View.
    /// </summary>
    public async Task StartPresentingAsync(int? from = null, bool presenterView = false)
    {
        if (this.Deck is null || this.presenting)
            return;

        this.editor?.StopPreview();
        this.Controller?.ClearSelection();

        if (from is { } index && index != this.SlideIndex)
            await this.OnSlideIndexChanged(index);

        this.presenterView = presenterView;
        this.presenting = true;
        this.StateHasChanged();
    }

    /// <summary>End the show and go back to editing.</summary>
    public Task StopPresentingAsync()
        => this.show is null ? Task.CompletedTask : this.show.StopPresentingAsync();

    async Task OnShowPresentingChanged(bool value)
    {
        this.presenting = value;

        if (!value && this.editor is not null)
            await this.editor.FocusAsync();

        if (this.PresentingChanged.HasDelegate)
            await this.PresentingChanged.InvokeAsync(value);

        this.StateHasChanged();
    }

    void PreviewSlide() => this.editor?.Preview();

    // ---- export ----

    /// <summary>One slide (the current one by default) as a PNG, <paramref name="width"/> pixels wide.</summary>
    public byte[]? ExportSlidePng(int? slide = null, int width = 1920)
        => this.Deck is { Slides.Count: > 0 } deck ? SlideExporter.ToPng(deck, slide ?? this.CurrentSlideIndex, width, this.Watermark) : null;

    /// <summary>Every slide as a PNG.</summary>
    public IReadOnlyList<byte[]> ExportSlidesPng(int width = 1920)
        => this.Deck is { } deck ? SlideExporter.ToPngs(deck, width, watermark: this.Watermark) : [];

    /// <summary>The deck as a PDF, a page per slide. Hidden slides are left out.</summary>
    public byte[]? ExportPdf() => this.Deck is { } deck ? SlideExporter.ToPdf(deck, watermark: this.Watermark) : null;

    // ---- delete confirmation ----

    /// <summary>Deletes the current slide, asking first unless <see cref="ConfirmSlideDelete"/> is off.</summary>
    public async Task RequestDeleteSlide()
    {
        if (this.Controller is not { CanDeleteSlide: true } controller || this.Disabled)
            return;

        var index = controller.Index;

        if (!this.ConfirmSlideDelete)
        {
            await this.Run(c => c.DeleteSlide(index));
            return;
        }

        if (this.ConfirmDeleteSlide is { } ask)
        {
            if (await ask(index))
                await this.Run(c => c.DeleteSlide(index));

            return;
        }

        this.pendingDelete = index;
        this.focusConfirm = true;
        this.StateHasChanged();
    }

    async Task ConfirmDeleteSlideAsync()
    {
        if (this.pendingDelete is not { } index)
            return;

        this.pendingDelete = null;
        await this.Run(c => c.DeleteSlide(index));
    }

    async Task CancelDeleteSlide()
    {
        this.pendingDelete = null;

        if (this.editor is not null)
            await this.editor.FocusAsync();
    }

    Task OnConfirmKeyDown(KeyboardEventArgs e)
        => e.Key == "Escape" ? this.CancelDeleteSlide() : Task.CompletedTask;

    string DescribeSlide(int index)
        => this.Deck?.Slides.ElementAtOrDefault(index)?.Title is { Length: > 0 } title
            ? $"“{title}” and everything on it"
            : "This slide and everything on it";

    async Task FocusEditorAsync()
    {
        if (this.editor is not null)
            await this.editor.FocusAsync();

        this.SyncNotesDraft();
        await this.SyncStatusAsync();
        this.StateHasChanged();
    }

    // ---- notes ----

    string? notesDraft;
    int notesSlide = -1;

    async Task ToggleNotesAsync()
    {
        this.ShowNotes = !this.ShowNotes;
        this.SyncNotesDraft(force: true);

        if (this.ShowNotesChanged.HasDelegate)
            await this.ShowNotesChanged.InvokeAsync(this.ShowNotes);

        await this.SyncStatusAsync();
    }

    async Task OnNotesInput(ChangeEventArgs e)
    {
        if (this.Controller is not { } controller || this.Disabled)
            return;

        this.notesDraft = e.Value as string;
        this.notesSlide = controller.Index;
        controller.SetNotes(this.notesDraft);

        if (this.ViewMode == SlideEditorViewMode.NotesPage)
            this.editor?.Invalidate();

        if (this.DeckChanged.HasDelegate)
            await this.DeckChanged.InvokeAsync();
    }

    void SyncNotesDraft(bool force = false)
    {
        if (this.Controller is not { } controller)
            return;

        static string Normal(string? value) => (value ?? string.Empty).Replace("\r\n", "\n").TrimEnd();

        if (force || controller.Index != this.notesSlide || Normal(this.notesDraft) != Normal(controller.Notes))
        {
            this.notesDraft = controller.Notes;
            this.notesSlide = controller.Index;
        }
    }

    // ---- menus ----

    RibbonMenuEntry Entry(string text, Action<SlideEditorController> action, bool isChecked = false, bool disabled = false, string? icon = null)
        => new()
        {
            Text = text,
            Icon = icon,
            IsChecked = isChecked,
            IsDisabled = disabled || this.Disabled,
            OnClick = EventCallback.Factory.Create(this, () => this.Run(action))
        };

    static RibbonMenuEntry Separator => new() { IsSeparator = true };

    List<RibbonMenuEntry> LayoutMenu
    {
        get
        {
            if (this.Controller is not { } controller)
                return [];

            var layouts = controller.Layouts;
            return
            [
                .. layouts.Select(layout => this.Entry(layout.Name, c => c.SetLayout(layout), layout.IsCurrent)),
                Separator,
                new RibbonMenuEntry
                {
                    Text = "New slide with layout",
                    IsDisabled = this.Disabled,
                    Children = [.. layouts.Select(layout => this.Entry(layout.Name, c => c.NewSlide(layout)))]
                }
            ];
        }
    }

    List<RibbonMenuEntry> SectionMenu
    {
        get
        {
            var sections = this.Controller?.Sections ?? [];
            var current = sections.ToList().FindIndex(x => x.Contains(this.CurrentSlideIndex));

            return
            [
                new RibbonMenuEntry { Text = "Add Section", IsDisabled = this.Disabled, OnClick = EventCallback.Factory.Create(this, () => this.OpenSection(-1)) },
                new RibbonMenuEntry { Text = "Rename Section", IsDisabled = this.Disabled || current < 0, OnClick = EventCallback.Factory.Create(this, () => this.OpenSection(current)) },
                this.Entry("Remove Section", c => c.RemoveSection(current), disabled: current < 0),
                this.Entry("Remove Section & Slides", c => c.RemoveSection(current, withSlides: true), disabled: current < 0),
                this.Entry("Move Section Up", c => c.MoveSection(current, -1), disabled: current <= 0),
                this.Entry("Move Section Down", c => c.MoveSection(current, 1), disabled: current < 0 || current >= sections.Count - 1)
            ];
        }
    }

    List<RibbonMenuEntry> CaseMenu =>
    [
        this.Entry("Sentence case.", c => c.ChangeCase(TextCase.Sentence)),
        this.Entry("lowercase", c => c.ChangeCase(TextCase.Lower)),
        this.Entry("UPPERCASE", c => c.ChangeCase(TextCase.Upper)),
        this.Entry("Capitalize Each Word", c => c.ChangeCase(TextCase.Capitalize)),
        this.Entry("tOGGLE cASE", c => c.ChangeCase(TextCase.Toggle))
    ];

    List<RibbonMenuEntry> SpacingMenu =>
    [
        this.Entry("Very Tight", c => c.SetCharacterSpacing(-3)),
        this.Entry("Tight", c => c.SetCharacterSpacing(-1.5)),
        this.Entry("Normal", c => c.SetCharacterSpacing(0)),
        this.Entry("Loose", c => c.SetCharacterSpacing(3)),
        this.Entry("Very Loose", c => c.SetCharacterSpacing(6))
    ];

    List<RibbonMenuEntry> LineSpacingMenu =>
    [
        .. new[] { 1.0, 1.5, 2.0, 2.5, 3.0 }.Select(x => this.Entry(x.ToString("0.0", Invariant), c => c.SetLineSpacing(x), Math.Abs(this.Format.LineSpacing - x) < 0.01))
    ];

    List<RibbonMenuEntry> DirectionMenu =>
    [
        this.Entry("Horizontal", c => c.SetTextDirection(ShapeTextDirection.Horizontal), this.Controller?.Selection?.TextDirection == ShapeTextDirection.Horizontal),
        this.Entry("Rotate all text 90°", c => c.SetTextDirection(ShapeTextDirection.Rotate90), this.Controller?.Selection?.TextDirection == ShapeTextDirection.Rotate90),
        this.Entry("Rotate all text 270°", c => c.SetTextDirection(ShapeTextDirection.Rotate270), this.Controller?.Selection?.TextDirection == ShapeTextDirection.Rotate270)
    ];

    List<RibbonMenuEntry> AlignTextMenu =>
    [
        this.Entry("Top", c => c.SetTextAnchor(TextAnchor.Top), this.Controller?.Selection?.Text?.Anchor == TextAnchor.Top),
        this.Entry("Middle", c => c.SetTextAnchor(TextAnchor.Middle), this.Controller?.Selection?.Text?.Anchor == TextAnchor.Middle),
        this.Entry("Bottom", c => c.SetTextAnchor(TextAnchor.Bottom), this.Controller?.Selection?.Text?.Anchor == TextAnchor.Bottom),
        Separator,
        this.Entry("Do Not Autofit", c => c.SetAutofit(TextAutofit.None), this.Controller?.Selection?.Autofit == TextAutofit.None),
        this.Entry("Shrink Text on Overflow", c => c.SetAutofit(TextAutofit.ShrinkOnOverflow), this.Controller?.Selection?.Autofit == TextAutofit.ShrinkOnOverflow),
        this.Entry("Resize Shape to Fit Text", c => c.SetAutofit(TextAutofit.ResizeShape), this.Controller?.Selection?.Autofit == TextAutofit.ResizeShape)
    ];

    List<RibbonMenuEntry> ShapesMenu =>
    [
        .. ShapeNames.All.Select(x => new RibbonMenuEntry
        {
            Text = x.Name,
            Icon = OfficeToolbarIcons.Get(ShapeIcons.For(x.Geometry)).Value,
            IsDisabled = this.Disabled,
            OnClick = EventCallback.Factory.Create(this, () => this.OnShapePicked(x.Geometry))
        })
    ];

    List<RibbonMenuEntry> IconsMenu =>
    [
        .. new[] { ShapeGeometry.Star5, ShapeGeometry.Plus, ShapeGeometry.Hexagon, ShapeGeometry.Cloud, ShapeGeometry.RightArrow, ShapeGeometry.Chevron, ShapeGeometry.Diamond, ShapeGeometry.Can }
            .Select(g => new RibbonMenuEntry
            {
                Text = ShapeNames.Of(g),
                Icon = OfficeToolbarIcons.Get(ShapeIcons.For(g)).Value,
                IsDisabled = this.Disabled,
                OnClick = EventCallback.Factory.Create(this, () => this.Run(c => c.AddIcon(g)))
            })
    ];

    List<RibbonMenuEntry> ChartMenu =>
    [
        .. Enum.GetValues<SlideChartKind>().Select(kind => new RibbonMenuEntry
        {
            Text = kind == SlideChartKind.Column ? "Clustered Column" : kind.ToString(),
            IsDisabled = this.Disabled,
            OnClick = EventCallback.Factory.Create(this, () => this.OpenChartEditor(kind))
        })
    ];

    List<RibbonMenuEntry> ChartTypeMenu =>
    [
        .. Enum.GetValues<SlideChartKind>().Select(kind => this.Entry(
            kind == SlideChartKind.Column ? "Clustered Column" : kind.ToString(),
            c => c.SetChartData(c.SelectedChart! with { Kind = kind }),
            this.Controller?.SelectedChart?.Kind == kind,
            this.Controller?.SelectedChart is null))
    ];

    List<RibbonMenuEntry> ArrangeMenu =>
    [
        this.Entry("Bring to Front", c => c.BringToFront()),
        this.Entry("Send to Back", c => c.SendToBack()),
        this.Entry("Bring Forward", c => c.BringForward()),
        this.Entry("Send Backward", c => c.SendBackward()),
        Separator,
        this.Entry("Group", c => c.Group(), disabled: this.Controller?.CanGroup != true),
        this.Entry("Ungroup", c => c.Ungroup(), disabled: this.Controller?.CanUngroup != true),
        Separator,
        new RibbonMenuEntry { Text = "Align", IsDisabled = !this.CanArrange, Children = this.AlignMenu },
        new RibbonMenuEntry { Text = "Rotate", IsDisabled = !this.CanArrange, Children = this.RotateMenu }
    ];

    List<RibbonMenuEntry> AlignMenu =>
    [
        this.Entry("Align Left", c => c.Align(ShapeAlignment.Left)),
        this.Entry("Align Center", c => c.Align(ShapeAlignment.Center)),
        this.Entry("Align Right", c => c.Align(ShapeAlignment.Right)),
        this.Entry("Align Top", c => c.Align(ShapeAlignment.Top)),
        this.Entry("Align Middle", c => c.Align(ShapeAlignment.Middle)),
        this.Entry("Align Bottom", c => c.Align(ShapeAlignment.Bottom)),
        Separator,
        this.Entry("Distribute Horizontally", c => c.Distribute(horizontally: true)),
        this.Entry("Distribute Vertically", c => c.Distribute(horizontally: false)),
        Separator,
        this.Entry("Align to Slide", c => c.AlignToSlide = true, this.Controller?.AlignToSlide == true),
        this.Entry("Align Selected Objects", c => c.AlignToSlide = false, this.Controller?.AlignToSlide == false)
    ];

    List<RibbonMenuEntry> GroupMenu =>
    [
        this.Entry("Group (Ctrl+G)", c => c.Group(), disabled: this.Controller?.CanGroup != true),
        this.Entry("Ungroup (Ctrl+Shift+G)", c => c.Ungroup(), disabled: this.Controller?.CanUngroup != true)
    ];

    List<RibbonMenuEntry> RotateMenu =>
    [
        this.Entry("Rotate Right 90°", c => c.RotateBy(90)),
        this.Entry("Rotate Left 90°", c => c.RotateBy(-90)),
        this.Entry("Flip Vertical", c => c.Flip(horizontal: false)),
        this.Entry("Flip Horizontal", c => c.Flip(horizontal: true))
    ];

    List<RibbonMenuEntry> QuickStyleMenu
    {
        get
        {
            var colors = this.ThemeColors;
            return
            [
                .. Enum.GetValues<SlideQuickStyleKind>().Select(kind => new RibbonMenuEntry
                {
                    Text = new SlideQuickStyle(kind, 1).Name.Split(" - ")[0],
                    IsDisabled = !this.CanFormat,
                    Children =
                    [
                        .. Enumerable.Range(1, 6).Select(accent => new RibbonMenuEntry
                        {
                            Text = $"Accent {accent}",
                            Icon = colors is null ? null : Swatch(colors.Accents[accent - 1], outlined: kind == SlideQuickStyleKind.ColoredOutline),
                            OnClick = EventCallback.Factory.Create(this, () => this.Run(c => c.ApplyQuickStyle(new SlideQuickStyle(kind, accent))))
                        })
                    ]
                })
            ];
        }
    }

    /// <summary>A small colour square as inline SVG, for a menu line's icon.</summary>
    static string Swatch(ArgbColor color, bool outlined = false)
        => outlined
            ? $"<svg width=\"16\" height=\"16\" viewBox=\"0 0 16 16\"><rect x=\"1.5\" y=\"1.5\" width=\"13\" height=\"13\" rx=\"2\" fill=\"#fff\" stroke=\"{OfficeColors.ToHex(color)}\" stroke-width=\"2\"/></svg>"
            : $"<svg width=\"16\" height=\"16\" viewBox=\"0 0 16 16\"><rect x=\"1\" y=\"1\" width=\"14\" height=\"14\" rx=\"2\" fill=\"{OfficeColors.ToHex(color)}\"/></svg>";

    List<RibbonMenuEntry> ThemeColorEntries(Action<SlideEditorController, ArgbColor> apply)
    {
        var colors = this.ThemeColors;
        if (colors is null)
            return [];

        ArgbColor[] palette = [colors.Light1, colors.Dark1, colors.Light2, colors.Dark2, .. colors.Accents];
        return [.. palette.Select((color, i) => new RibbonMenuEntry
        {
            Text = i switch { 0 => "Background 1", 1 => "Text 1", 2 => "Background 2", 3 => "Text 2", var n => $"Accent {n - 3}" },
            Icon = Swatch(color),
            IsDisabled = this.Disabled,
            OnClick = EventCallback.Factory.Create(this, () => this.Run(c => apply(c, color)))
        })];
    }

    List<RibbonMenuEntry> FillMenu
    {
        get
        {
            var colors = this.ThemeColors;
            var accent = colors?.Accent1 ?? new ArgbColor(255, 0x44, 0x72, 0xC4);
            var white = new ArgbColor(255, 255, 255, 255);

            return
            [
                new RibbonMenuEntry { Text = "Theme Colors", IsDisabled = !this.CanFormat, Children = this.ThemeColorEntries((c, color) => c.SetShapeFill(color)) },
                this.Entry("No Fill", c => c.SetShapeFill(SlideFillSpec.None)),
                Separator,
                new RibbonMenuEntry
                {
                    Text = "Gradient",
                    IsDisabled = !this.CanFormat,
                    Children =
                    [
                        this.Entry("Light Linear Down", c => c.SetShapeFill(SlideFillSpec.LinearGradient(Mix(accent, white, 0.7), accent, 90))),
                        this.Entry("Dark Linear Down", c => c.SetShapeFill(SlideFillSpec.LinearGradient(accent, Mix(accent, new ArgbColor(255, 0, 0, 0), 0.5), 90))),
                        this.Entry("Linear Right", c => c.SetShapeFill(SlideFillSpec.LinearGradient(Mix(accent, white, 0.6), accent, 0))),
                        this.Entry("Linear Diagonal", c => c.SetShapeFill(SlideFillSpec.LinearGradient(Mix(accent, white, 0.6), accent, 45))),
                        this.Entry("Two Accents", c => c.SetShapeFill(SlideFillSpec.LinearGradient(accent, colors?.Accent2 ?? accent, 90)))
                    ]
                }
            ];
        }
    }

    static ArgbColor Mix(ArgbColor a, ArgbColor b, double amount) => new(
        255,
        (byte)Math.Round(a.R + (b.R - a.R) * amount),
        (byte)Math.Round(a.G + (b.G - a.G) * amount),
        (byte)Math.Round(a.B + (b.B - a.B) * amount));

    List<RibbonMenuEntry> OutlineMenu =>
    [
        new RibbonMenuEntry { Text = "Theme Colors", IsDisabled = !this.HasShape, Children = this.ThemeColorEntries((c, color) => c.SetShapeOutlineColor(color)) },
        this.Entry("No Outline", c => c.RemoveShapeOutline()),
        Separator,
        new RibbonMenuEntry
        {
            Text = "Weight",
            IsDisabled = !this.HasShape,
            Children = [.. new[] { 0.25, 0.5, 0.75, 1, 1.5, 2.25, 3, 4.5, 6 }.Select(w => this.Entry($"{w.ToString("0.##", Invariant)} pt", c => c.SetShapeOutlineWeight(w)))]
        },
        new RibbonMenuEntry
        {
            Text = "Dashes",
            IsDisabled = !this.HasShape,
            Children =
            [
                .. new (LineDash Dash, string Name)[]
                {
                    (LineDash.Solid, "Solid"), (LineDash.SystemDot, "Round Dot"), (LineDash.SystemDash, "Square Dot"), (LineDash.Dash, "Dash"),
                    (LineDash.DashDot, "Dash Dot"), (LineDash.LargeDash, "Long Dash"), (LineDash.LongDashDot, "Long Dash Dot"), (LineDash.LongDashDotDot, "Long Dash Dot Dot")
                }.Select(x => this.Entry(x.Name, c => c.SetShapeOutlineDash(x.Dash), this.Controller?.Selection?.Outline?.Dash == x.Dash))
            ]
        }
    ];

    List<RibbonMenuEntry> EffectsMenu =>
    [
        this.Entry("Shadow", c => c.SetShapeShadow(!c.SelectionHasShadow), this.Controller?.SelectionHasShadow == true),
        this.Entry("No Shadow", c => c.SetShapeShadow(false))
    ];

    List<RibbonMenuEntry> SelectMenu =>
    [
        this.Entry("Select All (Ctrl+A)", c => c.SelectAllShapes()),
        this.Entry("Select None", c => c.ClearSelection())
    ];

    List<RibbonMenuEntry> DateMenu =>
    [
        .. new[] { "d", "D", "MMMM d, yyyy", "d MMMM yyyy", "yyyy-MM-dd", "t", "g" }.Select(format => new RibbonMenuEntry
        {
            Text = DateTime.Now.ToString(format, CultureInfo.CurrentCulture),
            IsDisabled = this.Disabled,
            OnClick = EventCallback.Factory.Create(this, () => this.InsertDate(format))
        }),
        Separator,
        new RibbonMenuEntry { Text = "Header & Footer…", IsDisabled = this.Disabled, OnClick = EventCallback.Factory.Create(this, this.OpenHeaderFooter) }
    ];

    List<RibbonMenuEntry> SlideSizeMenu =>
    [
        this.Entry("Standard (4:3)", c => c.SetSlideSize(SetSlideSizeCommand.Standard.Width, SetSlideSizeCommand.Standard.Height),
            this.Deck is { } a && Math.Abs(a.AspectRatio - 4d / 3) < 0.01),
        this.Entry("Widescreen (16:9)", c => c.SetSlideSize(SetSlideSizeCommand.Widescreen.Width, SetSlideSizeCommand.Widescreen.Height),
            this.Deck is { } b && Math.Abs(b.AspectRatio - 16d / 9) < 0.01),
        Separator,
        new RibbonMenuEntry { Text = "Custom Slide Size…", IsDisabled = this.Disabled, OnClick = EventCallback.Factory.Create(this, this.OpenSlideSize) }
    ];

    List<RibbonMenuEntry> TransitionOptionsMenu =>
    [
        .. this.TransitionDirections.Select(direction => this.Entry(
            SlideTransition.NameOf(direction),
            c => c.UpdateTransition(t => t with { Direction = direction }),
            this.Transition?.Direction == direction))
    ];

    List<RibbonMenuEntry> AnimationOptionsMenu =>
    [
        .. new[] { SlideTransitionDirection.FromBottom, SlideTransitionDirection.FromTop, SlideTransitionDirection.FromLeft, SlideTransitionDirection.FromRight }
            .Select(direction => this.Entry(
                SlideTransition.NameOf(direction),
                c => c.UpdateAnimation(this.SelectedAnimationIndex ?? -1, a => a with { Direction = direction }),
                this.SelectedAnimation?.Direction == direction))
    ];

    List<RibbonMenuEntry> AddAnimationMenu =>
    [
        .. Enum.GetValues<SlideAnimationClass>().Select(klass => new RibbonMenuEntry
        {
            Text = klass.ToString(),
            IsDisabled = !this.HasShape,
            Children = [.. SlideAnimation.Gallery.Where(x => SlideAnimation.ClassOf(x) == klass).Select(effect => this.Entry(SlideAnimation.NameOf(effect), c => c.Animate(effect, add: true)))]
        })
    ];

    // ---- handlers ----

    Task AddTextBox() => this.Run(c => c.AddTextBox(Math.Max(0, c.Deck.SlideWidth / 2 - 160), Math.Max(0, c.Deck.SlideHeight / 2 - 32)));

    Task OnHighlightPicked(ArgbColor? color) => this.Run(c => c.SetHighlight(color));

    static (double X, double Y) Centred(SlideEditorController controller, double width, double height)
        => (Math.Max(0, (controller.Deck.SlideWidth - width) / 2), Math.Max(0, (controller.Deck.SlideHeight - height) / 2));

    Task OnShapePicked(ShapeGeometry geometry) => this.Run(c =>
    {
        var (x, y) = Centred(c, this.ShapeWidth, this.ShapeHeight);
        c.AddShape(geometry, x, y, this.ShapeWidth, this.ShapeHeight, c.ThemeColorScheme?.Accent1);
    });

    Task OnTablePicked((int Rows, int Columns) size) => this.Run(c =>
    {
        var width = c.Deck.SlideWidth * 0.7;
        var height = Math.Min(c.Deck.SlideHeight * 0.6, size.Rows * 44);
        var (x, y) = Centred(c, width, height);
        c.AddTable(size.Rows, size.Columns, x, y, width, height);
    });

    Task OnImagePicked(OfficePickedImage image) => this.Run(c =>
    {
        var width = Math.Min(c.Deck.SlideWidth / 2, this.PictureWidth);
        var height = width * 0.75;
        var (x, y) = Centred(c, width, height);
        c.AddPicture(image.Data, image.ContentType, x, y, width, height, Path.GetFileNameWithoutExtension(image.FileName));
    });

    async Task OnMediaPicked(InputFileChangeEventArgs e, bool video)
    {
        try
        {
            using var stream = e.File.OpenReadStream(maxAllowedSize: 512L * 1024 * 1024);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);

            var contentType = string.IsNullOrEmpty(e.File.ContentType) ? (video ? "video/mp4" : "audio/mpeg") : e.File.ContentType;
            await this.Run(c => c.AddMedia(buffer.ToArray(), contentType, video, name: Path.GetFileNameWithoutExtension(e.File.Name)));
        }
        catch (Exception ex)
        {
            if (this.DropRejected.HasDelegate)
                await this.DropRejected.InvokeAsync(new OfficeDropRejected(e.File.Name, ex.Message));
        }
    }

    Task Align(TextAlignment alignment) => this.Run(c => c.SetParagraphAlignment(alignment));

    Task OnFontChanged(string family)
        => family is { Length: > 0 } ? this.Run(c => this.ForText(c, () => c.SetFontFamily(family))) : Task.CompletedTask;

    Task OnSizeChanged(double size)
        => size > 0 ? this.Run(c => this.ForText(c, () => c.SetFontSize(size))) : Task.CompletedTask;

    /// <summary>
    /// A character command with a shape selected rather than its text: PowerPoint applies it to all of
    /// the shape's text, so the caret goes in, selects everything, applies, and comes back out.
    /// </summary>
    void ForText(SlideEditorController c, Action apply)
    {
        if (c.IsEditingText)
        {
            apply();
            return;
        }

        if (c.Selection is not { Text: not null })
            return;

        c.BeginTextEditing(0, 0);
        c.SelectAll();
        apply();
        c.EndTextEditing();
    }

    Task OnColorChanged(string hex)
        => OfficeColors.TryParse(hex, out var color) ? this.Run(c => c.SetTextColor(color)) : Task.CompletedTask;

    Task OnFillColor(string hex)
        => OfficeColors.TryParse(hex, out var color) ? this.Run(c => c.SetShapeFill(color)) : Task.CompletedTask;

    Task OnOutlineColor(string hex)
        => OfficeColors.TryParse(hex, out var color) ? this.Run(c => c.SetShapeOutlineColor(color)) : Task.CompletedTask;

    Task OnCellFill(string hex)
        => OfficeColors.TryParse(hex, out var color) ? this.Run(c => c.SetCellFill(color)) : Task.CompletedTask;

    Task OnWidthChanged(ChangeEventArgs e)
        => TryNumber(e, out var inches) && inches > 0 ? this.Run(c => c.SetShapeSize(inches * 96, null)) : Task.CompletedTask;

    Task OnHeightChanged(ChangeEventArgs e)
        => TryNumber(e, out var inches) && inches > 0 ? this.Run(c => c.SetShapeSize(null, inches * 96)) : Task.CompletedTask;

    Task OnTransitionDuration(ChangeEventArgs e)
        => TryNumber(e, out var seconds) && seconds >= 0 ? this.Run(c => c.UpdateTransition(t => t with { Duration = TimeSpan.FromSeconds(seconds) })) : Task.CompletedTask;

    Task OnAdvanceOnClick(ChangeEventArgs e)
        => this.Run(c => c.UpdateTransition(t => t with { AdvanceOnClick = e.Value is true }));

    Task OnAdvanceAfterToggled(ChangeEventArgs e)
        => this.Run(c => c.UpdateTransition(t => t with { AdvanceAfter = e.Value is true ? TimeSpan.FromSeconds(5) : null }));

    Task OnAdvanceAfter(ChangeEventArgs e)
        => TryNumber(e, out var seconds) && seconds >= 0 ? this.Run(c => c.UpdateTransition(t => t with { AdvanceAfter = TimeSpan.FromSeconds(seconds) })) : Task.CompletedTask;

    Task OnAnimationTrigger(ChangeEventArgs e)
        => Enum.TryParse<SlideAnimationTrigger>(e.Value?.ToString(), out var trigger) && this.SelectedAnimationIndex is { } index
            ? this.Run(c => c.UpdateAnimation(index, a => a with { Trigger = trigger }))
            : Task.CompletedTask;

    Task OnAnimationDuration(ChangeEventArgs e)
        => TryNumber(e, out var seconds) && this.SelectedAnimationIndex is { } index
            ? this.Run(c => c.UpdateAnimation(index, a => a with { Duration = TimeSpan.FromSeconds(Math.Max(0, seconds)) }))
            : Task.CompletedTask;

    Task OnAnimationDelay(ChangeEventArgs e)
        => TryNumber(e, out var seconds) && this.SelectedAnimationIndex is { } index
            ? this.Run(c => c.UpdateAnimation(index, a => a with { Delay = TimeSpan.FromSeconds(Math.Max(0, seconds)) }))
            : Task.CompletedTask;

    Task MoveAnimation(int direction)
    {
        if (this.SelectedAnimationIndex is not { } index)
            return Task.CompletedTask;

        this.selectedAnimation = index + direction;
        return this.Run(c => c.MoveAnimation(index, direction));
    }

    void SelectAnimation(int index)
    {
        this.selectedAnimation = index;

        // Selecting a row selects its shape, as the animation pane does.
        if (this.Controller is { } controller && controller.Animations.ElementAtOrDefault(index) is { } animation &&
            controller.Current?.Shapes.ToList().FindIndex(x => x.Id == animation.ShapeId) is >= 0 and var shape)
        {
            controller.Select(shape);
            this.editor?.Invalidate();
        }
    }

    Task InsertSlideNumber()
    {
        if (this.Controller is { IsEditingText: true } controller)
            return this.Run(c => c.InsertField(SlideFieldKind.SlideNumber));

        this.OpenHeaderFooter();
        return Task.CompletedTask;
    }

    Task InsertDate(string format)
    {
        if (this.Controller is { IsEditingText: true })
            return this.Run(c => c.InsertField(SlideFieldKind.DateTime, format));

        this.OpenHeaderFooter();
        return Task.CompletedTask;
    }

    async Task ReplaceOne()
    {
        await this.Run(c => c.ReplaceCurrent(this.replacement));
    }

    async Task ReplaceAll()
    {
        await this.Run(c => c.ReplaceAll(this.replacement));
    }

    async Task MasterStyle(SlideMasterTextStyle style)
    {
        if (this.Controller is not { } controller || this.Disabled)
            return;

        controller.Master.SetTextStyle(style);
        this.editor?.Invalidate();
        await this.RaiseChangedAsync();
    }

    Task OnMasterColor(string hex)
        => OfficeColors.TryParse(hex, out var color) ? this.MasterStyle(new SlideMasterTextStyle { Color = color }) : Task.CompletedTask;

    Task OnWatermarkPicked(OfficeWatermark? mark)
    {
        this.Watermark = mark;
        this.StateHasChanged();
        return Task.CompletedTask;
    }

    // ---- dialogs ----

    void CloseDialog()
    {
        this.dialog = SlideDialog.None;
        _ = this.editor?.FocusAsync();
    }

    Task OnDialogKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
            this.CloseDialog();

        return Task.CompletedTask;
    }

    // chart
    SlideChartKind chartKind;
    string chartTitle = string.Empty;
    List<string> chartCategories = [];
    List<string> chartSeries = [];
    List<List<double>> chartValues = [];
    bool chartInserting;

    void OpenChartEditor() => this.OpenChartEditor(null);

    void OpenChartEditor(SlideChartKind? insert)
    {
        var chart = insert is { } kind ? SlideChart.Sample(kind) : this.Controller?.SelectedChart;
        if (chart is null)
            return;

        this.chartInserting = insert is not null;
        this.chartKind = chart.Kind;
        this.chartTitle = chart.Title ?? string.Empty;
        this.chartCategories = [.. chart.Categories];
        this.chartSeries = [.. chart.Series.Select(x => x.Name)];
        this.chartValues = [.. chart.Series.Select(s => Enumerable.Range(0, chart.Categories.Count).Select(i => i < s.Values.Count ? s.Values[i] : 0).ToList())];
        this.dialog = SlideDialog.Chart;
    }

    void AddChartRow()
    {
        this.chartCategories.Add($"Category {this.chartCategories.Count + 1}");
        foreach (var series in this.chartValues)
            series.Add(0);
    }

    void RemoveChartRow(int row)
    {
        if (this.chartCategories.Count <= 1)
            return;

        this.chartCategories.RemoveAt(row);
        foreach (var series in this.chartValues)
            series.RemoveAt(row);
    }

    void AddChartSeries()
    {
        this.chartSeries.Add($"Series {this.chartSeries.Count + 1}");
        this.chartValues.Add([.. Enumerable.Repeat(0d, this.chartCategories.Count)]);
    }

    void RemoveChartSeries(int column)
    {
        if (this.chartSeries.Count <= 1)
            return;

        this.chartSeries.RemoveAt(column);
        this.chartValues.RemoveAt(column);
    }

    void SetChartValue(int series, int row, ChangeEventArgs e)
    {
        if (TryNumber(e, out var value))
            this.chartValues[series][row] = value;
    }

    async Task ApplyChart()
    {
        var chart = new SlideChart(
            this.chartKind,
            [.. this.chartCategories],
            [.. this.chartSeries.Select((name, i) => new SlideChartSeries(name, [.. this.chartValues[i]]))])
        {
            Title = string.IsNullOrWhiteSpace(this.chartTitle) ? null : this.chartTitle
        };

        this.dialog = SlideDialog.None;
        await this.Run(c =>
        {
            if (this.chartInserting)
                c.AddChart(chart);
            else
                c.SetChartData(chart);
        });
    }

    // header & footer
    bool hfDate;
    bool hfDateFixed;
    string hfFixedDate = string.Empty;
    bool hfNumber;
    bool hfFooter;
    string hfFooterText = string.Empty;
    bool hfNotOnTitle;

    void OpenHeaderFooter()
    {
        var current = this.Controller?.CurrentHeaderFooter ?? new SlideHeaderFooter();
        this.hfDate = current.DateAndTime;
        this.hfDateFixed = false;
        this.hfFixedDate = DateTime.Now.ToString("d", CultureInfo.CurrentCulture);
        this.hfNumber = current.SlideNumber;
        this.hfFooter = current.Footer;
        this.hfFooterText = current.FooterText;
        this.hfNotOnTitle = false;
        this.dialog = SlideDialog.HeaderFooter;
    }

    Task ApplyHeaderFooter(bool toAll)
    {
        var settings = new SlideHeaderFooter
        {
            DateAndTime = this.hfDate,
            FixedDate = this.hfDateFixed ? this.hfFixedDate : null,
            SlideNumber = this.hfNumber,
            Footer = this.hfFooter,
            FooterText = this.hfFooterText,
            NotOnTitleSlide = this.hfNotOnTitle
        };

        this.dialog = SlideDialog.None;
        return this.Run(c => c.ApplyHeaderFooter(settings, toAll));
    }

    // link
    string linkKind = "url";
    string linkUrl = string.Empty;
    string linkDisplay = string.Empty;
    int linkSlide;
    string linkAction = SlideShowJumps.NextSlide;
    bool linkHasText;

    void OnLinkSlide(ChangeEventArgs e)
    {
        if (int.TryParse(e.Value?.ToString(), NumberStyles.Integer, Invariant, out var slide))
            this.linkSlide = slide;
    }

    void OpenLinkDialog()
    {
        if (this.Controller is not { SelectedShape: >= 0 } controller || this.Disabled)
            return;

        var current = controller.CurrentHyperlink;
        this.linkKind = current?.Slide is not null ? "slide" : current?.Action is not null && current.Url is null ? "action" : "url";
        this.linkUrl = current?.Url ?? string.Empty;
        this.linkSlide = current?.Slide ?? 0;
        this.linkAction = current?.Action ?? SlideShowJumps.NextSlide;
        this.linkDisplay = string.Empty;
        this.linkHasText = controller.IsEditingText && controller.TextSelection.IsEmpty;
        this.dialog = SlideDialog.Link;
    }

    Task ApplyLink(bool remove)
    {
        SlideHyperlink? link = remove ? null : this.linkKind switch
        {
            "slide" => new SlideHyperlink(null, this.linkSlide),
            "action" => new SlideHyperlink(null, null, this.linkAction),
            _ => string.IsNullOrWhiteSpace(this.linkUrl) ? null : new SlideHyperlink(this.linkUrl.Trim())
        };

        this.dialog = SlideDialog.None;
        return remove || link is not null
            ? this.Run(c => c.SetHyperlink(link, string.IsNullOrWhiteSpace(this.linkDisplay) ? null : this.linkDisplay))
            : Task.CompletedTask;
    }

    // background
    bool backgroundForMaster;
    string backgroundKind = "solid";
    string backgroundColor = "#FFFFFF";
    string backgroundColor2 = "#4472C4";
    byte[]? backgroundPicture;
    string backgroundPictureType = "image/png";

    void OpenBackground(bool master)
    {
        this.backgroundForMaster = master;
        var current = master ? this.Controller?.Master.CurrentPage?.Background : this.Controller?.Current?.Background;
        this.backgroundKind = current?.Image is not null ? "picture" : current?.GradientStops.Count > 0 ? "gradient" : "solid";
        this.backgroundColor = current?.Solid is { } solid ? OfficeColors.ToHex(solid) : current?.GradientStops.Count > 0 ? OfficeColors.ToHex(current.GradientStops[0].Color) : "#FFFFFF";
        this.backgroundColor2 = current?.GradientStops.Count > 1 ? OfficeColors.ToHex(current.GradientStops[^1].Color) : "#4472C4";
        this.backgroundPicture = null;
        this.dialog = SlideDialog.Background;
    }

    async Task OnBackgroundPicture(InputFileChangeEventArgs e)
    {
        using var stream = e.File.OpenReadStream(maxAllowedSize: 64L * 1024 * 1024);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        this.backgroundPicture = buffer.ToArray();
        this.backgroundPictureType = string.IsNullOrEmpty(e.File.ContentType) ? "image/png" : e.File.ContentType;
    }

    SlideBackgroundSpec? BackgroundSpec()
    {
        OfficeColors.TryParse(this.backgroundColor, out var first);
        OfficeColors.TryParse(this.backgroundColor2, out var second);

        return this.backgroundKind switch
        {
            "gradient" => SlideBackgroundSpec.Gradient(first, second),
            "picture" => this.backgroundPicture is { } picture ? SlideBackgroundSpec.Image(picture, this.backgroundPictureType) : null,
            _ => SlideBackgroundSpec.Solid(first)
        };
    }

    async Task ApplyBackground(bool toAll)
    {
        if (this.BackgroundSpec() is not { } spec)
            return;

        this.dialog = SlideDialog.None;

        if (this.backgroundForMaster)
            await this.Run(c => c.Master.SetBackground(spec));
        else
            await this.Run(c => c.SetBackground(spec, toAll));
    }

    Task ResetBackgroundFromDialog()
    {
        this.dialog = SlideDialog.None;
        return this.backgroundForMaster
            ? this.Run(c => c.Master.SetBackground(null))
            : this.Run(c => c.ResetBackground());
    }

    // slide size
    double sizeWidth;
    double sizeHeight;
    bool sizeScale = true;

    void OpenSlideSize()
    {
        this.sizeWidth = Math.Round((this.Deck?.SlideWidth ?? 1280) / 96, 2);
        this.sizeHeight = Math.Round((this.Deck?.SlideHeight ?? 720) / 96, 2);
        this.dialog = SlideDialog.SlideSize;
    }

    Task ApplySlideSize()
    {
        this.dialog = SlideDialog.None;
        return this.sizeWidth > 0.5 && this.sizeHeight > 0.5
            ? this.Run(c => c.SetSlideSize(this.sizeWidth * 96, this.sizeHeight * 96, this.sizeScale))
            : Task.CompletedTask;
    }

    // section
    int sectionIndex = -1;
    string sectionName = string.Empty;

    void OpenSection(int index)
    {
        this.sectionIndex = index;
        this.sectionName = index >= 0 && this.Controller?.Sections.ElementAtOrDefault(index) is { } section ? section.Name : "Untitled Section";
        this.dialog = SlideDialog.Section;
    }

    Task ApplySection()
    {
        this.dialog = SlideDialog.None;
        var name = string.IsNullOrWhiteSpace(this.sectionName) ? "Untitled Section" : this.sectionName.Trim();
        return this.sectionIndex < 0
            ? this.Run(c => c.AddSection(name))
            : this.Run(c => c.RenameSection(this.sectionIndex, name));
    }
}
