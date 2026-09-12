using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;

namespace Shiny.Blazor.Controls;

/// <summary>
/// A bound set of chips: one per item in <see cref="ItemsSource"/>, selectable singly or in any number,
/// reported back through <see cref="SelectedItem"/> and <see cref="SelectedItems"/>.
/// </summary>
/// <typeparam name="TItem">The bound item's type.</typeparam>
/// <remarks>
/// <para>
/// The parameter surface mirrors the MAUI <c>ChipGroup</c>. The difference from <c>ButtonGroup</c> is
/// the same on both hosts: a button group's segments are written out in markup and its selection is an
/// <em>index</em>; a chip group's chips come from a collection and its selection is the items
/// themselves, so a view model binds the objects it already has.
/// </para>
/// <para>
/// Every decision the group makes — what a click does to the selection, what the cap refuses, what a
/// removal takes out of the source — is an ordinary method on this class rather than logic buried in an
/// event handler, so it can be driven without a renderer.
/// </para>
/// </remarks>
public partial class ChipGroup<TItem>
{
    readonly List<TItem> selection = new();

    IReadOnlyList<TItem>? lastSelectedItemsParameter;
    TItem? lastSelectedItemParameter;
    bool hasSelectedItemShadow;
    bool hasRendered;

    /// <summary>
    /// What the chips stand for. Anything enumerable will do — strings, enum values, entities — and a
    /// chip shows <see cref="DisplaySelector"/>'s answer, or the item's <c>ToString()</c>.
    /// </summary>
    [Parameter] public IEnumerable<TItem>? ItemsSource { get; set; }

    /// <summary>
    /// The selected item, or <c>null</c> for none. Supports <c>@bind-SelectedItem</c>, and in
    /// <see cref="ChipSelectionMode.Multiple"/> it reports the first selected item.
    /// </summary>
    /// <remarks>
    /// For a value type — an <c>int</c> or an <c>enum</c> — "nothing selected" has no representation
    /// here, because <c>default(TItem)</c> is a perfectly good item. Bind <see cref="SelectedItems"/>
    /// instead, or make the type nullable (<c>ChipGroup&lt;int?&gt;</c>).
    /// </remarks>
    [Parameter] public TItem? SelectedItem { get; set; }

    /// <inheritdoc cref="SelectedItem"/>
    [Parameter] public EventCallback<TItem?> SelectedItemChanged { get; set; }

    /// <summary>Everything selected. Supports <c>@bind-SelectedItems</c>, and is kept accurate in single mode too.</summary>
    [Parameter] public IReadOnlyList<TItem>? SelectedItems { get; set; }

    /// <inheritdoc cref="SelectedItems"/>
    [Parameter] public EventCallback<IReadOnlyList<TItem>> SelectedItemsChanged { get; set; }

    /// <summary>
    /// How many chips may be selected at once. <see cref="ChipSelectionMode.Single"/> by default —
    /// selection is what a chip group is for.
    /// </summary>
    [Parameter] public ChipSelectionMode SelectionMode { get; set; } = ChipSelectionMode.Single;

    /// <summary>
    /// Whether clicking the selected chip in <see cref="ChipSelectionMode.Single"/> clears the
    /// selection. Off by default. Ignored in multiple mode, where a second click always toggles.
    /// </summary>
    [Parameter] public bool AllowDeselect { get; set; }

    /// <summary>
    /// How many chips may be selected in <see cref="ChipSelectionMode.Multiple"/>, or 0 for no limit.
    /// At the cap a click on an unselected chip does nothing — the oldest selection is not dropped.
    /// </summary>
    [Parameter] public int MaxSelectionCount { get; set; }

    /// <summary>Whether a selected chip draws a leading check. On by default.</summary>
    [Parameter] public bool ShowSelectionCheck { get; set; } = true;

    /// <summary>Whether each chip carries a remove affordance. Off by default.</summary>
    [Parameter] public bool AllowRemove { get; set; }

    /// <summary>
    /// Keeps the chips crisp and their selection visible but blocks changing it. Unlike
    /// <see cref="Disabled"/>, which also dims the group.
    /// </summary>
    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>Dims the whole group and makes it inert.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>
    /// Whether chips flow onto as many lines as they need. Off, they stay on one line and the row
    /// scrolls sideways.
    /// </summary>
    [Parameter] public bool Wrap { get; set; } = true;

    /// <summary>What a chip shows for an item. Unset, that is the item's <c>ToString()</c>.</summary>
    [Parameter] public Func<TItem, string>? DisplaySelector { get; set; }

