namespace Shiny.Controls.Resizable;

// Compiled into both Shiny.Maui.Controls and Shiny.Blazor.Controls (linked by the Blazor csproj), and
// mirrored by resizable.js so a Blazor drag never waits on a round trip. Keep the three in step.

/// <summary>The limits of one panel in a resizable group, all in percent of the group.</summary>
internal readonly record struct ResizablePanelLimits(
    double MinSize = 0,
    double MaxSize = 100,
    bool Collapsible = false,
    double CollapsedSize = 0)
{
    // Without this, `new ResizablePanelLimits()` is the zeroed default and a MaxSize of 0.
    public ResizablePanelLimits() : this(0) { }

    public bool IsCollapsed(double size) => this.Collapsible && size <= this.CollapsedSize + ResizableLayout.Epsilon;
}

/// <summary>
/// The arithmetic of a resizable panel group: every operation takes the sizes (percent, summing to
/// 100) and returns new ones, so a drag can always be computed from where it started and a cancelled
/// drag is just the old array.
/// </summary>
internal static class ResizableLayout
{
    internal const double Epsilon = 0.0001;

    /// <summary>
    /// Starting sizes: a panel's default where it has one, the rest shared equally by the others, then
    /// held to each panel's limits and scaled back to 100.
    /// </summary>
    public static double[] Initial(IReadOnlyList<double?> defaults, IReadOnlyList<ResizablePanelLimits> limits)
    {
        var count = defaults.Count;
        var sizes = new double[count];
        if (count == 0)
            return sizes;

        var given = 0d;
        var unset = 0;
        for (var i = 0; i < count; i++)
        {
            if (defaults[i] is { } d)
                given += d;
            else
                unset++;
        }

        var share = unset > 0 ? Math.Max(0, 100 - given) / unset : 0;
        for (var i = 0; i < count; i++)
            sizes[i] = defaults[i] ?? share;

        return Validate(sizes, limits);
    }

    /// <summary>
    /// Brings sizes from outside (a saved layout, <c>SetSizes</c>) back into shape: clamped to each
    /// panel's limits and summing to 100. A wrong count falls back to equal shares.
    /// </summary>
    public static double[] Validate(IReadOnlyList<double> sizes, IReadOnlyList<ResizablePanelLimits> limits)
    {
        var count = limits.Count;
        var result = new double[count];
        if (count == 0)
            return result;

        for (var i = 0; i < count; i++)
        {
            var s = i < sizes.Count && Double.IsFinite(sizes[i]) ? sizes[i] : 100d / count;
            result[i] = Clamp(s, limits[i]);
        }

        // Scale the open panels in proportion first, so [30, 30] becomes [50, 50] rather than [70, 30].
        // A collapsed panel stays collapsed rather than soaking up the difference.
        var collapsedSum = 0d;
        var openSum = 0d;
        for (var i = 0; i < count; i++)
        {
            if (limits[i].IsCollapsed(result[i]))
                collapsedSum += result[i];
            else
                openSum += result[i];
        }
        if (openSum > Epsilon)
        {
            var factor = (100 - collapsedSum) / openSum;
            for (var i = 0; i < count; i++)
            {
                if (!limits[i].IsCollapsed(result[i]))
                    result[i] = Math.Clamp(result[i] * factor, limits[i].MinSize, Math.Max(limits[i].MinSize, limits[i].MaxSize));
            }
        }

        // Then hand what the limits pushed back out panel by panel, each within its limits. Left over
        // only when the limits cannot add up to 100 - then the last panel takes it.
        var remaining = 100 - result.Sum();
        var open = Enumerable.Range(0, count).Where(i => !limits[i].IsCollapsed(result[i])).ToList();
        for (var pass = 0; pass < 2 && Math.Abs(remaining) > Epsilon; pass++)
        {
            foreach (var i in open)
            {
                var target = Math.Clamp(result[i] + remaining, limits[i].MinSize, limits[i].MaxSize);
                remaining -= target - result[i];
                result[i] = target;
            }
        }
        if (Math.Abs(remaining) > Epsilon)
            result[count - 1] += remaining;

        return result;
    }

