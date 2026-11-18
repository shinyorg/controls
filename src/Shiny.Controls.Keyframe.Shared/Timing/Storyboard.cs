namespace Shiny.Controls.Keyframe;

/// <summary>
/// Composes animation nodes on a shared clock, each pinned at its own offset. Storyboards are
/// themselves nodes, so they nest — a staggered list entrance can be one item in a larger sequence.
/// </summary>
public sealed class Storyboard : IAnimationNode
{
    readonly List<Entry> entries = [];

    readonly record struct Entry(IAnimationNode Node, TimeSpan Offset);

    /// <summary>Optional label, used for diagnostics.</summary>
    public string? Name { get; set; }

    /// <summary>The composed nodes and their offsets.</summary>
    public IEnumerable<(IAnimationNode Node, TimeSpan Offset)> Children
        => entries.Select(e => (e.Node, e.Offset));

    /// <inheritdoc />
    public TimeSpan TotalDuration
    {
        get
        {
            var longest = TimeSpan.Zero;
            foreach (var entry in entries)
            {
                var end = AddClamped(entry.Offset, entry.Node.TotalDuration);
                if (end > longest)
                    longest = end;
            }
            return longest;
        }
    }

    /// <summary>Adds a node at an explicit offset from the storyboard's start.</summary>
    public Storyboard Add(IAnimationNode node, TimeSpan offset = default)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (ReferenceEquals(node, this))
            throw new ArgumentException("A storyboard cannot contain itself.", nameof(node));

        entries.Add(new Entry(node, offset));
        return this;
    }

    /// <summary>Appends a node so it begins when everything already added has finished.</summary>
    /// <param name="node">The node to append.</param>
    /// <param name="gap">Optional pause inserted before it starts.</param>
    public Storyboard Then(IAnimationNode node, TimeSpan gap = default)
    {
        ArgumentNullException.ThrowIfNull(node);

        var current = TotalDuration;
        if (current == TimeSpan.MaxValue)
            throw new InvalidOperationException(
                "Cannot append after an infinitely repeating node — it never finishes, so the " +
                "appended node would never start. Add it at an explicit offset instead.");

        return Add(node, AddClamped(current, gap));
    }

    /// <summary>Adds a node at the same offset as the storyboard's start, running in parallel.</summary>
    public Storyboard With(IAnimationNode node) => Add(node, TimeSpan.Zero);

    /// <summary>
    /// Adds nodes spaced evenly apart — the standard staggered entrance for a list or grid.
    /// </summary>
    /// <param name="nodes">Nodes to add, in order.</param>
    /// <param name="interval">Delay between consecutive starts.</param>
    /// <param name="startAt">Offset for the first node.</param>
    public Storyboard Stagger(IEnumerable<IAnimationNode> nodes, TimeSpan interval, TimeSpan startAt = default)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        var offset = startAt;
        foreach (var node in nodes)
        {
            Add(node, offset);
            offset = AddClamped(offset, interval);
        }

        return this;
    }

    /// <inheritdoc />
    public void CaptureBaselines()
    {
        foreach (var entry in entries)
            entry.Node.CaptureBaselines();
    }

    /// <inheritdoc />
    public void RestoreBaselines()
    {
        foreach (var entry in entries)
            entry.Node.RestoreBaselines();
    }

    /// <inheritdoc />
    public void Release()
    {
        foreach (var entry in entries)
            entry.Node.Release();
    }

    /// <inheritdoc />
    public bool Evaluate(TimeSpan time)
    {
        var allFinished = true;

        foreach (var entry in entries)
        {
            // A child that has not started yet still gets evaluated with a negative offset: that is
            // what lets its Backwards fill hold the opening pose while the rest of the storyboard runs.
            var finished = entry.Node.Evaluate(time - entry.Offset);
            if (!finished)
                allFinished = false;
        }

        return allFinished;
    }

    static TimeSpan AddClamped(TimeSpan left, TimeSpan right)
    {
        if (left == TimeSpan.MaxValue || right == TimeSpan.MaxValue)
            return TimeSpan.MaxValue;

        var ticks = (decimal)left.Ticks + right.Ticks;
        return ticks >= TimeSpan.MaxValue.Ticks ? TimeSpan.MaxValue : new TimeSpan((long)ticks);
    }
}
