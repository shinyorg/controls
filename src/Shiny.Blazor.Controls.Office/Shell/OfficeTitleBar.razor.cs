using Shiny.Controls.Office.Icons;
using Shiny.Controls.Office.Shell;
using System.ComponentModel;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls.Office;

/// <summary>
/// The Office title bar: app tile, AutoSave, quick access (Save / Undo / Redo and extras), document
/// name with a rename dropdown, save status, the "Search for tools, help, and more" command search,
/// help and the account avatar.
/// </summary>
public partial class OfficeTitleBar : ComponentBase, IDisposable
{
    [Inject] IJSRuntime Js { get; set; } = default!;

    readonly string renameId = $"otb-{Guid.NewGuid():N}";
    ElementReference searchInput;
    bool docMenuOpen;
    string renameText = string.Empty;
    string query = string.Empty;
    bool searchFocused;
    bool searchExpanded;
    int active;
    IReadOnlyList<OfficeCommandMatch> results = [];
    OfficeShellOptions? observedOptions;

    /// <summary>The shell this bar is in, when it is in one. Public because a private cascade is skipped.</summary>
    [CascadingParameter] public OfficeShell? Shell { get; set; }

    /// <summary>The app. Null takes the shell's (or Word).</summary>
    [Parameter] public OfficeApp? App { get; set; }

    /// <summary>Overrides the app's accent. Any CSS colour.</summary>
    [Parameter] public string? AccentColor { get; set; }

    /// <summary>The document's name. Two-way bindable; the rename dropdown writes it.</summary>
    [Parameter] public string? DocumentName { get; set; }

    [Parameter] public EventCallback<string?> DocumentNameChanged { get; set; }

    /// <summary>Raised after a rename is committed, with the new name.</summary>
    [Parameter] public EventCallback<string> DocumentRenamed { get; set; }

    /// <summary>Where the document lives, shown in the name dropdown.</summary>
    [Parameter] public string? DocumentLocation { get; set; }

    /// <summary>Offers "Open file location" in the name dropdown.</summary>
    [Parameter] public EventCallback LocationRequested { get; set; }

    /// <summary>Drives the save status text beside the name.</summary>
    [Parameter] public OfficeSaveState SaveState { get; set; }

    /// <summary>Overrides the save status text.</summary>
    [Parameter] public string? SaveStatusText { get; set; }

    [Parameter] public bool ShowAutoSave { get; set; } = true;

    /// <summary>The AutoSave switch. Two-way bindable. Ignored when <see cref="Options"/> is given, which holds it instead.</summary>
    [Parameter] public bool AutoSave { get; set; }

    [Parameter] public EventCallback<bool> AutoSaveChanged { get; set; }

    [Parameter] public bool ShowSave { get; set; } = true;

    [Parameter] public bool ShowUndo { get; set; } = true;

    [Parameter] public bool ShowRedo { get; set; } = true;

    [Parameter] public bool CanUndo { get; set; } = true;

    [Parameter] public bool CanRedo { get; set; } = true;

    [Parameter] public EventCallback SaveRequested { get; set; }

    [Parameter] public EventCallback UndoRequested { get; set; }

    [Parameter] public EventCallback RedoRequested { get; set; }

    /// <summary>More quick access buttons after Save / Undo / Redo.</summary>
    [Parameter] public IReadOnlyList<OfficeQuickAccessItem>? QuickAccessItems { get; set; }

    /// <summary>What the search box searches. Null shows the box but finds nothing.</summary>
    [Parameter] public OfficeCommandIndex? CommandIndex { get; set; }

    [Parameter] public string SearchPlaceholder { get; set; } = "Search for tools, help, and more (Alt + Q)";

    /// <summary>Raised when Enter is pressed on a query no command matches — the host's cue to search the document.</summary>
    [Parameter] public EventCallback<string> SearchSubmitted { get; set; }

    /// <summary>Raised after a command is run from the search, with the command.</summary>
    [Parameter] public EventCallback<OfficeCommand> CommandExecuted { get; set; }

    [Parameter] public bool ShowHelp { get; set; } = true;

    [Parameter] public bool ShowSearch { get; set; } = true;

    [Parameter] public bool ShowAvatar { get; set; } = true;

    [Parameter] public EventCallback HelpRequested { get; set; }

    /// <summary>The signed-in user's name, for the avatar's initials and tooltip.</summary>
    [Parameter] public string? UserName { get; set; }

    /// <summary>The user settings. When given, the avatar and AutoSave switch follow it.</summary>
    [Parameter] public OfficeShellOptions? Options { get; set; }

    [Parameter] public EventCallback AccountRequested { get; set; }

    /// <summary>Forces the compact layout (search as an icon, no save status). Null follows the shell.</summary>
    [Parameter] public bool? Compact { get; set; }

    /// <summary>Keeps the document name visible in the compact layout.</summary>
    [Parameter] public bool ShowNameWhenCompact { get; set; } = true;

