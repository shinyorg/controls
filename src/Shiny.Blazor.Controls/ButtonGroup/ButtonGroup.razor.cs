using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;

namespace Shiny.Blazor.Controls;

/// <summary>
/// Joins related buttons into one segmented control — inner corners square off and adjacent outlines
/// collapse into a single edge.
/// </summary>
/// <remarks>
/// <para>
/// The parameter surface mirrors the MAUI <c>ButtonGroup</c>. It is a container for <em>actions</em>
/// first; <see cref="SelectionMode"/> is opt-in on top of that and turns the same markup into a
/// segmented picker. Leave it <see cref="ButtonGroupSelectionMode.None"/> (the default) and every
/// segment keeps its own <c>Clicked</c> and nothing here touches its appearance.
/// </para>
/// <para>
/// A nested <see cref="ButtonGroup"/> turns the outer one into a <em>cluster</em>: each inner group keeps
/// its own merged edges and the two are spaced apart, which is what makes an undo/redo pair beside a
/// bold/italic/underline trio read as two things rather than five.
/// </para>
/// </remarks>
public partial class ButtonGroup
{
    readonly List<ShinyButton> segments = new();
    readonly List<int> selection = new();

    int nestedGroups;
    int lastSelectedIndexParameter = -1;
    bool hasRendered;

    /// <summary>The group this group is nested inside, if any. Public because a private cascaded parameter is silently skipped.</summary>
    [CascadingParameter] public ButtonGroup? ParentGroup { get; set; }

    /// <summary>The segments: <c>ShinyButton</c>s, optionally with separators and text segments between them.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Which way the segments run. Horizontal by default.</summary>
    [Parameter] public ToolbarOrientation Orientation { get; set; } = ToolbarOrientation.Horizontal;

    /// <summary>
    /// Whether the group carries selection state. <see cref="ButtonGroupSelectionMode.None"/> by default:
    /// a group of actions has nothing selected, and turning this on is what makes it a picker.
    /// </summary>
    [Parameter] public ButtonGroupSelectionMode SelectionMode { get; set; } = ButtonGroupSelectionMode.None;

    /// <summary>
    /// The selected segment, or -1 for none. Supports <c>@bind-SelectedIndex</c>, and in
    /// <see cref="ButtonGroupSelectionMode.Multiple"/> it reports the first selected segment.
    /// </summary>
    [Parameter] public int SelectedIndex { get; set; } = -1;

    /// <inheritdoc cref="SelectedIndex"/>
    [Parameter] public EventCallback<int> SelectedIndexChanged { get; set; }

    /// <summary>Every selected index, in ascending order. Supports <c>@bind-SelectedIndexes</c>.</summary>
    [Parameter] public IReadOnlyList<int>? SelectedIndexes { get; set; }

    /// <inheritdoc cref="SelectedIndexes"/>
    [Parameter] public EventCallback<IReadOnlyList<int>> SelectedIndexesChanged { get; set; }

    /// <summary>
    /// Whether clicking the selected segment in <see cref="ButtonGroupSelectionMode.Single"/> clears the
    /// selection. Off by default. Ignored in multiple mode, where a second click always toggles.
    /// </summary>
    [Parameter] public bool AllowDeselect { get; set; }

    /// <summary>The appearance a selected segment takes while the group is in a selection mode.</summary>
    [Parameter] public ButtonAppearance SelectedAppearance { get; set; } = ButtonAppearance.Filled;

    /// <summary>
    /// The appearance an unselected segment takes — unless the segment set an <c>Appearance</c> of its
    /// own, which wins, so one odd segment in a picker stays odd.
    /// </summary>
    [Parameter] public ButtonAppearance UnselectedAppearance { get; set; } = ButtonAppearance.Outlined;

    /// <summary>
    /// Pulls each segment a hairline into the one before it, so two adjacent outlines paint as one edge
    /// rather than two. On by default.
    /// </summary>
    [Parameter] public bool CollapseBorders { get; set; } = true;

