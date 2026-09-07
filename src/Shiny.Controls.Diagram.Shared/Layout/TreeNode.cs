namespace Shiny.Controls.Diagramming;

/// <summary>
/// A node's scratch state while a hierarchy layout runs.
/// </summary>
/// <remarks>
/// Kept off <see cref="DiagramNode"/> deliberately. Eight of these fields are Buchheim bookkeeping -
/// threads, ancestors, pending shifts - that mean nothing between layout passes, and putting them on
/// the public model would be eight more properties for a consumer to wonder about and for a JSON
/// round-trip to carry.
/// </remarks>
sealed class TreeNode(DiagramNode node)
{
    /// <summary>The model node this stands for.</summary>
    public DiagramNode Node { get; } = node;

    /// <summary>The parent in the layout hierarchy, which is not always <see cref="DiagramNode.Parent"/>.</summary>
    public TreeNode? Parent { get; set; }

    /// <summary>Children, in the order they will be drawn.</summary>
    public List<TreeNode> Children { get; } = [];

    /// <summary>One-based position among siblings, which is what Buchheim's shift distribution divides by.</summary>
    public int Number { get; set; }

    /// <summary>How deep in the hierarchy - a root is zero.</summary>
    public int Depth { get; set; }

    /// <summary>The previous sibling, or null for a first child.</summary>
    public TreeNode? LeftSibling { get; set; }

    /// <summary>Provisional position along the sibling axis, before ancestors' modifiers are folded in.</summary>
    public double Prelim { get; set; }

    /// <summary>Accumulated offset applied to this node's whole subtree.</summary>
    public double Mod { get; set; }

    /// <summary>Pending shift, distributed over siblings by <c>ExecuteShifts</c>.</summary>
    public double Shift { get; set; }

    /// <summary>Pending shift gradient, distributed over siblings by <c>ExecuteShifts</c>.</summary>
    public double Change { get; set; }

    /// <summary>Contour link used when a subtree has run out of real children to walk.</summary>
    public TreeNode? Thread { get; set; }

    /// <summary>The subtree this node's contour currently belongs to.</summary>
    public TreeNode? Ancestor { get; set; }

    /// <summary>Final position along the sibling axis, as a centre.</summary>
    public double Across { get; set; }

    /// <summary>Final position along the growth axis, as a leading edge.</summary>
    public double Depth1D { get; set; }

    /// <summary>The node's extent along the sibling axis.</summary>
    public double AcrossSize { get; set; }

    /// <summary>The node's extent along the growth axis.</summary>
    public double DepthSize { get; set; }

    /// <summary>
    /// Every node in the subtree, this one first, on an explicit stack.
    /// </summary>
    /// <param name="root">The subtree to walk.</param>
    public static IEnumerable<TreeNode> Descendants(TreeNode root)
    {
        var stack = new Stack<TreeNode>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;

            for (var i = node.Children.Count - 1; i >= 0; i--)
                stack.Push(node.Children[i]);
        }
    }
}