    [Parameter] public string? CssClass { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? AdditionalAttributes { get; set; }


    internal OfficeAppInfo Info => OfficeAppInfo.For(this.App ?? this.Shell?.App ?? OfficeApp.Word);

    internal bool IsCompact => this.Compact ?? this.Shell?.ShellLayout.IsCompact ?? false;

    string EffectiveName => string.IsNullOrWhiteSpace(this.DocumentName) ? this.Info.DefaultDocumentName : this.DocumentName!;

    internal string? SaveStatus => this.SaveStatusText ?? OfficeSaveStateText.For(this.SaveState);

    bool EffectiveAutoSave => this.Options?.AutoSave ?? this.AutoSave;

    string? EffectiveUserName => this.Options?.UserName ?? this.UserName;

    string EffectiveInitials => this.Options?.EffectiveInitials ?? OfficeColorText.Initials(this.UserName);

    bool ShowResults => this.searchFocused && this.query.Trim().Length > 0;

    /// <summary>The matches the dropdown is showing.</summary>
    internal IReadOnlyList<OfficeCommandMatch> Results => this.results;

    string SearchCss
        => "office-titlebar__search"
           + (this.searchExpanded ? " is-expanded" : null)
           + (this.ShowSearch ? null : " is-hidden");

    string RootCss
        => string.Join(' ', new[] { "office-titlebar", this.IsCompact ? "is-compact" : null, this.CssClass }.Where(x => x is not null));

    /// <summary>Inside a shell the accent is inherited; standing alone, or given its own app, the bar carries it.</summary>
    string? RootStyle
        => this.App is not null || this.Shell is null || this.AccentColor is not null
            ? OfficeShellStyle.AccentVariables(this.Info, this.AccentColor)
            : null;


    protected override void OnParametersSet()
    {
        if (!ReferenceEquals(this.observedOptions, this.Options))
        {
            if (this.observedOptions is not null)
                this.observedOptions.PropertyChanged -= this.OnOptionsChanged;

            this.observedOptions = this.Options;
            if (this.observedOptions is not null)
                this.observedOptions.PropertyChanged += this.OnOptionsChanged;
        }
    }

    void OnOptionsChanged(object? sender, PropertyChangedEventArgs e) => _ = this.InvokeAsync(this.StateHasChanged);


    // ---- document name ----

    void ToggleDocMenu()
    {
        this.docMenuOpen = !this.docMenuOpen;
        this.renameText = this.EffectiveName;
    }

    void CloseDocMenu() => this.docMenuOpen = false;

    async Task OnRenameKeyAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
            await this.CommitRenameAsync();
        else if (e.Key == "Escape")
            this.docMenuOpen = false;
    }

    /// <summary>Renames the document as the dropdown's Rename button does. Blank names are ignored.</summary>
    internal async Task RenameAsync(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0 || trimmed == this.DocumentName)
            return;

        this.DocumentName = trimmed;
        await this.DocumentNameChanged.InvokeAsync(trimmed);
        await this.DocumentRenamed.InvokeAsync(trimmed);
    }

    async Task CommitRenameAsync()
    {
        await this.RenameAsync(this.renameText);
        this.docMenuOpen = false;
    }

    async Task OnLocationAsync()
    {
        this.docMenuOpen = false;
        await this.LocationRequested.InvokeAsync();
    }


    // ---- autosave ----

    async Task ToggleAutoSaveAsync()
    {
        var next = !this.EffectiveAutoSave;

        if (this.Options is not null)
            this.Options.AutoSave = next;

        this.AutoSave = next;
        await this.AutoSaveChanged.InvokeAsync(next);
    }


    // ---- search ----

    /// <summary>Sets the query and recomputes the matches, as typing does.</summary>
    internal void SetQuery(string text)
    {
        this.query = text;
        this.active = 0;
        this.results = this.CommandIndex?.Search(text) ?? [];
    }

    void OnQueryInput(ChangeEventArgs e) => this.SetQuery(e.Value?.ToString() ?? string.Empty);

    async Task OnSearchKeyAsync(KeyboardEventArgs e)
    {
        switch (e.Key)
        {
            case "ArrowDown" when this.results.Count > 0:
                this.active = (this.active + 1) % this.results.Count;
                break;

            case "ArrowUp" when this.results.Count > 0:
                this.active = (this.active - 1 + this.results.Count) % this.results.Count;
                break;

            case "Enter":
                await this.SubmitAsync();
                break;

            case "Escape":
                this.SetQuery(string.Empty);
                this.searchExpanded = false;
                break;
        }
    }

    /// <summary>Runs the highlighted match, or raises <see cref="SearchSubmitted"/> when nothing matched.</summary>
    internal async Task SubmitAsync()
    {
        if (this.results.Count > 0)
        {
            await this.RunAsync(this.results[Math.Clamp(this.active, 0, this.results.Count - 1)].Command);
            return;
        }

        if (this.query.Trim() is { Length: > 0 } text)
            await this.SearchSubmitted.InvokeAsync(text);
    }

    async Task RunAsync(OfficeCommand command)
    {
        if (!command.IsEnabled)
            return;

        this.SetQuery(string.Empty);
        this.searchExpanded = false;
        await command.Execute();
        await this.CommandExecuted.InvokeAsync(command);
    }

    void OnSearchBlur()
    {
        this.searchFocused = false;
        if (this.IsCompact && this.query.Length == 0)
            this.searchExpanded = false;
    }

    async Task ExpandSearchAsync()
    {
        this.searchExpanded = true;
        this.StateHasChanged();
        await Task.Yield();

        try
        {
            await this.searchInput.FocusAsync();
        }
        catch (InvalidOperationException) { }
        catch (JSException) { }
    }


    public void Dispose()
    {
        if (this.observedOptions is not null)
            this.observedOptions.PropertyChanged -= this.OnOptionsChanged;
    }
}