    /// <summary>
    /// Moves handle <paramref name="handleIndex"/> (between panel <c>handleIndex</c> and
    /// <c>handleIndex + 1</c>) by <paramref name="delta"/> percent: positive grows the panel before it.
    /// </summary>
    /// <remarks>
    /// The panel on the growing side takes the space; the other side gives it up starting with the panel
    /// next to the handle and moving outwards once that one is at its minimum, so a long drag pushes
    /// through several panels. A collapsible panel snaps shut once it is dragged more than halfway
    /// from its minimum towards its collapsed size, and a collapsed one snaps open to its minimum the
    /// same way.
    /// </remarks>
    public static double[] Resize(IReadOnlyList<double> start, IReadOnlyList<ResizablePanelLimits> limits, int handleIndex, double delta)
    {
        var sizes = start.ToArray();
        if (handleIndex < 0 || handleIndex >= sizes.Length - 1 || Math.Abs(delta) < Epsilon)
            return sizes;

        var growIndex = delta > 0 ? handleIndex : handleIndex + 1;
        var step = delta > 0 ? 1 : -1;
        var firstShrink = delta > 0 ? handleIndex + 1 : handleIndex;
        var wanted = Math.Abs(delta);

        var grow = limits[growIndex];
        var growSize = sizes[growIndex];

        // A collapsed panel opens to its minimum or not at all.
        var minimumGrowth = 0d;
        if (grow.IsCollapsed(growSize))
        {
            minimumGrowth = grow.MinSize - growSize;
            if (wanted < minimumGrowth / 2)
                return sizes;
            wanted = Math.Max(wanted, minimumGrowth);
        }

        wanted = Math.Min(wanted, grow.MaxSize - growSize);
        if (wanted <= Epsilon || wanted < minimumGrowth - Epsilon)
            return sizes;

        // Take the space from the shrinking side, nearest panel first, passing over collapsed ones.
        var shrinking = step > 0
            ? Enumerable.Range(firstShrink, sizes.Length - firstShrink)
            : Enumerable.Range(0, firstShrink + 1).Reverse();
        var taken = 0d;
        foreach (var i in shrinking.Where(i => !limits[i].IsCollapsed(sizes[i])))
        {
            if (taken >= wanted - Epsilon)
                break;

            var panel = limits[i];
            var need = wanted - taken;
            var size = sizes[i];

            var toMin = Math.Max(0, size - panel.MinSize);
            if (need <= toMin + Epsilon)
            {
                sizes[i] = size - need;
                taken += need;
                break;
            }

            sizes[i] = panel.MinSize;
            taken += toMin;
            need -= toMin;

            // Past the minimum: a collapsible panel shuts once the pull is past halfway to closed.
            if (panel.Collapsible && panel.CollapsedSize < panel.MinSize)
            {
                var gap = panel.MinSize - panel.CollapsedSize;
                if (need >= gap / 2)
                {
                    // Shutting frees the whole gap even when less was asked for; the growing panel can
                    // only take what its maximum allows, so collapse only if it fits.
                    if (growSize + taken + gap <= grow.MaxSize + Epsilon)
                    {
                        sizes[i] = panel.CollapsedSize;
                        taken += gap;
                    }
                }
                break;
            }
        }

        if (taken < minimumGrowth - Epsilon)
            return start.ToArray();

        sizes[growIndex] = growSize + taken;
        return sizes;
    }

    /// <summary>Shuts panel <paramref name="index"/> to its collapsed size, giving the space to a neighbour.</summary>
    public static double[] Collapse(IReadOnlyList<double> start, IReadOnlyList<ResizablePanelLimits> limits, int index)
    {
        var panel = limits[index];
        if (!panel.Collapsible || panel.IsCollapsed(start[index]) || start.Count < 2)
            return start.ToArray();

        var amount = start[index] - panel.CollapsedSize;
        return MoveEdge(start, limits, index, -amount);
    }

    /// <summary>
    /// Opens panel <paramref name="index"/> to <paramref name="size"/> (its size before it collapsed),
    /// or at least its minimum, taking the space from its neighbours.
    /// </summary>
    public static double[] Expand(IReadOnlyList<double> start, IReadOnlyList<ResizablePanelLimits> limits, int index, double? size = null)
    {
        var panel = limits[index];
        if (start.Count < 2)
            return start.ToArray();

        var target = Math.Clamp(size ?? panel.MinSize, panel.MinSize, panel.MaxSize);
        if (target <= start[index] + Epsilon)
            return start.ToArray();

        return MoveEdge(start, limits, index, target - start[index]);
    }

    /// <summary>Whether panel <paramref name="index"/> is at its collapsed size.</summary>
    public static bool IsCollapsed(IReadOnlyList<double> sizes, IReadOnlyList<ResizablePanelLimits> limits, int index)
        => index >= 0 && index < sizes.Count && limits[index].IsCollapsed(sizes[index]);

    // Grows (positive) or shrinks panel `index` through the handle after it, or the one before it for
    // the last panel.
    static double[] MoveEdge(IReadOnlyList<double> start, IReadOnlyList<ResizablePanelLimits> limits, int index, double change)
        => index < start.Count - 1
            ? Resize(start, limits, index, change)
            : Resize(start, limits, index - 1, -change);

    static double Clamp(double size, ResizablePanelLimits limits)
    {
        if (limits.Collapsible && size <= limits.CollapsedSize + Epsilon)
            return limits.CollapsedSize;

        return Math.Clamp(size, limits.MinSize, Math.Max(limits.MinSize, limits.MaxSize));
    }
}
