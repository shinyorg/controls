using Microsoft.AspNetCore.Components;

namespace Shiny.Blazor.Controls;

/// <summary>
/// A vertical timeline: a rail of markers with arbitrary content beside each one.
/// </summary>
/// <remarks>
/// The same three-state marker the wizard draws, turned on its side and made item-driven — an
/// activity feed, an order's progress, an audit trail, a changelog. Where the wizard owns its steps
/// and shows one at a time, this binds a collection and shows all of them at once.
/// </remarks>
/// <typeparam name="TItem">The bound item's type.</typeparam>
public partial class TimelineView<TItem>
{
    [Parameter] public IEnumerable<TItem>? ItemsSource { get; set; }

    /// <summary>The content beside each marker. Takes the item itself.</summary>
    [Parameter] public RenderFragment<TItem>? ItemTemplate { get; set; }

    /// <summary>
    /// The marker on the rail. Takes a <see cref="TimelineNode{TItem}"/>, not the item.
    /// </summary>
    /// <remarks>
    /// Left unset the control draws its own dot, which is what most timelines want — the template is
    /// for a marker that has to carry something, an icon or a number or a per-item colour.
    /// </remarks>
    [Parameter] public RenderFragment<TimelineNode<TItem>>? MarkerTemplate { get; set; }

    /// <summary>Optional content on the far side of the rail — a timestamp, a duration.</summary>
    [Parameter] public RenderFragment<TItem>? OppositeTemplate { get; set; }

    /// <summary>
    /// How far along the timeline the active position is. -1, the default, makes every node pending.
    /// </summary>
    /// <remarks>
    /// Nodes before it are <see cref="TimelineNodeState.Complete"/>, the one at it is
    /// <see cref="TimelineNodeState.Current"/>, and everything after is
    /// <see cref="TimelineNodeState.Pending"/> — the same three states the wizard's markers use, so a
    /// timeline and a wizard showing the same progress read the same way.
    /// </remarks>
    [Parameter] public int ActiveIndex { get; set; } = -1;

    /// <summary>
    /// Treats every node as active, whatever <see cref="ActiveIndex"/> says.
    /// </summary>
    /// <remarks>
    /// For a timeline of things that have all already happened — an audit trail, a delivered order, a
    /// changelog — where a trailing "pending" tail would be saying something untrue. Wins over
    /// <see cref="ActiveIndex"/> rather than being merged with it, so switching it on does not need
    /// the index moved to the end and back again to switch it off.
    /// </remarks>
    [Parameter] public bool AllActive { get; set; }

    [Parameter] public TimelineRailPosition RailPosition { get; set; } = TimelineRailPosition.Left;

    /// <summary>Marker diameter, in pixels.</summary>
    [Parameter] public double MarkerSize { get; set; } = 14;

    /// <summary>
    /// How far the marker's top sits below the top of its row.
    /// </summary>
    /// <remarks>
    /// Set so the marker lines up with the first line of text beside it rather than with the middle of
    /// the content box. Centring it instead would drift further out of alignment the taller the
    /// content got, which is the normal case here — content beside a timeline is arbitrary.
    /// </remarks>
    [Parameter] public double MarkerOffset { get; set; } = 4;

    [Parameter] public double LineThickness { get; set; } = 2;

    /// <summary>Gap between one node's content and the next. The rail runs through it unbroken.</summary>
    [Parameter] public double ItemSpacing { get; set; } = 16;

    /// <summary>Gap between the rail and the content beside it.</summary>
    [Parameter] public double RailSpacing { get; set; } = 12;

    /// <summary>The filled part of the rail, and any marker at or behind the active position.</summary>
    [Parameter] public string ActiveColor { get; set; } = "var(--shiny-color-primary, #0055D9)";

    [Parameter] public string PendingColor { get; set; } = "var(--shiny-color-surface-container-highest, #E2E2E9)";

    /// <summary>Raised when a node is clicked, with the item and its index.</summary>
    [Parameter] public EventCallback<(TItem Item, int Index)> NodeClicked { get; set; }

    [Parameter] public string? Class { get; set; }

    [Parameter] public string? Style { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? ExtraAttributes { get; set; }

    IReadOnlyList<TItem> Items => this.ItemsSource is null ? [] : this.ItemsSource as IReadOnlyList<TItem> ?? [.. this.ItemsSource];

    TimelineNodeState StateOf(int index)
        => TimelineNode.StateFor(index, this.ActiveIndex, this.AllActive);

    /// <summary>
    /// The state classes for one row.
    /// </summary>
    /// <remarks>
    /// <c>is-linked</c> is separate from the node's own state on purpose: the segment leading out of a
    /// node is filled only once the <i>next</i> node has been reached, so the rail reads as a progress
    /// bar rather than as a set of unrelated links. Without it the line under the current node would
    /// be filled all the way to a node nothing has got to yet.
    /// </remarks>
    string StateClass(TimelineNode<TItem> node)
    {
        var state = node.State switch
        {
            TimelineNodeState.Complete => "is-complete",
            TimelineNodeState.Current => "is-current",
            _ => "is-pending"
        };

        var linked = this.AllActive || node.Index < this.ActiveIndex ? " is-linked" : string.Empty;

        return state + linked;
    }

    string RootStyle =>
        $"--shiny-timeline-marker:{Css(this.MarkerSize)}px;" +
        $"--shiny-timeline-offset:{Css(this.MarkerOffset)}px;" +
        $"--shiny-timeline-line:{Css(this.LineThickness)}px;" +
        $"--shiny-timeline-gap:{Css(this.ItemSpacing)}px;" +
        $"--shiny-timeline-rail-gap:{Css(this.RailSpacing)}px;" +
        $"--shiny-timeline-active:{this.ActiveColor};" +
        $"--shiny-timeline-pending:{this.PendingColor};" +
        this.Style;

    static string Css(double value)
        => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    Task OnNodeClickAsync(TItem item, int index)
        => this.NodeClicked.HasDelegate ? this.NodeClicked.InvokeAsync((item, index)) : Task.CompletedTask;
}
