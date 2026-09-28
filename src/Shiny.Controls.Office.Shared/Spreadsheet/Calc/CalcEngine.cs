namespace Shiny.Controls.Office.Spreadsheet.Calc;

/// <summary>
/// Owns the parsed formulas, the dependency graph and the computed values for a workbook.
/// </summary>
/// <remarks>
/// The engine is deliberately decoupled from <see cref="Workbook"/> through <see cref="ICalcContext"/>
/// so the whole calculation layer can be exercised against an in-memory grid — which is what makes a
/// conformance suite of thousands of formulas practical to run.
/// </remarks>
public sealed class CalcEngine
{
    readonly Dictionary<CellAddress, FormulaNode> formulas = new();
    readonly Dictionary<CellAddress, CellValue> computed = new();
    readonly Dictionary<CellAddress, string> formulaText = new();
    readonly FormulaEvaluator evaluator;

    public CalcEngine(FunctionRegistry? functions = null)
        => this.evaluator = new FormulaEvaluator(functions ?? FunctionRegistry.Default);

    public DependencyGraph Dependencies { get; } = new();

    public FunctionRegistry Functions => this.evaluator.Functions;

    /// <summary>Cells that could not be ordered because they take part in a circular reference.</summary>
    public IReadOnlySet<CellAddress> CircularCells { get; private set; } = new HashSet<CellAddress>();

    public int FormulaCount => this.formulas.Count;

    public bool TryGetComputed(CellAddress cell, out CellValue value) => this.computed.TryGetValue(cell, out value);

    public string? GetFormulaText(CellAddress cell) => this.formulaText.GetValueOrDefault(cell);

    public bool IsFormula(CellAddress cell) => this.formulas.ContainsKey(cell);

    /// <summary>
    /// Registers a formula. Returns false when it does not parse, in which case the cell is recorded as
    /// #NAME? rather than being left silently absent.
    /// </summary>
    public bool SetFormula(CellAddress cell, string formula)
    {
        if (!FormulaParser.TryParse(formula, out var node, out _))
        {
            this.formulas.Remove(cell);
            this.formulaText[cell] = formula;
            this.Dependencies.ClearPrecedents(cell);
            this.computed[cell] = CellValue.FromError(CellError.Name);
            return false;
        }

        this.formulas[cell] = node!;
        this.formulaText[cell] = formula;
        this.Dependencies.SetPrecedents(cell, DependencyGraph.Collect(node!, cell.Sheet, this.NameResolver));
        return true;
    }

    /// <summary>
    /// Resolves a defined name to its parsed formula, for the dependency graph. Set by the workbook, so a
    /// formula over a named range recalculates when a cell inside it changes.
    /// </summary>
    public Func<string, string, FormulaNode?>? NameResolver { get; set; }

    /// <summary>Evaluates an expression and returns every value it produces — a range comes back whole.</summary>
    public IReadOnlyList<CellValue> EvaluateOnceValues(string formula, ICalcContext context)
    {
        if (!FormulaParser.TryParse(formula, out var node, out _))
            return [CellValue.FromError(CellError.Name)];

        return this.evaluator.EvaluateNode(node!, context).Flatten().ToList();
    }

    /// <summary>Evaluates an expression, keeping a range's shape.</summary>
    public CalcValue EvaluateOnceRaw(string formula, ICalcContext context)
    {
        if (!FormulaParser.TryParse(formula, out var node, out _))
            return CalcValue.Error(CellError.Name);

        return this.evaluator.EvaluateNode(node!, context);
    }

    public void RemoveFormula(CellAddress cell)
    {
        this.formulas.Remove(cell);
        this.formulaText.Remove(cell);
        this.computed.Remove(cell);
        this.Dependencies.ClearPrecedents(cell);
    }

    public void Clear()
    {
        this.formulas.Clear();
        this.formulaText.Clear();
        this.computed.Clear();
        this.Dependencies.Clear();
        this.CircularCells = new HashSet<CellAddress>();
    }

    /// <summary>Recomputes every formula. Used on load and after a bulk change.</summary>
    public void RecalculateAll(ICalcContext context)
    {
        var order = this.Dependencies.AllInEvaluationOrder(out var cycle);
        this.Apply(order, cycle, context);
    }

    /// <summary>
    /// Recomputes only what a change to <paramref name="changed"/> affects, in dependency order.
    /// </summary>
    public IReadOnlyList<CellAddress> Recalculate(IEnumerable<CellAddress> changed, ICalcContext context)
    {
        var order = this.Dependencies.AffectedInEvaluationOrder(changed, out var cycle);
        this.Apply(order, cycle, context);
        return order;
    }

    void Apply(List<CellAddress> order, HashSet<CellAddress> cycle, ICalcContext context)
    {
        this.CircularCells = cycle;

        // Excel reports a circular reference and leaves the cells at zero rather than failing the whole
        // calculation, so the rest of the sheet still computes.
        foreach (var circular in cycle)
            this.computed[circular] = CellValue.FromNumber(0);

        foreach (var address in order)
        {
            if (!this.formulas.TryGetValue(address, out var node))
                continue;

            var scoped = new RebasedCalcContext(context, address.Sheet, address.Cell);
            this.computed[address] = this.evaluator.Evaluate(node, scoped);
        }
    }

    /// <summary>Evaluates an expression without registering it — used by a formula bar preview.</summary>
    public CellValue EvaluateOnce(string formula, ICalcContext context)
    {
        if (!FormulaParser.TryParse(formula, out var node, out _))
            return CellValue.FromError(CellError.Name);

        return this.evaluator.Evaluate(node!, context);
    }
}
