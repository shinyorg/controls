using Microsoft.AspNetCore.Components;

namespace Shiny.Blazor.Controls;

/// <summary>
/// A gallery of previews inside a ribbon group — Word's Styles, Excel's Cell Styles, PowerPoint's
/// Themes — that scrolls a row at a time in place and expands to a full grid.
/// </summary>
/// <example>
/// <code>
/// &lt;RibbonGallery TItem="MyStyle" Items="styles" SelectedItem="current" Columns="5"
///                ItemText="s =&gt; s.Name" ItemSelected="Apply"&gt;
///     &lt;ItemTemplate&gt;&lt;span style="@context.Css"&gt;AaBbCc&lt;/span&gt;&lt;small&gt;@context.Name&lt;/small&gt;&lt;/ItemTemplate&gt;
/// &lt;/RibbonGallery&gt;
/// </code>
/// </example>
public partial class RibbonGallery<TItem> : RibbonItemBase
{
    int first;
    object? lastSelected;

    /// <summary>The entries, in order.</summary>
    [Parameter] public IReadOnlyList<TItem>? Items { get; set; }

    /// <summary>Draws one entry. Without it the entry's <see cref="ItemText"/> is shown.</summary>
    [Parameter] public RenderFragment<TItem>? ItemTemplate { get; set; }

    /// <summary>The entry's name — its tooltip, its text without a template, and what the command search finds.</summary>
    [Parameter] public Func<TItem, string?>? ItemText { get; set; }

    /// <summary>The highlighted entry — the style under the caret. Two-way bindable.</summary>
    [Parameter] public TItem? SelectedItem { get; set; }

    [Parameter] public EventCallback<TItem?> SelectedItemChanged { get; set; }

    /// <summary>Raised when an entry is picked, whether or not it was already the selected one.</summary>
    [Parameter] public EventCallback<TItem> ItemSelected { get; set; }

    /// <summary>How many entries the in-ribbon strip shows across. Default 5.</summary>
    [Parameter] public int Columns { get; set; } = 5;

    /// <summary>How many rows the strip shows. Default 1 — one row of tall previews, the way Word draws Styles.</summary>
    [Parameter] public int Rows { get; set; } = 1;

    /// <summary>How many columns the expanded grid uses. Defaults to <see cref="Columns"/>.</summary>
    [Parameter] public int? ExpandedColumns { get; set; }

    /// <summary>Width of one cell. Any CSS length; default 72px.</summary>
    [Parameter] public string ItemWidth { get; set; } = "72px";

    /// <summary>Height of one cell. Any CSS length; default fills the ribbon's rows.</summary>
    [Parameter] public string? ItemHeight { get; set; }

    /// <summary>Content under the expanded grid — "Create a Style", "Clear Formatting", "Apply Styles…".</summary>
    [Parameter] public RenderFragment? PanelFooter { get; set; }

    /// <summary>The expand button's tooltip. Default "More".</summary>
    [Parameter] public string MoreText { get; set; } = "More";

    /// <summary>Lets <see cref="SelectedItem"/> be compared by something other than <c>Equals</c>.</summary>
    [Parameter] public IEqualityComparer<TItem>? Comparer { get; set; }


    /// <summary>Identifies the gallery to the placement pass, which anchors the panel against it.</summary>
    internal string Id { get; } = $"rgal-{Guid.NewGuid():N}";

    bool IsPanelOpen => this.Ribbon?.IsPanelOpen(this.Id) == true;

    int Count => this.Items?.Count ?? 0;

    int PageSize => Math.Max(1, this.Columns) * Math.Max(1, this.EffectiveRows);

    /// <summary>The simplified ribbon is one row, so the strip is too.</summary>
    int EffectiveRows => this.Ribbon?.DisplayMode == RibbonDisplayMode.Simplified ? 1 : Math.Max(1, this.Rows);

    /// <summary>The first entry the strip shows. Always a multiple of <see cref="Columns"/>.</summary>
    internal int FirstVisible => this.first;

    internal bool CanScrollBack => this.first > 0;

    internal bool CanScrollForward => this.first + this.PageSize < this.Count;

