using Microsoft.AspNetCore.Components;
using Shiny.Controls.Office.Icons;
using Shiny.Controls.Office.Shell;

namespace Shiny.Blazor.Controls.Office;

/// <summary>
/// The File backstage — Home, New, Open, Info, Save, Save As, Print, Export, History and Options — as a
/// full-window page that raises an event for every task and performs none of them.
/// </summary>
/// <remarks>
/// Inside an <see cref="OfficeShell"/> it opens with the shell's <c>IsBackstageOpen</c> and Back closes
/// the shell's backstage; standing alone, bind <see cref="IsOpen"/>.
/// </remarks>
public partial class OfficeBackstage : ComponentBase
{
    OfficeShellOptions? fallbackOptions;

    [CascadingParameter] public OfficeShell? Shell { get; set; }

    /// <summary>The app. Null takes the shell's.</summary>
    [Parameter] public OfficeApp? App { get; set; }

    [Parameter] public string? AccentColor { get; set; }

    /// <summary>Whether it is showing, when not in a shell. Two-way bindable.</summary>
    [Parameter] public bool IsOpen { get; set; }

    [Parameter] public EventCallback<bool> IsOpenChanged { get; set; }

    /// <summary>The page showing. Two-way bindable; opens on Home.</summary>
    [Parameter] public OfficeBackstagePage SelectedPage { get; set; } = OfficeBackstagePage.Home;

    [Parameter] public EventCallback<OfficeBackstagePage> SelectedPageChanged { get; set; }

    /// <summary>The open document's name, for the Info page.</summary>
    [Parameter] public string? DocumentName { get; set; }

    /// <summary>The templates on Home and New. The blank one is added when not in the list.</summary>
    [Parameter] public IReadOnlyList<OfficeTemplate>? Templates { get; set; }

    /// <summary>How many templates Home shows before "More templates". Default 6.</summary>
    [Parameter] public int HomeTemplateCount { get; set; } = 6;

    [Parameter] public IReadOnlyList<OfficeRecentFile>? RecentFiles { get; set; }

    /// <summary>What the Info page says about the document.</summary>
    [Parameter] public OfficeDocumentInfo? DocumentInfo { get; set; }

    [Parameter] public bool ShowHistory { get; set; }

    /// <summary>Save As choices. Null uses the app's.</summary>
    [Parameter] public IReadOnlyList<OfficeFileFormat>? SaveAsFormats { get; set; }

    /// <summary>Export choices. Null uses the app's.</summary>
    [Parameter] public IReadOnlyList<OfficeFileFormat>? ExportFormats { get; set; }

    /// <summary>The settings the Options page edits in place.</summary>
    [Parameter] public OfficeShellOptions? Options { get; set; }

    /// <summary>The Print page's preview — the editor rendering its pages.</summary>
    [Parameter] public RenderFragment? PrintPreview { get; set; }

    /// <summary>Extra settings under the Print button — printer, copies, range.</summary>
    [Parameter] public RenderFragment? PrintSettings { get; set; }

    /// <summary>The History page's content — the host's version list.</summary>
    [Parameter] public RenderFragment? HistoryContent { get; set; }

    [Parameter] public EventCallback<OfficeTemplate> TemplateSelected { get; set; }

    [Parameter] public EventCallback<OfficeRecentFile> RecentFileSelected { get; set; }

    /// <summary>Browse was pressed on the Open page — show a file picker.</summary>
    [Parameter] public EventCallback OpenRequested { get; set; }

    [Parameter] public EventCallback SaveRequested { get; set; }

    [Parameter] public EventCallback<OfficeFileFormat> SaveAsRequested { get; set; }

    [Parameter] public EventCallback<OfficeFileFormat> ExportRequested { get; set; }

    [Parameter] public EventCallback PrintRequested { get; set; }

    [Parameter] public EventCallback ProtectRequested { get; set; }

    [Parameter] public EventCallback InspectRequested { get; set; }

    /// <summary>Raised after the Options page changes a setting.</summary>
    [Parameter] public EventCallback<OfficeShellOptions> OptionsChanged { get; set; }

