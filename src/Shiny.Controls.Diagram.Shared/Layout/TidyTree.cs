namespace Shiny.Controls.Diagramming;

/// <summary>
/// Buchheim, Junger and Leipert's linear-time refinement of Walker's tidy-tree algorithm, extended
/// for nodes of different sizes.
/// </summary>
/// <remarks>
/// <para>
/// The property that matters is the tidy one: a parent sits centred over its children, subtrees never
/// overlap, and two identical subtrees are drawn identically wherever they appear. The naive
/// alternative - place each level left to right by index - is a few lines long and produces a chart
/// where a parent floats over the wrong child and wide subtrees collide.
/// </para>
/// <para>
/// Shared by <see cref="TreeLayout"/> and <see cref="MindMapLayout"/>, which differ only in what they
/// hand it and how they read the result back: a mindmap packs each half of the root's children
/// separately and grows them in opposite directions.
/// </para>
/// <para>
/// Both walks run on explicit stacks. A four-thousand-node chain is unusual but not impossible, and
/// the failure mode of a recursive walk over one is a stack overflow, which cannot be caught and
/// takes the app with it.
/// </para>
/// </remarks>
static class TidyTree
{
    /// <summary>
    /// Packs a subtree, leaving each node's <see cref="TreeNode.Across"/> set to its final position
    /// along the sibling axis.
    /// </summary>
    /// <param name="root">The subtree to pack.</param>
    /// <param name="options">Supplies the gap between siblings.</param>
    public static void Pack(TreeNode root, DiagramLayoutOptions options)
    {
        FirstWalk(root, options);
        SecondWalk(root);
    }

    static void FirstWalk(TreeNode root, DiagramLayoutOptions options)
    {
        var stack = new Stack<Frame>();
        stack.Push(new Frame(root));

        while (stack.Count > 0)
        {
            var frame = stack.Peek();

            if (frame.Index < frame.Node.Children.Count)
            {
                var child = frame.Node.Children[frame.Index];
                frame.Index++;
                stack.Push(new Frame(child));
                continue;
            }

            var node = frame.Node;

            if (node.Children.Count == 0)
            {
                node.Prelim = node.LeftSibling is null
                    ? 0
                    : node.LeftSibling.Prelim + Separation(node.LeftSibling, node, options);
            }
            else
            {
                ExecuteShifts(node);

                var midpoint = (node.Children[0].Prelim + node.Children[^1].Prelim) / 2;

                if (node.LeftSibling is null)
                {
                    node.Prelim = midpoint;
                }
                else
                {
                    node.Prelim = node.LeftSibling.Prelim + Separation(node.LeftSibling, node, options);
                    node.Mod = node.Prelim - midpoint;
                }
            }

            stack.Pop();

            if (stack.Count > 0)
            {
                var parent = stack.Peek();
                parent.DefaultAncestor = Apportion(node, parent.DefaultAncestor!, options);
            }
        }
    }

    /// <summary>
    /// The gap two nodes must keep when they end up side by side. Sizes are halved because both
    /// positions are centres, which is what keeps a wide node from overlapping a narrow neighbour.
    /// </summary>
    static double Separation(TreeNode left, TreeNode right, DiagramLayoutOptions options) =>
        ((left.AcrossSize + right.AcrossSize) / 2) + options.NodeSpacing;

    static TreeNode Apportion(TreeNode node, TreeNode defaultAncestor, DiagramLayoutOptions options)
    {
        var leftSibling = node.LeftSibling;
        if (leftSibling is null)
            return defaultAncestor;

        // Four contour walkers: inner and outer, on the right of the left subtree and the left of the
        // right subtree. The threads are what make this linear - a contour that has run out of real
        // children continues through a thread laid down by an earlier pass.
        var insideRight = node;
        var outsideRight = node;
        var insideLeft = leftSibling;
        var outsideLeft = insideRight.Parent!.Children[0];

        var insideRightMod = insideRight.Mod;
        var outsideRightMod = outsideRight.Mod;
        var insideLeftMod = insideLeft.Mod;
        var outsideLeftMod = outsideLeft.Mod;

        while (NextRight(insideLeft) is { } nextInsideLeft && NextLeft(insideRight) is { } nextInsideRight)
        {
            insideLeft = nextInsideLeft;
            insideRight = nextInsideRight;
            outsideLeft = NextLeft(outsideLeft)!;
            outsideRight = NextRight(outsideRight)!;

            outsideRight.Ancestor = node;

            var shift = insideLeft.Prelim + insideLeftMod
                - (insideRight.Prelim + insideRightMod)
                + Separation(insideLeft, insideRight, options);

            if (shift > 0)
            {
                MoveSubtree(Ancestor(insideLeft, node, defaultAncestor), node, shift);
                insideRightMod += shift;
                outsideRightMod += shift;
            }

            insideLeftMod += insideLeft.Mod;
            insideRightMod += insideRight.Mod;
            outsideLeftMod += outsideLeft.Mod;
            outsideRightMod += outsideRight.Mod;
        }

        if (NextRight(insideLeft) is { } danglingLeft && NextRight(outsideRight) is null)
        {
            outsideRight.Thread = danglingLeft;
            outsideRight.Mod += insideLeftMod - outsideRightMod;
        }

        if (NextLeft(insideRight) is { } danglingRight && NextLeft(outsideLeft) is null)
        {
            outsideLeft.Thread = danglingRight;
            outsideLeft.Mod += insideRightMod - outsideLeftMod;
            defaultAncestor = node;
        }

        return defaultAncestor;
    }

    static TreeNode? NextLeft(TreeNode node) =>
        node.Children.Count > 0 ? node.Children[0] : node.Thread;

    static TreeNode? NextRight(TreeNode node) =>
        node.Children.Count > 0 ? node.Children[^1] : node.Thread;

    static TreeNode Ancestor(TreeNode insideLeft, TreeNode node, TreeNode defaultAncestor) =>
        insideLeft.Ancestor is { } candidate && ReferenceEquals(candidate.Parent, node.Parent)
            ? candidate
            : defaultAncestor;

    static void MoveSubtree(TreeNode from, TreeNode to, double shift)
    {
        var subtrees = to.Number - from.Number;
        if (subtrees == 0)
            return;

        to.Change -= shift / subtrees;
        to.Shift += shift;
        from.Change += shift / subtrees;
        to.Prelim += shift;
        to.Mod += shift;
    }

    static void ExecuteShifts(TreeNode node)
    {
        var shift = 0d;
        var change = 0d;

        for (var i = node.Children.Count - 1; i >= 0; i--)
        {
            var child = node.Children[i];
            child.Prelim += shift;
            child.Mod += shift;
            change += child.Change;
            shift += child.Shift + change;
        }
    }

    static void SecondWalk(TreeNode root)
    {
        var stack = new Stack<(TreeNode Node, double Mod)>();
        stack.Push((root, 0));

        while (stack.Count > 0)
        {
            var (node, mod) = stack.Pop();
            node.Across = node.Prelim + mod;

            for (var i = node.Children.Count - 1; i >= 0; i--)
                stack.Push((node.Children[i], mod + node.Mod));
        }
    }

    sealed class Frame(TreeNode node)
    {
        public TreeNode Node { get; } = node;
        public int Index { get; set; }
        public TreeNode? DefaultAncestor { get; set; } = node.Children.Count > 0 ? node.Children[0] : null;
    }
}
