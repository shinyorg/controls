namespace Shiny.Controls.Office.Spreadsheet.Calc;

/// <summary>
/// Walks a parsed formula and produces a value.
/// </summary>
public sealed class FormulaEvaluator(FunctionRegistry functions)
{
    public FormulaEvaluator() : this(FunctionRegistry.Default)
    {
    }

    public FunctionRegistry Functions { get; } = functions;

    public CellValue Evaluate(FormulaNode node, ICalcContext context)
        => this.EvaluateNode(node, context).Scalar;

    public CalcValue EvaluateNode(FormulaNode node, ICalcContext context) => node switch
    {
        LiteralNode literal => CalcValue.From(literal.Value),
        ReferenceNode reference => this.EvaluateReference(reference, context),
        RangeNode range => this.EvaluateRange(range, context),
        UnknownNameNode name => this.EvaluateName(name, context),
        MissingArgumentNode => CalcValue.Blank,
        UnaryNode unary => this.EvaluateUnary(unary, context),
        BinaryNode binary => this.EvaluateBinary(binary, context),
        FunctionNode function => this.EvaluateFunction(function, context),
        _ => CalcValue.Error(CellError.Value)
    };

    [ThreadStatic]
    static int nameDepth;

    /// <summary>
    /// A defined name, evaluated as the formula it stands for. Unknown names are #NAME?, and a name
    /// that refers back to itself stops rather than recursing until the stack dies.
    /// </summary>
    CalcValue EvaluateName(UnknownNameNode node, ICalcContext context)
    {
        if (nameDepth > 32 || context.ResolveName(node.Name, context.CurrentSheet) is not { } resolved)
            return CalcValue.Error(CellError.Name);

        nameDepth++;
        try
        {
            return this.EvaluateNode(resolved, context);
        }
        finally
        {
            nameDepth--;
        }
    }

    CalcValue EvaluateReference(ReferenceNode node, ICalcContext context)
    {
        if (node.Sheet is not null && !context.SheetExists(node.Sheet))
            return CalcValue.Error(CellError.Ref);

        if (!node.Cell.IsValid)
            return CalcValue.Error(CellError.Ref);

        return CalcValue.From(context.GetValue(node.Sheet, node.Cell));
    }

    CalcValue EvaluateRange(RangeNode node, ICalcContext context)
    {
        if (node.Sheet is not null && !context.SheetExists(node.Sheet))
            return CalcValue.Error(CellError.Ref);

        var range = node.Range;
        var array = new CalcArray(range.RowCount, range.ColumnCount);

        for (var row = 0; row < range.RowCount; row++)
        {
            for (var column = 0; column < range.ColumnCount; column++)
                array[row, column] = context.GetValue(node.Sheet, new CellRef(range.Left + column, range.Top + row));
        }

        return CalcValue.From(array);
    }

    CalcValue EvaluateUnary(UnaryNode node, ICalcContext context)
    {
        var operand = this.EvaluateNode(node.Operand, context).Scalar;
        if (operand.IsError)
            return CalcValue.From(operand);

        if (!Coercion.TryToNumber(operand, out var number, out var error))
            return CalcValue.Error(error);

        return node.Operator switch
        {
            UnaryOperator.Negate => CalcValue.From(-number),
            UnaryOperator.Plus => CalcValue.From(number),
            UnaryOperator.Percent => CalcValue.From(number / 100d),
            _ => CalcValue.Error(CellError.Value)
        };
    }

    CalcValue EvaluateBinary(BinaryNode node, ICalcContext context)
    {
        var left = this.EvaluateNode(node.Left, context).Scalar;
        var right = this.EvaluateNode(node.Right, context).Scalar;

        // Errors win over everything, and the leftmost one wins.
        if (left.IsError)
            return CalcValue.From(left);

        if (right.IsError)
            return CalcValue.From(right);

        return node.Operator switch
        {
            BinaryOperator.Concat => CalcValue.From(Coercion.ToText(left) + Coercion.ToText(right)),
            BinaryOperator.Equal or BinaryOperator.NotEqual or BinaryOperator.LessThan or
            BinaryOperator.LessThanOrEqual or BinaryOperator.GreaterThan or BinaryOperator.GreaterThanOrEqual
                => Compare(node.Operator, left, right),
            _ => Arithmetic(node.Operator, left, right)
        };
    }

    static CalcValue Compare(BinaryOperator op, CellValue left, CellValue right)
    {
        int comparison;
        if (Coercion.EqualityWithBlank(left, right, out var areEqual))
        {
            // Blank equals zero and equals "", so ordering has to be derived from equality rather than
            // from a rank comparison that would place blank arbitrarily.
            comparison = areEqual ? 0 : Coercion.Compare(Normalise(left), Normalise(right));
        }
        else
        {
            comparison = Coercion.Compare(left, right);
        }

        var result = op switch
        {
            BinaryOperator.Equal => comparison == 0,
            BinaryOperator.NotEqual => comparison != 0,
            BinaryOperator.LessThan => comparison < 0,
            BinaryOperator.LessThanOrEqual => comparison <= 0,
            BinaryOperator.GreaterThan => comparison > 0,
            _ => comparison >= 0
        };

        return CalcValue.From(result);

        static CellValue Normalise(CellValue value) => value.IsBlank ? CellValue.FromNumber(0) : value;
    }

    static CalcValue Arithmetic(BinaryOperator op, CellValue left, CellValue right)
    {
        if (!Coercion.TryToNumber(left, out var a, out var leftError))
            return CalcValue.Error(leftError);

        if (!Coercion.TryToNumber(right, out var b, out var rightError))
            return CalcValue.Error(rightError);

        switch (op)
        {
            case BinaryOperator.Add:
                return CalcValue.From(a + b);
            case BinaryOperator.Subtract:
                return CalcValue.From(a - b);
            case BinaryOperator.Multiply:
                return CalcValue.From(a * b);
            case BinaryOperator.Divide:
                return b == 0 ? CalcValue.Error(CellError.Div0) : CalcValue.From(a / b);
            case BinaryOperator.Power:
                var power = Math.Pow(a, b);
                return double.IsNaN(power) || double.IsInfinity(power)
                    ? CalcValue.Error(CellError.Num)
                    : CalcValue.From(power);
            default:
                return CalcValue.Error(CellError.Value);
        }
    }

    CalcValue EvaluateFunction(FunctionNode node, ICalcContext context)
    {
        if (!this.Functions.TryGet(node.Name, out var function))
            return CalcValue.Error(CellError.Name);

        if (node.Arguments.Count < function.MinArguments ||
            (function.MaxArguments >= 0 && node.Arguments.Count > function.MaxArguments))
            return CalcValue.Error(CellError.Value);

        var arguments = new CalcArguments(node.Arguments, this, context);

        try
        {
            return function.Invoke(arguments);
        }
        catch (CalcErrorException ex)
        {
            return CalcValue.Error(ex.Error);
        }
    }
}

/// <summary>Thrown inside a function implementation to return an Excel error value.</summary>
public sealed class CalcErrorException(CellError error) : Exception($"Calculation error {error}")
{
    public CellError Error { get; } = error;
}
