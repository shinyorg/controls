using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Shiny.Controls.Office.Shell;

namespace Shiny.Blazor.Controls.Office;

/// <summary>
/// The navigation pane: document search, a heading tree, optional page thumbnails and search results.
/// </summary>
public partial class OfficeNavigationPane : ComponentBase
{
    readonly HashSet<string> collapsed = new(StringComparer.Ordinal);
    IReadOnlyList<OfficeHeadingNode> rows = [];
    IReadOnlyList<OfficeHeading>? builtFrom;

    [CascadingParameter] public OfficeShell? Shell { get; set; }

    /// <summary>The document's headings in document order.</summary>
    [Parameter] public IReadOnlyList<OfficeHeading>? Headings { get; set; }

    /// <summary>The heading the caret is under — highlighted.</summary>
    [Parameter] public string? CurrentHeadingId { get; set; }

    [Parameter] public EventCallback<OfficeHeading> HeadingSelected { get; set; }

    /// <summary>The search box's text. Two-way bindable.</summary>
    [Parameter] public string? SearchText { get; set; }

    [Parameter] public EventCallback<string?> SearchTextChanged { get; set; }

    /// <summary>Raised when Enter is pressed in the search box — the editor finds and fills <see cref="SearchResults"/>.</summary>
    [Parameter] public EventCallback<string> SearchRequested { get; set; }

    [Parameter] public IReadOnlyList<OfficeSearchResult>? SearchResults { get; set; }

    [Parameter] public EventCallback<OfficeSearchResult> ResultSelected { get; set; }

    [Parameter] public OfficeNavigationTab SelectedTab { get; set; }

    [Parameter] public EventCallback<OfficeNavigationTab> SelectedTabChanged { get; set; }

    [Parameter] public bool ShowPagesTab { get; set; }

    /// <summary>The Pages tab — the host's page thumbnails.</summary>
    [Parameter] public RenderFragment? PagesContent { get; set; }

    [Parameter] public string SearchPlaceholder { get; set; } = "Search document";

    [Parameter] public bool ShowClose { get; set; } = true;

    /// <summary>Raised by the close button. With no handler, closes the shell's left pane.</summary>
    [Parameter] public EventCallback CloseRequested { get; set; }


    /// <summary>The rows the Headings tab draws, with collapsed branches left out.</summary>
    internal IReadOnlyList<OfficeHeadingNode> Rows => this.rows;


    protected override void OnParametersSet()
    {
        if (!ReferenceEquals(this.builtFrom, this.Headings))
        {
            this.builtFrom = this.Headings;
            this.Rebuild();
        }

        if (!this.ShowPagesTab && this.SelectedTab == OfficeNavigationTab.Pages)
            this.SelectedTab = OfficeNavigationTab.Headings;
    }

    void Rebuild() => this.rows = OfficeHeadingTree.Flatten(OfficeHeadingTree.Build(this.Headings), this.collapsed);

    internal void Toggle(OfficeHeadingNode node)
    {
        if (!this.collapsed.Remove(node.Heading.Id))
            this.collapsed.Add(node.Heading.Id);

        this.Rebuild();
    }

    Task SelectHeadingAsync(OfficeHeading heading) => this.HeadingSelected.InvokeAsync(heading);

    public async Task SelectTabAsync(OfficeNavigationTab tab)
    {
        if (tab == this.SelectedTab)
            return;

        this.SelectedTab = tab;
        await this.SelectedTabChanged.InvokeAsync(tab);
    }

    async Task OnSearchInputAsync(ChangeEventArgs e)
    {
        this.SearchText = e.Value?.ToString();
        await this.SearchTextChanged.InvokeAsync(this.SearchText);
    }

    async Task OnSearchKeyAsync(KeyboardEventArgs e)
    {
        if (e.Key != "Enter")
            return;

        await this.SearchAsync(this.SearchText);
    }

    /// <summary>Runs a search as Enter in the box does: raises <see cref="SearchRequested"/> and shows Results.</summary>
    public async Task SearchAsync(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        await this.SelectTabAsync(OfficeNavigationTab.Results);
        await this.SearchRequested.InvokeAsync(text.Trim());
    }

    async Task CloseAsync()
    {
        if (this.CloseRequested.HasDelegate)
            await this.CloseRequested.InvokeAsync();
        else if (this.Shell is { } shell)
            await shell.SetLeftPaneOpenAsync(false);
    }
}