    /// <summary>
    /// Replaces a chip's label — the item is the context. The selection check and the remove button are
    /// not part of it: every chip keeps the same state and the same way out, however it is drawn.
    /// </summary>
    [Parameter] public RenderFragment<TItem>? ChipContent { get; set; }

    /// <summary>Raised for every click on a chip, including one that changes nothing.</summary>
    [Parameter] public EventCallback<TItem> ChipTapped { get; set; }

    /// <summary>Raised whenever the selection changes, however it changed.</summary>
    [Parameter] public EventCallback<IReadOnlyList<TItem>> SelectionChanged { get; set; }

    /// <summary>
    /// Vets a removal before it happens — return false to keep the chip. This is what makes a
    /// confirmation or a server round-trip possible: refuse it here, do the work, and take the item out
    /// of the collection yourself.
    /// </summary>
    [Parameter] public Func<TItem, bool>? ChipRemoving { get; set; }

    /// <summary>Raised after a chip is removed.</summary>
    [Parameter] public EventCallback<TItem> ChipRemoved { get; set; }

    /// <summary>An unselected chip's fill. Unset it is transparent, so an outlined chip stays outlined.</summary>
    [Parameter] public string? ChipBackgroundColor { get; set; }

    /// <summary>An unselected chip's ink. Unset follows the theme.</summary>
    [Parameter] public string? ChipTextColor { get; set; }

    /// <summary>A selected chip's fill. Unset follows the theme's secondary container.</summary>
    [Parameter] public string? SelectedChipBackgroundColor { get; set; }

    /// <summary>A selected chip's ink. Unset follows the theme.</summary>
    [Parameter] public string? SelectedChipTextColor { get; set; }

    /// <summary>An unselected chip's outline. Unset follows the theme. A selected chip draws none.</summary>
    [Parameter] public string? ChipBorderColor { get; set; }

    /// <summary>Chip corner radius in px. The default, <c>-1</c>, follows the theme.</summary>
    [Parameter] public double CornerRadius { get; set; } = -1d;

    /// <summary>Extra classes on the group.</summary>
    [Parameter] public string? CssClass { get; set; }

    /// <summary>Anything else lands on the group's container element.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? AdditionalAttributes { get; set; }


    // ---------------------------------------------------------------------------------------------
    // State
    // ---------------------------------------------------------------------------------------------

    /// <summary>The items the group currently has chips for, in order.</summary>
    public IReadOnlyList<TItem> Items
        => this.ItemsSource is null ? [] : this.ItemsSource.Where(static x => x is not null).ToList();

    /// <summary>Everything selected, in source order.</summary>
    public IReadOnlyList<TItem> Current => this.selection;

    /// <summary>Whether <paramref name="item"/> is currently selected.</summary>
    public bool IsSelected(TItem item) => this.IndexIn(this.selection, item) >= 0;


    protected override void OnInitialized() => this.TakeSelectionParameters();

    protected override void OnParametersSet() => this.TakeSelectionParameters();


    /// <summary>
    /// Adopts the bound selection when the parent actually changed it. The shadows are what make "did
    /// the parent change it" answerable: without them, a parent re-supplying the value it already had
    /// would undo a click the moment the group re-rendered.
    /// </summary>
    internal void TakeSelectionParameters()
    {
        if (!ReferenceEquals(this.SelectedItems, this.lastSelectedItemsParameter) && this.SelectedItems is not null)
        {
            this.lastSelectedItemsParameter = this.SelectedItems;
            this.Adopt(this.SelectedItems);
            this.MirrorSelectedItem();
            return;
        }

        if (!this.hasSelectedItemShadow || !Same(this.SelectedItem, this.lastSelectedItemParameter))
        {
            this.lastSelectedItemParameter = this.SelectedItem;
            this.hasSelectedItemShadow = true;
            this.Adopt(this.SelectedItem is null ? [] : [this.SelectedItem]);
            this.MirrorSelectedItems();
        }
    }


    /// <summary>Takes a bound selection as the truth, dropping anything the source does not offer.</summary>
    void Adopt(IEnumerable<TItem> source)
    {
        this.selection.Clear();

        if (this.SelectionMode == ChipSelectionMode.None)
            return;

        var items = this.Items;

        foreach (var item in source)
        {
            if (item is null || this.IndexIn(this.selection, item) >= 0)
                continue;

            // A selection with no chip to show for it is one the user cannot see and cannot undo. It is
            // only dropped once there is a source to judge it against - before that, everything stands.
            if (this.ItemsSource is not null && this.IndexIn(items, item) < 0)
                continue;

            this.selection.Add(item);

            if (this.SelectionMode == ChipSelectionMode.Single)
                break;
        }
    }


