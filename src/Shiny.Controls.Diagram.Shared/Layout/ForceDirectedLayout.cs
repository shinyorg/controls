namespace Shiny.Controls.Diagramming;

/// <summary>
/// Physics relaxation - Fruchterman and Reingold.
/// </summary>
/// <remarks>
/// <para>
/// Every node repels every other; every connection pulls its two ends together; the whole thing
/// cools over a fixed number of passes. It is the layout for a graph with no hierarchy worth
/// speaking of - a network, a dependency web - and the wrong choice for anything an org chart or a
/// decision tree would recognise, where <see cref="TreeLayout"/> and <see cref="LayeredLayout"/>
/// produce something a reader can follow.
/// </para>
/// <para>
/// <b>The starting positions are a circle indexed by supply order, not random.</b> Fruchterman and
/// Reingold seed randomly, and doing that here would mean the MAUI and Blazor controls settle the
/// same graph into two different pictures - and the same control into a different picture on every
/// rebuild. Determinism costs nothing here and is worth more than the marginally better minimum a
/// random restart occasionally finds.
/// </para>
/// <para>
/// The pass count is fixed rather than run to convergence: this runs on the UI thread inside a layout
/// pass, and a convergence test that does not converge is a frozen app.
/// </para>
/// </remarks>
public sealed class ForceDirectedLayout : IDiagramLayout
{
    /// <inheritdoc />
    public void Arrange(DiagramLayoutContext context)
    {
        context.EnsureSizes();

        var nodes = context.Nodes;
        if (nodes.Count == 0)
            return;

        var options = context.Options;

        if (nodes.Count == 1)
        {
            if (!nodes[0].IsPinned)
            {
                nodes[0].X = options.Margin;
                nodes[0].Y = options.Margin;
            }
            return;
        }

        var index = new Dictionary<string, int>(nodes.Count, StringComparer.Ordinal);
        for (var i = 0; i < nodes.Count; i++)
            index.TryAdd(nodes[i].Id, i);

        var x = new double[nodes.Count];
        var y = new double[nodes.Count];
        var dx = new double[nodes.Count];
        var dy = new double[nodes.Count];

        // The ideal edge length, and the constant the whole model is scaled by.
        var k = Math.Max(options.NodeWidth, options.NodeHeight) + options.NodeSpacing;

        // A circle big enough that the initial repulsion has somewhere to push into. Starting every
        // node at the origin makes the first pass a division by nearly zero.
        var radius = k * nodes.Count / (Math.PI * 2);

        for (var i = 0; i < nodes.Count; i++)
        {
            // A pinned node starts where it actually is, so it pushes on its neighbours from its real
            // place rather than from a seed position it is never going to leave.
            if (nodes[i].IsPinned)
            {
                x[i] = nodes[i].X + (nodes[i].Width / 2);
                y[i] = nodes[i].Y + (nodes[i].Height / 2);
                continue;
            }

            var angle = Math.PI * 2 * i / nodes.Count;
            x[i] = radius * Math.Cos(angle);
            y[i] = radius * Math.Sin(angle);
        }

        var edges = new List<(int Source, int Target)>();

        foreach (var connection in context.Connections)
        {
            if (!index.TryGetValue(connection.SourceId, out var source) ||
                !index.TryGetValue(connection.TargetId, out var target))
            {
                continue;
            }

            if (source != target)
                edges.Add((source, target));
        }

        var iterations = Math.Max(1, options.ForceIterations);
        var temperature = radius / 4;
        var cooling = temperature / (iterations + 1);

        for (var pass = 0; pass < iterations; pass++)
        {
            Array.Clear(dx);
            Array.Clear(dy);

            for (var i = 0; i < nodes.Count; i++)
            {
                for (var j = i + 1; j < nodes.Count; j++)
                {
                    var ox = x[i] - x[j];
                    var oy = y[i] - y[j];
                    var distance = Math.Sqrt((ox * ox) + (oy * oy));

                    // Two nodes exactly on top of each other have no direction to separate along.
                    // Nudging by their index keeps that deterministic instead of leaving them stuck.
                    if (distance < 1e-6)
                    {
                        ox = (i - j) * 1e-3;
                        oy = 1e-3;
                        distance = Math.Sqrt((ox * ox) + (oy * oy));
                    }

                    var force = k * k / distance;
                    var fx = ox / distance * force;
                    var fy = oy / distance * force;

                    dx[i] += fx;
                    dy[i] += fy;
                    dx[j] -= fx;
                    dy[j] -= fy;
                }
            }

            foreach (var (source, target) in edges)
            {
                var ox = x[source] - x[target];
                var oy = y[source] - y[target];
                var distance = Math.Sqrt((ox * ox) + (oy * oy));

                if (distance < 1e-6)
                    continue;

                var force = distance * distance / k;
                var fx = ox / distance * force;
                var fy = oy / distance * force;

                dx[source] -= fx;
                dy[source] -= fy;
                dx[target] += fx;
                dy[target] += fy;
            }

            for (var i = 0; i < nodes.Count; i++)
            {
                // A pinned node is an anchor: it still pushes on everything else, and nothing moves
                // it. That is what makes force-directed usable as a nudge on a hand-arranged diagram
                // rather than only as an all-or-nothing rearrangement.
                if (nodes[i].IsPinned)
                    continue;

                var length = Math.Sqrt((dx[i] * dx[i]) + (dy[i] * dy[i]));
                if (length < 1e-9)
                    continue;

                var step = Math.Min(length, temperature);
                x[i] += dx[i] / length * step;
                y[i] += dy[i] / length * step;
            }

            temperature -= cooling;
        }

        Normalise(nodes, x, y, options);
    }

    static void Normalise(IReadOnlyList<DiagramNode> nodes, double[] x, double[] y, DiagramLayoutOptions options)
    {
        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var anyPinned = false;

        for (var i = 0; i < nodes.Count; i++)
        {
            anyPinned |= nodes[i].IsPinned;
            minX = Math.Min(minX, x[i] - (nodes[i].Width / 2));
            minY = Math.Min(minY, y[i] - (nodes[i].Height / 2));
        }

        if (minX == double.MaxValue)
            return;

        // Pinned nodes anchor the diagram in place, so there is nothing to shift the rest against:
        // moving the unpinned nodes to the margin while the pinned ones stayed put would pull the
        // simulated arrangement apart, which is the one thing the simulation was for.
        var shiftX = anyPinned ? 0 : options.Margin - minX;
        var shiftY = anyPinned ? 0 : options.Margin - minY;

        for (var i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].IsPinned)
                continue;

            nodes[i].X = x[i] - (nodes[i].Width / 2) + shiftX;
            nodes[i].Y = y[i] - (nodes[i].Height / 2) + shiftY;
        }
    }
}