    IEnumerable<TItem> WindowItems => (this.Items ?? []).Skip(this.first).Take(this.PageSize);


    internal override IReadOnlyList<(string Text, Func<Task> Run)>? CommandChoices
        => this.ItemText is null || this.Items is null
            ? null
            : this.Items
                .Select(item => (Text: this.ItemText(item), Item: item))
                .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                .Select(x => (x.Text!, (Func<Task>)(() => this.PickAsync(x.Item))))
                .ToList();


    bool IsSelected(TItem item)
        => this.SelectedItem is not null && (this.Comparer ?? EqualityComparer<TItem>.Default).Equals(item, this.SelectedItem);


    protected override void OnParametersSet()
    {
        base.OnParametersSet();

        // Bring a newly selected entry into the strip - moving the caret into a Heading 2 should show
        // Heading 2 highlighted, not leave it scrolled off to the right.
        if (!Equals(this.SelectedItem, this.lastSelected))
        {
            this.lastSelected = this.SelectedItem;
            var index = this.IndexOfSelected();
            if (index >= 0)
                this.first = ScrollIntoView(this.first, index, this.PageSize, Math.Max(1, this.Columns), this.Count);
        }

        this.first = Math.Clamp(this.first, 0, MaxFirst(this.PageSize, Math.Max(1, this.Columns), this.Count));
    }


    int IndexOfSelected()
    {
        if (this.Items is null || this.SelectedItem is null)
            return -1;

        var comparer = this.Comparer ?? EqualityComparer<TItem>.Default;
        for (var i = 0; i < this.Items.Count; i++)
        {
            if (comparer.Equals(this.Items[i], this.SelectedItem))
                return i;
        }

        return -1;
    }


    /// <summary>Moves the strip a row back (-1) or forward (1).</summary>
    internal void Scroll(int direction)
    {
        var columns = Math.Max(1, this.Columns);
        this.first = Math.Clamp(this.first + (direction * columns), 0, MaxFirst(this.PageSize, columns, this.Count));
    }


    /// <summary>The largest first index — the start of the last row that still fills the strip.</summary>
    internal static int MaxFirst(int pageSize, int columns, int count)
    {
        if (count <= pageSize)
            return 0;

        var lastRowStart = ((count - 1) / columns) * columns;
        var rows = pageSize / columns;
        return Math.Max(0, lastRowStart - ((rows - 1) * columns));
    }


    /// <summary>The first index that puts <paramref name="selected"/> in view, moving as little as possible, row-aligned.</summary>
    internal static int ScrollIntoView(int first, int selected, int pageSize, int columns, int count)
    {
        if (selected < first)
            return (selected / columns) * columns;

        if (selected >= first + pageSize)
            return Math.Min(((selected / columns) * columns) - pageSize + columns, MaxFirst(pageSize, columns, count));

        return first;
    }


    void TogglePanel()
    {
        if (this.IsDisabled || this.Ribbon is not { } ribbon)
            return;

        if (this.IsPanelOpen)
            ribbon.CloseMenu();
        else
            ribbon.OpenPanel(this.Id);
    }


    async Task PickAsync(TItem item)
    {
        if (this.IsDisabled)
            return;

        this.Ribbon?.CloseMenu();
        this.SelectedItem = item;
        await this.SelectedItemChanged.InvokeAsync(item).ConfigureAwait(false);
        await this.ItemSelected.InvokeAsync(item).ConfigureAwait(false);
        this.Ribbon?.NotifyInvoked();
    }


    string GalleryStyle
    {
        get
        {
            var css = $"--shiny-gallery-cols:{Math.Max(1, this.Columns)};--shiny-gallery-rows:{this.EffectiveRows};--shiny-gallery-item-w:{this.ItemWidth};";
            if (!string.IsNullOrWhiteSpace(this.ItemHeight))
                css += $"--shiny-gallery-item-h:{this.ItemHeight};";

            return css;
        }
    }

    string GridStyle => $"grid-template-columns:repeat({Math.Max(1, this.ExpandedColumns ?? this.Columns)}, var(--shiny-gallery-item-w, 72px));";
}