    [Parameter] public EventCallback Closed { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? AdditionalAttributes { get; set; }


    internal OfficeAppInfo Info => OfficeAppInfo.For(this.App ?? this.Shell?.App ?? OfficeApp.Word);

    internal bool IsVisible => this.IsOpen || this.Shell?.IsBackstageOpen == true;

    internal IReadOnlyList<OfficeBackstageEntry> Entries => OfficeBackstageText.Entries(this.ShowHistory);

    internal IReadOnlyList<OfficeTemplate> AllTemplates => OfficeBackstageText.WithBlank(this.Info.App, this.Templates);

    IReadOnlyList<OfficeRecentFile> OrderedRecent => OfficeBackstageText.Order(this.RecentFiles);

    IReadOnlyList<OfficeFileFormat> EffectiveSaveAsFormats => this.SaveAsFormats ?? this.Info.SaveAsFormats;

    IReadOnlyList<OfficeFileFormat> EffectiveExportFormats => this.ExportFormats ?? this.Info.ExportFormats;

    OfficeShellOptions EffectiveOptions => this.Options ?? (this.fallbackOptions ??= new OfficeShellOptions());

    string EffectiveName => string.IsNullOrWhiteSpace(this.DocumentName) ? this.Info.DefaultDocumentName : this.DocumentName!;

    /// <summary>"Document", "Workbook", "Presentation" — the Protect / Inspect cards' titles.</summary>
    string TitleNoun => this.Info.App switch
    {
        OfficeApp.Excel => "Workbook",
        OfficeApp.PowerPoint => "Presentation",
        OfficeApp.OneNote => "Notebook",
        _ => "Document"
    };

    string? RootStyle
        => this.App is not null || this.Shell is null || this.AccentColor is not null
            ? OfficeShellStyle.AccentVariables(this.Info, this.AccentColor)
            : null;


    /// <summary>Shows a page, or — for Save — saves and closes.</summary>
    public async Task SelectPageAsync(OfficeBackstagePage page)
    {
        if (page == OfficeBackstagePage.Save)
        {
            await this.SaveAsync();
            return;
        }

        if (page == this.SelectedPage)
            return;

        this.SelectedPage = page;
        await this.SelectedPageChanged.InvokeAsync(page);
    }

    Task OnEntryAsync(OfficeBackstageEntry entry) => this.SelectPageAsync(entry.Page);

    /// <summary>Closes the backstage, as Back and Escape do.</summary>
    public async Task CloseAsync()
    {
        this.IsOpen = false;
        await this.IsOpenChanged.InvokeAsync(false);

        if (this.Shell is { } shell)
            await shell.CloseBackstageAsync();

        await this.Closed.InvokeAsync();
    }

    public async Task SaveAsync()
    {
        await this.SaveRequested.InvokeAsync();
        await this.CloseAsync();
    }

    public async Task ChooseTemplateAsync(OfficeTemplate template)
    {
        await this.TemplateSelected.InvokeAsync(template);
        await this.CloseAsync();
    }

    public async Task ChooseRecentAsync(OfficeRecentFile file)
    {
        await this.RecentFileSelected.InvokeAsync(file);
        await this.CloseAsync();
    }

    public async Task ChooseSaveAsAsync(OfficeFileFormat format)
    {
        await this.SaveAsRequested.InvokeAsync(format);
        await this.CloseAsync();
    }

    public async Task ChooseExportAsync(OfficeFileFormat format)
    {
        await this.ExportRequested.InvokeAsync(format);
        await this.CloseAsync();
    }

    public async Task OpenAsync()
    {
        await this.OpenRequested.InvokeAsync();
        await this.CloseAsync();
    }

    public Task PrintAsync() => this.PrintRequested.InvokeAsync();

    async Task SetUserNameAsync(string? name)
    {
        this.EffectiveOptions.UserName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        await this.OptionsChanged.InvokeAsync(this.EffectiveOptions);
    }

    async Task SetInitialsAsync(string? initials)
    {
        this.EffectiveOptions.Initials = string.IsNullOrWhiteSpace(initials) ? null : initials.Trim();
        await this.OptionsChanged.InvokeAsync(this.EffectiveOptions);
    }

    async Task SetThemeAsync(OfficeThemeChoice theme)
    {
        this.EffectiveOptions.Theme = theme;
        await this.OptionsChanged.InvokeAsync(this.EffectiveOptions);
    }

    async Task SetAutoSaveAsync(bool on)
    {
        this.EffectiveOptions.AutoSave = on;
        await this.OptionsChanged.InvokeAsync(this.EffectiveOptions);
    }
}
