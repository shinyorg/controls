namespace Shiny.Maui.Controls;

/// <summary>Where a node sits relative to the timeline's active position.</summary>
public enum TimelineNodeState
{
    /// <summary>Not reached yet. Drawn in the muted colour.</summary>
    Pending,

    /// <summary>Where the timeline is now. Drawn filled, with a ring around it.</summary>
    Current,

    /// <summary>Behind the active position. Drawn filled.</summary>
    Complete
}

/// <summary>Which side of the content the rail runs down.</summary>
public enum TimelineRailPosition
{
    Left,
    Right
}

/// <summary>
/// One node's place in the timeline, handed to a <see cref="TimelineView.MarkerTemplate"/>.
/// </summary>
/// <remarks>
/// <para>
/// The marker template binds to this rather than to the item, because everything that decides how a
/// marker is drawn — which state it is in, whether it caps either end of the rail — is a property of
/// the node's <i>position</i>, and none of it exists on the item. The item is on
/// <see cref="Item"/> for a template that wants both.
/// </para>
/// <para>
/// <see cref="TimelineView.ItemTemplate"/> is deliberately the other way round and binds straight to the
/// item, because content beside a timeline is ordinary content and should not have to reach through a
/// wrapper to say <c>{Binding Item.Title}</c>.
/// </para>
/// </remarks>
public sealed record TimelineNode(object Item, int Index, TimelineNodeState State, bool IsFirst, bool IsLast)
{
    /// <summary>True for anything at or behind the active position — the filled part of the rail.</summary>
    public bool IsActive => this.State is not TimelineNodeState.Pending;

    /// <summary>
    /// Which state a node at <paramref name="index"/> is in.
    /// </summary>
    /// <remarks>
    /// The one place the rule lives, so the control, a marker template and a test all agree on it.
    /// <paramref name="allActive"/> wins outright rather than being merged with the index: switching
    /// it on should not need the index moved to the end, nor moved back again to switch it off.
    /// </remarks>
    public static TimelineNodeState StateFor(int index, int activeIndex, bool allActive)
    {
        if (allActive)
            return TimelineNodeState.Complete;

        if (index < activeIndex)
            return TimelineNodeState.Complete;

        return index == activeIndex ? TimelineNodeState.Current : TimelineNodeState.Pending;
    }
}