    /// <summary>
    /// Keeps the other half of the binding pair agreeing with what was just adopted, so a component
    /// bound only one way still reads correctly from both.
    /// </summary>
    /// <remarks>
    /// Neither of these invokes the matching change callback. Adoption is the parent telling the group
    /// what is selected; announcing it straight back would be a round trip that ends where it started,
    /// and doing it from a parameter pass is how a render loop begins.
    /// </remarks>
    void MirrorSelectedItem()
    {
        this.lastSelectedItemParameter = this.selection.Count == 0 ? default : this.selection[0];
        this.hasSelectedItemShadow = true;
        this.SelectedItem = this.lastSelectedItemParameter;
    }


    /// <inheritdoc cref="MirrorSelectedItem"/>
    void MirrorSelectedItems()
    {
        var snapshot = this.selection.ToList();
        this.lastSelectedItemsParameter = snapshot;
        this.SelectedItems = snapshot;
    }


    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender)
            this.hasRendered = true;
    }


    /// <summary>
    /// Re-render, but only once there is something to re-render: asking for a repaint before the render
    /// handle exists throws, and every click path runs through here.
    /// </summary>
    void Repaint()
    {
        if (this.hasRendered)
            this.StateHasChanged();
    }


    // ---------------------------------------------------------------------------------------------
    // Selection
    // ---------------------------------------------------------------------------------------------

    /// <summary>Clicks a chip, exactly as the user would. Returns whether the selection moved.</summary>
    public async Task<bool> TapAsync(TItem item)
    {
        if (this.Disabled || this.ReadOnly)
            return false;

        var at = this.IndexIn(this.selection, item);
        var changed = false;

        switch (this.SelectionMode)
        {
            case ChipSelectionMode.Single:
                if (at >= 0)
                {
                    // Re-clicking the answer is a no-op unless deselection is allowed: a picker that can
                    // be emptied by clicking its own answer again is a picker with no answer.
                    if (this.AllowDeselect)
                    {
                        this.selection.Clear();
                        changed = true;
                    }
                }
                else
                {
                    this.selection.Clear();
                    this.selection.Add(item);
                    changed = true;
                }
                break;

            case ChipSelectionMode.Multiple:
                if (at >= 0)
                {
                    this.selection.RemoveAt(at);
                    changed = true;
                }
                else if (this.MaxSelectionCount <= 0 || this.selection.Count < this.MaxSelectionCount)
                {
                    this.selection.Add(item);
                    changed = true;
                }
                break;
        }

        if (changed)
            await this.PublishAsync();

        if (this.ChipTapped.HasDelegate)
            await this.ChipTapped.InvokeAsync(item);

        return changed;
    }


    /// <summary>Selects <paramref name="item"/>. Returns false when it is not in the source, or the selection is capped.</summary>
    public Task<bool> SelectAsync(TItem item) => this.ApplySelectionAsync(item, true);

    /// <summary>Deselects <paramref name="item"/>. Returns false when it was not selected.</summary>
    public Task<bool> DeselectAsync(TItem item) => this.ApplySelectionAsync(item, false);


    /// <summary>Clears the selection entirely.</summary>
    public async Task ClearSelectionAsync()
    {
        if (this.selection.Count == 0)
            return;

        this.selection.Clear();
        await this.PublishAsync();
    }


    async Task<bool> ApplySelectionAsync(TItem item, bool selected)
    {
        if (this.SelectionMode == ChipSelectionMode.None || this.IndexIn(this.Items, item) < 0)
            return false;

        var at = this.IndexIn(this.selection, item);

        if (selected)
        {
            if (at >= 0)
                return false;

            if (this.SelectionMode == ChipSelectionMode.Single)
                this.selection.Clear();
            else if (this.MaxSelectionCount > 0 && this.selection.Count >= this.MaxSelectionCount)
                return false;

            this.selection.Add(item);
        }
        else
        {
            if (at < 0)
                return false;

            this.selection.RemoveAt(at);
        }

        await this.PublishAsync();
        return true;
    }


    async Task PublishAsync()
    {
        this.SortSelection();

        var snapshot = this.selection.ToList();
        var first = snapshot.Count == 0 ? default : snapshot[0];

        // The shadows move with the values, or the next parameter pass would read the parent's older
        // selection as a change and undo what was just clicked.
        this.lastSelectedItemsParameter = snapshot;
        this.SelectedItems = snapshot;
        this.lastSelectedItemParameter = first;
        this.hasSelectedItemShadow = true;
        this.SelectedItem = first;

        if (this.SelectedItemChanged.HasDelegate)
            await this.SelectedItemChanged.InvokeAsync(first);

        if (this.SelectedItemsChanged.HasDelegate)
            await this.SelectedItemsChanged.InvokeAsync(snapshot);

        if (this.SelectionChanged.HasDelegate)
            await this.SelectionChanged.InvokeAsync(snapshot);

        this.Repaint();
    }


    void SortSelection()
    {
        var items = this.Items;
        this.selection.Sort((a, b) => this.IndexIn(items, a).CompareTo(this.IndexIn(items, b)));
    }


    // ---------------------------------------------------------------------------------------------
    // Removal
    // ---------------------------------------------------------------------------------------------

    /// <summary>Removes a chip, exactly as its ✕ would. Returns false when it was refused.</summary>
    public async Task<bool> RemoveAsync(TItem item)
    {
        if (this.Disabled || this.ReadOnly)
            return false;

        if (this.ChipRemoving is not null && !this.ChipRemoving(item))
            return false;

        // Taken out of the source when the source is a list that can be written to. When it is not - a
        // LINQ projection, an array - the callback is the only signal and the parent owns the removal.
        if (this.ItemsSource is IList<TItem> list && !list.IsReadOnly)
        {
            var at = this.IndexIn(list.ToList(), item);
            if (at >= 0)
                list.RemoveAt(at);
        }

        var wasSelected = this.selection.RemoveAll(x => Same(x, item)) > 0;

        if (wasSelected)
            await this.PublishAsync();
        else
            this.Repaint();

        if (this.ChipRemoved.HasDelegate)
            await this.ChipRemoved.InvokeAsync(item);

        return true;
    }


    // ---------------------------------------------------------------------------------------------
    // Rendering
    // ---------------------------------------------------------------------------------------------

    /// <summary>What a chip shows for an item.</summary>
    public string DisplayFor(TItem item)
        => this.DisplaySelector is not null
            ? this.DisplaySelector(item)
            : item?.ToString() ?? String.Empty;


    static bool Same(TItem? a, TItem? b) => EqualityComparer<TItem?>.Default.Equals(a, b);


    int IndexIn(IReadOnlyList<TItem> list, TItem item)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (Same(list[i], item))
                return i;
        }

        return -1;
    }


    /// <summary>
    /// <c>aria-pressed</c> only where there is something to press: a group with no selection mode is a
    /// row of actions, and claiming a toggle state it does not have would announce one to a screen
    /// reader that never changes.
    /// </summary>
    string? AriaPressed(bool selected)
        => this.SelectionMode == ChipSelectionMode.None
            ? null
            : selected ? "true" : "false";


    string ChipClasses(bool selected)
        => selected ? "shiny-chips__chip is-selected" : "shiny-chips__chip";


    string CssClasses
    {
        get
        {
            var sb = new StringBuilder("shiny-chips");

            if (this.Disabled)
                sb.Append(" is-disabled");

            if (this.ReadOnly)
                sb.Append(" is-readonly");

            if (!this.Wrap)
                sb.Append(" is-nowrap");

            if (!String.IsNullOrEmpty(this.CssClass))
                sb.Append(' ').Append(this.CssClass);

            return sb.ToString();
        }
    }


    string? InlineStyle
    {
        get
        {
            var sb = new StringBuilder();

            // Only what the caller explicitly set: an inline declaration beats the scoped stylesheet
            // outright, so writing these unconditionally would make every themed rule dead on arrival.
            if (this.CornerRadius >= 0)
                sb.Append(CultureInfo.InvariantCulture, $"--shiny-chip-radius:{this.CornerRadius}px;");

            if (!String.IsNullOrEmpty(this.ChipBackgroundColor))
                sb.Append("--shiny-chip-bg:").Append(this.ChipBackgroundColor).Append(';');

            if (!String.IsNullOrEmpty(this.ChipTextColor))
                sb.Append("--shiny-chip-fg:").Append(this.ChipTextColor).Append(';');

            if (!String.IsNullOrEmpty(this.SelectedChipBackgroundColor))
                sb.Append("--shiny-chip-selected-bg:").Append(this.SelectedChipBackgroundColor).Append(';');

            if (!String.IsNullOrEmpty(this.SelectedChipTextColor))
                sb.Append("--shiny-chip-selected-fg:").Append(this.SelectedChipTextColor).Append(';');

            if (!String.IsNullOrEmpty(this.ChipBorderColor))
                sb.Append("--shiny-chip-stroke:").Append(this.ChipBorderColor).Append(';');

            return sb.Length == 0 ? null : sb.ToString();
        }
    }
}
