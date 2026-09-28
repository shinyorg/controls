namespace Shiny.Controls.Office.Spreadsheet.Calc;

/// <summary>A cell identified across sheets.</summary>
public readonly record struct CellAddress(string Sheet, CellRef Cell)
{
    public override string ToString() => $"{this.Sheet}!{this.Cell.Relative()}";
}

/// <summary>
/// Tracks which formula cells depend on which cells, so an edit recomputes only what it affects.
/// </summary>
/// <remarks>
/// Dependencies are stored per precedent cell. A formula over a large range registers against every cell
/// in it, which is the trade-off that keeps invalidation an O(dependents) lookup instead of a scan of
/// every formula in the workbook on every keystroke.
/// </remarks>
public sealed class DependencyGraph
{
    readonly Dictionary<CellAddress, HashSet<CellAddress>> dependents = new();
    readonly Dictionary<CellAddress, HashSet<CellAddress>> precedents = new();

    public int TrackedCells => this.precedents.Count;

    /// <summary>Replaces everything known about what <paramref name="formulaCell"/> reads.</summary>
    public void SetPrecedents(CellAddress formulaCell, IEnumerable<CellAddress> reads)
    {
        this.ClearPrecedents(formulaCell);

        var set = new HashSet<CellAddress>(reads);
        if (set.Count == 0)
        {
            // Still tracked: a formula with no references (=NOW(), =1+1) must remain a known formula cell.
            this.precedents[formulaCell] = set;
            return;
        }

        this.precedents[formulaCell] = set;
        foreach (var precedent in set)
        {
            if (!this.dependents.TryGetValue(precedent, out var list))
                this.dependents[precedent] = list = new HashSet<CellAddress>();

            list.Add(formulaCell);
        }
    }

    public void ClearPrecedents(CellAddress formulaCell)
    {
        if (!this.precedents.Remove(formulaCell, out var existing))
            return;

        foreach (var precedent in existing)
        {
            if (!this.dependents.TryGetValue(precedent, out var list))
                continue;

            list.Remove(formulaCell);
            if (list.Count == 0)
                this.dependents.Remove(precedent);
        }
    }

    public bool IsFormula(CellAddress cell) => this.precedents.ContainsKey(cell);

    public IReadOnlySet<CellAddress> DirectDependents(CellAddress cell)
        => this.dependents.TryGetValue(cell, out var list) ? list : EmptySet;

    static readonly HashSet<CellAddress> EmptySet = new();

    /// <summary>
    /// Every formula cell affected by a change to <paramref name="changed"/>, in an order where each
    /// cell comes after everything it depends on.
    /// </summary>
    /// <param name="cycle">Cells involved in a circular reference, which cannot be ordered.</param>
    public List<CellAddress> AffectedInEvaluationOrder(IEnumerable<CellAddress> changed, out HashSet<CellAddress> cycle)
    {
        // Collect the affected set first; ordering only makes sense within it.
        var affected = new HashSet<CellAddress>();
        var pending = new Stack<CellAddress>();

        foreach (var start in changed)
        {
            // A changed cell that is itself a formula has to recompute too. Seeding only its dependents
            // recalculates everything downstream of a new formula while leaving the formula itself
            // holding no value at all.
            if (this.IsFormula(start) && affected.Add(start))
                pending.Push(start);

            foreach (var dependent in this.DirectDependents(start))
            {
                if (affected.Add(dependent))
                    pending.Push(dependent);
            }
        }

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var dependent in this.DirectDependents(current))
            {
                if (affected.Add(dependent))
                    pending.Push(dependent);
            }
        }

        return this.TopologicalSort(affected, out cycle);
    }

    /// <summary>Orders every known formula cell. Used for a full recalculation.</summary>
    public List<CellAddress> AllInEvaluationOrder(out HashSet<CellAddress> cycle)
        => this.TopologicalSort(new HashSet<CellAddress>(this.precedents.Keys), out cycle);

    List<CellAddress> TopologicalSort(HashSet<CellAddress> nodes, out HashSet<CellAddress> cycle)
    {
        // Kahn's algorithm over the subgraph induced by `nodes`. Anything left with a non-zero in-degree
        // at the end is part of a cycle, which is how circular references get reported rather than
        // recursed into until the stack dies.
        var inDegree = new Dictionary<CellAddress, int>();
        foreach (var node in nodes)
            inDegree[node] = 0;

        foreach (var node in nodes)
        {
            if (!this.precedents.TryGetValue(node, out var reads))
                continue;

            foreach (var read in reads)
            {
                if (nodes.Contains(read))
                    inDegree[node]++;
            }
        }

        var ready = new Queue<CellAddress>(inDegree.Where(x => x.Value == 0).Select(x => x.Key));
        var ordered = new List<CellAddress>(nodes.Count);

        while (ready.Count > 0)
        {
            var current = ready.Dequeue();
            ordered.Add(current);

            foreach (var dependent in this.DirectDependents(current))
            {
                if (!inDegree.ContainsKey(dependent))
                    continue;

                if (--inDegree[dependent] == 0)
                    ready.Enqueue(dependent);
            }
        }

        cycle = ordered.Count == nodes.Count
            ? EmptySet
            : new HashSet<CellAddress>(nodes.Where(x => !ordered.Contains(x)));

        return ordered;
    }

    public void Clear()
    {
        this.dependents.Clear();
        this.precedents.Clear();
    }

    /// <summary>Walks a parsed formula and collects every cell it reads.</summary>
    public static IEnumerable<CellAddress> Collect(FormulaNode node, string currentSheet)
        => Collect(node, currentSheet, null);

    /// <summary>
    /// Collects every cell a formula reads, following defined names through <paramref name="names"/> —
    /// without which a formula over <c>Sales</c> never recalculates when a cell in the named range
    /// changes.
    /// </summary>
    public static IEnumerable<CellAddress> Collect(FormulaNode node, string currentSheet, Func<string, string, FormulaNode?>? names)
    {
        var results = new List<CellAddress>();
        Walk(node, currentSheet, results, names, 0);
        return results;
    }

    static void Walk(FormulaNode node, string currentSheet, List<CellAddress> results, Func<string, string, FormulaNode?>? names, int depth)
    {
        switch (node)
        {
            case UnknownNameNode name when names is not null && depth < 16:
                if (names(name.Name, currentSheet) is { } resolved)
                    Walk(resolved, currentSheet, results, names, depth + 1);

                break;

            case ReferenceNode reference:
                results.Add(new CellAddress(reference.Sheet ?? currentSheet, reference.Cell.Relative()));
                break;

            case RangeNode range:
                var sheet = range.Sheet ?? currentSheet;

                // A whole-column reference would enumerate a million cells; cap the registration at the
                // range's own size and let very large ranges fall back to a full recalculation.
                if (range.Range.CellCount > MaxTrackedRangeCells)
                {
                    results.Add(new CellAddress(sheet, range.Range.TopLeft));
                    break;
                }

                foreach (var cell in range.Range.Cells())
                    results.Add(new CellAddress(sheet, cell));

                break;

            case UnaryNode unary:
                Walk(unary.Operand, currentSheet, results, names, depth);
                break;

            case BinaryNode binary:
                Walk(binary.Left, currentSheet, results, names, depth);
                Walk(binary.Right, currentSheet, results, names, depth);
                break;

            case FunctionNode function:
                foreach (var argument in function.Arguments)
                    Walk(argument, currentSheet, results, names, depth);

                break;
        }
    }

    /// <summary>Above this many cells, a range registers only its origin rather than every cell.</summary>
    public const int MaxTrackedRangeCells = 65536;
}
