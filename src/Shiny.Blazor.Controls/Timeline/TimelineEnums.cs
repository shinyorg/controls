namespace Shiny.Blazor.Controls;

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

/// <summary>The rule deciding which state a node is in.</summary>
/// <remarks>
/// Non-generic, so the rule has one home whatever the bound item's type is — and so a test can reach
/// it without naming an item type it does not care about.
/// </remarks>
public static class TimelineNode
{
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

/// <summary>Which side of the content the rail runs down.</summary>
public enum TimelineRailPosition
{
    Left,
    Right
}

/// <summary>
/// One node's place in the timeline, handed to <c>MarkerTemplate</c>.
/// </summary>
/// <remarks>
/// The marker template binds to this rather than to the item, because everything that decides how a
/// marker is drawn — which state it is in, whether it caps either end of the rail — is a property of
/// the node's <i>position</i>, and none of it exists on the item. <c>ItemTemplate</c> is deliberately
/// the other way round and takes the item itself, because content beside a timeline is ordinary
/// content and should not have to reach through a wrapper to get at its own fields.
/// </remarks>
/// <typeparam name="TItem">The bound item's type.</typeparam>
public sealed record TimelineNode<TItem>(TItem Item, int Index, TimelineNodeState State, bool IsFirst, bool IsLast)
{
    /// <summary>True for anything at or behind the active position — the filled part of the rail.</summary>
    public bool IsActive => this.State is not TimelineNodeState.Pending;
}
