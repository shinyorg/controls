namespace Shiny.Controls.Office.Spreadsheet.Calc;

public sealed record CalcFunction(
    string Name,
    int MinArguments,
    int MaxArguments,
    Func<CalcArguments, CalcValue> Invoke)
{
    /// <summary>Use as <see cref="MaxArguments"/> for a function that takes any number of arguments.</summary>
    public const int Unlimited = -1;
}

/// <summary>
/// Name-to-implementation lookup for worksheet functions.
/// </summary>
public sealed class FunctionRegistry
{
    readonly Dictionary<string, CalcFunction> functions = new(StringComparer.OrdinalIgnoreCase);

    public static FunctionRegistry Default { get; } = CreateDefault();

    public IReadOnlyCollection<string> Names => this.functions.Keys;

    public int Count => this.functions.Count;

    public void Add(CalcFunction function) => this.functions[function.Name] = function;

    public void Add(string name, int min, int max, Func<CalcArguments, CalcValue> invoke)
        => this.Add(new CalcFunction(name, min, max, invoke));

    public bool TryGet(string name, out CalcFunction function) => this.functions.TryGetValue(name, out function!);

    public bool Contains(string name) => this.functions.ContainsKey(name);

    static FunctionRegistry CreateDefault()
    {
        var registry = new FunctionRegistry();
        MathFunctions.Register(registry);
        StatisticalFunctions.Register(registry);
        LogicalFunctions.Register(registry);
        TextFunctions.Register(registry);
        LookupFunctions.Register(registry);
        DateFunctions.Register(registry);
        InformationFunctions.Register(registry);
        ModernFunctions.Register(registry);
        return registry;
    }
}