    /// <summary>
    /// The radius of the group's four outer corners, in px. The default, <c>-1</c>, follows the theme's
    /// medium corner token — the same one an ungrouped button uses.
    /// </summary>
    [Parameter] public double CornerRadius { get; set; } = -1d;

    /// <summary>Extra classes on the group element.</summary>
    [Parameter] public string? CssClass { get; set; }

    /// <summary>Raised whenever the selection changes, however it changed.</summary>
    [Parameter] public EventCallback<IReadOnlyList<int>> SelectionChanged { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? AdditionalAttributes { get; set; }


    // ---------------------------------------------------------------------------------------------
    // Lifecycle
    // ---------------------------------------------------------------------------------------------

    protected override void OnInitialized()
    {
        this.ParentGroup?.RegisterNested();
        this.SyncFromParameters(first: true);
    }


    protected override void OnParametersSet() => this.SyncFromParameters(first: false);


    /// <summary>
    /// Takes the selection from the parameters when the parent actually changed one. The shadow is what
    /// makes "did the parent change it" answerable: without it, a parent re-supplying the value it
    /// already had would undo a click the moment the group re-rendered.
    /// </summary>
    void SyncFromParameters(bool first)
    {
        if (this.SelectionMode == ButtonGroupSelectionMode.None)
        {
            if (this.selection.Count > 0)
            {
                this.selection.Clear();
                this.NotifySegments();
            }
            return;
        }

        if (this.SelectedIndexes is { } indexes && (first || !this.SameAsSelection(indexes)))
        {
            this.selection.Clear();
            this.selection.AddRange(indexes.Distinct().Where(x => x >= 0).OrderBy(x => x));
            this.lastSelectedIndexParameter = this.SelectedIndex;
            this.NotifySegments();
            return;
        }

        if (first || this.SelectedIndex != this.lastSelectedIndexParameter)
        {
            this.lastSelectedIndexParameter = this.SelectedIndex;

            this.selection.Clear();
            if (this.SelectedIndex >= 0)
                this.selection.Add(this.SelectedIndex);

            this.NotifySegments();
        }
    }


    bool SameAsSelection(IReadOnlyList<int> indexes)
        => indexes.Count == this.selection.Count && indexes.All(this.selection.Contains);


    // ---------------------------------------------------------------------------------------------
    // Segment registration
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Adds a segment. Registration order is render order, which is what gives a segment its index.
    /// </summary>
    /// <remarks>
    /// It deliberately does not re-render the group. A child that tells its host to re-render from its
    /// own parameter pass puts the renderer in a loop — the host re-renders, the child's parameters are
    /// set again, and round it goes. Nothing about the group's own markup depends on the list anyway:
    /// each segment asks the group what it should look like as it renders itself.
    /// </remarks>
    internal void Register(ShinyButton button)
    {
        if (!this.segments.Contains(button))
            this.segments.Add(button);
    }


    internal void Unregister(ShinyButton button) => this.segments.Remove(button);

    internal void RegisterNested()
    {
        this.nestedGroups++;

        // One re-render, and only the first time, so the cluster class can appear. See Register for why
        // this is otherwise avoided.
        if (this.nestedGroups == 1)
            this.InvokeAsync(this.Repaint);
    }


    /// <summary>The index of a segment among the selectable ones, or -1 when it is not one of ours.</summary>
    internal int IndexOf(ShinyButton button) => this.segments.IndexOf(button);


    /// <summary>
    /// What a segment should paint as. Null means "the group has no opinion" — which is every segment of
    /// a group that is not in a selection mode.
    /// </summary>
    internal ButtonAppearance? AppearanceFor(ShinyButton button, bool appearanceSupplied)
    {
        if (this.SelectionMode == ButtonGroupSelectionMode.None)
            return null;

        var index = this.IndexOf(button);
        if (index < 0)
            return null;

        if (this.selection.Contains(index))
            return this.SelectedAppearance;

        return appearanceSupplied ? button.Appearance : this.UnselectedAppearance;
    }


    internal bool IsSelected(ShinyButton button)
    {
        var index = this.IndexOf(button);
        return index >= 0 && this.selection.Contains(index);
    }


    // ---------------------------------------------------------------------------------------------
    // Selection
    // ---------------------------------------------------------------------------------------------

    internal async Task NotifyClickedAsync(ShinyButton button)
    {
        if (this.SelectionMode == ButtonGroupSelectionMode.None)
            return;

        var index = this.IndexOf(button);
        if (index < 0)
            return;

        var isSelected = this.selection.Contains(index);

        // Re-clicking the selected segment is a no-op unless deselection is allowed: a picker that can be
        // emptied by clicking its own answer again is a picker with no answer.
        if (this.SelectionMode == ButtonGroupSelectionMode.Single && isSelected && !this.AllowDeselect)
            return;

        await this.ApplySelectionAsync(index, !isSelected);
    }


    /// <summary>Select the segment at <paramref name="index"/>.</summary>
    public Task SelectAsync(int index) => this.ApplySelectionAsync(index, true);

    /// <summary>Deselect the segment at <paramref name="index"/>.</summary>
    public Task DeselectAsync(int index) => this.ApplySelectionAsync(index, false);

    /// <summary>Clear the selection entirely.</summary>
    public async Task ClearSelectionAsync()
    {
        if (this.selection.Count == 0)
            return;

        this.selection.Clear();
        await this.AfterSelectionChangedAsync();
    }


    async Task ApplySelectionAsync(int index, bool selected)
    {
        if (this.SelectionMode == ButtonGroupSelectionMode.None || index < 0)
            return;

        if (selected)
        {
            if (this.SelectionMode == ButtonGroupSelectionMode.Single)
                this.selection.Clear();

            if (!this.selection.Contains(index))
                this.selection.Add(index);
        }
        else
        {
            this.selection.Remove(index);
        }

        this.selection.Sort();
        await this.AfterSelectionChangedAsync();
    }


    async Task AfterSelectionChangedAsync()
    {
        var indexes = this.selection.ToList();
        var first = indexes.Count == 0 ? -1 : indexes[0];

        this.SelectedIndex = first;
        this.lastSelectedIndexParameter = first;
        this.SelectedIndexes = indexes;

        this.NotifySegments();
        this.Repaint();

        if (this.SelectedIndexChanged.HasDelegate)
            await this.SelectedIndexChanged.InvokeAsync(first);

        if (this.SelectedIndexesChanged.HasDelegate)
            await this.SelectedIndexesChanged.InvokeAsync(indexes);

        if (this.SelectionChanged.HasDelegate)
            await this.SelectionChanged.InvokeAsync(indexes);
    }


    /// <summary>
    /// Re-render, but only once there is something to re-render: a nested group registers during the
    /// outer one's own first render pass, and asking for a repaint before the render handle exists
    /// throws.
    /// </summary>
    void Repaint()
    {
        if (this.hasRendered)
            this.StateHasChanged();
    }


    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender)
            this.hasRendered = true;
    }


    void NotifySegments()
    {
        foreach (var segment in this.segments)
            segment.NotifyGroupChanged();
    }


    // ---------------------------------------------------------------------------------------------
    // Rendering
    // ---------------------------------------------------------------------------------------------

    string CssClasses
    {
        get
        {
            var sb = new StringBuilder("shiny-btn-group");

            sb.Append(this.Orientation is ToolbarOrientation.Vertical
                ? " shiny-btn-group--vertical"
                : " shiny-btn-group--horizontal");

            if (this.nestedGroups > 0)
                sb.Append(" shiny-btn-group--cluster");
            else if (this.CollapseBorders)
                sb.Append(" shiny-btn-group--collapse");

            if (!String.IsNullOrEmpty(this.CssClass))
                sb.Append(' ').Append(this.CssClass);

            return sb.ToString();
        }
    }


    string? InlineStyle
        => this.CornerRadius >= 0
            ? String.Format(CultureInfo.InvariantCulture, "--shiny-btn-group-radius:{0}px;", this.CornerRadius)
            : null;
}
